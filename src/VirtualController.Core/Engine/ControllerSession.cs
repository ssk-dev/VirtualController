using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Timing;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Engine;

/// <summary>
/// Verbindet alle Bausteine fuer genau einen virtuellen Controller: haelt die benoetigten
/// physischen Device-Reader offen, pollt sie in einem eigenen Hochfrequenz-Loop, wendet die
/// Mapping-Tabelle des Profils an und uebertraegt das Ergebnis an den virtuellen Controller.
/// Eine Instanz entspricht exakt einem Eintrag in der Controller-Liste der UI.
/// </summary>
public sealed class ControllerSession : IDisposable
{
    private readonly Dictionary<string, IDeviceReader> _readers = new();
    private readonly Dictionary<string, DeviceState> _latestStates = new();
    private readonly VirtualPadState _padState = new();
    private readonly IVirtualPad _pad;
    private readonly PrecisionLoop _loop;
    private readonly object _profileLock = new();
    private IReadOnlyDictionary<string, DeviceSettings> _deviceSettings;

    /// <summary>Zustand der letzten Auswertung je Umschalt-Ausloeser (Toggle-Trigger des Controllers sowie
    /// Switch-Trigger jedes Modus), fuer die Erkennung einer steigenden Flanke (Uebergang false -&gt; true)
    /// zwischen zwei aufeinanderfolgenden Ticks - <see cref="Mapping.MappingEngine.IsPhysicalInputActive"/>
    /// selbst ist zustandslos und liefert nur den aktuellen Momentanwert. Key = (DeviceId, Kind, Index).</summary>
    private readonly Dictionary<(string DeviceId, PhysicalInputKind Kind, int Index), bool> _triggerWasActive = new();

    public VirtualControllerProfile Profile { get; private set; }

    /// <summary>Letzter Fehler aus dem Loop-Thread (z.B. Treiber nicht installiert), fuer Anzeige in der UI.</summary>
    public Exception? LastError { get; private set; }

    public event Action<Exception>? Faulted;

    public ControllerSession(
        VirtualControllerProfile profile,
        IVirtualPad pad,
        IReadOnlyList<PhysicalDeviceInfo> availableDevices,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        Profile = profile;
        _pad = pad;
        _deviceSettings = deviceSettings ?? new Dictionary<string, DeviceSettings>();

        OpenRequiredReaders(availableDevices);
        _loop = new PrecisionLoop($"VCtrl-{profile.Name}", profile.PollingRateHz, Tick);
    }

    public void Start()
    {
        _pad.Connect();
        _loop.Start();
    }

    public void Stop()
    {
        _loop.Stop();
        _pad.Disconnect();
    }

    /// <summary>Ersetzt das Profil (z.B. nach Aenderung der Mapping-Tabelle in der UI) thread-sicher.</summary>
    public void UpdateProfile(VirtualControllerProfile profile, IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        lock (_profileLock)
        {
            Profile = profile;
            OpenRequiredReaders(availableDevices);
        }
    }

    /// <summary>Aktualisiert die geraeteweiten Einstellungen (u.a. deaktivierte einzelne Eingaben), ohne dass
    /// sich das Mapping-Profil selbst geaendert haben muss (z.B. nach einer Aenderung im Konfigurationsdialog).</summary>
    public void UpdateDeviceSettings(IReadOnlyDictionary<string, DeviceSettings> deviceSettings)
    {
        lock (_profileLock)
        {
            _deviceSettings = deviceSettings;
        }
    }

    /// <summary>Physische Geraete (per <see cref="PhysicalDeviceInfo.DeviceId"/>), die diese Session aktuell
    /// tatsaechlich benoetigt - dieselbe Menge, die <see cref="OpenRequiredReaders"/> intern verwendet, um zu
    /// entscheiden, welche Reader offen bleiben muessen. Wird von <see cref="ControllerManager"/> genutzt, um
    /// die zugehoerigen physischen Geraete waehrend der Laufzeit dieser Session per HidHide zu sperren (siehe
    /// <see cref="Devices.HidHideController"/>).</summary>
    public IReadOnlyCollection<string> NeededDeviceIds => ComputeNeededDeviceIds();

    /// <summary>Ermittelt, welche physischen Geraete in IRGENDEINEM Modus (nicht nur dem aktuell aktiven) als
    /// Mapping-Quelle referenziert werden, sowie die Geraete hinter den Umschalt-Ausloesern (Toggle-/Switch-
    /// Trigger) - unabhaengig vom aktuell aktiven Modus, da ein Moduswechsel zur Laufzeit sonst ein erneutes
    /// Oeffnen der Reader bzw. erneutes Sperren/Entsperren via HidHide erfordern wuerde.</summary>
    private HashSet<string> ComputeNeededDeviceIds()
    {
        return Profile.Modes
            .SelectMany(mode => mode.Mappings)
            .Select(m => m.SourceDeviceId)
            .Concat(Profile.Modes
                .Where(mode => mode.SwitchTrigger is not null)
                .Select(mode => mode.SwitchTrigger!.DeviceId))
            .Concat(Profile.ToggleTrigger is not null
                ? new[] { Profile.ToggleTrigger.DeviceId }
                : Array.Empty<string>())
            .Distinct()
            .ToHashSet();
    }

    private void OpenRequiredReaders(IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        // Reader muessen fuer Geraete offen bleiben, die in IRGENDEINEM Modus benoetigt werden (nicht nur
        // im aktuell aktiven), da ein Moduswechsel zur Laufzeit sonst ein erneutes Oeffnen erfordern wuerde.
        // Ebenso muessen die Geraete hinter den Umschalt-Ausloesern (Toggle-/Switch-Trigger) offen sein,
        // damit deren Flankenerkennung unabhaengig vom aktuell aktiven Modus funktioniert.
        var neededDeviceIds = ComputeNeededDeviceIds();
        var byId = availableDevices.ToDictionary(d => d.DeviceId);

        // Nicht mehr benoetigte Reader schliessen.
        foreach (var existingId in _readers.Keys.Where(id => !neededDeviceIds.Contains(id)).ToList())
        {
            _readers[existingId].Dispose();
            _readers.Remove(existingId);
            _latestStates.Remove(existingId);
        }

        // Fehlende Reader fuer neu gemappte Geraete oeffnen.
        foreach (var deviceId in neededDeviceIds)
        {
            if (_readers.ContainsKey(deviceId) || !byId.TryGetValue(deviceId, out var info))
            {
                continue;
            }

            _readers[deviceId] = DeviceEnumerator.OpenReader(info);
            _latestStates[deviceId] = DeviceState.Empty(info.ButtonCount);
        }
    }

    /// <summary>
    /// Wertet den aktuell konfigurierten Moduswechsel-Mechanismus (<see cref="VirtualControllerProfile.ModeSwitchMechanism"/>)
    /// anhand der zuletzt gepollten Device-States aus und aktualisiert bei Bedarf <see cref="VirtualControllerProfile.ActiveModeId"/>.
    /// Reine Flankenerkennung (steigende Flanke = ausgeloest) mittels <see cref="_triggerWasActive"/>, da
    /// <see cref="MappingEngine.IsPhysicalInputActive"/> selbst zustandslos ist und bei dauerhaft gedrueckter
    /// Eingabe sonst bei jedem Tick erneut (bzw. im Toggle-Fall staendig weiter) umschalten wuerde.
    /// </summary>
    private void EvaluateModeSwitching()
    {
        switch (Profile.ModeSwitchMechanism)
        {
            case ModeSwitchMechanism.Toggle:
                if (Profile.ToggleTrigger is { } toggleTrigger && UpdateTriggerEdgeAndCheckRising(toggleTrigger))
                {
                    AdvanceToNextEnabledMode();
                }
                break;

            case ModeSwitchMechanism.Switch:
                foreach (var mode in Profile.Modes)
                {
                    if (mode.Enabled && mode.SwitchTrigger is { } switchTrigger && UpdateTriggerEdgeAndCheckRising(switchTrigger))
                    {
                        Profile.ActiveModeId = mode.Id;
                    }
                }
                break;
        }
    }

    /// <summary>Schaltet <see cref="VirtualControllerProfile.ActiveModeId"/> zyklisch (mit Umlauf) zum
    /// naechsten aktivierten Modus weiter, ausgehend von der Reihenfolge in <see cref="VirtualControllerProfile.Modes"/>.
    /// Tut nichts, falls kein Modus aktiviert ist.</summary>
    private void AdvanceToNextEnabledMode()
    {
        var enabledModes = Profile.Modes.Where(m => m.Enabled).ToList();
        if (enabledModes.Count == 0)
        {
            return;
        }

        int currentIndex = enabledModes.FindIndex(m => m.Id == Profile.ActiveModeId);
        int nextIndex = (currentIndex + 1) % enabledModes.Count;
        Profile.ActiveModeId = enabledModes[nextIndex].Id;
    }

    private bool UpdateTriggerEdgeAndCheckRising(PhysicalInputTrigger trigger)
    {
        var key = (trigger.DeviceId, trigger.Kind, trigger.Index);
        bool wasActive = _triggerWasActive.TryGetValue(key, out var previous) && previous;
        bool isActive = MappingEngine.IsPhysicalInputActive(trigger, _latestStates, _deviceSettings);
        _triggerWasActive[key] = isActive;
        return isActive && !wasActive;
    }

    private void Tick()
    {
        try
        {
            lock (_profileLock)
            {
                foreach (var (deviceId, reader) in _readers)
                {
                    if (reader.Poll(out var state))
                    {
                        _latestStates[deviceId] = state;
                    }
                }

                EvaluateModeSwitching();

                MappingEngine.Apply(Profile, _latestStates, _padState, _deviceSettings);
            }

            _pad.Submit(_padState);
        }
        catch (Exception ex)
        {
            LastError = ex;
            Faulted?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _loop.Dispose();
        foreach (var reader in _readers.Values)
        {
            reader.Dispose();
        }
        _pad.Dispose();
    }
}

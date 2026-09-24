using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Engine;

/// <summary>
/// Zentrale Verwaltung aller virtuellen Controller der Anwendung. Haelt genau eine
/// <see cref="ViGEmClient"/>-Verbindung zum ViGEmBus-Treiber (pro Prozess ausreichend und
/// empfohlen) und je einen laufenden <see cref="ControllerSession"/> pro konfiguriertem
/// virtuellem Controller.
/// </summary>
public sealed class ControllerManager : IDisposable
{
    private readonly Dictionary<Guid, ControllerSession> _sessions = new();
    private readonly HidHideController _hidHide = new();

    /// <summary>Merkt sich je Profil-Id die aktuell fuer dieses Profil per HidHide gesperrten PnP-Instanz-IDs
    /// (siehe <see cref="ResolveHidHideInstanceIds"/>) - benoetigt, um bei Entfernen/Aktualisieren eines
    /// Controllers exakt dieselben Instanz-IDs wieder korrekt freizugeben (<see cref="HidHideController.Unlock"/>
    /// ist referenzgezaehlt, siehe dessen Dokumentation).</summary>
    private readonly Dictionary<Guid, HashSet<string>> _hidHideLockedInstanceIds = new();

    public event Action<Guid, Exception>? SessionFaulted;

    /// <summary>Wird mit einer anzeigefertigen deutschen Hinweismeldung ausgeloest, wenn im Zusammenhang mit
    /// der HidHide-Integration eine Situation auftritt, die der Nutzer sehen sollte, aber die keinen
    /// Abbruch/Fehler darstellt (z.B. verwaiste Sperren eines vorherigen Absturzes wurden automatisch
    /// bereinigt, oder HidHide ist fuer ein Profil aktiviert, aber der Treiber ist nicht verfuegbar).
    /// Ergaenzt die automatische Bereinigung um eine explizite UI-Rueckmeldung (siehe <see cref="MainViewModel"/>).</summary>
    public event Action<string>? HidHideWarning;

    /// <summary>Ob der HidHide-Treiber installiert und betriebsbereit ist - fuer die UI, um die zugehoerige
    /// Checkbox ("physische Geraete sperren, waehrend dieser Controller laeuft") auszugrauen, falls nicht.</summary>
    public bool IsHidHideAvailable => _hidHide.IsAvailable;

    /// <summary>
    /// Initialisiert die Verbindung zum ViGEmBus-Treiber. Muss erfolgreich sein, bevor
    /// virtuelle Controller erzeugt werden koennen.
    /// </summary>
    /// <exception cref="VigemBusNotFoundException">ViGEmBus ist auf diesem Rechner nicht installiert.</exception>
    public void Initialize()
    {
        _client ??= new ViGEmClient();

        // Einmalig beim Start pruefen, ob von einem vorherigen, abgestuerzten Programmlauf noch eigene
        // HidHide-Sperren uebrig sind (regulaeres Beenden haette sie bereits ueber Unlock() entfernt) -
        // und diese automatisch entfernen, damit physische Geraete nicht dauerhaft faelschlicherweise fuer
        // andere Anwendungen gesperrt bleiben.
        var orphaned = _hidHide.CleanupOrphanedLocks();
        if (orphaned.Count > 0)
        {
            HidHideWarning?.Invoke(
                $"HidHide: {orphaned.Count} verwaiste Geraete-Sperre(n) von einem vorherigen, nicht ordnungsgemaess beendeten Programmlauf wurden automatisch entfernt.");
        }
    }

    public IReadOnlyList<PhysicalDeviceInfo> GetAvailablePhysicalDevices() => DeviceEnumerator.EnumerateAll();

    public ControllerSession AddController(VirtualControllerProfile profile, IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        if (_client is null)
        {
            throw new InvalidOperationException($"{nameof(ControllerManager)}.{nameof(Initialize)}() muss zuerst aufgerufen werden.");
        }

        // Sicherheitsnetz gegen einen Geraete-Leak: falls fuer dieses Profil bereits eine Session
        // laeuft (z.B. weil "Start" ohne vorheriges "Stop" erneut ausgeloest wurde, etwa nach einem
        // Layout-Wechsel), zuerst die alte Session korrekt beenden (Disconnect vom ViGEmBus-Treiber),
        // statt sie im Dictionary stillschweigend zu ueberschreiben - andernfalls bleibt der alte
        // virtuelle Controller dauerhaft (bis Prozessende) als Geraet in Windows sichtbar angemeldet.
        RemoveController(profile.Id);

        var pad = VirtualPadFactory.Create(_client, profile.Backend);
        var session = new ControllerSession(profile, pad, GetAvailablePhysicalDevices(), deviceSettings);
        session.Faulted += ex => SessionFaulted?.Invoke(profile.Id, ex);

        _sessions[profile.Id] = session;
        session.Start();

        var instanceIds = ResolveHidHideInstanceIds(profile, session);
        _hidHide.Lock(instanceIds);
        _hidHideLockedInstanceIds[profile.Id] = instanceIds;

        return session;
    }

    public void RemoveController(Guid profileId)
    {
        if (_hidHideLockedInstanceIds.Remove(profileId, out var lockedInstanceIds))
        {
            _hidHide.Unlock(lockedInstanceIds);
        }

        if (_sessions.Remove(profileId, out var session))
        {
            session.Dispose();
        }
    }

    public void UpdateController(VirtualControllerProfile profile, IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        if (_sessions.TryGetValue(profile.Id, out var session))
        {
            session.UpdateProfile(profile, GetAvailablePhysicalDevices());
            if (deviceSettings is not null)
            {
                session.UpdateDeviceSettings(deviceSettings);
            }

            // HidHide-Sperren per Differenz aktualisieren: eine Profilaenderung (z.B. andere zugewiesene
            // Geraete oder Umschalten von HidHideEnabled) muss sich sofort auswirken, ohne dass der
            // Controller dafuer neu gestartet werden muss.
            var newInstanceIds = ResolveHidHideInstanceIds(profile, session);
            _hidHideLockedInstanceIds.TryGetValue(profile.Id, out var previousInstanceIds);
            previousInstanceIds ??= new HashSet<string>();

            var noLongerNeeded = previousInstanceIds.Where(id => !newInstanceIds.Contains(id)).ToList();
            var newlyNeeded = newInstanceIds.Where(id => !previousInstanceIds.Contains(id)).ToList();

            _hidHide.Unlock(noLongerNeeded);
            _hidHide.Lock(newlyNeeded);
            _hidHideLockedInstanceIds[profile.Id] = newInstanceIds;
        }
    }

    /// <summary>Ermittelt anhand von <see cref="VirtualControllerProfile.HidHideEnabled"/> und der aktuell von
    /// <paramref name="session"/> tatsaechlich benoetigten physischen Geraete (<see cref="ControllerSession.NeededDeviceIds"/>)
    /// die Menge der PnP-Instanz-IDs, die fuer dieses Profil per HidHide gesperrt sein sollen. Liefert eine
    /// leere Menge, falls die Option deaktiviert ist oder HidHide nicht installiert/betriebsbereit ist.</summary>
    private HashSet<string> ResolveHidHideInstanceIds(VirtualControllerProfile profile, ControllerSession session)
    {
        if (!profile.HidHideEnabled)
        {
            return new HashSet<string>();
        }

        if (!_hidHide.IsAvailable)
        {
            HidHideWarning?.Invoke(
                $"HidHide ist fuer \"{profile.Name}\" aktiviert, der HidHide-Treiber ist aber nicht installiert/betriebsbereit - die physischen Geraete werden NICHT gesperrt.");
            return new HashSet<string>();
        }

        var availableDevices = GetAvailablePhysicalDevices().ToDictionary(d => d.DeviceId);
        var result = new HashSet<string>();

        foreach (var deviceId in session.NeededDeviceIds)
        {
            if (!availableDevices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            foreach (var instanceId in _hidHide.ResolveInstanceIds(device))
            {
                result.Add(instanceId);
            }
        }

        return result;
    }

    /// <summary>Verteilt geaenderte geraeteweite Einstellungen (z.B. eine im Konfigurationsdialog
    /// deaktivierte Eingabe) sofort an alle aktuell laufenden Sessions, ohne dass dafuer ein
    /// vollstaendiges Mapping-Profil-Update erforderlich ist.</summary>
    public void BroadcastDeviceSettings(IReadOnlyDictionary<string, DeviceSettings> deviceSettings)
    {
        foreach (var session in _sessions.Values)
        {
            session.UpdateDeviceSettings(deviceSettings);
        }
    }

    public IReadOnlyDictionary<Guid, ControllerSession> Sessions => _sessions;

    private ViGEmClient? _client;

    public void Dispose()
    {
        foreach (var lockedInstanceIds in _hidHideLockedInstanceIds.Values)
        {
            _hidHide.Unlock(lockedInstanceIds);
        }
        _hidHideLockedInstanceIds.Clear();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        _client?.Dispose();
        _client = null;
    }
}

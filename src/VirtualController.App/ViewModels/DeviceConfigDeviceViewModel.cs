using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.App.Diagnostics;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Repraesentiert ein komplettes physisches Geraet im Konfigurationsdialog: globaler Enable/Disable-
/// Schalter (deaktivierte Geraete verschwinden aus der Geraeteauswahl aller virtuellen Controller)
/// sowie eine aufklappbare Liste aller physischen Eingaben mit Umbenennung und Einzel-Enable/Disable.
/// </summary>
public sealed partial class DeviceConfigDeviceViewModel : ObservableObject, IDisposable
{
    /// <summary>Aktualisierungsrate der Live-Hervorhebung, identisch zu <see cref="DeviceSelectionViewModel"/>.</summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromMilliseconds(33);

    public PhysicalDeviceInfo Device { get; private set; }

    private readonly DeviceSettings _settings;
    private readonly Action _notifyAvailabilityChanged;
    private readonly Action _notifySettingsChanged;
    private bool _inputsBuilt;

    private DispatcherTimer? _liveTimer;
    private IDeviceReader? _liveReader;
    private DeviceConfigAxisGroupViewModel? _axisGroup;

    /// <summary>Ob der "Gerätekonfiguration"-Tab des Hauptfensters aktuell tatsaechlich sichtbar ist UND
    /// das Fenster nicht minimiert ist (siehe <see cref="SetScreenActive"/>, gesetzt durch
    /// <see cref="DeviceConfigViewModel"/> anhand von <see cref="MainViewModel"/>). Nur wenn dies zutrifft
    /// UND <see cref="IsSelected"/> true ist, wird tatsaechlich live gepollt (siehe
    /// <see cref="RefreshLiveMonitoringState"/>) - andernfalls waere das Polling reine Verschwendung, da
    /// die zugehoerige Live-Hervorhebung/Achsen-Vorschau ohnehin nicht sichtbar sein kann.</summary>
    private bool _isScreenActive;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>Ob dieses Geraet aktuell physisch angeschlossen ist. Wird per <see cref="UpdateConnectionState"/>
    /// bei jedem Geraete-Scan (siehe MainViewModel.RefreshDevices, inkl. periodischem Hotplug-Polling)
    /// aktualisiert. Getrennte Geraete bleiben in der Liste sichtbar (ausgegraut) und weiterhin
    /// konfigurierbar (Umbenennung, Kalibrierung), damit deren Einstellungen nicht verloren gehen.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Ob dieses Geraet aktuell als Tab im Konfigurationsdialog ausgewaehlt ist. Ersetzt das
    /// frueher hier verwendete "IsExpanded" (Expander), seit Geraete als Tabs statt als aufklappbare
    /// Liste dargestellt werden - die Semantik (Inputs bei Bedarf aufbauen, Live-Ueberwachung nur fuer
    /// den aktuell sichtbaren Tab starten/stoppen) bleibt unveraendert.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Ob der Nutzer dieses Geraet manuell ausgeblendet hat (siehe <see cref="DeviceSettings.Hidden"/>).
    /// Ausgeblendete Geraete verschwinden aus der Hauptliste dieses Tabs (siehe MainWindow.xaml, separate
    /// Liste "Ausgeblendete Geräte") und aus der Geraeteauswahl aller virtuellen Controller, bleiben aber
    /// jederzeit per <see cref="ToggleHiddenCommand"/> wieder einblendbar - gedacht u.a. fuer die eigenen,
    /// per ViGEmBus emulierten virtuellen Controller dieser Anwendung, die sonst nicht von echter Hardware
    /// unterscheidbar sind.</summary>
    [ObservableProperty]
    private bool _hidden;

    public ObservableCollection<IDeviceConfigGroup> InputGroups { get; } = new();

    public string DisplayName => Device.DisplayName;

    /// <summary>Steuert den gruenen/grauen Status-Punkt in der Geraeteliste (siehe MainWindow.xaml,
    /// "Gerätekonfiguration"-Tab): gruen nur wenn das Geraet sowohl aktiviert als auch aktuell
    /// angeschlossen ist, ansonsten grau - unabhaengig davon, ob nur eine oder beide Bedingungen
    /// nicht erfuellt sind (siehe BoolToStatusBrushConverter).</summary>
    public bool IsActiveIndicator => Enabled && IsConnected;

    public DeviceConfigDeviceViewModel(PhysicalDeviceInfo device, DeviceSettings settings, Action notifyAvailabilityChanged, Action notifySettingsChanged, bool isConnected)
    {
        Device = device;
        _settings = settings;
        _notifyAvailabilityChanged = notifyAvailabilityChanged;
        _notifySettingsChanged = notifySettingsChanged;
        _enabled = settings.Enabled;
        _hidden = settings.Hidden;
        _isConnected = isConnected;
    }

    /// <summary>Aktualisiert den Verbindungsstatus dieses bereits vorhandenen Geraete-ViewModels, ohne es
    /// zu ersetzen (siehe DeviceConfigViewModel.UpdateDevices - wird bei jedem Geraete-Scan aufgerufen,
    /// inkl. periodischem Hotplug-Polling, und darf daher laufende Bearbeitungen nicht stoeren). Die
    /// zugrunde liegende <see cref="Device"/>-Beschreibung wird dabei nur aktualisiert, solange die
    /// Eingabenliste noch nicht aufgebaut wurde (<see cref="EnsureInputsBuilt"/>) - so verwendet der
    /// erstmalige Aufbau stets die genauesten verfuegbaren Faehigkeiten (live erkannt statt aus
    /// DeviceSettings.LastKnown* rekonstruiert), waehrend ein bereits ausgewaehltes/aufgebautes Geraet
    /// unveraendert bleibt.</summary>
    public void UpdateConnectionState(PhysicalDeviceInfo device, bool isConnected)
    {
        if (!_inputsBuilt)
        {
            Device = device;
        }

        IsConnected = isConnected;
    }

    partial void OnEnabledChanged(bool value)
    {
        _settings.Enabled = value;
        _notifyAvailabilityChanged();
        OnPropertyChanged(nameof(IsActiveIndicator));
    }

    partial void OnHiddenChanged(bool value)
    {
        _settings.Hidden = value;
        _notifyAvailabilityChanged();
    }

    /// <summary>Blendet dieses Geraet aus (verschieben in die Liste "Ausgeblendete Geräte") bzw. wieder ein -
    /// wird sowohl vom "Ausblenden"-Button in der Hauptliste als auch vom "Einblenden"-Button in der Liste
    /// der ausgeblendeten Geraete aufgerufen (siehe MainWindow.xaml).</summary>
    [RelayCommand]
    private void ToggleHidden() => Hidden = !Hidden;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsActiveIndicator));

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            EnsureInputsBuilt();
        }

        RefreshLiveMonitoringState();
    }

    /// <summary>Legt fest, ob der "Gerätekonfiguration"-Tab des Hauptfensters aktuell tatsaechlich sichtbar
    /// ist UND das Fenster nicht minimiert ist - nur dann darf ueberhaupt live gepollt werden (siehe
    /// <see cref="RefreshLiveMonitoringState"/>). Wird von <see cref="DeviceConfigViewModel.SetScreenActive"/>
    /// bei jeder relevanten Aenderung (Tab-Wechsel, Minimieren/Wiederherstellen des Fensters) fuer alle
    /// seine <see cref="DeviceConfigViewModel.Devices"/> neu gesetzt.</summary>
    public void SetScreenActive(bool value)
    {
        if (_isScreenActive == value)
        {
            return;
        }

        _isScreenActive = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Startet bzw. stoppt das Live-Polling dieses Geraets anhand der kombinierten Bedingung
    /// "Bildschirm aktiv UND als aktuell ausgewaehltes Geraet im Detailbereich dargestellt" - beide
    /// Bedingungen muessen gleichzeitig erfuellt sein, da eine Live-Hervorhebung/Achsen-Vorschau sonst gar
    /// nicht sichtbar sein kann.</summary>
    private void RefreshLiveMonitoringState()
    {
        if (_isScreenActive && IsSelected)
        {
            StartLiveMonitoring();
        }
        else
        {
            StopLiveMonitoring();
        }
    }

    /// <summary>Ordnet eine physische Eingabe (Button oder D-Pad-Richtung) einer der Anzeige-Gruppen zu,
    /// damit der Nutzer bei Geraeten mit vielen Eingaben schneller die gesuchte Kategorie findet, statt
    /// eine lange, unstrukturierte Liste durchsuchen zu muessen. Achsen werden gesondert behandelt (siehe
    /// <see cref="EnsureInputsBuilt"/>), da Positiv-/Negativ-Eintraege dort paarweise mit gemeinsamem
    /// Rahmen dargestellt werden (<see cref="DeviceConfigAxisGroupViewModel"/>).</summary>
    private static string GetGroupName(PhysicalInputKind kind) => kind switch
    {
        PhysicalInputKind.Button => "Buttons",
        PhysicalInputKind.DPad or PhysicalInputKind.DPadUp or PhysicalInputKind.DPadDown
            or PhysicalInputKind.DPadLeft or PhysicalInputKind.DPadRight => "D-Pad",
        _ => "Sonstige"
    };

    private void EnsureInputsBuilt()
    {
        if (_inputsBuilt)
        {
            return;
        }

        _inputsBuilt = true;

        var groupsByName = new Dictionary<string, DeviceConfigInputGroupViewModel>();
        var axisPairsByIndex = new Dictionary<int, DeviceConfigAxisPairViewModel>();
        var orderedAxisIndices = new List<int>();
        var axisGroup = new DeviceConfigAxisGroupViewModel();

        foreach (var inputRef in PhysicalInputCatalog.BuildInputs(Device))
        {
            var key = PhysicalInputCatalog.BuildStorageKey(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
            if (!_settings.Inputs.TryGetValue(key, out var inputSettings))
            {
                inputSettings = new InputSettings();
                _settings.Inputs[key] = inputSettings;
            }

            var row = new DeviceConfigInputRowViewModel(Device, inputRef, inputSettings, _notifySettingsChanged);

            if (inputRef.Kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative)
            {
                // Positiv- und Negativ-Eintrag derselben physischen Achse teilen sich den Index (siehe
                // PhysicalInputCatalog.AddAxisPair) -> zu einem gemeinsamen Paar mit gemeinsamem Rahmen
                // zusammenfassen, statt sie als lose, zusammenhanglose Zeilen darzustellen. Ob mehrere
                // solcher Paare anschliessend noch weiter zu einem kompletten Stick kombiniert werden,
                // entscheidet BuildAxisGroupItems anhand von GetStickAxisPairs.
                if (!axisPairsByIndex.TryGetValue(inputRef.Index, out var pair))
                {
                    pair = new DeviceConfigAxisPairViewModel();
                    axisPairsByIndex[inputRef.Index] = pair;
                    orderedAxisIndices.Add(inputRef.Index);
                }

                if (inputRef.Kind == PhysicalInputKind.AxisPositive)
                {
                    pair.Positive = row;
                }
                else
                {
                    pair.Negative = row;
                }

                continue;
            }

            string groupName = GetGroupName(inputRef.Kind);
            if (!groupsByName.TryGetValue(groupName, out var group))
            {
                group = new DeviceConfigInputGroupViewModel(groupName);
                groupsByName[groupName] = group;
                InputGroups.Add(group);
            }

            group.Inputs.Add(row);
        }

        foreach (var pair in axisPairsByIndex.Values)
        {
            // Erst jetzt, nachdem Positiv- und ein etwaiger Negativ-Eintrag beide zugewiesen sind, laesst
            // sich der korrekte (bidirektionale oder einseitige) Wertebereich der eingebetteten
            // Live-Visualisierung bestimmen (siehe DeviceConfigAxisPairViewModel.BuildVisualization).
            pair.BuildVisualization();
        }

        BuildAxisGroupItems(axisGroup, orderedAxisIndices, axisPairsByIndex);
        _axisGroup = axisGroup;

        if (axisGroup.Items.Count > 0)
        {
            // Direkt nach der Button-Gruppe einfuegen (bzw. an erster Stelle, falls es keine gibt), damit
            // die Reihenfolge "Buttons -> Achsen -> D-Pad -> Sonstige" erhalten bleibt.
            int insertIndex = groupsByName.TryGetValue("Buttons", out var buttonGroup) ? InputGroups.IndexOf(buttonGroup) + 1 : 0;
            InputGroups.Insert(insertIndex, axisGroup);
        }
    }

    /// <summary>
    /// Kombiniert - analog zur identischen Paarungslogik der Live-Vorschau (<see cref="AxisVisualizationFactory"/>) -
    /// zwei zusammengehoerige Achsen-Paare (X und Y desselben Sticks) zu einem einzigen
    /// <see cref="DeviceConfigStickGroupViewModel"/>, damit der Nutzer den kompletten Stick (alle vier
    /// Positiv-/Negativ-Zeilen) mit einem einzigen Schalter deaktivieren kann. Achsen ohne erkannten
    /// Stick-Partner (Trigger, Schieberegler, unpartnerte Rotationsachsen) bleiben als eigenstaendiger
    /// <see cref="DeviceConfigAxisPairViewModel"/> bestehen. Die Reihenfolge in <paramref name="axisGroup"/>
    /// folgt dabei stets der urspruenglichen Katalog-Reihenfolge (<paramref name="orderedAxisIndices"/>).
    /// </summary>
    private void BuildAxisGroupItems(
        DeviceConfigAxisGroupViewModel axisGroup,
        List<int> orderedAxisIndices,
        Dictionary<int, DeviceConfigAxisPairViewModel> axisPairsByIndex)
    {
        var stickByFirstIndex = new Dictionary<int, (int YIndex, string Name, bool InvertYForDisplay)>();
        var consumedAsStickY = new HashSet<int>();

        foreach (var (xIndex, yIndex, name, invertYForDisplay) in GetStickAxisPairs(Device.Api))
        {
            if (axisPairsByIndex.ContainsKey(xIndex) && axisPairsByIndex.ContainsKey(yIndex))
            {
                stickByFirstIndex[xIndex] = (yIndex, name, invertYForDisplay);
                consumedAsStickY.Add(yIndex);
            }
        }

        foreach (var index in orderedAxisIndices)
        {
            if (stickByFirstIndex.TryGetValue(index, out var stickInfo))
            {
                var xPair = axisPairsByIndex[index];
                var yPair = axisPairsByIndex[stickInfo.YIndex];
                axisGroup.Items.Add(new DeviceConfigStickGroupViewModel(
                    stickInfo.Name, xPair, yPair, Device, _settings, _notifySettingsChanged, stickInfo.InvertYForDisplay));
                continue;
            }

            if (consumedAsStickY.Contains(index))
            {
                continue; // Bereits oben als Y-Achse eines kombinierten Sticks verarbeitet.
            }

            axisGroup.Items.Add(axisPairsByIndex[index]);
        }
    }

    /// <summary>Bekannte X/Y-Achsenindex-Kombinationen, die gemeinsam einen vollwertigen 2D-Stick
    /// bilden - identische Zuordnung wie zuvor in <see cref="AxisVisualizationFactory"/> verwendet
    /// (dort fuer die Live-Vorschau, hier zusaetzlich fuer die gemeinsame Enable/Disable-Gruppierung und
    /// die eingebettete 2D-Visualisierung). Bei XInput repraesentieren Z/RotationX zusaetzlich den
    /// rechten Stick; bei DirectInput haben diese Slots keine feste Bedeutung und duerfen deshalb NICHT
    /// automatisch zu einem Stick kombiniert werden. <c>InvertYForDisplay</c> spiegelt die
    /// API-abhaengige Y-Vorzeichenkonvention wider (XInput: positiv = vorwaerts/oben; DirectInput:
    /// positiv = zurueckziehen/abwaerts).</summary>
    private static IEnumerable<(int XIndex, int YIndex, string Name, bool InvertYForDisplay)> GetStickAxisPairs(InputApi api)
    {
        yield return (
            (int)PhysicalAxisId.X, (int)PhysicalAxisId.Y,
            api == InputApi.XInput ? "Linker Stick" : "Stick (X/Y)",
            api == InputApi.XInput);

        if (api == InputApi.XInput)
        {
            yield return ((int)PhysicalAxisId.Z, (int)PhysicalAxisId.RotationX, "Rechter Stick", true);
        }
    }

    private void StartLiveMonitoring()
    {
        if (_liveTimer is not null || !IsConnected)
        {
            return;
        }

        try
        {
            _liveReader = DeviceEnumerator.OpenReader(Device);
        }
        catch
        {
            // Geraet aktuell nicht oeffenbar (z.B. gerade getrennt) -> Live-Hervorhebung einfach ueberspringen.
            _liveReader = null;
            return;
        }

        // TEMPORAERES DEBUG-LOGGING fuer den Bug "manche Achsen reagieren nicht auf Wertaenderung /
        // Negativ-Richtung bleibt dauerhaft hervorgehoben" (DirectInput-Geraete): protokolliert einmalig
        // beim Start der Live-Ueberwachung den kompletten Achsen-Katalog dieses Geraets (welche
        // PhysicalAxisId-Slots laut Enumeration tatsaechlich vorhanden sind) sowie alle daraus erzeugten
        // Eingabe-Referenzen (Index + Kind + Name), damit sich Slot-Indizes zweifelsfrei mit den unten in
        // OnLiveTimerTick protokollierten Rohwerten abgleichen lassen. Bitte nach Abschluss der Diagnose
        // wieder entfernen.
        DebugLog.Write($"[AxisDebug] StartLiveMonitoring Device='{Device.DisplayName}' Api={Device.Api} " +
            $"AvailableAxes=[{string.Join(", ", Device.AvailableAxes.Select(a => $"{(int)a}:{a}"))}]");
        foreach (var inputRef in PhysicalInputCatalog.BuildInputs(Device))
        {
            DebugLog.Write($"[AxisDebug] Catalog Kind={inputRef.Kind} Index={inputRef.Index} Name='{inputRef.DisplayName}'");
        }

        _liveTimer = new DispatcherTimer { Interval = LivePollInterval };
        _liveTimer.Tick += OnLiveTimerTick;
        _liveTimer.Start();
    }

    private void StopLiveMonitoring()
    {
        if (_liveTimer is not null)
        {
            _liveTimer.Tick -= OnLiveTimerTick;
            _liveTimer.Stop();
            _liveTimer = null;
        }

        _liveReader?.Dispose();
        _liveReader = null;

        foreach (var group in InputGroups)
        {
            foreach (var row in group.AllRows)
            {
                row.IsActive = false;
            }
        }

        _axisGroup?.Reset();
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        if (_liveReader is null)
        {
            return;
        }

        if (!_liveReader.Poll(out var state))
        {
            // Geraet wurde getrennt -> Ueberwachung stoppen, bis der Nutzer erneut aufklappt.
            StopLiveMonitoring();
            return;
        }

        // TEMPORAERES DEBUG-LOGGING fuer den Bug "manche Achsen reagieren nicht auf Wertaenderung /
        // Negativ-Richtung bleibt dauerhaft hervorgehoben" (DirectInput-Geraete): protokolliert nur die
        // Slots/Buttons, deren Rohwert sich seit dem letzten Tick tatsaechlich veraendert hat (Diff-Log),
        // damit sich beim schrittweisen Bewegen jeder einzelnen Achse bzw. Druecken jeder einzelnen Taste
        // exakt nachvollziehen laesst, welcher DeviceState.Axes[]-Slot reagiert (oder eben nicht). Bitte
        // nach Abschluss der Diagnose wieder entfernen.
        LogRawStateDiff(state);

        foreach (var group in InputGroups)
        {
            foreach (var row in group.AllRows)
            {
                row.UpdateActiveState(state);
            }
        }

        _axisGroup?.UpdateFromState(state);
    }

    /// <summary>TEMPORAERES DEBUG-LOGGING (siehe <see cref="OnLiveTimerTick"/>) - letzter protokollierter
    /// Rohzustand, um nur tatsaechliche Aenderungen zu loggen statt bei 30 Hz die komplette Konsole/Datei
    /// zuzumuellen. Bitte nach Abschluss der Diagnose zusammen mit LogRawStateDiff wieder entfernen.</summary>
    private float[]? _lastLoggedAxes;
    private bool[]? _lastLoggedButtons;
    private int _lastLoggedPov = int.MinValue;

    private void LogRawStateDiff(DeviceState state)
    {
        if (_lastLoggedAxes is null || _lastLoggedAxes.Length != state.Axes.Length)
        {
            _lastLoggedAxes = new float[state.Axes.Length];
            Array.Fill(_lastLoggedAxes, float.NaN);
        }

        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (MathF.Abs(state.Axes[i] - _lastLoggedAxes[i]) > 0.01f || (float.IsNaN(_lastLoggedAxes[i]) && state.Axes[i] != 0f))
            {
                DebugLog.Write($"[AxisDebug] Axes[{i}] ({(PhysicalAxisId)i}): {_lastLoggedAxes[i]:F3} -> {state.Axes[i]:F3}");
                _lastLoggedAxes[i] = state.Axes[i];
            }
        }

        if (_lastLoggedButtons is null || _lastLoggedButtons.Length != state.Buttons.Length)
        {
            _lastLoggedButtons = new bool[state.Buttons.Length];
        }

        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i] != _lastLoggedButtons[i])
            {
                DebugLog.Write($"[AxisDebug] Buttons[{i}]: {_lastLoggedButtons[i]} -> {state.Buttons[i]}");
                _lastLoggedButtons[i] = state.Buttons[i];
            }
        }

        if (state.PovDirectionDegrees != _lastLoggedPov)
        {
            DebugLog.Write($"[AxisDebug] PovDirectionDegrees: {_lastLoggedPov} -> {state.PovDirectionDegrees}");
            _lastLoggedPov = state.PovDirectionDegrees;
        }
    }

    public void Dispose() => StopLiveMonitoring();
}

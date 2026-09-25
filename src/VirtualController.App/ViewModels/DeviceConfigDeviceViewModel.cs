using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Benchmark;
using VirtualController.Core.Devices;
using VirtualController.Core.Logging;

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

    private DeviceStateLogger? _stateLogger;
    private BenchmarkSession? _benchmarkSession;

    /// <summary>Ob fuer dieses Geraet aktuell eine Zustands-Protokollierung (siehe <see cref="DeviceStateLogger"/>)
    /// laeuft - steuert den Beschriftungswechsel "Log starten"/"Log stoppen" des zugehoerigen Buttons
    /// (siehe DeviceConfigTemplates.xaml) und laeuft, anders als das Live-Polling (<see cref="StartLiveMonitoring"/>),
    /// unabhaengig von Tab-Sichtbarkeit/Auswahl weiter, bis der Nutzer explizit stoppt.</summary>
    [ObservableProperty]
    private bool _isLogging;

    /// <summary>Pfad der zuletzt geschriebenen bzw. aktuell laufenden Log-Datei, fuer eine Anzeige im UI
    /// (z.B. Tooltip des Log-Buttons) - null, solange noch nie geloggt wurde.</summary>
    [ObservableProperty]
    private string? _lastLogFilePath;

    /// <summary>Ob fuer dieses Geraet aktuell eine Hardware-Benchmark-Sitzung (siehe <see cref="BenchmarkSession"/>)
    /// laeuft - analog zu <see cref="IsLogging"/>, laeuft ebenso unabhaengig von Tab-Sichtbarkeit weiter,
    /// bis der Nutzer explizit stoppt (dann wird das Ergebnis als JSON exportiert, siehe <see cref="ToggleBenchmark"/>).</summary>
    [ObservableProperty]
    private bool _isBenchmarking;

    /// <summary>Pfad der zuletzt exportierten Benchmark-JSON-Datei, fuer eine Anzeige im UI (z.B. Tooltip
    /// des Benchmark-Buttons) - null, solange noch nie ein Benchmark abgeschlossen wurde.</summary>
    [ObservableProperty]
    private string? _lastBenchmarkFilePath;

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

    /// <summary>Startet bzw. stoppt die Zustands-Protokollierung dieses Geraets (siehe <see cref="DeviceStateLogger"/>) -
    /// bewusst unabhaengig von <see cref="IsSelected"/>/Tab-Sichtbarkeit, damit eine einmal gestartete
    /// Protokollierung auch beim Wechsel zu einem anderen Geraet/Tab weiterlaeuft, bis der Nutzer sie
    /// hier erneut stoppt.</summary>
    [RelayCommand]
    private void ToggleLogging()
    {
        if (IsLogging)
        {
            StopLogging();
            return;
        }

        if (!IsConnected)
        {
            System.Windows.MessageBox.Show(
                $"\"{DisplayName}\" ist aktuell nicht angeschlossen - Protokollierung kann erst nach dem Anschliessen gestartet werden.",
                "Protokollierung nicht moeglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            _stateLogger = new DeviceStateLogger(Device);
            _stateLogger.LogFailed += OnStateLoggerFailed;
            LastLogFilePath = _stateLogger.FilePath;
            _stateLogger.Start();
            IsLogging = true;
        }
        catch (Exception ex)
        {
            // Datei-/Ordnerzugriff kann fehlschlagen (z.B. fehlende Berechtigung) - dies darf die
            // restliche Konfiguration nicht beeintraechtigen, der Nutzer wird lediglich informiert.
            _stateLogger?.Dispose();
            _stateLogger = null;
            System.Windows.MessageBox.Show(
                $"Protokollierung fuer \"{DisplayName}\" konnte nicht gestartet werden:\n{ex.Message}",
                "Protokollierung fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>Wird auf dem Log-Hintergrund-Thread ausgeloest (siehe <see cref="DeviceStateLogger.LogFailed"/>),
    /// z.B. wenn das Geraet waehrend einer laufenden Protokollierung getrennt wird - wechselt daher per
    /// Dispatcher auf den UI-Thread, bevor <see cref="IsLogging"/> (ein gebundenes ViewModel-Property)
    /// veraendert wird.</summary>
    private void OnStateLoggerFailed(Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsLogging = false;
            System.Windows.MessageBox.Show(
                $"Protokollierung fuer \"{DisplayName}\" wurde wegen eines Fehlers beendet:\n{ex.Message}",
                "Protokollierung beendet", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }

    private void StopLogging()
    {
        if (_stateLogger is null)
        {
            IsLogging = false;
            return;
        }

        _stateLogger.LogFailed -= OnStateLoggerFailed;
        _stateLogger.Dispose();
        _stateLogger = null;
        IsLogging = false;
    }

    /// <summary>Oeffnet (bzw. aktiviert ein bereits offenes) Echtzeit-Anzeigefenster fuer den Hardware-Benchmark
    /// dieses Geraets (siehe <see cref="Views.BenchmarkWindow"/>). Das Fenster ist bewusst NICHT modal und
    /// besitzt keinen eigenen Lebenszyklus fuer die Sitzung selbst: Schliessen des Fensters stoppt einen
    /// laufenden Benchmark NICHT - die Sitzung laeuft, wie <see cref="IsBenchmarking"/> es bereits fuer den
    /// Inline-Button dokumentiert, unabhaengig von jeglicher UI-Sichtbarkeit weiter, bis der Nutzer explizit
    /// stoppt (per Button im Popup oder erneutem Aufruf von <see cref="ToggleBenchmarkCommand"/>).</summary>
    [RelayCommand]
    private void OpenBenchmarkWindow()
        => Views.BenchmarkWindow.ShowFor(this, System.Windows.Application.Current?.MainWindow);

    /// <summary>Liefert eine Momentaufnahme des bisherigen Benchmark-Ergebnisses waehrend eine Sitzung noch
    /// laeuft (siehe <see cref="BenchmarkSession.GetSnapshot"/>) - fuer die Echtzeit-Anzeige im Popup-Fenster
    /// (<see cref="Views.BenchmarkWindow"/>). Liefert null, solange <see cref="IsBenchmarking"/> false ist.</summary>
    public BenchmarkResult? GetLiveBenchmarkSnapshot() => _benchmarkSession?.GetSnapshot();

    /// <summary>Startet bzw. stoppt eine Hardware-Benchmark-Sitzung dieses Geraets (siehe <see cref="BenchmarkSession"/>) -
    /// analog zu <see cref="ToggleLogging"/> unabhaengig von <see cref="IsSelected"/>/Tab-Sichtbarkeit. Beim
    /// Stoppen wird das Ergebnis sofort als JSON exportiert (siehe <see cref="BenchmarkJsonExporter"/>), damit
    /// der Nutzer es nicht separat "speichern" muss.</summary>
    [RelayCommand]
    private void ToggleBenchmark()
    {
        if (IsBenchmarking)
        {
            StopBenchmark();
            return;
        }

        if (!IsConnected)
        {
            System.Windows.MessageBox.Show(
                $"\"{DisplayName}\" ist aktuell nicht angeschlossen - der Benchmark kann erst nach dem Anschliessen gestartet werden.",
                "Benchmark nicht moeglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            _benchmarkSession = BenchmarkSession.TryCreate(Device);
            if (_benchmarkSession is null)
            {
                System.Windows.MessageBox.Show(
                    $"Fuer \"{DisplayName}\" konnte kein zugehoeriges HID-Geraet ermittelt werden - der Hardware-Benchmark " +
                    "steht nur fuer Geraete mit erkennbarem HID-Pfad zur Verfuegung (z.B. nicht fuer manche reinen XInput-Geraete).",
                    "Benchmark nicht moeglich", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            _benchmarkSession.BenchmarkFailed += OnBenchmarkFailed;
            _benchmarkSession.Start();
            IsBenchmarking = true;
        }
        catch (Exception ex)
        {
            // Analog zu ToggleLogging: ein Fehlschlag beim Start (z.B. Geraet bereits exklusiv durch eine
            // andere Anwendung geoeffnet) darf die restliche Konfiguration nicht beeintraechtigen.
            _benchmarkSession?.Dispose();
            _benchmarkSession = null;
            System.Windows.MessageBox.Show(
                $"Benchmark fuer \"{DisplayName}\" konnte nicht gestartet werden:\n{ex.Message}",
                "Benchmark fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>Wird auf dem Benchmark-Hintergrund-Thread ausgeloest (siehe <see cref="BenchmarkSession.BenchmarkFailed"/>),
    /// z.B. wenn das Geraet waehrend einer laufenden Sitzung getrennt wird - wechselt daher per Dispatcher auf
    /// den UI-Thread, bevor <see cref="IsBenchmarking"/> (ein gebundenes ViewModel-Property) veraendert wird.
    /// Exportiert das bis dahin gesammelte (Teil-)Ergebnis trotzdem, statt es zu verwerfen.</summary>
    private void OnBenchmarkFailed(Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            FinishBenchmark();
            System.Windows.MessageBox.Show(
                $"Benchmark fuer \"{DisplayName}\" wurde wegen eines Fehlers beendet:\n{ex.Message}",
                "Benchmark beendet", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }

    private void StopBenchmark() => FinishBenchmark();

    /// <summary>Gemeinsame Beendigungslogik fuer regulaeres Stoppen (<see cref="StopBenchmark"/>) und den
    /// Fehlerfall (<see cref="OnBenchmarkFailed"/>): stoppt die Sitzung, exportiert das Ergebnis als JSON
    /// und gibt die Sitzung frei. Ein Exportfehler (z.B. fehlende Schreibrechte) wird dem Nutzer gemeldet,
    /// darf aber den restlichen Aufraeumvorgang nicht verhindern.</summary>
    private void FinishBenchmark()
    {
        if (_benchmarkSession is null)
        {
            IsBenchmarking = false;
            return;
        }

        _benchmarkSession.BenchmarkFailed -= OnBenchmarkFailed;

        try
        {
            var result = _benchmarkSession.Stop();
            var path = BenchmarkJsonExporter.BuildDefaultFilePath(Device);
            BenchmarkJsonExporter.Write(path, result);
            LastBenchmarkFilePath = path;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Benchmark-Ergebnis fuer \"{DisplayName}\" konnte nicht exportiert werden:\n{ex.Message}",
                "Export fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _benchmarkSession.Dispose();
            _benchmarkSession = null;
            IsBenchmarking = false;
        }
    }

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

        foreach (var group in InputGroups)
        {
            foreach (var row in group.AllRows)
            {
                row.UpdateActiveState(state);
            }
        }

        _axisGroup?.UpdateFromState(state);
    }

    public void Dispose()
    {
        StopLiveMonitoring();
        StopLogging();
        StopBenchmark();
    }
}

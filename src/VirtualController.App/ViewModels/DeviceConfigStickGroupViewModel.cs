using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Fasst die beiden zusammengehoerigen Achsen eines vollwertigen Sticks (z.B. "Linker Stick" = X+Y,
/// oder DirectInput "Stick (X/Y)") zu einem einzigen, optisch klar abgegrenzten Block zusammen: X- und
/// Y-Achse werden als zwei <see cref="DeviceConfigAxisPairViewModel"/> direkt untereinander dargestellt,
/// zusaetzlich gibt es ueber <see cref="Enabled"/> einen einzigen Schalter, der den kompletten Stick
/// (alle vier zugrunde liegenden Zeilen: X+/X-/Y+/Y-) auf einmal (de-)aktiviert - der Nutzer muss dafuer
/// nicht mehr jede der vier Einzelzeilen separat abhaken. Der Stick besitzt zudem einen vom Nutzer frei
/// vergebbaren, persistierten Namen (<see cref="Name"/>), eine eingebettete 2D-Live-Vorschau
/// (<see cref="Visualization"/>) sowie kombinierte Kalibrierungsbefehle, die X und Y gleichzeitig messen,
/// damit der Nutzer den Stick nur einmal bewegen muss statt jede Achse einzeln zu kalibrieren. Nur fuer
/// Achsen relevant, die tatsaechlich zu einem 2D-Stick gehoeren (identische Paarungslogik wie zuvor in
/// <see cref="AxisVisualizationFactory"/>); einzelne Achsen (Trigger, Schieberegler, Rotationsachsen ohne
/// Partner) bleiben als eigenstaendiger <see cref="DeviceConfigAxisPairViewModel"/> ohne umschliessenden
/// Stick-Block.
/// </summary>
public sealed partial class DeviceConfigStickGroupViewModel : ObservableObject, IDeviceConfigAxisItem
{
    private static readonly TimeSpan RangeCalibrationDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CenterGraceDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CenterSampleDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DeadzoneGraceDuration = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DeadzoneSampleDuration = TimeSpan.FromSeconds(2);

    private readonly PhysicalDeviceInfo _device;
    private readonly DeviceSettings _settings;
    private readonly Action _notifyChanged;
    private readonly string _defaultName;
    private readonly string _storageKey;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isCalibrating;

    [ObservableProperty]
    private string? _calibrationStatus;

    public DeviceConfigAxisPairViewModel XAxis { get; }

    public DeviceConfigAxisPairViewModel YAxis { get; }

    /// <summary>Eingebettete Live-Visualisierung des kompletten Sticks als quadratisches Koordinatenfeld
    /// (siehe <see cref="Views.Controls.Axis2DPadControl"/>), kombiniert aus den bereits vorhandenen
    /// Einzelachsen-Visualisierungen von <see cref="XAxis"/> und <see cref="YAxis"/>. Direkt in dieser
    /// Karte dargestellt statt in einem separaten globalen "Live-Vorschau"-Abschnitt.</summary>
    public Axis2DVisualizationViewModel? Visualization { get; private set; }

    public bool HasVisualization => Visualization is not null;

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => XAxis.AllRows.Concat(YAxis.AllRows);

    /// <summary>Tri-State-Ausleseweg fuer den gemeinsamen Master-Schalter im Kopfbereich: true, wenn beide
    /// Achsen (X und Y) vollstaendig aktiv sind, false, wenn beide vollstaendig deaktiviert sind, und null
    /// (unbestimmt), wenn sich die zugrunde liegenden Richtungen unterscheiden - z.B. wenn der Nutzer nur
    /// eine einzelne Richtung (etwa Y+) deaktiviert hat. Eine rein binaere Darstellung wuerde in diesem
    /// Mischfall faelschlich "kompletter Stick deaktiviert" suggerieren, obwohl alle anderen Richtungen
    /// unveraendert aktiv bleiben. Nur zum Anzeigen gedacht - das tatsaechliche Umschalten erfolgt
    /// ausschliesslich ueber <see cref="ToggleEnabledCommand"/>.</summary>
    public bool? EnabledState
    {
        get
        {
            var x = XAxis.EnabledState;
            var y = YAxis.EnabledState;
            if (x == true && y == true)
            {
                return true;
            }

            return x == false && y == false ? false : null;
        }
    }

    /// <summary>Setzt beim Anklicken der Master-Checkbox stets alle vier zugrunde liegenden Zeilen
    /// (X+/X-/Y+/Y-) explizit auf denselben Wert: liegt aktuell kein einheitlicher "alles aktiv"-Zustand
    /// vor (also false oder unbestimmt/gemischt), werden alle vier eingeschaltet; war der Stick zuvor
    /// bereits vollstaendig aktiv, werden alle vier ausgeschaltet.</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        var newValue = EnabledState != true;
        XAxis.Positive.Enabled = newValue;
        if (XAxis.Negative is not null)
        {
            XAxis.Negative.Enabled = newValue;
        }

        YAxis.Positive.Enabled = newValue;
        if (YAxis.Negative is not null)
        {
            YAxis.Negative.Enabled = newValue;
        }
    }

    /// <summary>Vereinfachter boolescher Ausleseweg fuer IsEnabled-Bindungen (Kalibrierungsblock,
    /// Visualisierung), die - anders als eine <see cref="System.Windows.Controls.CheckBox"/> - keinen
    /// Tri-State-Wert entgegennehmen koennen: true, solange mindestens eine Richtung aktiv ist (deckt auch
    /// den Mischfall ab), false nur, wenn der komplette Stick (alle vier Richtungen) deaktiviert ist.</summary>
    public bool IsAnyEnabled => EnabledState != false;

    /// <param name="name">Standard-Anzeigename (z.B. "Linker Stick"), Fallback solange der Nutzer keinen eigenen vergeben hat.</param>
    /// <param name="xAxis">X-Achse dieses Sticks; muss bereits ihre eingebettete <see cref="DeviceConfigAxisPairViewModel.Visualization"/> besitzen.</param>
    /// <param name="yAxis">Y-Achse dieses Sticks; muss bereits ihre eingebettete <see cref="DeviceConfigAxisPairViewModel.Visualization"/> besitzen.</param>
    /// <param name="device">Physisches Geraet, dem dieser Stick angehoert - fuer eigene Kalibrierungs-Reader.</param>
    /// <param name="settings">Geraeteweite Einstellungen, in denen <see cref="Name"/> persistiert wird (siehe <see cref="DeviceSettings.StickNames"/>).</param>
    /// <param name="notifyChanged">Callback, um das Gesamtprofil als geaendert zu markieren.</param>
    /// <param name="invertYForDisplay">stets true: positiver Y-Wert bedeutet Vorwaerts/Oben, einheitlich fuer XInput und DirectInput (der rohe Y-Wert wird bei DirectInput bereits in <see cref="DirectInputDeviceReader"/> an der Quelle negiert), siehe <see cref="GetStickAxisPairs"/>.</param>
    public DeviceConfigStickGroupViewModel(
        string name,
        DeviceConfigAxisPairViewModel xAxis,
        DeviceConfigAxisPairViewModel yAxis,
        PhysicalDeviceInfo device,
        DeviceSettings settings,
        Action notifyChanged,
        bool invertYForDisplay)
    {
        _device = device;
        _settings = settings;
        _notifyChanged = notifyChanged;
        _defaultName = name;
        _storageKey = PhysicalInputCatalog.BuildStickStorageKey(xAxis.Positive.Ref.Index);

        XAxis = xAxis;
        YAxis = yAxis;
        _name = settings.StickNames.TryGetValue(_storageKey, out var customName) && !string.IsNullOrWhiteSpace(customName)
            ? customName
            : name;

        XAxis.PropertyChanged += OnAxisPropertyChanged;
        YAxis.PropertyChanged += OnAxisPropertyChanged;

        // Der Stick bietet oben im Kopfbereich bereits einen einzigen kombinierten Enable/Disable-
        // Schalter fuer beide Achsen zugleich an - der achseneigene Master-Schalter jeder einzelnen
        // DeviceConfigAxisPairViewModel-Karte waere hier redundant und wird deshalb ausgeblendet.
        XAxis.SuppressMasterToggle = true;
        YAxis.SuppressMasterToggle = true;

        if (XAxis.Visualization is not null && YAxis.Visualization is not null)
        {
            // Beide Einzelachsen-Visualisierungen werden zu einem gemeinsamen 2D-Pad kombiniert; die
            // eigenen (Einzelachsen-)Anzeigen von XAxis/YAxis werden dafuer ausgeblendet, damit der Wert
            // nicht doppelt (einmal als Balken, einmal als Punkt im Koordinatenfeld) dargestellt wird.
            Visualization = new Axis2DVisualizationViewModel(string.Empty, XAxis.Visualization, YAxis.Visualization, invertYForDisplay);
            XAxis.SuppressOwnVisualizationDisplay = true;
            YAxis.SuppressOwnVisualizationDisplay = true;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == _defaultName)
        {
            _settings.StickNames.Remove(_storageKey);
        }
        else
        {
            _settings.StickNames[_storageKey] = value;
        }

        _notifyChanged();
    }

    public void UpdateVisualization(DeviceState state) => Visualization?.UpdateFromState(state);

    public void ResetVisualization() => Visualization?.Reset();

    private void OnAxisPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceConfigAxisPairViewModel.EnabledState))
        {
            OnPropertyChanged(nameof(EnabledState));
            OnPropertyChanged(nameof(IsAnyEnabled));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateRangeAsync()
    {
        await RunCalibrationAsync("Bewege den Stick jetzt mehrmals in alle Richtungen bis zu allen Anschlaegen...", async reader =>
        {
            var samples = await AxisCalibrationService.SampleRangeAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index }, RangeCalibrationDuration).ConfigureAwait(true);
            XAxis.Positive.CalibratedMin = samples[0].Min;
            XAxis.Positive.CalibratedMax = samples[0].Max;
            YAxis.Positive.CalibratedMin = samples[1].Min;
            YAxis.Positive.CalibratedMax = samples[1].Max;
            return $"Bereich kalibriert: X Min={samples[0].Min:F2}/Max={samples[0].Max:F2}, Y Min={samples[1].Min:F2}/Max={samples[1].Max:F2}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task SetCenterAsync()
    {
        await RunCalibrationAsync("Stick jetzt loslassen (Ruheposition wird gemessen)...", async reader =>
        {
            await Task.Delay(CenterGraceDuration).ConfigureAwait(true);
            var centers = await AxisCalibrationService.SampleCenterAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index }, CenterSampleDuration).ConfigureAwait(true);
            XAxis.Positive.CalibratedCenter = centers[0];
            YAxis.Positive.CalibratedCenter = centers[1];
            return $"Zentrum gesetzt: X={centers[0]:F3}, Y={centers[1]:F3}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateDeadzoneAsync()
    {
        await RunCalibrationAsync("Stick jetzt loslassen (Stickdrift wird gemessen)...", async reader =>
        {
            var deadzones = await AxisCalibrationService.SampleDeadzoneAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index },
                DeadzoneGraceDuration, DeadzoneSampleDuration).ConfigureAwait(true);
            XAxis.Positive.Deadzone = deadzones[0];
            YAxis.Positive.Deadzone = deadzones[1];
            return $"Deadzone kalibriert: X={deadzones[0]:F3}, Y={deadzones[1]:F3}";
        });
    }

    [RelayCommand]
    private void ResetCalibration()
    {
        XAxis.Positive.CalibratedMin = null;
        XAxis.Positive.CalibratedMax = null;
        XAxis.Positive.CalibratedCenter = null;
        YAxis.Positive.CalibratedMin = null;
        YAxis.Positive.CalibratedMax = null;
        YAxis.Positive.CalibratedCenter = null;
        CalibrationStatus = "Kalibrierung zurueckgesetzt.";
    }

    private bool CanCalibrate() => !IsCalibrating;

    partial void OnIsCalibratingChanged(bool value)
    {
        CalibrateRangeCommand.NotifyCanExecuteChanged();
        SetCenterCommand.NotifyCanExecuteChanged();
        CalibrateDeadzoneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Kapselt das gemeinsame Muster aller kombinierten Kalibrierungsaktionen: Status setzen,
    /// kurzlebigen Reader oeffnen, Messung durchfuehren, Reader wieder schliessen, Status/Fehler anzeigen -
    /// analog zu <see cref="DeviceConfigInputRowViewModel"/>, jedoch fuer X und Y gleichzeitig.</summary>
    private async Task RunCalibrationAsync(string startStatus, Func<IDeviceReader, Task<string>> action)
    {
        IsCalibrating = true;
        CalibrationStatus = startStatus;

        try
        {
            using var reader = DeviceEnumerator.OpenReader(_device);
            CalibrationStatus = await action(reader).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CalibrationStatus = $"Kalibrierung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsCalibrating = false;
        }
    }
}

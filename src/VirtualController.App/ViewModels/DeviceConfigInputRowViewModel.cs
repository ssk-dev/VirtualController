using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Eine einzelne Zeile im Geraete-Konfigurationsdialog: repraesentiert eine physische Eingabe
/// (Button, Achsen-Richtung, Slider, D-Pad-Richtung) eines Geraets mit ihrem aktuellen Namen und
/// Enable/Disable-Zustand. Schreibt Aenderungen direkt in die zugrunde liegende <see cref="InputSettings"/>,
/// die Teil des insgesamt gespeicherten Profils ist. Bei Achsen zusaetzlich: Bereichskalibrierung
/// (Min/Max/Zentrum), automatische Deadzone-Kalibrierung (Stickdrift-Messung) und Antwortkurve.
/// </summary>
public sealed partial class DeviceConfigInputRowViewModel : ObservableObject
{
    /// <summary>Ab diesem absoluten Achsenausschlag gilt eine Achsen-Richtung als "aktiv" (analog zu
    /// <see cref="PhysicalInputRowViewModel"/>, fuer eine reaktionsfreudige Live-Anzeige in diesem Dialog).</summary>
    private const float AxisActiveThreshold = 0.3f;

    private static readonly TimeSpan RangeCalibrationDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CenterGraceDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CenterSampleDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DeadzoneGraceDuration = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DeadzoneSampleDuration = TimeSpan.FromSeconds(2);

    public PhysicalInputRef Ref { get; }

    /// <summary>Zugrunde liegende Einstellungen dieser Eingabe (Kalibrierung/Deadzone/Kurve/Enabled).
    /// Oeffentlich zugaenglich, damit z.B. <see cref="DeviceConfigAxisPairViewModel"/> daraus die
    /// eingebettete Live-Visualisierung (<see cref="AxisVisualizationViewModel"/>) mit derselben
    /// Einstellungsinstanz aufbauen kann, statt eine zweite, unabhaengige Kopie zu verwalten.</summary>
    public InputSettings Settings => _settings;

    /// <summary>Fuer ComboBox-Bindings in der View.</summary>
    public static IReadOnlyList<AxisCurveType> CurveTypeOptions { get; } = Enum.GetValues<AxisCurveType>();

    private readonly PhysicalDeviceInfo _device;
    private readonly InputSettings _settings;
    private readonly Action _notifyChanged;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private float? _calibratedMin;

    [ObservableProperty]
    private float? _calibratedMax;

    [ObservableProperty]
    private float? _calibratedCenter;

    [ObservableProperty]
    private float _deadzone;

    [ObservableProperty]
    private AxisCurveType _curveType;

    [ObservableProperty]
    private float _curveStrength;

    [ObservableProperty]
    private bool _isCalibrating;

    [ObservableProperty]
    private string? _calibrationStatus;

    /// <summary>Ob diese physische Eingabe aktuell aktiv ist (Live-Hervorhebung, analog zu
    /// <see cref="PhysicalInputRowViewModel.IsActive"/>), waehrend das zugehoerige Geraet in diesem
    /// Dialog aufgeklappt ist. Wird per <see cref="UpdateActiveState"/> vom Live-Polling der
    /// uebergeordneten <see cref="DeviceConfigDeviceViewModel"/> aktualisiert.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Aktualisiert den Aktiv-Status anhand des aktuellen Geraetezustands, fuer die Live-Hervorhebung in der UI.</summary>
    public void UpdateActiveState(DeviceState state)
    {
        IsActive = Ref.Kind switch
        {
            PhysicalInputKind.Button => Ref.Index < state.Buttons.Length && state.Buttons[Ref.Index],
            PhysicalInputKind.AxisPositive => state.GetAxisRaw(Ref.Index) >= AxisActiveThreshold,
            PhysicalInputKind.AxisNegative => state.GetAxisRaw(Ref.Index) <= -AxisActiveThreshold,
            PhysicalInputKind.DPad => state.PovDirectionDegrees >= 0,
            PhysicalInputKind.DPadUp => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasUp(),
            PhysicalInputKind.DPadDown => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasDown(),
            PhysicalInputKind.DPadLeft => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasLeft(),
            PhysicalInputKind.DPadRight => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasRight(),
            _ => false
        };
    }

    /// <summary>true fuer beide Achsen-Richtungseintraege (AxisPositive/AxisNegative), false fuer Button/DPad.</summary>
    public bool IsAxis => Ref.Kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;

    /// <summary>
    /// Eine physische Achse besitzt nur eine Kalibrierung/Deadzone/Kurve, obwohl sie im Katalog als
    /// zwei getrennte Eintraege (je einer pro Ausschlagsrichtung fuer die Mapping-Zuordnung) auftritt.
    /// <see cref="Mapping.MappingEngine"/> liest diese Einstellungen stets ueber den kanonischen
    /// AxisPositive-Schluessel derselben Achsen-Nummer - Kalibrierungs-/Kurven-Steuerelemente werden
    /// deshalb nur auf dem AxisPositive-Eintrag angezeigt, damit der Nutzer nicht versehentlich auf dem
    /// wirkungslosen AxisNegative-Eintrag Einstellungen vornimmt.
    /// </summary>
    public bool IsCalibratable => Ref.Kind == PhysicalInputKind.AxisPositive;

    public DeviceConfigInputRowViewModel(PhysicalDeviceInfo device, PhysicalInputRef inputRef, InputSettings settings, Action notifyChanged)
    {
        _device = device;
        Ref = inputRef;
        _settings = settings;
        _notifyChanged = notifyChanged;
        _name = settings.CustomName ?? inputRef.DisplayName;
        _enabled = settings.Enabled;
        _calibratedMin = settings.CalibratedMin;
        _calibratedMax = settings.CalibratedMax;
        _calibratedCenter = settings.CalibratedCenter;
        _deadzone = settings.Deadzone;
        _curveType = settings.CurveType;
        _curveStrength = settings.CurveStrength;
    }

    partial void OnNameChanged(string value)
    {
        _settings.CustomName = string.IsNullOrWhiteSpace(value) ? null : value;
        _notifyChanged();
    }

    partial void OnEnabledChanged(bool value)
    {
        _settings.Enabled = value;
        _notifyChanged();
    }

    partial void OnCalibratedMinChanged(float? value)
    {
        _settings.CalibratedMin = value;
        _notifyChanged();
    }

    partial void OnCalibratedMaxChanged(float? value)
    {
        _settings.CalibratedMax = value;
        _notifyChanged();
    }

    partial void OnCalibratedCenterChanged(float? value)
    {
        _settings.CalibratedCenter = value;
        _notifyChanged();
    }

    partial void OnDeadzoneChanged(float value)
    {
        _settings.Deadzone = value;
        _notifyChanged();
    }

    partial void OnCurveTypeChanged(AxisCurveType value)
    {
        _settings.CurveType = value;
        _notifyChanged();
    }

    partial void OnCurveStrengthChanged(float value)
    {
        _settings.CurveStrength = value;
        _notifyChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateRangeAsync()
    {
        await RunCalibrationAsync("Bewege den Stick/die Achse jetzt mehrmals bis zu beiden Anschlaegen...", async reader =>
        {
            var sample = await AxisCalibrationService.SampleRangeAsync(reader, Ref.Index, RangeCalibrationDuration).ConfigureAwait(true);
            CalibratedMin = sample.Min;
            CalibratedMax = sample.Max;
            return $"Bereich kalibriert: Min={sample.Min:F2}, Max={sample.Max:F2}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task SetCenterAsync()
    {
        await RunCalibrationAsync("Achse jetzt loslassen (Ruheposition wird gemessen)...", async reader =>
        {
            await Task.Delay(CenterGraceDuration).ConfigureAwait(true);
            float center = await AxisCalibrationService.SampleCenterAsync(reader, Ref.Index, CenterSampleDuration).ConfigureAwait(true);
            CalibratedCenter = center;
            return $"Zentrum gesetzt: {center:F3}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateDeadzoneAsync()
    {
        await RunCalibrationAsync("Achse jetzt loslassen (Stickdrift wird gemessen)...", async reader =>
        {
            float deadzone = await AxisCalibrationService.SampleDeadzoneAsync(
                reader, Ref.Index, DeadzoneGraceDuration, DeadzoneSampleDuration).ConfigureAwait(true);
            Deadzone = deadzone;
            return $"Deadzone kalibriert: {deadzone:F3}";
        });
    }

    [RelayCommand]
    private void ResetCalibration()
    {
        CalibratedMin = null;
        CalibratedMax = null;
        CalibratedCenter = null;
        CalibrationStatus = "Kalibrierung zurueckgesetzt.";
    }

    private bool CanCalibrate() => !IsCalibrating;

    partial void OnIsCalibratingChanged(bool value)
    {
        CalibrateRangeCommand.NotifyCanExecuteChanged();
        SetCenterCommand.NotifyCanExecuteChanged();
        CalibrateDeadzoneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Kapselt das gemeinsame Muster aller Kalibrierungsaktionen: Status setzen, kurzlebigen
    /// Reader oeffnen, Messung durchfuehren, Reader wieder schliessen, Status/Fehler anzeigen.</summary>
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

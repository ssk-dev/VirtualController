using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// One row in the device configuration dialog, representing a physical input (button, axis direction, slider,
/// or D-pad direction) with its current name and enabled state. Writes changes directly to the underlying
/// <see cref="InputSettings"/>, which is part of the saved profile. Axis rows also provide range calibration
/// (min/max/center), automatic deadzone calibration (stick drift measurement), and response curves.
/// </summary>
public sealed partial class DeviceConfigInputRowViewModel : ObservableObject
{
    /// <summary>An axis direction is considered active at this absolute deflection (matching
    /// <see cref="PhysicalInputRowViewModel"/>) for responsive live feedback in this dialog.</summary>
    private const float AxisActiveThreshold = 0.3f;

    private static readonly TimeSpan RangeCalibrationDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CenterGraceDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CenterSampleDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DeadzoneGraceDuration = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DeadzoneSampleDuration = TimeSpan.FromSeconds(2);

    public PhysicalInputRef Ref { get; }

    /// <summary>Underlying settings for this input (calibration, deadzone, curve, and enabled state). Publicly
    /// accessible so <see cref="DeviceConfigAxisPairViewModel"/>, for example, can build its embedded live
    /// visualization (<see cref="AxisVisualizationViewModel"/>) from the same settings instance rather than
    /// maintaining a separate copy.</summary>
    public InputSettings Settings => _settings;

    /// <summary>Options for ComboBox bindings in the view.</summary>
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

    /// <summary>Whether this physical input is currently active (live highlight, like
    /// <see cref="PhysicalInputRowViewModel.IsActive"/>) while its device is expanded in this dialog.
    /// Updated by <see cref="UpdateActiveState"/> from the parent
    /// <see cref="DeviceConfigDeviceViewModel"/>'s live polling.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Updates the active state from the current device state for live highlighting in the UI.</summary>
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

    /// <summary>True for both axis direction entries (AxisPositive/AxisNegative); false for buttons and D-pad inputs.</summary>
    public bool IsAxis => Ref.Kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;

    /// <summary>
    /// A physical axis has one calibration, deadzone, and curve even though the catalog exposes two entries,
    /// one for each deflection direction used by mapping. <see cref="Mapping.MappingEngine"/> always reads these
    /// settings through the canonical AxisPositive key for that axis index, so calibration and curve controls
    /// appear only on the AxisPositive entry and cannot be changed through the ineffective AxisNegative entry.
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
        await RunCalibrationAsync("Move the stick/axis to both limits several times...", async reader =>
        {
            var sample = await AxisCalibrationService.SampleRangeAsync(reader, Ref.Index, RangeCalibrationDuration).ConfigureAwait(true);
            CalibratedMin = sample.Min;
            CalibratedMax = sample.Max;
            return $"Range calibrated: min={sample.Min:F2}, max={sample.Max:F2}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task SetCenterAsync()
    {
        await RunCalibrationAsync("Release the axis (measuring its resting position)...", async reader =>
        {
            await Task.Delay(CenterGraceDuration).ConfigureAwait(true);
            float center = await AxisCalibrationService.SampleCenterAsync(reader, Ref.Index, CenterSampleDuration).ConfigureAwait(true);
            CalibratedCenter = center;
            return $"Center set: {center:F3}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateDeadzoneAsync()
    {
        await RunCalibrationAsync("Release the axis (measuring stick drift)...", async reader =>
        {
            float deadzone = await AxisCalibrationService.SampleDeadzoneAsync(
                reader, Ref.Index, DeadzoneGraceDuration, DeadzoneSampleDuration).ConfigureAwait(true);
            Deadzone = deadzone;
            return $"Deadzone calibrated: {deadzone:F3}";
        });
    }

    [RelayCommand]
    private void ResetCalibration()
    {
        CalibratedMin = null;
        CalibratedMax = null;
        CalibratedCenter = null;
        CalibrationStatus = "Calibration reset.";
    }

    private bool CanCalibrate() => !IsCalibrating;

    partial void OnIsCalibratingChanged(bool value)
    {
        CalibrateRangeCommand.NotifyCanExecuteChanged();
        SetCenterCommand.NotifyCanExecuteChanged();
        CalibrateDeadzoneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shared flow for calibration actions: set status, open a short-lived reader, perform the
    /// measurement, dispose the reader, and show the result or error.</summary>
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
            CalibrationStatus = $"Calibration failed: {ex.Message}";
        }
        finally
        {
            IsCalibrating = false;
        }
    }
}

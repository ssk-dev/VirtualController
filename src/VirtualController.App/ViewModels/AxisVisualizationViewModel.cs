using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Display-only model of the current live input value for one physical axis in the generic device
/// configuration visualization (see <see cref="Views.Controls.AxisGaugeControl"/>). It does not generate
/// or modify controller values. Calibration (<see cref="AxisSignalProcessor.Calibrate"/>) reads existing
/// <see cref="InputSettings"/> and the most recently polled <see cref="DeviceState"/>; the deadzone display
/// simply reflects the configured value.
/// </summary>
public sealed partial class AxisVisualizationViewModel : ObservableObject, IAxisVisualizationItem
{
    private readonly InputSettings _settings;
    private readonly int _axisSlotIndex;
    private readonly bool _bidirectional;

    /// <summary>Axis display name without a direction suffix, since this visualization represents both
    /// directions of the physical axis as one value.</summary>
    public string Name { get; }

    /// <summary>Lower bound of this axis's full range: -1.0 for centered sticks/rotation axes, 0.0 for
    /// unidirectional triggers/sliders.</summary>
    public float MinValue => _bidirectional ? -1f : 0f;

    /// <summary>Upper bound of this axis's full range (always 1.0).</summary>
    public float MaxValue => 1f;

    /// <summary>True for centered sticks/rotation axes (-1.0 .. 1.0, with zero in the middle); false for
    /// unidirectional triggers/sliders (0.0 .. 1.0, with zero at the edge). Controls whether the view shows
    /// an additional center marker.</summary>
    public bool IsBidirectional => _bidirectional;

    /// <summary>Current calibrated live value (see <see cref="AxisSignalProcessor.Calibrate"/>), deliberately
    /// without applying the deadzone or curve so the marker moves smoothly through the deadzone, which is
    /// visualized separately.</summary>
    [ObservableProperty]
    private float _value;

    /// <summary>Currently configured device-wide deadzone for this axis (see <see cref="InputSettings.Deadzone"/>).</summary>
    [ObservableProperty]
    private float _deadzone;

    /// <summary>True when the current value leaves the configured deadzone. Used by
    /// <see cref="Views.Controls.AxisGaugeControl"/> to briefly flash on the transition from inside to outside.</summary>
    [ObservableProperty]
    private bool _isOutsideDeadzone;

    /// <param name="name">Display name without a direction suffix.</param>
    /// <param name="settings">Device-wide calibration/deadzone settings for the canonical AxisPositive input.</param>
    /// <param name="axisSlotIndex">Generic axis slot index (see <see cref="PhysicalAxisId"/>) used to read <see cref="DeviceState.Axes"/>.</param>
    /// <param name="bidirectional">True for centered sticks/rotation axes (-1.0 .. 1.0); false for triggers/sliders (0.0 .. 1.0).</param>
    public AxisVisualizationViewModel(string name, InputSettings settings, int axisSlotIndex, bool bidirectional)
    {
        Name = name;
        _settings = settings;
        _axisSlotIndex = axisSlotIndex;
        _bidirectional = bidirectional;
        _deadzone = settings.Deadzone;
    }

    private float Range => MaxValue - MinValue;

    private static double Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    /// <summary>Current value's position as a fraction of the full axis width (0.0 = <see cref="MinValue"/>,
    /// 1.0 = <see cref="MaxValue"/>). Used as the star weight for the column before the marker so its position
    /// remains proportional regardless of the control's pixel width.</summary>
    public double MarkerFraction => Clamp01((Value - MinValue) / Range);

    /// <summary>Remaining fraction after the marker (1.0 - <see cref="MarkerFraction"/>), used as the star
    /// weight for the third (right) column.</summary>
    public double AfterMarkerFraction => 1.0 - MarkerFraction;

    /// <summary>Fractional position where the deadzone band begins (left edge), accounting for the full range.
    /// For example, a unidirectional trigger spans 0..1, so its deadzone cannot extend below zero.</summary>
    public double DeadzoneStartFraction => Clamp01((MathF.Max(MinValue, -Deadzone) - MinValue) / Range);

    /// <summary>Fractional position where the deadzone band ends (right edge).</summary>
    private double DeadzoneEndFraction => Clamp01((MathF.Min(MaxValue, Deadzone) - MinValue) / Range);

    /// <summary>Deadzone band width as a fraction of the full axis width, growing in proportion to the
    /// configured deadzone value.</summary>
    public double DeadzoneWidthFraction => Math.Max(0.0, DeadzoneEndFraction - DeadzoneStartFraction);

    /// <summary>Remaining fraction after the deadzone band, used as the star weight for the third column.</summary>
    public double AfterDeadzoneFraction => 1.0 - DeadzoneStartFraction - DeadzoneWidthFraction;

    partial void OnValueChanged(float value)
    {
        OnPropertyChanged(nameof(MarkerFraction));
        OnPropertyChanged(nameof(AfterMarkerFraction));
    }

    partial void OnDeadzoneChanged(float value)
    {
        OnPropertyChanged(nameof(DeadzoneStartFraction));
        OnPropertyChanged(nameof(DeadzoneWidthFraction));
        OnPropertyChanged(nameof(AfterDeadzoneFraction));
    }

    /// <summary>Updates the value and deadzone state from the most recently polled device state. Called by
    /// the parent <see cref="DeviceConfigDeviceViewModel"/>'s live polling.</summary>
    public void UpdateFromState(DeviceState state)
    {
        float raw = state.GetAxisRaw(_axisSlotIndex);
        Value = AxisSignalProcessor.Calibrate(raw, _settings, _bidirectional);
        // Read the deadzone from settings on each tick so changes made while the dialog is open appear immediately.
        Deadzone = _settings.Deadzone;
        IsOutsideDeadzone = MathF.Abs(Value) > Deadzone;
    }

    /// <summary>Resets the display to its resting state when the device is collapsed in the configuration
    /// dialog or disconnected, preventing a stale position from remaining visible.</summary>
    public void Reset()
    {
        Value = 0f;
        IsOutsideDeadzone = false;
    }
}

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Groups the two related axes of a complete stick (e.g. "Left stick" = X+Y, or DirectInput "Stick (X/Y)")
/// into one visually distinct block. The X and Y axes appear as two <see cref="DeviceConfigAxisPairViewModel"/>
/// instances, with one <see cref="Enabled"/> switch for all four underlying rows (X+/X-/Y+/Y-). The stick
/// also has a user-editable, persisted name (<see cref="Name"/>), an embedded 2D live preview
/// (<see cref="Visualization"/>), and combined calibration commands that measure X and Y simultaneously.
/// Applies only to axes that form a 2D stick, using the same pairing logic as
/// <see cref="AxisVisualizationFactory"/>. Standalone axes (triggers, sliders, or unpaired rotation axes)
/// remain individual <see cref="DeviceConfigAxisPairViewModel"/> instances.
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

    /// <summary>Embedded live visualization of the complete stick as a square coordinate field
    /// (see <see cref="Views.Controls.Axis2DPadControl"/>), combining the existing individual axis
    /// visualizations from <see cref="XAxis"/> and <see cref="YAxis"/>. Displayed in this card rather
    /// than a separate global preview section.</summary>
    public Axis2DVisualizationViewModel? Visualization { get; private set; }

    public bool HasVisualization => Visualization is not null;

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => XAxis.AllRows.Concat(YAxis.AllRows);

    /// <summary>Tri-state value for the shared master switch: true when both X and Y axes are fully enabled,
    /// false when both are disabled, and null when their directions differ (e.g. only Y+ was disabled). A
    /// binary value would incorrectly suggest that the whole stick is disabled in this mixed state. This is
    /// for display only; toggling is handled exclusively by <see cref="ToggleEnabledCommand"/>.</summary>
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

    /// <summary>Sets all four underlying rows (X+/X-/Y+/Y-) to the same value when the master checkbox is
    /// clicked. If they are not all enabled (false or mixed), enables all four; otherwise disables all four.</summary>
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

    /// <summary>Simplified boolean value for IsEnabled bindings (calibration and visualization), which cannot
    /// accept a tri-state value like a <see cref="System.Windows.Controls.CheckBox"/>: true while any direction
    /// is enabled, false only when all four stick directions are disabled.</summary>
    public bool IsAnyEnabled => EnabledState != false;

    /// <param name="name">Default display name (e.g. "Left stick"), used until the user provides a custom name.</param>
    /// <param name="xAxis">X axis of this stick, which must already have its embedded <see cref="DeviceConfigAxisPairViewModel.Visualization"/>.</param>
    /// <param name="yAxis">Y axis of this stick, which must already have its embedded <see cref="DeviceConfigAxisPairViewModel.Visualization"/>.</param>
    /// <param name="device">Physical device that owns this stick, used for dedicated calibration readers.</param>
    /// <param name="settings">Device settings where <see cref="Name"/> is persisted (see <see cref="DeviceSettings.StickNames"/>).</param>
    /// <param name="notifyChanged">Callback that marks the overall profile as changed.</param>
    /// <param name="invertYForDisplay">Always true: positive Y means forward/up for both XInput and DirectInput. DirectInput negates the raw Y value in <see cref="DirectInputDeviceReader"/> (see <see cref="GetStickAxisPairs"/>).</param>
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

        // The stick already has one combined enable/disable switch in its header, so hide the redundant
        // master switch on each individual DeviceConfigAxisPairViewModel card.
        XAxis.SuppressMasterToggle = true;
        YAxis.SuppressMasterToggle = true;

        if (XAxis.Visualization is not null && YAxis.Visualization is not null)
        {
            // Combine both axis visualizations into one 2D pad and hide the individual X/Y displays to avoid
            // showing each value twice (as both a bar and a point in the coordinate field).
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
        await RunCalibrationAsync("Move the stick in every direction to each limit several times...", async reader =>
        {
            var samples = await AxisCalibrationService.SampleRangeAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index }, RangeCalibrationDuration).ConfigureAwait(true);
            XAxis.Positive.CalibratedMin = samples[0].Min;
            XAxis.Positive.CalibratedMax = samples[0].Max;
            YAxis.Positive.CalibratedMin = samples[1].Min;
            YAxis.Positive.CalibratedMax = samples[1].Max;
            return $"Range calibrated: X min={samples[0].Min:F2}/max={samples[0].Max:F2}, Y min={samples[1].Min:F2}/max={samples[1].Max:F2}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task SetCenterAsync()
    {
        await RunCalibrationAsync("Release the stick (measuring its resting position)...", async reader =>
        {
            await Task.Delay(CenterGraceDuration).ConfigureAwait(true);
            var centers = await AxisCalibrationService.SampleCenterAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index }, CenterSampleDuration).ConfigureAwait(true);
            XAxis.Positive.CalibratedCenter = centers[0];
            YAxis.Positive.CalibratedCenter = centers[1];
            return $"Center set: X={centers[0]:F3}, Y={centers[1]:F3}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateDeadzoneAsync()
    {
        await RunCalibrationAsync("Release the stick (measuring stick drift)...", async reader =>
        {
            var deadzones = await AxisCalibrationService.SampleDeadzoneAsync(
                reader, new[] { XAxis.Positive.Ref.Index, YAxis.Positive.Ref.Index },
                DeadzoneGraceDuration, DeadzoneSampleDuration).ConfigureAwait(true);
            XAxis.Positive.Deadzone = deadzones[0];
            YAxis.Positive.Deadzone = deadzones[1];
            return $"Deadzone calibrated: X={deadzones[0]:F3}, Y={deadzones[1]:F3}";
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
        CalibrationStatus = "Calibration reset.";
    }

    private bool CanCalibrate() => !IsCalibrating;

    partial void OnIsCalibratingChanged(bool value)
    {
        CalibrateRangeCommand.NotifyCanExecuteChanged();
        SetCenterCommand.NotifyCanExecuteChanged();
        CalibrateDeadzoneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shared flow for combined calibration actions: set status, open a short-lived reader, perform
    /// the measurement, dispose the reader, and show the result or error. Like
    /// <see cref="DeviceConfigInputRowViewModel"/>, but measures X and Y together.</summary>
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

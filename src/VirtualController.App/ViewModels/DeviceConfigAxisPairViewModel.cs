using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// A related positive and negative entry for the same physical axis. The view displays both entries and their
/// calibration controls (shown only on the positive entry; see <see cref="DeviceConfigInputRowViewModel.IsCalibratable"/>)
/// together in a shared frame instead of as separate rows. <see cref="Enabled"/> also provides a shared
/// enable/disable switch for both directions so users do not have to toggle each direction individually.
/// </summary>
public sealed partial class DeviceConfigAxisPairViewModel : ObservableObject, IDeviceConfigAxisItem
{
    private DeviceConfigInputRowViewModel _positive = null!;
    private DeviceConfigInputRowViewModel? _negative;

    /// <summary>Canonical axis entry used for mapping; always present.</summary>
    public DeviceConfigInputRowViewModel Positive
    {
        get => _positive;
        internal set
        {
            if (_positive is not null)
            {
                _positive.PropertyChanged -= OnRowPropertyChanged;
            }

            _positive = value;
            _positive.PropertyChanged += OnRowPropertyChanged;
        }
    }

    /// <summary>Opposite-direction entry for the same axis. Unidirectional physical axes (triggers and sliders)
    /// have no negative entry, so the view hides that row.</summary>
    public DeviceConfigInputRowViewModel? Negative
    {
        get => _negative;
        internal set
        {
            if (_negative is not null)
            {
                _negative.PropertyChanged -= OnRowPropertyChanged;
            }

            _negative = value;

            if (_negative is not null)
            {
                _negative.PropertyChanged += OnRowPropertyChanged;
            }

            OnPropertyChanged(nameof(HasNegative));
        }
    }

    /// <summary>True when this axis has both positive and negative entries (centered sticks/rotation axes);
    /// false for unidirectional triggers and sliders.</summary>
    public bool HasNegative => Negative is not null;

    /// <summary>Embedded live visualization of this axis (a horizontal slider; see
    /// <see cref="Views.Controls.AxisGaugeControl"/>), displayed in this card rather than a separate global
    /// preview section. Null until <see cref="BuildVisualization"/> is called, which requires the final
    /// <see cref="Negative"/> entry to determine the bidirectional range.</summary>
    public AxisVisualizationViewModel? Visualization { get; private set; }

    /// <summary>True when this axis belongs to a combined stick (see <see cref="DeviceConfigStickGroupViewModel"/>)
    /// whose 2D pad already displays both axis values. Hides the redundant individual bar while keeping
    /// <see cref="Visualization"/> alive and updated because it supplies raw data to the 2D pad.</summary>
    public bool SuppressOwnVisualizationDisplay { get; set; }

    public bool HasVisualization => Visualization is not null && !SuppressOwnVisualizationDisplay;

    /// <summary>True when this axis belongs to a combined stick (see <see cref="DeviceConfigStickGroupViewModel"/>),
    /// which already provides one switch for both axes; the axis-level master switch would be redundant.
    /// Standalone axes (triggers, sliders, and unpaired rotation axes) keep <see cref="ShowMasterToggle"/> enabled
    /// so the whole axis can be toggled at once.</summary>
    public bool SuppressMasterToggle { get; set; }

    public bool ShowMasterToggle => !SuppressMasterToggle;

    /// <summary>Creates the embedded live visualization from the positive entry's existing
    /// <see cref="InputSettings"/>. <see cref="DeviceConfigDeviceViewModel"/> calls this only after
    /// <see cref="Positive"/> and any <see cref="Negative"/> entry are assigned, so <see cref="HasNegative"/>
    /// provides the correct unidirectional or bidirectional range. The name is intentionally empty because
    /// the card already displays the editable name in its header.</summary>
    public void BuildVisualization()
    {
        Visualization = new AxisVisualizationViewModel(string.Empty, Positive.Settings, Positive.Ref.Index, HasNegative);
        OnPropertyChanged(nameof(HasVisualization));
    }

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows
        => Negative is null ? new[] { Positive } : new[] { Positive, Negative };

    public void UpdateVisualization(DeviceState state) => Visualization?.UpdateFromState(state);

    public void ResetVisualization() => Visualization?.Reset();

    /// <summary>Tri-state value for the shared master switch: true when both directions are enabled, false when
    /// both are disabled, and null when they differ (e.g. only the negative direction was disabled). A binary
    /// display would incorrectly imply that the whole axis is disabled in that mixed state. This value is for
    /// display only in a three-state, one-way-bound <see cref="System.Windows.Controls.CheckBox"/>; toggling
    /// is handled exclusively by <see cref="ToggleEnabledCommand"/> to avoid getting stuck in the indeterminate state.</summary>
    public bool? EnabledState
    {
        get
        {
            var positiveEnabled = Positive.Enabled;
            var negativeEnabled = Negative?.Enabled ?? positiveEnabled;
            return positiveEnabled == negativeEnabled ? positiveEnabled : null;
        }
    }

    /// <summary>Simplified boolean value for IsEnabled bindings (e.g. the embedded visualization), which cannot
    /// accept a tri-state value like a <see cref="System.Windows.Controls.CheckBox"/>: true while either
    /// direction is enabled, false only when both are disabled.</summary>
    public bool IsAnyEnabled => EnabledState != false;

    /// <summary>Sets both directions to the same value when the master checkbox is clicked. If both are not
    /// currently enabled (false or mixed), enables both; if both are enabled, disables both.</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        var newValue = EnabledState != true;
        Positive.Enabled = newValue;
        if (Negative is not null)
        {
            Negative.Enabled = newValue;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceConfigInputRowViewModel.Enabled))
        {
            OnPropertyChanged(nameof(EnabledState));
            OnPropertyChanged(nameof(IsAnyEnabled));
        }
    }
}

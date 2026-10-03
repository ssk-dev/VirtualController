using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// One row in a device's expandable physical input list. Shows the display name (which the user can rename),
/// highlights the row while its physical input is active, and offers an "Assign" action to use that input
/// directly as the source for a new mapping row.
/// </summary>
public sealed partial class PhysicalInputRowViewModel : ObservableObject
{
    /// <summary>An axis direction is considered active at this absolute deflection (less strict than the capture
    /// threshold to provide responsive live feedback).</summary>
    private const float AxisActiveThreshold = 0.3f;

    public PhysicalInputRef Ref { get; }

    private readonly Action<PhysicalInputRef, string> _saveCustomName;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Whether this physical input is enabled in the device configuration dialog. Captured when the
    /// row is created; this is sufficient because every device availability change
    /// (<see cref="MainViewModel.NotifyDeviceAvailabilityChanged"/>) rebuilds the entire device selection list.
    /// The view uses this value to dim disabled inputs.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Raised when the user assigns this physical input to a new mapping row.</summary>
    public event Action<PhysicalInputRef>? AssignRequested;

    public PhysicalInputRowViewModel(PhysicalInputRef inputRef, string displayName, bool isEnabled, Action<PhysicalInputRef, string> saveCustomName)
    {
        Ref = inputRef;
        _saveCustomName = saveCustomName;
        _name = displayName;
        _isEnabled = isEnabled;
    }

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

    [RelayCommand]
    private void Assign() => AssignRequested?.Invoke(Ref);

    partial void OnNameChanged(string value) => _saveCustomName(Ref, value);
}

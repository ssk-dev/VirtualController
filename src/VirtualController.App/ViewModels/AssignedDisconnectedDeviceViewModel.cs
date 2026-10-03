namespace VirtualController.App.ViewModels;

/// <summary>
/// Represents a physical device assigned to a virtual controller (see
/// <see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>) that is currently disconnected.
/// It appears dimmed below "Available devices" in the "Assigned devices" list (see
/// <see cref="VirtualControllerViewModel.AssignedDisconnectedDeviceSelections"/>) to show that the assignment
/// remains in place while the device is disconnected. This entry is informational and not interactive,
/// unlike <see cref="DeviceSelectionViewModel"/>, which is used only for connected devices.
/// </summary>
public sealed class AssignedDisconnectedDeviceViewModel
{
    /// <summary>Unique device ID (see <see cref="Core.Devices.PhysicalDeviceInfo.DeviceId"/>).</summary>
    public string DeviceId { get; }

    /// <summary>Last known display name (see <see cref="Core.Devices.DeviceSettings.LastKnownDisplayName"/>),
    /// or the raw <see cref="DeviceId"/> if no display name has ever been recorded.</summary>
    public string DisplayName { get; }

    public AssignedDisconnectedDeviceViewModel(string deviceId, string displayName)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
    }
}

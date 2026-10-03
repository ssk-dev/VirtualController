namespace VirtualController.App.ViewModels;

/// <summary>
/// Shared interface for the two input group types in the device configuration dialog:
/// <see cref="DeviceConfigInputGroupViewModel"/> (flat Buttons/D-pad list) and
/// <see cref="DeviceConfigAxisGroupViewModel"/> (framed positive/negative axis pairs). Lets
/// <see cref="DeviceConfigDeviceViewModel"/> manage both types in one <c>InputGroups</c> collection for live
/// updates and reset without knowing the concrete type. The view selects a template from the runtime type.
/// </summary>
public interface IDeviceConfigGroup
{
    string GroupName { get; }

    /// <summary>All input rows in this group, regardless of flat or paired nesting, for generic live updates
    /// and reset.</summary>
    IEnumerable<DeviceConfigInputRowViewModel> AllRows { get; }
}

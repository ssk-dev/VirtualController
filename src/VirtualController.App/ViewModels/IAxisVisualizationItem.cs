using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Shared interface for the two axis visualization item types (<see cref="AxisVisualizationViewModel"/> for
/// individual axes and <see cref="Axis2DVisualizationViewModel"/> for combined X/Y axes). Lets
/// <see cref="DeviceConfigDeviceViewModel"/> update any number of device axes through live polling without
/// type checks. The view selects the horizontal slider or square coordinate field from the runtime type
/// through implicit DataTemplates.
/// </summary>
public interface IAxisVisualizationItem
{
    /// <summary>Updates the value(s) and deadzone state from the most recently polled device state.</summary>
    void UpdateFromState(DeviceState state);

    /// <summary>Resets the display to its resting state, e.g. when the device is collapsed in the configuration dialog.</summary>
    void Reset();
}

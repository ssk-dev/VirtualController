using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Shared interface for the item types in the Axes group (<see cref="DeviceConfigAxisGroupViewModel.Items"/>):
/// a standalone physical axis (<see cref="DeviceConfigAxisPairViewModel"/>, e.g. a trigger/slider) or a
/// complete stick made from two related axes (<see cref="DeviceConfigStickGroupViewModel"/>, e.g. "Left stick" = X+Y).
/// Like <see cref="IAxisVisualizationItem"/>, the view chooses a template from the runtime type while
/// <see cref="DeviceConfigDeviceViewModel"/> handles live updates and reset generically through
/// <see cref="AllRows"/>, <see cref="UpdateVisualization"/>, and <see cref="ResetVisualization"/>.
/// </summary>
public interface IDeviceConfigAxisItem
{
    /// <summary>All contained input rows (positive/negative directions), regardless of nesting, for generic
    /// live updates and reset.</summary>
    IEnumerable<DeviceConfigInputRowViewModel> AllRows { get; }

    /// <summary>Updates the live visualization embedded in this card (see
    /// <see cref="DeviceConfigAxisPairViewModel.Visualization"/> or
    /// <see cref="DeviceConfigStickGroupViewModel.Visualization"/>) from the most recently polled device state.</summary>
    void UpdateVisualization(DeviceState state);

    /// <summary>Resets the embedded live visualization to its resting state, e.g. when the device is collapsed
    /// in the configuration dialog.</summary>
    void ResetVisualization();
}

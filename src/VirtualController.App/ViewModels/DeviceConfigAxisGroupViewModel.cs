using System.Collections.ObjectModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Groups all device axes in the configuration dialog as <see cref="IDeviceConfigAxisItem"/> entries: either
/// one positive/negative pair (<see cref="DeviceConfigAxisPairViewModel"/> for triggers, sliders, and unpaired
/// rotation axes) or a complete stick made from two paired axes (<see cref="DeviceConfigStickGroupViewModel"/>,
/// e.g. "Left stick" = X+Y). Each axis or stick appears as a distinct block with a name, enabled state, and
/// calibration controls instead of a flat list of unrelated rows.
/// </summary>
public sealed class DeviceConfigAxisGroupViewModel : IDeviceConfigGroup
{
    public string GroupName => "Axes";

    public ObservableCollection<IDeviceConfigAxisItem> Items { get; } = new();

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => Items.SelectMany(i => i.AllRows);

    /// <summary>Updates the live visualizations embedded in all cards from the most recently polled device
    /// state. Replaces the former flat <c>AxisVisualizations</c> list; each axis or stick card now owns its
    /// visualization directly.</summary>
    public void UpdateFromState(DeviceState state)
    {
        foreach (var item in Items)
        {
            item.UpdateVisualization(state);
        }
    }

    /// <summary>Resets all embedded live visualizations to their resting state, e.g. when the device is
    /// collapsed in the configuration dialog.</summary>
    public void Reset()
    {
        foreach (var item in Items)
        {
            item.ResetVisualization();
        }
    }
}


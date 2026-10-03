using System.Collections.ObjectModel;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Groups a device's physical inputs under a named heading (e.g. "Buttons", "D-Pad") so users can find a
/// category quickly instead of searching a long, unstructured list. Axes use
/// <see cref="DeviceConfigAxisGroupViewModel"/> so positive/negative entries can be shown as framed pairs.
/// </summary>
public sealed class DeviceConfigInputGroupViewModel : IDeviceConfigGroup
{
    public string GroupName { get; }

    public ObservableCollection<DeviceConfigInputRowViewModel> Inputs { get; } = new();

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => Inputs;

    public DeviceConfigInputGroupViewModel(string groupName)
    {
        GroupName = groupName;
    }
}

using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Builds a human-readable name for a physical input ("device name - input name") from its device ID, kind,
/// and index. Uses a user-provided custom name (see <see cref="DeviceSettings"/>) and the last known device
/// name when the device is disconnected. Centralizes this logic for mapping rows
/// (<see cref="MappingRowViewModel"/>) and mode-switch triggers (<see cref="ModeViewModel"/> and
/// <see cref="VirtualControllerViewModel"/>).
/// </summary>
public static class PhysicalInputDisplayNameHelper
{
    public static string Build(
        string deviceId,
        PhysicalInputKind kind,
        int index,
        IReadOnlyList<PhysicalDeviceInfo> knownDevices,
        IReadOnlyDictionary<string, DeviceSettings> deviceSettings,
        out bool isConnected)
    {
        // No physical source is assigned yet (a new mapping row before Capture or Assign). There is no useful
        // display name and the device is not disconnected; the source selection is simply pending. Set
        // isConnected to true so the view does not show its red "(disconnected)" indicator.
        if (string.IsNullOrEmpty(deviceId))
        {
            isConnected = true;
            return string.Empty;
        }

        var device = knownDevices.FirstOrDefault(d => d.DeviceId == deviceId);
        isConnected = device is not null;

        string deviceName = device?.DisplayName
            ?? (deviceSettings.TryGetValue(deviceId, out var settings) ? settings.LastKnownDisplayName : null)
            ?? deviceId;

        var storageKey = PhysicalInputCatalog.BuildStorageKey(deviceId, kind, index);
        string? customInputName = deviceSettings.TryGetValue(deviceId, out var deviceSettingsEntry)
            && deviceSettingsEntry.Inputs.TryGetValue(storageKey, out var inputSettings)
            ? inputSettings.CustomName
            : null;

        string inputName = customInputName
            ?? (device is not null
                ? PhysicalInputCatalog.BuildInputs(device).FirstOrDefault(i => i.Kind == kind && i.Index == index)?.DisplayName
                : null)
            ?? $"{kind} {index}";

        return $"{deviceName} \u2013 {inputName}";
    }
}

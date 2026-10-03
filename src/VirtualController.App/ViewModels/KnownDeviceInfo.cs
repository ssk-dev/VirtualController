using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// A physical device known to <see cref="MainViewModel"/> because it is currently connected or was previously
/// detected and configured (see <see cref="Devices.DeviceSettings.LastKnownButtonCount"/>). Previously known
/// devices remain visible and configurable on the Device Configuration tab while disconnected. Returned by
/// <see cref="MainViewModel.GetAllKnownDevices"/> and consumed by <see cref="DeviceConfigViewModel.UpdateDevices"/>
/// to incrementally reconcile the device list instead of rebuilding it on every scan, including periodic hot-plug scans.
/// </summary>
public sealed record KnownDeviceInfo(PhysicalDeviceInfo Device, bool IsConnected);

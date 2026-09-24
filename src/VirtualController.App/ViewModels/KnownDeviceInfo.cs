using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Ein physisches Geraet, das dem <see cref="MainViewModel"/> bekannt ist - entweder weil es aktuell
/// tatsaechlich angeschlossen ist, oder weil es zuvor bereits einmal erkannt und konfiguriert wurde
/// (siehe <see cref="Devices.DeviceSettings.LastKnownButtonCount"/> etc.) und deshalb im
/// "Gerätekonfiguration"-Tab weiterhin sichtbar/konfigurierbar bleiben soll, obwohl es momentan getrennt
/// ist. Wird von <see cref="MainViewModel.GetAllKnownDevices"/> geliefert und von
/// <see cref="DeviceConfigViewModel.UpdateDevices"/> konsumiert, um die Geraeteliste inkrementell
/// abzugleichen, statt sie bei jedem (auch periodischen Hotplug-)Scan komplett neu aufzubauen.
/// </summary>
public sealed record KnownDeviceInfo(PhysicalDeviceInfo Device, bool IsConnected);

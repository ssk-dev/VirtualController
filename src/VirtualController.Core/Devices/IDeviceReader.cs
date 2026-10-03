namespace VirtualController.Core.Devices;

/// <summary>
/// Unified reader interface for a physical input device, independent of the underlying API (XInput or
/// DirectInput). Implementations must be safe for repeated <see cref="Poll"/> calls from a dedicated polling thread.
/// </summary>
public interface IDeviceReader : IDisposable
{
    PhysicalDeviceInfo Info { get; }

    /// <summary>Reads the current state. Returns false if the device is no longer connected.</summary>
    bool Poll(out DeviceState state);
}

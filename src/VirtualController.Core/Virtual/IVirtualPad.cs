using VirtualController.Core.Mapping;

namespace VirtualController.Core.Virtual;

/// <summary>
/// Unified output interface for a virtual controller registered with the ViGEmBus driver. Implementations
/// translate the generic <see cref="VirtualPadState"/> to the backend's native report format
/// (Xbox 360 = XInput, DualShock 4 = HID).
/// </summary>
public interface IVirtualPad : IDisposable
{
    VirtualBackend Backend { get; }

    /// <summary>Registers the virtual controller with ViGEmBus. Throws if the driver is not installed.</summary>
    void Connect();

    /// <summary>Unregisters the virtual controller; Windows removes the device immediately.</summary>
    void Disconnect();

    /// <summary>Sends the current state to the driver as one HID/XInput report.</summary>
    void Submit(VirtualPadState state);
}

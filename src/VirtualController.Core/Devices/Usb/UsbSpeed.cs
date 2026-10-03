namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Signaling speed reported by the hub for a connected USB device (see <c>USB_DEVICE_SPEED</c> in
/// <c>usbioctl.h</c>). Numeric values intentionally match the raw values reported by Windows
/// (<see cref="Usb.UsbHubNativeInterop"/>) so newer speeds (e.g. USB 3.1/3.2) can be added later without
/// breaking existing values.
/// </summary>
public enum UsbSpeed
{
    /// <summary>Hub-reported value did not match a known speed.</summary>
    Unknown = -1,

    /// <summary>USB 1.0/1.1 Low-Speed (1.5 Mbit/s), typical of simple HID devices such as some keyboards.</summary>
    Low = 0,

    /// <summary>USB 1.1 Full-Speed (12 Mbit/s), typical of most gamepads/joysticks.</summary>
    Full = 1,

    /// <summary>USB 2.0 High-Speed (480 Mbit/s).</summary>
    High = 2,

    /// <summary>USB 3.x SuperSpeed (5 Gbit/s or faster), uncommon for HID input devices.</summary>
    Super = 3,
}

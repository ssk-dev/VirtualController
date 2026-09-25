namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Vom Hub gemeldete Signalisierungsgeschwindigkeit eines angeschlossenen USB-Geraets (siehe
/// <c>USB_DEVICE_SPEED</c> in <c>usbioctl.h</c>). Numerische Werte entsprechen absichtlich den
/// von Windows gemeldeten Rohwerten (<see cref="Usb.UsbHubNativeInterop"/>), damit eine
/// zukuenftige Erweiterung um weitere, neuere Geschwindigkeitsstufen (z.B. USB 3.1/3.2) ohne
/// Bruch bestehender Werte moeglich ist.
/// </summary>
public enum UsbSpeed
{
    /// <summary>Vom Hub gemeldeter Wert liess sich keiner der bekannten Stufen zuordnen.</summary>
    Unknown = -1,

    /// <summary>USB 1.0/1.1 Low-Speed (1,5 Mbit/s) - typisch fuer sehr einfache HID-Geraete (z.B. manche Tastaturen).</summary>
    Low = 0,

    /// <summary>USB 1.1 Full-Speed (12 Mbit/s) - typisch fuer die meisten Gamepads/Joysticks.</summary>
    Full = 1,

    /// <summary>USB 2.0 High-Speed (480 Mbit/s).</summary>
    High = 2,

    /// <summary>USB 3.x SuperSpeed (5 Gbit/s oder mehr) - fuer HID-Eingabegeraete unueblich.</summary>
    Super = 3,
}

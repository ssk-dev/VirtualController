namespace VirtualController.Core.Virtual;

/// <summary>
/// Das tatsaechlich von Windows/ViGEmBus als Geraet erzeugte Ziel.
/// </summary>
public enum VirtualBackend
{
    /// <summary>XInput-Geraet, von Windows und praktisch allen Spielen als "Xbox 360 Controller" erkannt.</summary>
    Xbox360,

    /// <summary>HID-Gamepad, von Windows als "Wireless Controller" (Sony DualShock 4) erkannt.</summary>
    DualShock4
}

public static class LayoutBackendMap
{
    /// <summary>
    /// Legt fest, welches ViGEmBus-Backend fuer ein gewaehltes Anzeige-Layout verwendet wird.
    /// Nur PlayStation nutzt DualShock4, alle anderen (inkl. Nintendo, kosmetisch) nutzen Xbox360.
    /// </summary>
    public static VirtualBackend Resolve(ControllerLayout layout) => layout switch
    {
        ControllerLayout.PlayStation => VirtualBackend.DualShock4,
        _ => VirtualBackend.Xbox360
    };
}

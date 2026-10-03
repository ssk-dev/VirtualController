namespace VirtualController.Core.Virtual;

/// <summary>
/// Device target actually created by Windows/ViGEmBus.
/// </summary>
public enum VirtualBackend
{
    /// <summary>XInput device recognized by Windows and nearly all games as an "Xbox 360 Controller".</summary>
    Xbox360,

    /// <summary>HID gamepad recognized by Windows as "Wireless Controller" (Sony DualShock 4).</summary>
    DualShock4
}

public static class LayoutBackendMap
{
    /// <summary>
    /// Selects the ViGEmBus backend for a display layout. PlayStation uses DualShock 4; all other layouts,
    /// including cosmetic Nintendo, use Xbox 360.
    /// </summary>
    public static VirtualBackend Resolve(ControllerLayout layout) => layout switch
    {
        ControllerLayout.PlayStation => VirtualBackend.DualShock4,
        _ => VirtualBackend.Xbox360
    };
}

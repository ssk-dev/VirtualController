namespace VirtualController.Core.Devices;

/// <summary>API used to read a physical controller.</summary>
public enum InputApi
{
    /// <summary>XInput (Xbox-compatible controllers). Very low latency; supports up to four devices (user indices 0-3).</summary>
    XInput,

    /// <summary>DirectInput (generic HID joysticks/gamepads, PlayStation controllers, older devices).</summary>
    DirectInput
}

/// <summary>Digital button state for a physical device at a point in time (bitmask per API raw value).</summary>
public readonly record struct RawButtonState(int ButtonIndex, bool Pressed);

/// <summary>
/// One physical input that can be mapped: a digital button, analog axis, or D-pad direction. Shown as a row
/// in the mapping table for each physical controller.
/// </summary>
public enum PhysicalInputKind
{
    Button,
    AxisPositive,
    AxisNegative,

    /// <summary>Deprecated: formerly represented the entire DirectInput D-pad (POV) as one entry. Retained so
    /// existing profiles using this value continue to load. New mappings use the four individual directions
    /// (<see cref="DPadUp"/> etc.) so the D-pad behaves consistently as a four-way pad, like XInput's separate
    /// digital direction buttons.</summary>
    DPad,

    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight
}

/// <summary>Unique reference to a physical input on a specific device.</summary>
public sealed record PhysicalInputRef(
    string DeviceId,
    PhysicalInputKind Kind,
    int Index,
    string DisplayName);

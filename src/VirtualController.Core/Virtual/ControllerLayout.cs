namespace VirtualController.Core.Virtual;

/// <summary>
/// Cosmetic layout for UI button labels/arrangement. Windows recognizes only the underlying
/// <see cref="VirtualBackend"/> (Xbox 360 = XInput device, DualShock 4 = HID gamepad). ViGEmBus has no native
/// Nintendo target, so Nintendo layout changes only button labels (B/A/X/Y reversed relative to Xbox) while
/// using the Xbox 360 backend.
/// </summary>
public enum ControllerLayout
{
    Xbox,
    PlayStation,
    Nintendo
}

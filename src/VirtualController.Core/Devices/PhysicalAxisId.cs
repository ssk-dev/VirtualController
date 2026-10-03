namespace VirtualController.Core.Devices;

/// <summary>
/// Raw axis slot indices (0-7), matching the native DirectInput DIJOYSTATE2 order (X, Y, Z, X/Y/Z rotation,
/// Slider0, Slider1). <see cref="DirectInputDeviceReader"/> populates only slots actually present according to
/// <see cref="PhysicalDeviceInfo.AvailableAxes"/>. <see cref="XInputDeviceReader"/> uses slots 0-5 with a
/// different, historical meaning (left/right stick axes and triggers); these names are only a reference for
/// DirectInput. See each reader for its exact layout.
/// </summary>
public enum PhysicalAxisId
{
    X = 0,
    Y = 1,
    Z = 2,
    RotationX = 3,
    RotationY = 4,
    RotationZ = 5,
    Slider0 = 6,
    Slider1 = 7
}

public static class DeviceStateExtensions
{
    /// <summary>
    /// Reads the raw value from an axis slot (0-7). Returns the neutral value zero for invalid or unavailable
    /// device slots (never a phantom value).
    /// </summary>
    public static float GetAxisRaw(this DeviceState state, int slotIndex)
        => slotIndex >= 0 && slotIndex < state.Axes.Length ? state.Axes[slotIndex] : 0f;
}

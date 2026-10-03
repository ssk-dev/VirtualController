namespace VirtualController.Core.Devices;

/// <summary>
/// Snapshot of a physical controller's state at one polling instant. Buttons[i] is the ith digital button in
/// API order (XInput bitmask order, DirectInput button-array index).
/// </summary>
public sealed class DeviceState
{
    /// <summary>Number of generic axis slots (see <see cref="PhysicalAxisId"/>).</summary>
    public const int AxisSlotCount = 8;

    public required bool[] Buttons { get; init; }

    /// <summary>
    /// Generic axis values indexed by <see cref="PhysicalAxisId"/> (slots 0-7). Stick-like axes are normalized
    /// to -1.0 .. 1.0, triggers/sliders to 0.0 .. 1.0. Slots not present on the device remain zero (neutral,
    /// with no phantom deflection).
    /// </summary>
    public required float[] Axes { get; init; }

    /// <summary>DirectInput usually reports the D-pad as a POV angle (0-35900, -1 = centered); XInput reports
    /// it as four digital bits included in Buttons.</summary>
    public required int PovDirectionDegrees { get; init; }

    public static DeviceState Empty(int buttonCount) => new()
    {
        Buttons = new bool[buttonCount],
        Axes = new float[AxisSlotCount],
        PovDirectionDegrees = -1
    };
}

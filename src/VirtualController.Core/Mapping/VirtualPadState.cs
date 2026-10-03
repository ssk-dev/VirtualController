using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Aggregated output state for a virtual controller, independent of the specific ViGEmBus backend. Created by
/// the mapping engine and converted by the virtual wrappers (Xbox 360/DualShock 4) to their native report format.
/// </summary>
public sealed class VirtualPadState
{
    public HashSet<VirtualButton> PressedButtons { get; } = new();
    public float LeftStickX { get; set; }
    public float LeftStickY { get; set; }
    public float RightStickX { get; set; }
    public float RightStickY { get; set; }
    public float LeftTrigger { get; set; }
    public float RightTrigger { get; set; }
    public DPadDirection DPad { get; set; } = DPadDirection.None;

    public void Reset()
    {
        PressedButtons.Clear();
        LeftStickX = LeftStickY = RightStickX = RightStickY = 0f;
        LeftTrigger = RightTrigger = 0f;
        DPad = DPadDirection.None;
    }
}

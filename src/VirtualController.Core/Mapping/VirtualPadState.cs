using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Aggregierter Ziel-Zustand eines virtuellen Controllers, unabhaengig vom konkreten
/// ViGEmBus-Backend. Wird von der Mapping-Engine erzeugt und von den Virtual-Wrappern
/// (Xbox360/DualShock4) in das jeweilige natives Report-Format uebertragen.
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

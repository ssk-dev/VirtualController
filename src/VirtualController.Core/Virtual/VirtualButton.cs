namespace VirtualController.Core.Virtual;

/// <summary>
/// Vendor-independent buttons for a modern gamepad. South/East/West/North map to A/B/X/Y (Xbox),
/// Cross/Circle/Square/Triangle (PlayStation), or B/A/Y/X (Nintendo, reversed).
/// </summary>
public enum VirtualButton
{
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftThumbClick,
    RightThumbClick,
    Back,
    Start,
    Guide,
    Share
}

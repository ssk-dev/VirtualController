namespace VirtualController.Core.Virtual;

/// <summary>
/// Provides virtual controller element labels for the selected <see cref="ControllerLayout"/> (e.g. Xbox:
/// A/B/X/Y, PlayStation: Cross/Circle/Square/Triangle, Nintendo: B/A/Y/X reversed). Used by the UI so the
/// mapping table's target-value selector shows familiar controller labels instead of generic enum names
/// (South/East/West/North, etc.).
/// </summary>
public static class VirtualControllerLabels
{
    public static string GetButtonLabel(ControllerLayout layout, VirtualButton button) => layout switch
    {
        ControllerLayout.PlayStation => button switch
        {
            VirtualButton.South => "Cross",
            VirtualButton.East => "Circle",
            VirtualButton.West => "Square",
            VirtualButton.North => "Triangle",
            VirtualButton.LeftShoulder => "L1",
            VirtualButton.RightShoulder => "R1",
            VirtualButton.LeftThumbClick => "L3",
            VirtualButton.RightThumbClick => "R3",
            VirtualButton.Back => "Share",
            VirtualButton.Start => "Options",
            VirtualButton.Guide => "PS",
            VirtualButton.Share => "Touchpad",
            _ => button.ToString()
        },
        ControllerLayout.Nintendo => button switch
        {
            // Cosmetic mapping reversed from Xbox (see ControllerLayout docs): A/B and X/Y are swapped.
            VirtualButton.South => "B",
            VirtualButton.East => "A",
            VirtualButton.West => "Y",
            VirtualButton.North => "X",
            VirtualButton.LeftShoulder => "L",
            VirtualButton.RightShoulder => "R",
            VirtualButton.LeftThumbClick => "Left stick (click)",
            VirtualButton.RightThumbClick => "Right stick (click)",
            VirtualButton.Back => "-",
            VirtualButton.Start => "+",
            VirtualButton.Guide => "Home",
            VirtualButton.Share => "Screenshot",
            _ => button.ToString()
        },
        _ => button switch // Xbox (Standard)
        {
            VirtualButton.South => "A",
            VirtualButton.East => "B",
            VirtualButton.West => "X",
            VirtualButton.North => "Y",
            VirtualButton.LeftShoulder => "LB",
            VirtualButton.RightShoulder => "RB",
            VirtualButton.LeftThumbClick => "Left stick (click)",
            VirtualButton.RightThumbClick => "Right stick (click)",
            VirtualButton.Back => "Back",
            VirtualButton.Start => "Start",
            VirtualButton.Guide => "Guide",
            VirtualButton.Share => "Share",
            _ => button.ToString()
        }
    };

    public static string GetTriggerLabel(ControllerLayout layout, VirtualTrigger trigger) => layout switch
    {
        ControllerLayout.PlayStation => trigger switch
        {
            VirtualTrigger.LeftTrigger => "L2",
            VirtualTrigger.RightTrigger => "R2",
            _ => trigger.ToString()
        },
        ControllerLayout.Nintendo => trigger switch
        {
            VirtualTrigger.LeftTrigger => "ZL",
            VirtualTrigger.RightTrigger => "ZR",
            _ => trigger.ToString()
        },
        _ => trigger switch // Xbox (default)
        {
            VirtualTrigger.LeftTrigger => "LT",
            VirtualTrigger.RightTrigger => "RT",
            _ => trigger.ToString()
        }
    };

    /// <summary>Axis labels are layout-independent; all three layouts use the same stick arrangement.</summary>
    public static string GetAxisLabel(VirtualAxis axis) => axis switch
    {
        VirtualAxis.LeftStickX => "Left stick X",
        VirtualAxis.LeftStickY => "Left stick Y",
        VirtualAxis.RightStickX => "Right stick X",
        VirtualAxis.RightStickY => "Right stick Y",
        _ => axis.ToString()
    };

    /// <summary>D-pad labels are layout-independent; all three layouts use the same directional cross.</summary>
    public static string GetDPadLabel(DPadDirection direction) => direction switch
    {
        DPadDirection.None => "None",
        DPadDirection.Up => "Up",
        DPadDirection.UpRight => "Up-Right",
        DPadDirection.Right => "Right",
        DPadDirection.DownRight => "Down-Right",
        DPadDirection.Down => "Down",
        DPadDirection.DownLeft => "Down-Left",
        DPadDirection.Left => "Left",
        DPadDirection.UpLeft => "Up-Left",
        _ => direction.ToString()
    };

    /// <summary>Returns the translation key for a virtual button label, used by the UI to look up
    /// localized text from the translation files. The key is layout-independent; the translation
    /// files contain the layout-specific labels.</summary>
    public static string GetButtonTranslationKey(ControllerLayout layout, VirtualButton button) => layout switch
    {
        ControllerLayout.PlayStation => $"virtual.playstation.{button.ToString().ToLowerInvariant()}",
        ControllerLayout.Nintendo => $"virtual.nintendo.{button.ToString().ToLowerInvariant()}",
        _ => $"virtual.xbox.{button.ToString().ToLowerInvariant()}"
    };

    public static string GetTriggerTranslationKey(ControllerLayout layout, VirtualTrigger trigger) => layout switch
    {
        ControllerLayout.PlayStation => $"virtual.playstation.{trigger.ToString().ToLowerInvariant()}",
        ControllerLayout.Nintendo => $"virtual.nintendo.{trigger.ToString().ToLowerInvariant()}",
        _ => $"virtual.xbox.{trigger.ToString().ToLowerInvariant()}"
    };

    public static string GetAxisTranslationKey(VirtualAxis axis) => $"virtual.axis.{axis.ToString().ToLowerInvariant()}";

    public static string GetDPadTranslationKey(DPadDirection direction) => $"virtual.dpad.{direction.ToString().ToLowerInvariant()}";
}

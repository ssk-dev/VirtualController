namespace VirtualController.Core.Virtual;

/// <summary>8-Wege-Richtung des D-Pads (Kreuz).</summary>
public enum DPadDirection
{
    None,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft
}

public static class DPadDirectionExtensions
{
    public static bool HasUp(this DPadDirection d) => d is DPadDirection.Up or DPadDirection.UpRight or DPadDirection.UpLeft;
    public static bool HasDown(this DPadDirection d) => d is DPadDirection.Down or DPadDirection.DownRight or DPadDirection.DownLeft;
    public static bool HasLeft(this DPadDirection d) => d is DPadDirection.Left or DPadDirection.UpLeft or DPadDirection.DownLeft;
    public static bool HasRight(this DPadDirection d) => d is DPadDirection.Right or DPadDirection.UpRight or DPadDirection.DownRight;

    /// <summary>Baut eine Richtung aus vier unabhaengigen Digital-Flags (z.B. vier gemappten Buttons).</summary>
    public static DPadDirection FromFlags(bool up, bool down, bool left, bool right)
    {
        if (up && right) return DPadDirection.UpRight;
        if (down && right) return DPadDirection.DownRight;
        if (down && left) return DPadDirection.DownLeft;
        if (up && left) return DPadDirection.UpLeft;
        if (up) return DPadDirection.Up;
        if (down) return DPadDirection.Down;
        if (left) return DPadDirection.Left;
        if (right) return DPadDirection.Right;
        return DPadDirection.None;
    }

    /// <summary>Wandelt einen rohen DirectInput-POV-Winkel (Hundertstel-Grad, 0 = oben, im Uhrzeigersinn, -1 = zentriert)
    /// in eine Richtung um. Wird von allen Stellen genutzt, die ein einzelnes POV-Element als 4-Wege-Kreuz
    /// (vier separate Digital-Eingaben) behandeln, damit ueberall exakt dieselben Winkel-Grenzen gelten.</summary>
    public static DPadDirection FromPovDegrees(int povDegrees)
    {
        if (povDegrees < 0) return DPadDirection.None;

        int deg = (povDegrees / 100) % 360;
        return deg switch
        {
            >= 337 or < 23 => DPadDirection.Up,
            >= 23 and < 68 => DPadDirection.UpRight,
            >= 68 and < 113 => DPadDirection.Right,
            >= 113 and < 158 => DPadDirection.DownRight,
            >= 158 and < 203 => DPadDirection.Down,
            >= 203 and < 248 => DPadDirection.DownLeft,
            >= 248 and < 293 => DPadDirection.Left,
            _ => DPadDirection.UpLeft
        };
    }
}

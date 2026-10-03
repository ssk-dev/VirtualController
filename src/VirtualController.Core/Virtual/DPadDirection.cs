namespace VirtualController.Core.Virtual;

/// <summary>Eight-way D-pad direction.</summary>
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

    /// <summary>Combines four independent digital flags (e.g. four mapped buttons) into a direction.</summary>
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

    /// <summary>Converts a raw DirectInput POV angle (hundredths of a degree, 0 = up, clockwise, -1 = centered)
    /// into a direction. Shared by all code treating one POV element as four digital directions so angle
    /// boundaries remain consistent.</summary>
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

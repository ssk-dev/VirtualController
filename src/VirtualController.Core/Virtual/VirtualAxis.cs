namespace VirtualController.Core.Virtual;

/// <summary>Analoge Stick-Achsen, normalisiert auf den Bereich -1.0 .. 1.0.</summary>
public enum VirtualAxis
{
    LeftStickX,
    LeftStickY,
    RightStickX,
    RightStickY
}

/// <summary>Analoge Trigger, normalisiert auf den Bereich 0.0 .. 1.0.</summary>
public enum VirtualTrigger
{
    LeftTrigger,
    RightTrigger
}

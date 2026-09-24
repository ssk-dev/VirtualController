namespace VirtualController.Core.Devices;

/// <summary>
/// Momentaufnahme des Zustands eines physischen Controllers zu einem Abtastzeitpunkt.
/// Buttons[i] entspricht dem i-ten digitalen Knopf in der Reihenfolge der API (XInput: Bitmaske-Reihenfolge,
/// DirectInput: Buttons-Array-Index).
/// </summary>
public sealed class DeviceState
{
    /// <summary>Anzahl der generischen Achsen-Slots (siehe <see cref="PhysicalAxisId"/>).</summary>
    public const int AxisSlotCount = 8;

    public required bool[] Buttons { get; init; }

    /// <summary>
    /// Generische Achsen-Rohwerte, indiziert per <see cref="PhysicalAxisId"/> (Slots 0-7).
    /// Stick-artige Achsen liegen normalisiert bei -1.0 .. 1.0, Trigger/Slider bei 0.0 .. 1.0.
    /// Slots, die das jeweilige Geraet nicht besitzt, bleiben stets 0 (Neutralwert, kein Phantom-Ausschlag).
    /// </summary>
    public required float[] Axes { get; init; }

    /// <summary>DirectInput liefert das D-Pad meist als POV-Winkel (0-35900, -1 = zentriert); XInput als vier Digital-Bits (in Buttons enthalten).</summary>
    public required int PovDirectionDegrees { get; init; }

    public static DeviceState Empty(int buttonCount) => new()
    {
        Buttons = new bool[buttonCount],
        Axes = new float[AxisSlotCount],
        PovDirectionDegrees = -1
    };
}

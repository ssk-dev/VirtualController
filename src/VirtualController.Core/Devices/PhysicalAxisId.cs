namespace VirtualController.Core.Devices;

/// <summary>
/// Rohe Achsen-Slot-Indizes (0-7), passend zur nativen DirectInput-DIJOYSTATE2-Reihenfolge
/// (X, Y, Z, X-Rotation, Y-Rotation, Z-Rotation, Slider0, Slider1). <see cref="DirectInputDeviceReader"/>
/// befuellt genau die Slots, die laut <see cref="PhysicalDeviceInfo.AvailableAxes"/> tatsaechlich
/// am Geraet existieren. <see cref="XInputDeviceReader"/> nutzt dieselben Slot-Plaetze 0-5, aber mit
/// eigener, historisch gewachsener Bedeutung (0=LinkerStickX, 1=LinkerStickY, 2=RechterStickX,
/// 3=RechterStickY, 4=LinkerTrigger, 5=RechterTrigger) - diese Namen hier dienen nur als
/// Lesehilfe fuer den DirectInput-Fall, siehe die jeweiligen Reader fuer die genaue Belegung.
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
    /// Liest den Rohwert eines Achsen-Slots (0-7) aus. Ausserhalb des gueltigen Bereichs oder fuer
    /// am Geraet nicht vorhandene Slots liegt stets der Neutralwert 0 vor (nie ein Phantom-Wert).
    /// </summary>
    public static float GetAxisRaw(this DeviceState state, int slotIndex)
        => slotIndex >= 0 && slotIndex < state.Axes.Length ? state.Axes[slotIndex] : 0f;
}

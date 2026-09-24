namespace VirtualController.Core.Devices;

/// <summary>Zugrundeliegende API, über die ein physischer Controller ausgelesen wird.</summary>
public enum InputApi
{
    /// <summary>XInput (Xbox-kompatible Controller). Sehr geringe Latenz, bis zu 4 Geräte (User-Index 0-3).</summary>
    XInput,

    /// <summary>DirectInput (generische HID-Joysticks/Gamepads, PlayStation-Controller, alte Geräte).</summary>
    DirectInput
}

/// <summary>Digitaler Button-Zustand eines physischen Geraets zu einem Zeitpunkt (Bitmaske je API-Rohwert).</summary>
public readonly record struct RawButtonState(int ButtonIndex, bool Pressed);

/// <summary>
/// Ein einzelnes physisches Eingabeelement, das gemappt werden kann: ein digitaler Button,
/// eine analoge Achse oder eine D-Pad Richtung. Wird in der UI als Zeile der Mapping-Tabelle
/// pro physischem Controller angezeigt.
/// </summary>
public enum PhysicalInputKind
{
    Button,
    AxisPositive,
    AxisNegative,

    /// <summary>Veraltet: fasste frueher das gesamte D-Pad (POV) eines DirectInput-Geraets in einem einzigen Eintrag zusammen.
    /// Bleibt nur erhalten, damit bereits gespeicherte Profile mit diesem Wert weiterhin fehlerfrei geladen werden koennen.
    /// Fuer neue Zuordnungen werden stattdessen die vier einzelnen Richtungen (<see cref="DPadUp"/> etc.) verwendet,
    /// damit ein D-Pad sich immer wie ein echtes 4-Wege-Kreuz verhaelt (analog zu XInput, das die vier Richtungen
    /// bereits als eigene Digitalbuttons liefert).</summary>
    DPad,

    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight
}

/// <summary>Eindeutige Referenz auf ein physisches Eingabeelement eines konkreten Geraets.</summary>
public sealed record PhysicalInputRef(
    string DeviceId,
    PhysicalInputKind Kind,
    int Index,
    string DisplayName);

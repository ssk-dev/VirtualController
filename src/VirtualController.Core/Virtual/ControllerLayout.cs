namespace VirtualController.Core.Virtual;

/// <summary>
/// Rein kosmetisches Layout fuer die Anzeige (Beschriftung/Anordnung der Buttons in der UI).
/// Windows selbst erkennt technisch nur den zugrunde liegenden <see cref="VirtualBackend"/>
/// (Xbox360 = XInput-Geraet, DualShock4 = HID-Gamepad). Ein natives "Nintendo"-Zielgeraet
/// existiert im ViGEmBus-Treiber nicht - "Nintendo" bildet daher nur die Button-Beschriftung
/// (B/A/X/Y seitenverkehrt zu Xbox) auf dem Xbox360-Backend nach.
/// </summary>
public enum ControllerLayout
{
    Xbox,
    PlayStation,
    Nintendo
}

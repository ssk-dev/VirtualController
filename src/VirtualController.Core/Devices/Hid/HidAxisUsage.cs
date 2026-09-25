namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Herstellerunabhaengige HID-"Generic Desktop"-Achsen-Usages (Usage Page 0x01), analog zu
/// <see cref="VirtualController.Core.Devices.PhysicalAxisId"/>, aber bewusst als eigenstaendiger Typ
/// gehalten: <see cref="PhysicalAxisId"/> beschreibt DirectInput-/XInput-Slot-Positionen (inklusive der
/// XInput-spezifischen Umdeutung), waehrend dieser Typ ausschliesslich die rohen, im HID-Report-Deskriptor
/// deklarierten Usage-IDs benennt (0x30=X ... 0x38=Wheel) - relevant fuer die Signal-Kennzahlen
/// (<c>SignalMetrics</c>), die bewusst auf den rohen Report-Bytes statt auf den bereits durch DirectInput
/// normalisierten Werten arbeiten (siehe <see cref="HidAxisReportParser"/>).
/// </summary>
public enum HidAxisUsage
{
    X = 0,
    Y = 1,
    Z = 2,
    RotationX = 3,
    RotationY = 4,
    RotationZ = 5,

    /// <summary>Erster Slider (Usage 0x36). Manche Geraete melden zwei Slider-Usages im selben
    /// Report - der zweite wird auf <see cref="Slider1"/> abgebildet (siehe <see cref="HidAxisReportParser"/>,
    /// analog zur bestehenden Slider0/Slider1-Behandlung in <c>DeviceEnumerator.DetectAvailableAxes</c>).</summary>
    Slider0 = 6,
    Slider1 = 7,
    Dial = 8,
    Wheel = 9
}

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Vendor-independent HID Generic Desktop axis usages (Usage Page 0x01). Kept separate from
/// <see cref="VirtualController.Core.Devices.PhysicalAxisId"/>: PhysicalAxisId describes DirectInput/XInput
/// slot positions, including XInput-specific remapping, while this enum names only the raw usage IDs declared
/// by the HID report descriptor (0x30=X ... 0x38=Wheel). Used by SignalMetrics, which works with raw report
/// bytes rather than DirectInput-normalized values (see <see cref="HidAxisReportParser"/>).
/// </summary>
public enum HidAxisUsage
{
    X = 0,
    Y = 1,
    Z = 2,
    RotationX = 3,
    RotationY = 4,
    RotationZ = 5,

    /// <summary>First slider (usage 0x36). Some devices report two Slider usages in one report; the second maps
    /// to <see cref="Slider1"/> (see <see cref="HidAxisReportParser"/> and the existing Slider0/Slider1 handling
    /// in <c>DeviceEnumerator.DetectAvailableAxes</c>).</summary>
    Slider0 = 6,
    Slider1 = 7,
    Dial = 8,
    Wheel = 9
}

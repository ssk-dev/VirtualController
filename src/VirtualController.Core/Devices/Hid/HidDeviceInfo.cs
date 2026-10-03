namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Static HID metadata that does not change while a device remains connected, used by the benchmark's
/// identification/transport metrics (see <see cref="HidDeviceInfoReader"/>). Contains only primitive types and
/// strings so it can be used outside <c>Devices.Hid</c> without exposing the underlying HID library (currently HidSharp).
/// </summary>
/// <param name="VendorId">USB Vendor ID, siehe <see cref="PhysicalDeviceInfo.VendorId"/>.</param>
/// <param name="ProductId">USB Product ID, siehe <see cref="PhysicalDeviceInfo.ProductId"/>.</param>
/// <param name="Manufacturer">Manufacturer string descriptor, if provided by the device and readable; otherwise null.</param>
/// <param name="ProductName">Product string descriptor, if provided by the device and readable; otherwise null.</param>
/// <param name="SerialNumber">Serial number string descriptor, if provided by the device and readable; otherwise null.</param>
/// <param name="ReleaseNumberBcd">BCD-encoded version reported by the device (<c>bcdDevice</c>). It can be treated
/// as an approximate firmware version, but is not guaranteed to be a human-readable firmware string (see
/// <see cref="HidDeviceInfoReader"/> documentation).</param>
/// <param name="MaxInputReportLength">Nominal input report length in bytes (transport metric "Report Size").</param>
/// <param name="MaxOutputReportLength">Nominal output report length in bytes.</param>
/// <param name="MaxFeatureReportLength">Nominal feature report length in bytes.</param>
/// <param name="DevicePath">OS device path for diagnostics/further processing, e.g. <see cref="UsbTopologyResolver"/>.</param>
/// <param name="UsbPortNumber">Physical port number on the parent USB hub, if available; otherwise null.</param>
/// <param name="UsbHubDevicePath">Device path of the parent USB hub, if available; used by
/// <see cref="UsbTopologyResolver"/> to query USB speed/endpoint/polling interval.</param>
public sealed record HidDeviceInfo(
    ushort VendorId,
    ushort ProductId,
    string? Manufacturer,
    string? ProductName,
    string? SerialNumber,
    int ReleaseNumberBcd,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength,
    string DevicePath,
    int? UsbPortNumber,
    string? UsbHubDevicePath);

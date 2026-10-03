using HidSharp;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Resolves vendor/product IDs (see <see cref="PhysicalDeviceInfo.VendorId"/> and
/// <see cref="PhysicalDeviceInfo.ProductId"/>) to HID metadata (<see cref="HidDeviceInfo"/>) and optionally
/// opens a raw report source (<see cref="IHidReportSource"/>). This is the only place in the project that
/// directly uses <see cref="HidSharp.HidDevice"/> (see the encapsulation note in <see cref="IHidReportSource"/>).
///
/// Important limitation (see also <see cref="HidDeviceInfo.ReleaseNumberBcd"/>): HID has no dedicated
/// firmware-version string descriptor. <see cref="HidDeviceInfo.ReleaseNumberBcd"/> (HidD_GetAttributes ->
/// <c>VersionNumber</c>) is an arbitrary BCD-encoded number assigned by the manufacturer. It often reflects
/// firmware/hardware revision but is not guaranteed to; some devices always report 0x0100 or 0x0000 regardless
/// of actual firmware. Benchmark results must label it as an estimate ("device-reported firmware version,
/// unverified"), not a reliable value.
///
/// HidSharp also returns multiple matches when identical devices (same VID/PID) are connected at once.
/// In that case, <see cref="TryResolve"/> deliberately does not choose one automatically; callers should
/// disambiguate using serial number or device path when multiple candidates exist (see <see cref="TryResolveAll"/>).
/// </summary>
public static class HidDeviceInfoReader
{
    /// <summary>
    /// Returns all currently connected HID devices with matching vendor/product IDs. May return multiple
    /// results (see class documentation); callers such as the benchmark must determine which device is meant,
    /// e.g. by serial number or asking the user.
    /// </summary>
    public static IReadOnlyList<HidDeviceInfo> TryResolveAll(ushort vendorId, ushort productId)
    {
        var result = new List<HidDeviceInfo>();

        IEnumerable<HidDevice> candidates;
        try
        {
            candidates = DeviceList.Local.GetHidDevices(vendorId, productId);
        }
        catch
        {
            // HidSharp/OS access to the device list can fail (e.g. due to missing permissions). Treat that as
            // no devices found rather than crashing.
            return result;
        }

        foreach (var device in candidates)
        {
            var info = TryReadInfo(device);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result;
    }

    /// <summary>Convenience method for the common case of one matching device. See <see cref="TryResolveAll"/>
    /// for handling multiple identical devices connected at once.</summary>
    public static HidDeviceInfo? TryResolve(ushort vendorId, ushort productId)
    {
        var all = TryResolveAll(vendorId, productId);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Opens a raw report source for the device at the given OS device path (see
    /// <see cref="HidDeviceInfo.DevicePath"/>). Returns null if the device cannot be found or opening it fails,
    /// e.g. because another app already has it open exclusively.</summary>
    public static IHidReportSource? TryOpenReportSource(string devicePath)
    {
        HidDevice? device;
        try
        {
            device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == devicePath);
        }
        catch
        {
            return null;
        }

        if (device is null)
        {
            return null;
        }

        try
        {
            if (!device.TryOpen(out HidStream stream))
            {
                return null;
            }

            return new HidSharpReportSource(stream, device.GetMaxInputReportLength());
        }
        catch
        {
            // The device may have disconnected while opening, or another app (e.g. a running game) may hold
            // it exclusively. Both are expected cases and must not crash the app.
            return null;
        }
    }

    private static HidDeviceInfo? TryReadInfo(HidDevice device)
    {
        try
        {
            int? usbPortNumber = null;
            string? usbHubDevicePath = null;
            try
            {
                var usbPort = device.GetUsbPort();
                if (usbPort is not null)
                {
                    usbPortNumber = usbPort.PortNumber;
                    usbHubDevicePath = usbPort.HubDevicePath;
                }
            }
            catch
            {
                // USB topology information is optional (see UsbTopologyResolver); its absence must not discard
                // the other metadata that was read successfully.
            }

            return new HidDeviceInfo(
                VendorId: (ushort)device.VendorID,
                ProductId: (ushort)device.ProductID,
                Manufacturer: TryReadOptionalString(() => device.GetManufacturer()),
                ProductName: TryReadOptionalString(() => device.GetProductName()),
                SerialNumber: TryReadOptionalString(() => device.GetSerialNumber()),
                ReleaseNumberBcd: device.ReleaseNumberBcd,
                MaxInputReportLength: device.GetMaxInputReportLength(),
                MaxOutputReportLength: device.GetMaxOutputReportLength(),
                MaxFeatureReportLength: device.GetMaxFeatureReportLength(),
                DevicePath: device.DevicePath,
                UsbPortNumber: usbPortNumber,
                UsbHubDevicePath: usbHubDevicePath);
        }
        catch
        {
            // One unreadable device (e.g. recently disconnected) must not prevent resolving the remaining valid
            // candidates (see TryResolveAll).
            return null;
        }
    }

    /// <summary>Optional HID string descriptors (manufacturer/product/serial number) often throw rather than
    /// return null when missing or inaccessible. This wrapper converts all failures to null because a missing
    /// optional string should not invalidate the entire device record.</summary>
    private static string? TryReadOptionalString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}

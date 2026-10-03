using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Combines HID metadata (<see cref="HidDeviceInfo"/>, especially <see cref="HidDeviceInfo.UsbHubDevicePath"/>
/// and <see cref="HidDeviceInfo.UsbPortNumber"/>) with raw USB hub IOCTL queries
/// (<see cref="UsbHubNativeInterop"/>) behind one defensive entry point for benchmark transport metrics
/// (USB speed, endpoint information, nominal polling rate).
///
/// Returns null, rather than throwing, for any failure (missing hub path, unreachable hub, failed IOCTL).
/// Callers should display these metrics as unavailable rather than aborting the entire benchmark. See
/// <see cref="UsbHubNativeInterop"/> documentation for reliability caveats in this untested native code.
/// </summary>
public static class UsbTopologyResolver
{
    /// <summary>
    /// Resolves USB speed, endpoints, and speed-corrected nominal polling interval for the device described by
    /// <paramref name="hidInfo"/>. Returns null if <see cref="HidDeviceInfo.UsbHubDevicePath"/> or
    /// <see cref="HidDeviceInfo.UsbPortNumber"/> is unavailable (see <see cref="HidDeviceInfoReader"/>) or the
    /// native query fails.
    /// </summary>
    public static UsbConnectionInfo? TryResolve(HidDeviceInfo hidInfo)
    {
        if (hidInfo.UsbHubDevicePath is null || hidInfo.UsbPortNumber is not { } portNumber)
        {
            return null;
        }

        var raw = UsbHubNativeInterop.TryGetNodeConnectionInformation(hidInfo.UsbHubDevicePath, portNumber);
        if (raw is null)
        {
            return null;
        }

        return ApplySpeedCorrection(raw);
    }

    /// <summary>
    /// <see cref="UsbHubNativeInterop"/> initially calculates <see cref="UsbEndpointInfo.NominalPollingIntervalMs"/>
    /// assuming Low-/Full-Speed <c>bInterval</c> encoding (direct milliseconds), because device speed is not
    /// known while parsing endpoints. High-/SuperSpeed uses the microframe formula
    /// <c>2^(bInterval-1) * 0.125 ms</c> (USB 2.0 spec section 9.6.6); apply that correction to all endpoints
    /// once <see cref="UsbConnectionInfo.Speed"/> is known.
    /// </summary>
    private static UsbConnectionInfo ApplySpeedCorrection(UsbConnectionInfo raw)
    {
        if (raw.Speed != UsbSpeed.High && raw.Speed != UsbSpeed.Super)
        {
            // Low/Full/Unknown: already calculated correctly as a direct millisecond value.
            return raw;
        }

        var correctedEndpoints = raw.Endpoints
            .Select(e => e with
            {
                NominalPollingIntervalMs = e.IntervalRaw > 0
                    ? Math.Pow(2, e.IntervalRaw - 1) * 0.125
                    : 0,
            })
            .ToList();

        return raw with { Endpoints = correctedEndpoints };
    }
}

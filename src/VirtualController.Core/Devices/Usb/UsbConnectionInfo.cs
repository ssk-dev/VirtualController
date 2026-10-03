namespace VirtualController.Core.Devices.Usb;

/// <summary>USB endpoint transfer type, from bits 0-1 of the endpoint descriptor's <c>bmAttributes</c>.</summary>
public enum UsbTransferType
{
    Control = 0,
    Isochronous = 1,
    Bulk = 2,
    Interrupt = 3,
}

/// <summary>Endpoint transfer direction, from bit 7 of <c>bEndpointAddress</c>.</summary>
public enum UsbEndpointDirection
{
    Out = 0,
    In = 1,
}

/// <summary>
/// One USB endpoint reported by the hub (<c>USB_PIPE_INFO</c>/<c>USB_ENDPOINT_DESCRIPTOR</c>; see
/// <see cref="UsbHubNativeInterop"/>). Most HID input devices have exactly one interrupt-IN endpoint for reports.
/// </summary>
/// <param name="EndpointAddress">Raw endpoint address, including the direction bit (<c>bEndpointAddress</c>).</param>
/// <param name="Direction">Direction derived from bit 7 of <paramref name="EndpointAddress"/>.</param>
/// <param name="TransferType">Type derived from <c>bmAttributes</c>; almost always <see cref="UsbTransferType.Interrupt"/> for HID devices.</param>
/// <param name="MaxPacketSize">Maximum packet size in bytes (<c>wMaxPacketSize</c>).</param>
/// <param name="IntervalRaw">Raw descriptor <c>bInterval</c>; its meaning depends on speed. See
/// <see cref="NominalPollingIntervalMs"/> for the converted value in milliseconds.</param>
/// <param name="NominalPollingIntervalMs">Nominal polling interval requested by the device/driver in milliseconds.
/// For Low-/Full-Speed it is <paramref name="IntervalRaw"/> directly; for High-/SuperSpeed it is
/// <c>2^(IntervalRaw-1) * 0.125 ms</c> (microframes; see USB 2.0 spec section 9.6.6). This is the requested
/// interval, not the actual polling rate, which must be measured separately from report timestamps because
/// the OS/hub driver may not honor it exactly.</param>
public sealed record UsbEndpointInfo(
    byte EndpointAddress,
    UsbEndpointDirection Direction,
    UsbTransferType TransferType,
    ushort MaxPacketSize,
    byte IntervalRaw,
    double NominalPollingIntervalMs);

/// <summary>
/// Device connection information reported by the parent USB hub (see <see cref="UsbTopologyResolver"/> and
/// <see cref="UsbHubNativeInterop"/>), used by benchmark transport metrics (USB speed, endpoints, nominal polling rate).
/// </summary>
/// <param name="Speed">Signaling speed at which the device is currently connected.</param>
/// <param name="DeviceAddress">USB bus address assigned by the hub, for diagnostics only.</param>
/// <param name="CurrentConfigurationValue">Active USB configuration for the device.</param>
/// <param name="Endpoints">All currently open device endpoints (pipes) reported by the hub.</param>
public sealed record UsbConnectionInfo(
    UsbSpeed Speed,
    ushort DeviceAddress,
    byte CurrentConfigurationValue,
    IReadOnlyList<UsbEndpointInfo> Endpoints)
{
    /// <summary>Primary endpoint for input devices: the first interrupt-IN endpoint, or the first endpoint if
    /// none exists. Simplifies benchmark display, which shows one nominal polling rate per device rather than
    /// a complete USB analyzer view.</summary>
    public UsbEndpointInfo? PrimaryInputEndpoint =>
        Endpoints.FirstOrDefault(e => e.TransferType == UsbTransferType.Interrupt && e.Direction == UsbEndpointDirection.In)
        ?? Endpoints.FirstOrDefault();
}

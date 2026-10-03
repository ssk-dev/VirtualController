using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Raw P/Invoke to the Windows USB hub IOCTL interface
/// (<c>IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX</c>, see <c>usbioctl.h</c> in the Windows Driver Kit) to
/// query connection details (USB speed, endpoints, nominal polling interval) for a device attached to a hub.
/// This is below the HID layer (see <see cref="Devices.Hid.IHidReportSource"/>), so it lives in its own P/Invoke layer.
///
/// Reliability note: this code uses publicly documented structure layouts that Microsoft does not guarantee
/// as a stable public API. The <c>usbioctl.h</c>/<c>usb100.h</c> headers are part of the WDK, not the regular
/// Windows SDK for application developers. Byte offsets in <see cref="TryGetNodeConnectionInformation"/> are
/// specified manually and documented instead of relying on <see cref="StructLayoutAttribute"/> marshaling,
/// making unexpected compiler padding easier to diagnose. This code has not been tested on physical hardware,
/// so every call is defensive (Try method, no exceptions escape); see <see cref="UsbTopologyResolver"/> for
/// caller-side fallback behavior.
/// </summary>
internal static class UsbHubNativeInterop
{
    // CTL_CODE(FILE_DEVICE_USB=0x22, USB_GET_NODE_CONNECTION_INFORMATION_EX=274, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
    // = (0x22 << 16) | (0 << 14) | (274 << 2) | 0 = 0x220448. See class documentation for caveats about
    // this WDK value, which is not available as a Windows SDK constant.
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x00220448;

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    // Fixed (non-PipeList) portion of USB_NODE_CONNECTION_INFORMATION_EX; see the offset table in
    // TryGetNodeConnectionInformation.
    private const int FixedHeaderSize = 36;

    // Size of one USB_PIPE_INFO entry (7-byte USB_ENDPOINT_DESCRIPTOR + 1-byte alignment padding +
    // 4-byte ScheduleOffset = 12 bytes); see the offset table below.
    private const int PipeInfoSize = 12;

    // Generous upper bound for expected endpoints. HID input devices usually have only 1-3; this only affects
    // buffer size and costs a few hundred bytes, so use a generous limit instead of making a second query for
    // the exact size.
    private const int MaxExpectedPipes = 30;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// Opens the specified hub device path (see <see cref="Devices.Hid.HidDeviceInfo.UsbHubDevicePath"/>) and
    /// queries connection information for the device on the given port. Returns null if any step fails
    /// (hub unavailable, IOCTL unsupported, or unexpected/short response); never lets an exception escape.
    /// </summary>
    public static UsbConnectionInfo? TryGetNodeConnectionInformation(string hubDevicePath, int portNumber)
    {
        SafeFileHandle? hubHandle = null;
        try
        {
            hubHandle = CreateFileW(
                hubDevicePath,
                GENERIC_WRITE,
                FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (hubHandle.IsInvalid)
            {
                return null;
            }

            int bufferSize = FixedHeaderSize + MaxExpectedPipes * PipeInfoSize;
            var buffer = new byte[bufferSize];
            // ConnectionIndex (first 4 bytes; see offset table below) is the only caller-supplied input. The
            // same buffer is used for input and output, following Microsoft's usbview example for
            // METHOD_BUFFERED USB IOCTLs.
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)portNumber);

            bool success = DeviceIoControl(
                hubHandle,
                IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX,
                buffer,
                (uint)buffer.Length,
                buffer,
                (uint)buffer.Length,
                out uint bytesReturned,
                IntPtr.Zero);

            if (!success || bytesReturned < FixedHeaderSize)
            {
                return null;
            }

            return ParseNodeConnectionInformation(buffer, (int)bytesReturned);
        }
        catch
        {
            // See class documentation: this layer must never let an exception escape because it relies on
            // untested assumptions about a WDK-only structure layout.
            return null;
        }
        finally
        {
            hubHandle?.Dispose();
        }
    }

    /// <summary>
    /// Layout of <c>USB_NODE_CONNECTION_INFORMATION_EX</c> (usbioctl.h), represented manually with byte offsets
    /// using standard x86/x64 alignment (no <c>#pragma pack</c> in the original header):
    /// <code>
    /// Offset  0 (4 Byte)  ULONG  ConnectionIndex
    /// Offset  4 (18 Byte) USB_DEVICE_DESCRIPTOR DeviceDescriptor (siehe unten)
    /// Offset 22 (1 Byte)  UCHAR  CurrentConfigurationValue
    /// Offset 23 (1 Byte)  UCHAR  Speed (USB_DEVICE_SPEED: 0=Low,1=Full,2=High,3=Super)
    /// Offset 24 (1 Byte)  BOOLEAN DeviceIsHub
    /// Offset 25 (1 Byte)  -- Alignment padding before the next USHORT field --
    /// Offset 26 (2 Byte)  USHORT DeviceAddress
    /// Offset 28 (4 Byte)  ULONG  NumberOfOpenPipes
    /// Offset 32 (4 Byte)  USB_CONNECTION_STATUS ConnectionStatus
    /// Offset 36            USB_PIPE_INFO PipeList[NumberOfOpenPipes] (siehe ParsePipeInfo)
    /// </code>
    /// USB_DEVICE_DESCRIPTOR (18 Byte, ab Offset 4): bLength(1) bDescriptorType(1) bcdUSB(2)
    /// bDeviceClass(1) bDeviceSubClass(1) bDeviceProtocol(1) bMaxPacketSize0(1) idVendor(2) idProduct(2)
    /// bcdDevice(2) iManufacturer(1) iProduct(1) iSerialNumber(1) bNumConfigurations(1). Not needed here
    /// because VID/PID are already obtained more reliably from <see cref="Devices.Hid.HidDeviceInfo"/>; listed
    /// only to document offsets of subsequent fields.
    /// </summary>
    private static UsbConnectionInfo ParseNodeConnectionInformation(byte[] buffer, int bytesReturned)
    {
        byte rawSpeed = buffer[23];
        ushort deviceAddress = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(26, 2));
        uint numberOfOpenPipesRaw = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28, 4));
        byte currentConfigurationValue = buffer[22];

        // Defend against a driver reporting more pipes than fit in the returned bytes. Limit iteration to the
        // complete PipeInfo entries actually present so the code never reads past the buffer.
        int availablePipeBytes = Math.Max(0, bytesReturned - FixedHeaderSize);
        int actualPipeCount = Math.Min((int)numberOfOpenPipesRaw, availablePipeBytes / PipeInfoSize);

        var endpoints = new List<UsbEndpointInfo>(actualPipeCount);
        for (int i = 0; i < actualPipeCount; i++)
        {
            int pipeOffset = FixedHeaderSize + i * PipeInfoSize;
            endpoints.Add(ParsePipeInfo(buffer, pipeOffset));
        }

        return new UsbConnectionInfo(
            Speed: MapSpeed(rawSpeed),
            DeviceAddress: deviceAddress,
            CurrentConfigurationValue: currentConfigurationValue,
            Endpoints: endpoints);
    }

    /// <summary>
    /// Layout of <c>USB_PIPE_INFO</c> (usbioctl.h), 12 bytes per entry:
    /// <code>
    /// Offset 0 (7 Byte) USB_ENDPOINT_DESCRIPTOR EndpointDescriptor:
    ///   +0 (1) bLength
    ///   +1 (1) bDescriptorType
    ///   +2 (1) bEndpointAddress
    ///   +3 (1) bmAttributes
    ///   +4 (2) wMaxPacketSize
    ///   +6 (1) bInterval
    /// Offset 7 (1 Byte) -- Alignment padding before the following ULONG field --
    /// Offset 8 (4 Byte) ULONG ScheduleOffset (not needed here)
    /// </code>
    /// </summary>
    private static UsbEndpointInfo ParsePipeInfo(byte[] buffer, int offset)
    {
        byte endpointAddress = buffer[offset + 2];
        byte bmAttributes = buffer[offset + 3];
        ushort maxPacketSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2));
        byte intervalRaw = buffer[offset + 6];

        var direction = (endpointAddress & 0x80) != 0 ? UsbEndpointDirection.In : UsbEndpointDirection.Out;
        var transferType = (UsbTransferType)(bmAttributes & 0x03);

        return new UsbEndpointInfo(
            EndpointAddress: endpointAddress,
            Direction: direction,
            TransferType: transferType,
            MaxPacketSize: maxPacketSize,
            IntervalRaw: intervalRaw,
            NominalPollingIntervalMs: CalculateNominalIntervalMs(intervalRaw, transferType));
    }

    /// <summary>
    /// Converts the raw endpoint descriptor <c>bInterval</c> to milliseconds per USB 2.0 specification
    /// section 9.6.6. For Low-/Full-Speed interrupt endpoints, <c>bInterval</c> is directly the value in
    /// milliseconds (1-255). For High-/SuperSpeed, it is a microframe exponent: interval = 2^(bInterval-1)
    /// microframes, with 1 microframe = 0.125 ms. Meaningful mainly for interrupt endpoints (as used by most
    /// HID input devices); other transfer types return the raw value unchanged as an estimate.
    /// </summary>
    private static double CalculateNominalIntervalMs(byte intervalRaw, UsbTransferType transferType)
    {
        if (intervalRaw == 0)
        {
            return 0;
        }

        // Speed is intentionally not considered here because it is unknown to this method. UsbTopologyResolver
        // handles the Low/Full- vs. High/SuperSpeed encoding one level up, where both speed and endpoint are
        // available. Use Low/Full-Speed interpretation as the base value (correct for most connected HID
        // gamepads/joysticks); see UsbTopologyResolver.ApplySpeedCorrection for High/SuperSpeed conversion.
        return transferType == UsbTransferType.Interrupt ? intervalRaw : intervalRaw;
    }

    private static UsbSpeed MapSpeed(byte rawSpeed) => rawSpeed switch
    {
        0 => UsbSpeed.Low,
        1 => UsbSpeed.Full,
        2 => UsbSpeed.High,
        3 => UsbSpeed.Super,
        _ => UsbSpeed.Unknown,
    };
}

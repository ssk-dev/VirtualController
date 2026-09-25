using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Rohes P/Invoke gegen die Windows-USB-Hub-IOCTL-Schnittstelle (<c>IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX</c>,
/// siehe <c>usbioctl.h</c> im Windows Driver Kit), um Verbindungsdetails (USB-Speed, Endpoints,
/// nominales Polling-Intervall) eines an einem Hub angeschlossenen Geraets zu ermitteln - unterhalb
/// der HID-Ebene (siehe <see cref="Devices.Hid.IHidReportSource"/>), daher als eigener P/Invoke-Layer.
///
/// WICHTIGER HINWEIS ZUR ZUVERLAESSIGKEIT: Dieser Code basiert auf oeffentlich dokumentierten, aber
/// von Microsoft nicht als stabile Public-API garantierten Struktur-Layouts (die Header <c>usbioctl.h</c>/
/// <c>usb100.h</c> sind Teil des WDK, nicht des regulaeren Windows-SDK fuer Anwendungsentwickler). Die
/// Byte-Offsets in <see cref="TryGetNodeConnectionInformation"/> wurden bewusst manuell und mit
/// dokumentierten Kommentaren statt per automatischem <see cref="StructLayoutAttribute"/>-Marshalling
/// festgelegt, um Fehler durch unerwartetes Compiler-Padding nachvollziehbar zu machen. Dieser Code
/// konnte NICHT auf echter Hardware getestet werden - jeder Aufruf ist entsprechend defensiv
/// (Try-Methode, keine Ausnahmen nach aussen) gestaltet; siehe <see cref="UsbTopologyResolver"/> fuer
/// die Einbettung mit vollstaendigem Fallback-Verhalten.
/// </summary>
internal static class UsbHubNativeInterop
{
    // CTL_CODE(FILE_DEVICE_USB=0x22, USB_GET_NODE_CONNECTION_INFORMATION_EX=274, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
    // = (0x22 << 16) | (0 << 14) | (274 << 2) | 0 = 0x220448 - siehe Klassendokumentation zur
    // Unsicherheit dieser aus dem WDK stammenden, nicht per SDK-Konstante verfuegbaren Werte.
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x00220448;

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    // Fixer (nicht-PipeList-) Teil von USB_NODE_CONNECTION_INFORMATION_EX, siehe Offset-Tabelle
    // in TryGetNodeConnectionInformation.
    private const int FixedHeaderSize = 36;

    // Groesse eines einzelnen USB_PIPE_INFO-Eintrags (7 Byte USB_ENDPOINT_DESCRIPTOR + 1 Byte
    // Alignment-Padding + 4 Byte ScheduleOffset = 12 Byte), siehe Offset-Tabelle weiter unten.
    private const int PipeInfoSize = 12;

    // Grosszuegige Obergrenze der zu erwartenden Endpoints - HID-Eingabegeraete besitzen ueblicherweise
    // nur 1-3 Endpoints; dieser Wert bestimmt lediglich die Puffergroesse und kostet nur wenige hundert
    // Byte, daher bewusst grosszuegig gewaehlt statt eine zweite Anfrage mit exakter Groesse zu benoetigen.
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
    /// Oeffnet den angegebenen Hub-Geraetepfad (siehe <see cref="Devices.Hid.HidDeviceInfo.UsbHubDevicePath"/>)
    /// und fragt die Verbindungsinformationen des Geraets am angegebenen Port ab. Gibt null zurueck,
    /// wenn irgendein Schritt fehlschlaegt (Hub nicht (mehr) erreichbar, IOCTL nicht unterstuetzt,
    /// unerwartetes/zu kurzes Antwortformat) - niemals eine Ausnahme nach aussen.
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
            // ConnectionIndex (erste 4 Byte, siehe Offset-Tabelle unten) ist der einzige vom Aufrufer
            // zu befuellende Eingabewert - derselbe Puffer wird fuer Ein- und Ausgabe verwendet (siehe
            // Microsofts eigenes "usbview"-Beispiel fuer dieses Muster bei METHOD_BUFFERED-USB-IOCTLs).
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
            // Siehe Klassendokumentation: dieser Layer darf unter keinen Umstaenden eine Ausnahme
            // nach aussen durchlassen, da er auf ungetesteten Annahmen ueber ein WDK-only-Struktur-
            // Layout beruht.
            return null;
        }
        finally
        {
            hubHandle?.Dispose();
        }
    }

    /// <summary>
    /// Layout von <c>USB_NODE_CONNECTION_INFORMATION_EX</c> (usbioctl.h), manuell mit Byte-Offsets
    /// nachgebildet (Standard-x86/x64-Alignment, kein <c>#pragma pack</c> im Original-Header):
    /// <code>
    /// Offset  0 (4 Byte)  ULONG  ConnectionIndex
    /// Offset  4 (18 Byte) USB_DEVICE_DESCRIPTOR DeviceDescriptor (siehe unten)
    /// Offset 22 (1 Byte)  UCHAR  CurrentConfigurationValue
    /// Offset 23 (1 Byte)  UCHAR  Speed (USB_DEVICE_SPEED: 0=Low,1=Full,2=High,3=Super)
    /// Offset 24 (1 Byte)  BOOLEAN DeviceIsHub
    /// Offset 25 (1 Byte)  -- Alignment-Padding vor dem naechsten USHORT-Feld --
    /// Offset 26 (2 Byte)  USHORT DeviceAddress
    /// Offset 28 (4 Byte)  ULONG  NumberOfOpenPipes
    /// Offset 32 (4 Byte)  USB_CONNECTION_STATUS ConnectionStatus
    /// Offset 36            USB_PIPE_INFO PipeList[NumberOfOpenPipes] (siehe ParsePipeInfo)
    /// </code>
    /// USB_DEVICE_DESCRIPTOR (18 Byte, ab Offset 4): bLength(1) bDescriptorType(1) bcdUSB(2)
    /// bDeviceClass(1) bDeviceSubClass(1) bDeviceProtocol(1) bMaxPacketSize0(1) idVendor(2) idProduct(2)
    /// bcdDevice(2) iManufacturer(1) iProduct(1) iSerialNumber(1) bNumConfigurations(1) - wird hier nicht
    /// benoetigt (VID/PID kommen bereits zuverlaessiger aus <see cref="Devices.Hid.HidDeviceInfo"/>),
    /// daher ausschliesslich zur korrekten Offset-Berechnung der nachfolgenden Felder aufgefuehrt.
    /// </summary>
    private static UsbConnectionInfo ParseNodeConnectionInformation(byte[] buffer, int bytesReturned)
    {
        byte rawSpeed = buffer[23];
        ushort deviceAddress = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(26, 2));
        uint numberOfOpenPipesRaw = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28, 4));
        byte currentConfigurationValue = buffer[22];

        // Verteidigung gegen einen (theoretisch moeglichen, aber unerwarteten) Treiber, der mehr Pipes
        // meldet, als tatsaechlich in den zurueckgegebenen Bytes Platz haben - Iteration wird auf die
        // tatsaechlich vorhandenen vollstaendigen PipeInfo-Eintraege begrenzt, um niemals ausserhalb
        // des Puffers zu lesen.
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
    /// Layout von <c>USB_PIPE_INFO</c> (usbioctl.h), 12 Byte je Eintrag:
    /// <code>
    /// Offset 0 (7 Byte) USB_ENDPOINT_DESCRIPTOR EndpointDescriptor:
    ///   +0 (1) bLength
    ///   +1 (1) bDescriptorType
    ///   +2 (1) bEndpointAddress
    ///   +3 (1) bmAttributes
    ///   +4 (2) wMaxPacketSize
    ///   +6 (1) bInterval
    /// Offset 7 (1 Byte) -- Alignment-Padding vor dem folgenden ULONG-Feld --
    /// Offset 8 (4 Byte) ULONG ScheduleOffset (hier nicht benoetigt)
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
    /// Rechnet den rohen <c>bInterval</c>-Wert eines Endpoint-Deskriptors gemaess USB-2.0-Spezifikation
    /// Abschnitt 9.6.6 in Millisekunden um. Bei Low-/Full-Speed-Interrupt-Endpoints ist <c>bInterval</c>
    /// direkt der Wert in Millisekunden (1-255). Bei High-/SuperSpeed-Geraeten bezeichnet er stattdessen
    /// einen Mikroframe-Exponenten: Intervall = 2^(bInterval-1) Mikroframes, 1 Mikroframe = 0,125 ms.
    /// Nur fuer Interrupt-Endpoints sinnvoll (bei HID-Eingabegeraeten praktisch immer der Fall) - fuer
    /// andere Transferarten wird der rohe Wert unveraendert als Naeherung zurueckgegeben.
    /// </summary>
    private static double CalculateNominalIntervalMs(byte intervalRaw, UsbTransferType transferType)
    {
        if (intervalRaw == 0)
        {
            return 0;
        }

        // Speed wird hier bewusst NICHT beruecksichtigt (dieser Methode nicht bekannt) - die
        // Unterscheidung Low/Full- vs. High/SuperSpeed-Kodierung erfolgt bereits eine Ebene hoeher in
        // UsbTopologyResolver, wo Speed UND Endpoint gemeinsam vorliegen. Diese Methode nimmt daher
        // vorerst die (fuer die grosse Mehrheit angeschlossener HID-Gamepads/Joysticks zutreffende)
        // Low-/Full-Speed-Interpretation als Basiswert an; siehe UsbTopologyResolver.ApplySpeedCorrection
        // fuer die High-/SuperSpeed-Korrektur.
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

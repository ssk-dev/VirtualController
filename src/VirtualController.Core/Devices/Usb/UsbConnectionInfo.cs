namespace VirtualController.Core.Devices.Usb;

/// <summary>USB-Transferart eines Endpoints, aus <c>bmAttributes</c> Bits 0-1 des USB-Endpoint-Deskriptors.</summary>
public enum UsbTransferType
{
    Control = 0,
    Isochronous = 1,
    Bulk = 2,
    Interrupt = 3,
}

/// <summary>Uebertragungsrichtung eines Endpoints, aus Bit 7 von <c>bEndpointAddress</c>.</summary>
public enum UsbEndpointDirection
{
    Out = 0,
    In = 1,
}

/// <summary>
/// Ein einzelner USB-Endpoint eines Geraets, wie vom Hub gemeldet (<c>USB_PIPE_INFO</c>/
/// <c>USB_ENDPOINT_DESCRIPTOR</c>, siehe <see cref="UsbHubNativeInterop"/>). Die meisten
/// HID-Eingabegeraete besitzen genau einen Interrupt-IN-Endpoint fuer Input-Reports.
/// </summary>
/// <param name="EndpointAddress">Rohe Endpoint-Adresse inkl. Richtungs-Bit (<c>bEndpointAddress</c>).</param>
/// <param name="Direction">Aus <paramref name="EndpointAddress"/> Bit 7 abgeleitete Richtung.</param>
/// <param name="TransferType">Aus <c>bmAttributes</c> abgeleitete Transferart - bei HID-Geraeten fast immer <see cref="UsbTransferType.Interrupt"/>.</param>
/// <param name="MaxPacketSize">Maximale Paketgroesse in Byte (<c>wMaxPacketSize</c>).</param>
/// <param name="IntervalRaw">Roher <c>bInterval</c>-Wert des Deskriptors - Bedeutung ist geschwindigkeitsabhaengig,
/// siehe <see cref="NominalPollingIntervalMs"/> fuer die bereits umgerechnete Millisekunden-Angabe.</param>
/// <param name="NominalPollingIntervalMs">Vom Geraet/Treiber nominal angefordertes Abfrageintervall in Millisekunden -
/// bei Low-/Full-Speed direkt <paramref name="IntervalRaw"/> (in ms), bei High-/SuperSpeed
/// <c>2^(IntervalRaw-1) * 0.125 ms</c> (Mikroframes), siehe USB-2.0-Spezifikation Abschnitt 9.6.6.
/// Dies ist das vom Geraet GEWUENSCHTE Intervall, NICHT die tatsaechlich erreichte Polling-Rate -
/// letztere muss separat aus echten Report-Zeitstempeln (siehe zukuenftiger <c>RawHidReportReader</c>)
/// gemessen werden, da das Betriebssystem/der Hub-Treiber dieses Intervall in der Praxis nicht immer
/// exakt einhaelt.</param>
public sealed record UsbEndpointInfo(
    byte EndpointAddress,
    UsbEndpointDirection Direction,
    UsbTransferType TransferType,
    ushort MaxPacketSize,
    byte IntervalRaw,
    double NominalPollingIntervalMs);

/// <summary>
/// Vom uebergeordneten USB-Hub gemeldete Verbindungsinformationen eines Geraets (siehe
/// <see cref="UsbTopologyResolver"/>/<see cref="UsbHubNativeInterop"/>) - Grundlage fuer die
/// Transport-Kennzahlen des geplanten Geraete-Benchmarks (USB Speed, Endpoint, Nominal Polling Rate).
/// </summary>
/// <param name="Speed">Signalisierungsgeschwindigkeit, mit der das Geraet aktuell verbunden ist.</param>
/// <param name="DeviceAddress">Vom Hub vergebene USB-Bus-Adresse (rein diagnostisch).</param>
/// <param name="CurrentConfigurationValue">Aktive USB-Konfiguration des Geraets.</param>
/// <param name="Endpoints">Alle vom Hub gemeldeten, aktuell offenen Endpoints (Pipes) des Geraets.</param>
public sealed record UsbConnectionInfo(
    UsbSpeed Speed,
    ushort DeviceAddress,
    byte CurrentConfigurationValue,
    IReadOnlyList<UsbEndpointInfo> Endpoints)
{
    /// <summary>Der fuer Eingabegeraete relevante Haupt-Endpoint: der erste Interrupt-IN-Endpoint, falls
    /// vorhanden - sonst der erste Endpoint ueberhaupt. Vereinfachung fuer die Benchmark-Anzeige, die
    /// (anders als ein vollstaendiger USB-Analyzer) nur EINEN "Nominal Polling Rate"-Wert je Geraet zeigt.</summary>
    public UsbEndpointInfo? PrimaryInputEndpoint =>
        Endpoints.FirstOrDefault(e => e.TransferType == UsbTransferType.Interrupt && e.Direction == UsbEndpointDirection.In)
        ?? Endpoints.FirstOrDefault();
}

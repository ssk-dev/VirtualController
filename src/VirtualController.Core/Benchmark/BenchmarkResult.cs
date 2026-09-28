using System.Runtime.InteropServices;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices;
using VirtualController.Core.Devices.Hid;
using VirtualController.Core.Devices.Usb;

namespace VirtualController.Core.Benchmark;

/// <summary>Identification-Kennzahlen (siehe geplantes Benchmark-Feature) - stammen aus
/// <see cref="HidDeviceInfo"/> (Phase 2), ergaenzt um die vom Anwender vergebenen/erkannten
/// <see cref="PhysicalDeviceInfo"/>-Angaben (Api/Slot), da letztere fuer die Zuordnung zur restlichen
/// Anwendungskonfiguration relevant sind, HidDeviceInfo diese aber nicht kennt.</summary>
/// <param name="ReleaseNumberBcd">Siehe <see cref="HidDeviceInfo.ReleaseNumberBcd"/> - NUR eine
/// Naeherung fuer eine Firmware-Version, nicht garantiert korrekt (siehe Klassendokumentation von
/// <see cref="HidDeviceInfoReader"/>).</param>
public sealed record BenchmarkIdentificationInfo(
    string DeviceDisplayName,
    InputApi Api,
    int ApiSlot,
    ushort? VendorId,
    ushort? ProductId,
    string? Manufacturer,
    string? ProductName,
    string? SerialNumber,
    int ReleaseNumberBcd,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength);

/// <summary>Ein einzelner USB-Endpoint innerhalb der Transport-Kennzahlen, siehe <see cref="UsbEndpointInfo"/>
/// (Phase 2) - hier ohne den dortigen rohen <c>IntervalRaw</c>-Wert, da <see cref="NominalPollingIntervalMs"/>
/// bereits die daraus abgeleitete, direkt interpretierbare Millisekunden-Angabe ist.</summary>
public sealed record BenchmarkEndpointInfo(
    byte EndpointAddress,
    UsbEndpointDirection Direction,
    UsbTransferType TransferType,
    ushort MaxPacketSize,
    double NominalPollingIntervalMs);

/// <param name="Supported">False, wenn die USB-Hub-Topologie-Abfrage (Phase 2, <see cref="UsbTopologyResolver"/>)
/// fehlgeschlagen ist (z.B. fehlende Berechtigung, oder Geraet nicht direkt an einem erreichbaren Hub) -
/// in diesem Fall sind alle uebrigen Felder dieses Datensatzes ohne Aussagekraft (Standardwerte).</param>
public sealed record BenchmarkTransportInfo(
    bool Supported,
    UsbSpeed? Speed,
    ushort? DeviceAddress,
    byte? CurrentConfigurationValue,
    IReadOnlyList<BenchmarkEndpointInfo> Endpoints);

/// <summary>Ein einzelnes, waehrend der Geraete-Aufloesung (<see cref="HidDeviceInfoReader.TryResolveAll"/>)
/// gefundenes HID-Interface mit passender Vendor-/Product-ID - siehe <see cref="BenchmarkDiagnosticsInfo"/>.
/// Composite-Geraete (z.B. Joysticks mit mehreren HID-Top-Level-Collections unter derselben VID/PID)
/// koennen hier mehr als einen Eintrag liefern, von denen ggf. nur einer tatsaechlich Input-Reports
/// sendet - diese Kennzahl macht genau diesen Fall (statt eines stillen "0 Samples")
/// im Benchmark-Ergebnis sichtbar.</summary>
/// <param name="IsResolved">Ob dies das Interface ist, dessen <see cref="IHidReportSource"/> tatsaechlich
/// fuer diese Sitzung geoeffnet wurde (siehe <see cref="BenchmarkSession.TryCreate"/> - aktuell bewusst
/// immer der erste gefundene Kandidat, siehe dortige Dokumentation).</param>
public sealed record BenchmarkHidCandidateInfo(
    string DevicePath,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength,
    bool IsResolved);

/// <summary>Immer (unabhaengig vom Erfolg der restlichen Messung) befuellte Diagnose-Angaben zur
/// HID-Geraete-Aufloesung - Grundlage fuer die Fehlersuche bei Sitzungen, die trotz erfolgreich
/// geoeffneter Report-Quelle durchgehend 0 Samples liefern (siehe <see cref="Candidates"/>: mehr als ein
/// Eintrag deutet auf ein Composite-Geraet hin, bei dem ggf. das falsche Interface geoeffnet wurde).</summary>
/// <param name="Candidates">Alle zum Zeitpunkt der Sitzungserstellung gefundenen HID-Interfaces mit
/// passender Vendor-/Product-ID, siehe <see cref="HidDeviceInfoReader.TryResolveAll"/>.</param>
public sealed record BenchmarkDiagnosticsInfo(
    IReadOnlyList<BenchmarkHidCandidateInfo> Candidates);

/// <summary>Betriebssystem-Kenndaten zum Zeitpunkt der Messung - vom Nutzer explizit gewuenscht (siehe
/// Session-Vorgabe), da dieselbe Hardware unter verschiedenen Windows-Versionen/-Builds unterschiedliches
/// Timing-Verhalten zeigen kann (z.B. durch geaenderte USB-Treiber/HID-Stack-Implementierungen).</summary>
public sealed record BenchmarkEnvironmentInfo(
    string OsDescription,
    string OsVersion,
    Architecture OsArchitecture,
    string FrameworkDescription);

/// <summary>
/// Vollstaendiges Ergebnis einer einzelnen Benchmark-Sitzung (siehe <see cref="BenchmarkSession"/>) -
/// die Struktur, die als JSON nach "%AppData%\VirtualController\Benchmark\benchmark-device-{marke}-{name}.json"
/// exportiert wird (siehe <see cref="BenchmarkSession.BuildDefaultFilePath"/>).
/// </summary>
/// <param name="SchemaVersion">Format-Version dieser Exportdatei, fuer eine spaetere abwaertskompatible
/// Weiterentwicklung des JSON-Formats (aktuell 2 - Version 2 ergaenzt <see cref="Diagnostics"/>).</param>
/// <param name="Signal">Je Achse ein Eintrag (siehe <see cref="SignalAxisResult"/>) - als Liste statt
/// eines nach <see cref="HidAxisUsage"/> geschluesselten Dictionary exportiert, da <see cref="SignalAxisResult"/>
/// das jeweilige <see cref="SignalAxisResult.Axis"/> bereits selbst enthaelt und ein enum-geschluesseltes
/// Dictionary sich mit dem Standard-JSON-Serializer nicht ohne Weiteres lesbar (Enum-Name statt Zahl als
/// Objektschluessel) exportieren laesst.</param>
/// <param name="Diagnostics">Immer befuellte Fehlersuche-Angaben zur HID-Geraete-Aufloesung (siehe
/// <see cref="BenchmarkDiagnosticsInfo"/>) - unabhaengig davon, ob die restliche Messung erfolgreich war.</param>
public sealed record BenchmarkResult(
    int SchemaVersion,
    DateTime GeneratedAtUtc,
    double DurationSeconds,
    BenchmarkIdentificationInfo Identification,
    BenchmarkTransportInfo Transport,
    PollingRateResult Timing,
    LatencyResult Latency,
    ReliabilityResult Reliability,
    IReadOnlyList<SignalAxisResult> Signal,
    BenchmarkEnvironmentInfo Environment,
    BenchmarkDiagnosticsInfo Diagnostics);

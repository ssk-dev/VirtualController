using System.Runtime.InteropServices;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices;
using VirtualController.Core.Devices.Hid;
using VirtualController.Core.Devices.Usb;

namespace VirtualController.Core.Benchmark;

/// <summary>
/// Orchestriert eine einzelne Benchmark-Sitzung eines physischen HID-Geraets: oeffnet die rohe
/// Report-Quelle (<see cref="HidDeviceInfoReader"/>/<see cref="RawHidReportReader"/>, Phase 2/3), fuettert
/// jeden eintreffenden Report an alle Metrics-Klassen (<see cref="PollingRateMetrics"/>/<see cref="LatencyMetrics"/>/
/// <see cref="ReliabilityMetrics"/>/<see cref="SignalMetrics"/>) und liefert am Ende ein vollstaendiges
/// <see cref="BenchmarkResult"/> zum Export (siehe <see cref="BenchmarkJsonExporter"/>).
///
/// Laeuft, wie <see cref="Logging.DeviceStateLogger"/>, bis <see cref="Stop"/> aufgerufen wird oder das
/// Geraet die Verbindung verliert - unabhaengig von jeglicher UI-Sichtbarkeit.
///
/// WICHTIGE EINSCHRAENKUNG: Dieses Feature ist bewusst NUR fuer HID-Geraete verfuegbar (nicht fuer reine
/// XInput-Geraete ohne zugehoerigen HID-Pfad), da die Timing-/Latenz-/Reliability-/Signal-Kennzahlen alle
/// auf rohen HID-Reports beruhen (siehe <see cref="RawHidReportReader"/>) - dies unterscheidet den
/// Benchmark bewusst von <see cref="Logging.DeviceStateLogger"/>, der auf der API-unabhaengigen
/// <see cref="IDeviceReader"/>/<see cref="DeviceState"/>-Ebene arbeitet. <see cref="TryCreate"/> gibt daher
/// null zurueck, wenn sich kein passendes HID-Geraet zum uebergebenen <see cref="PhysicalDeviceInfo"/>
/// aufloesen laesst (z.B. weil <see cref="PhysicalDeviceInfo.VendorId"/>/<see cref="PhysicalDeviceInfo.ProductId"/>
/// nicht ermittelbar waren, siehe <see cref="PhysicalDeviceInfo.VendorId"/>-Dokumentation).
/// </summary>
public sealed class BenchmarkSession : IDisposable
{
    private readonly PhysicalDeviceInfo _device;
    private readonly HidDeviceInfo _hidInfo;
    private readonly UsbConnectionInfo? _usbInfo;
    private readonly RawHidReportReader _reader;
    private readonly HidAxisReportParser? _axisParser;

    private readonly PollingRateMetrics _pollingRateMetrics = new();
    private readonly LatencyMetrics _latencyMetrics;
    private readonly ReliabilityMetrics _reliabilityMetrics;
    private readonly SignalMetrics? _signalMetrics;

    private readonly System.Diagnostics.Stopwatch _durationStopwatch = new();
    private bool _disposed;

    /// <summary>Schuetzt alle Metrics-Zugriffe (siehe <see cref="OnSampleReceived"/> und <see cref="BuildResult"/>)
    /// gegen gleichzeitigen Zugriff: <see cref="OnSampleReceived"/> laeuft auf dem internen Lese-Thread des
    /// <see cref="RawHidReportReader"/>, waehrend <see cref="GetSnapshot"/> (Echtzeit-Anzeige, z.B. aus einem
    /// UI-Timer) und <see cref="Stop"/> vom UI-Thread aus aufgerufen werden koennen, waehrend die Sitzung noch
    /// laeuft - ohne diese Sperre koennte z.B. das interne Histogramm von <see cref="SignalMetrics"/> waehrend
    /// einer gleichzeitigen Aufzaehlung (<c>ComputeResult</c>) veraendert werden und eine
    /// <see cref="InvalidOperationException"/> ("Collection was modified") auslösen.</summary>
    private readonly object _metricsLock = new();

    /// <summary>Wird auf dem internen Lese-Thread ausgeloest (siehe <see cref="RawHidReportReader.ReadFailed"/>),
    /// z.B. wenn das Geraet waehrend einer laufenden Sitzung getrennt wird - NICHT bei regulaerem
    /// <see cref="Stop"/>-Aufruf.</summary>
    public event Action<Exception>? BenchmarkFailed;

    /// <summary>Ob die Sitzung aktuell laeuft - siehe <see cref="RawHidReportReader.IsRunning"/>.</summary>
    public bool IsRunning => _reader.IsRunning;

    private BenchmarkSession(PhysicalDeviceInfo device, HidDeviceInfo hidInfo, UsbConnectionInfo? usbInfo,
        RawHidReportReader reader, HidAxisReportParser? axisParser)
    {
        _device = device;
        _hidInfo = hidInfo;
        _usbInfo = usbInfo;
        _reader = reader;
        _axisParser = axisParser;

        double? nominalIntervalMs = usbInfo?.PrimaryInputEndpoint?.NominalPollingIntervalMs;
        _latencyMetrics = new LatencyMetrics(nominalIntervalMs);
        _reliabilityMetrics = new ReliabilityMetrics(nominalIntervalMs);
        _signalMetrics = axisParser is { AvailableAxes.Count: > 0 } ? new SignalMetrics(axisParser.AvailableAxes) : null;

        _reader.SampleReceived += OnSampleReceived;
        _reader.ReadFailed += OnReadFailed;
    }

    /// <summary>
    /// Loest anhand von <see cref="PhysicalDeviceInfo.VendorId"/>/<see cref="PhysicalDeviceInfo.ProductId"/>
    /// das zugehoerige HID-Geraet auf und oeffnet dessen Report-Quelle - siehe Klassendokumentation fuer die
    /// Faelle, in denen dies fehlschlaegt (kein HID-Geraet, keine VID/PID bekannt, Geraet bereits exklusiv
    /// geoeffnet). Bei mehreren gleichzeitig angeschlossenen, identischen Geraeten (gleiche VID/PID) wird -
    /// wie bei <see cref="HidDeviceInfoReader.TryResolve"/> dokumentiert - bewusst das erste gefundene
    /// verwendet, da <see cref="PhysicalDeviceInfo"/> aktuell keine Seriennummer fuehrt, ueber die sich
    /// eindeutig disambiguieren liesse.
    /// </summary>
    public static BenchmarkSession? TryCreate(PhysicalDeviceInfo device)
    {
        if (device.VendorId is not { } vendorId || device.ProductId is not { } productId)
        {
            return null;
        }

        var hidInfo = HidDeviceInfoReader.TryResolve(vendorId, productId);
        if (hidInfo is null)
        {
            return null;
        }

        var source = HidDeviceInfoReader.TryOpenReportSource(hidInfo.DevicePath);
        if (source is null)
        {
            return null;
        }

        var usbInfo = UsbTopologyResolver.TryResolve(hidInfo);
        var axisParser = HidAxisReportParser.TryCreate(hidInfo.DevicePath);
        var reader = new RawHidReportReader(source);

        return new BenchmarkSession(device, hidInfo, usbInfo, reader, axisParser);
    }

    public void Start()
    {
        _durationStopwatch.Restart();
        _reader.Start();
    }

    /// <summary>Stoppt die laufende Sitzung und liefert das vollstaendige Ergebnis - kann nach dem Stoppen
    /// beliebig oft erneut aufgerufen werden (liefert stets denselben, bereits berechneten Endstand),
    /// solange <see cref="Dispose"/> noch nicht aufgerufen wurde.</summary>
    public BenchmarkResult Stop()
    {
        _reader.Stop();
        _durationStopwatch.Stop();
        return BuildResult();
    }

    private void OnSampleReceived(HidReportSample sample)
    {
        lock (_metricsLock)
        {
            _pollingRateMetrics.Add(sample);
            _latencyMetrics.Add(sample);
            _reliabilityMetrics.Add(sample);

            if (_signalMetrics is not null && _axisParser is not null
                && _axisParser.TryParse(sample.Report, out var rawAxisValues))
            {
                _signalMetrics.Add(rawAxisValues);
            }
        }
    }

    private void OnReadFailed(Exception ex) => BenchmarkFailed?.Invoke(ex);

    private BenchmarkResult BuildResult()
    {
        var identification = new BenchmarkIdentificationInfo(
            DeviceDisplayName: _device.DisplayName,
            Api: _device.Api,
            ApiSlot: _device.ApiSlot,
            VendorId: _hidInfo.VendorId,
            ProductId: _hidInfo.ProductId,
            Manufacturer: _hidInfo.Manufacturer,
            ProductName: _hidInfo.ProductName,
            SerialNumber: _hidInfo.SerialNumber,
            ReleaseNumberBcd: _hidInfo.ReleaseNumberBcd,
            MaxInputReportLength: _hidInfo.MaxInputReportLength,
            MaxOutputReportLength: _hidInfo.MaxOutputReportLength,
            MaxFeatureReportLength: _hidInfo.MaxFeatureReportLength);

        var transport = _usbInfo is { } usb
            ? new BenchmarkTransportInfo(
                Supported: true,
                Speed: usb.Speed,
                DeviceAddress: usb.DeviceAddress,
                CurrentConfigurationValue: usb.CurrentConfigurationValue,
                Endpoints: usb.Endpoints
                    .Select(e => new BenchmarkEndpointInfo(e.EndpointAddress, e.Direction, e.TransferType, e.MaxPacketSize, e.NominalPollingIntervalMs))
                    .ToList())
            : new BenchmarkTransportInfo(Supported: false, Speed: null, DeviceAddress: null, CurrentConfigurationValue: null, Endpoints: Array.Empty<BenchmarkEndpointInfo>());

        var environment = new BenchmarkEnvironmentInfo(
            OsDescription: RuntimeInformation.OSDescription,
            OsVersion: Environment.OSVersion.VersionString,
            OsArchitecture: RuntimeInformation.OSArchitecture,
            FrameworkDescription: RuntimeInformation.FrameworkDescription);

        PollingRateResult timing;
        LatencyResult latency;
        ReliabilityResult reliability;
        List<SignalAxisResult> signal;

        lock (_metricsLock)
        {
            timing = _pollingRateMetrics.ComputeResult();
            latency = _latencyMetrics.ComputeResult();
            reliability = _reliabilityMetrics.ComputeResult();
            signal = _signalMetrics?.ComputeResult().Values.ToList() ?? new List<SignalAxisResult>();
        }

        return new BenchmarkResult(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTime.UtcNow,
            DurationSeconds: _durationStopwatch.Elapsed.TotalSeconds,
            Identification: identification,
            Transport: transport,
            Timing: timing,
            Latency: latency,
            Reliability: reliability,
            Signal: signal,
            Environment: environment);
    }

    /// <summary>Liefert eine Momentaufnahme des bisherigen Ergebnisses, WAEHREND die Sitzung noch laeuft -
    /// fuer eine Echtzeit-Anzeige (siehe geplantes Benchmark-Popup-Fenster). Im Gegensatz zu <see cref="Stop"/>
    /// wird der Lese-Thread dabei nicht angehalten; <see cref="BenchmarkResult.DurationSeconds"/> spiegelt die
    /// bisher verstrichene Zeit wider (der Stopwatch laeuft weiter). Kann beliebig oft waehrend einer laufenden
    /// Sitzung aufgerufen werden, typischerweise periodisch aus einem UI-Timer.</summary>
    public BenchmarkResult GetSnapshot() => BuildResult();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.SampleReceived -= OnSampleReceived;
        _reader.ReadFailed -= OnReadFailed;
        _reader.Dispose();
    }
}

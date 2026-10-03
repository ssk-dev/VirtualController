using System.Runtime.InteropServices;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices;
using VirtualController.Core.Devices.Hid;
using VirtualController.Core.Devices.Usb;

namespace VirtualController.Core.Benchmark;

/// <summary>
/// Orchestrates one benchmark session for a physical HID device: opens its raw report source
/// (<see cref="HidDeviceInfoReader"/>/<see cref="RawHidReportReader"/>, phases 2/3), feeds each report to all
/// metric classes (<see cref="PollingRateMetrics"/>, <see cref="LatencyMetrics"/>,
/// <see cref="ReliabilityMetrics"/>, <see cref="SignalMetrics"/>), and returns a complete
/// <see cref="BenchmarkResult"/> for export (see <see cref="BenchmarkJsonExporter"/>).
///
/// Like <see cref="Logging.DeviceStateLogger"/>, runs until <see cref="Stop"/> is called or the device disconnects,
/// independently of UI visibility.
///
/// Important limitation: this feature is available only for HID devices, not XInput-only devices without an
/// associated HID path, because all timing/latency/reliability/signal metrics rely on raw HID reports (see
/// <see cref="RawHidReportReader"/>). This differs from <see cref="Logging.DeviceStateLogger"/>, which works
/// at the API-independent <see cref="IDeviceReader"/>/<see cref="DeviceState"/> level. Therefore,
/// <see cref="TryCreate"/> returns null when no matching HID device can be resolved for the supplied
/// <see cref="PhysicalDeviceInfo"/>, e.g. when its <see cref="PhysicalDeviceInfo.VendorId"/> or
/// <see cref="PhysicalDeviceInfo.ProductId"/> is unavailable.
/// </summary>
public sealed class BenchmarkSession : IDisposable
{
    private readonly PhysicalDeviceInfo _device;
    private readonly HidDeviceInfo _hidInfo;
    private readonly IReadOnlyList<HidDeviceInfo> _hidCandidates;
    private readonly UsbConnectionInfo? _usbInfo;
    private readonly RawHidReportReader _reader;
    private readonly HidAxisReportParser? _axisParser;

    private readonly PollingRateMetrics _pollingRateMetrics = new();
    private readonly LatencyMetrics _latencyMetrics;
    private readonly ReliabilityMetrics _reliabilityMetrics;
    private readonly SignalMetrics? _signalMetrics;

    private readonly System.Diagnostics.Stopwatch _durationStopwatch = new();
    private bool _disposed;

    /// <summary>Protects metric access in <see cref="OnSampleReceived"/> and <see cref="BuildResult"/> from
    /// concurrent modification. OnSampleReceived runs on <see cref="RawHidReportReader"/>'s reader thread,
    /// while <see cref="GetSnapshot"/> (e.g. from a UI timer) and <see cref="Stop"/> may run on the UI thread.
    /// Without this lock, <see cref="SignalMetrics"/>' internal histogram could change while
    /// <c>ComputeResult</c> enumerates it, causing an <see cref="InvalidOperationException"/>.</summary>
    private readonly object _metricsLock = new();

    /// <summary>Raised on the internal reader thread (see <see cref="RawHidReportReader.ReadFailed"/>), e.g. if
    /// the device disconnects during a session. Not raised during a normal <see cref="Stop"/> call.</summary>
    public event Action<Exception>? BenchmarkFailed;

    /// <summary>Whether the session is currently running; see <see cref="RawHidReportReader.IsRunning"/>.</summary>
    public bool IsRunning => _reader.IsRunning;

    private BenchmarkSession(PhysicalDeviceInfo device, HidDeviceInfo hidInfo, IReadOnlyList<HidDeviceInfo> hidCandidates,
        UsbConnectionInfo? usbInfo, RawHidReportReader reader, HidAxisReportParser? axisParser)
    {
        _device = device;
        _hidInfo = hidInfo;
        _hidCandidates = hidCandidates;
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
    /// Resolves the associated HID device from <see cref="PhysicalDeviceInfo.VendorId"/> and
    /// <see cref="PhysicalDeviceInfo.ProductId"/>, then opens its report source. See the class documentation
    /// for failure cases (no HID device, unknown VID/PID, or device already opened exclusively).
    ///
    /// When a composite device has multiple HID interfaces with the same VID/PID (e.g. a Saitek X-56 Rhino
    /// stick with separate joystick-input and vendor/firmware interfaces), do not blindly use the first one.
    /// It may be a non-input interface with no axes, resulting in zero samples. Instead, check each candidate
    /// with <see cref="HidAxisReportParser"/> for declared Generic Desktop axes (see <see cref="HidAxisUsage"/>)
    /// and prefer the first candidate with at least one axis, a reliable indicator of a joystick/gamepad input
    /// interface. If none declares axes (e.g. a keyboard/button-only device), fall back to the first candidate
    /// because <see cref="PhysicalDeviceInfo"/> currently has no serial number to disambiguate them. Include
    /// every candidate in the result for diagnostics, regardless of which one was selected (see
    /// <see cref="BenchmarkDiagnosticsInfo"/>).
    /// </summary>
    public static BenchmarkSession? TryCreate(PhysicalDeviceInfo device)
    {
        if (device.VendorId is not { } vendorId || device.ProductId is not { } productId)
        {
            return null;
        }

        var hidCandidates = HidDeviceInfoReader.TryResolveAll(vendorId, productId);
        if (hidCandidates.Count == 0)
        {
            return null;
        }

        var hidInfo = hidCandidates[0];
        HidAxisReportParser? axisParser = null;

        foreach (var candidate in hidCandidates)
        {
            var candidateAxisParser = HidAxisReportParser.TryCreate(candidate.DevicePath);
            if (candidateAxisParser is { AvailableAxes.Count: > 0 })
            {
                hidInfo = candidate;
                axisParser = candidateAxisParser;
                break;
            }
        }

        var source = HidDeviceInfoReader.TryOpenReportSource(hidInfo.DevicePath);
        if (source is null)
        {
            return null;
        }

        var usbInfo = UsbTopologyResolver.TryResolve(hidInfo);
        axisParser ??= HidAxisReportParser.TryCreate(hidInfo.DevicePath);
        var reader = new RawHidReportReader(source);

        return new BenchmarkSession(device, hidInfo, hidCandidates, usbInfo, reader, axisParser);
    }

    public void Start()
    {
        _durationStopwatch.Restart();
        _reader.Start();
    }

    /// <summary>Stops the session and returns its complete result. Can be called repeatedly after stopping,
    /// returning the same final result until <see cref="Dispose"/> is called.</summary>
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

        var diagnostics = new BenchmarkDiagnosticsInfo(
            Candidates: _hidCandidates
                .Select(c => new BenchmarkHidCandidateInfo(
                    DevicePath: c.DevicePath,
                    MaxInputReportLength: c.MaxInputReportLength,
                    MaxOutputReportLength: c.MaxOutputReportLength,
                    MaxFeatureReportLength: c.MaxFeatureReportLength,
                    IsResolved: c.DevicePath == _hidInfo.DevicePath))
                .ToList());

        return new BenchmarkResult(
            SchemaVersion: 2,
            GeneratedAtUtc: DateTime.UtcNow,
            DurationSeconds: _durationStopwatch.Elapsed.TotalSeconds,
            Identification: identification,
            Transport: transport,
            Timing: timing,
            Latency: latency,
            Reliability: reliability,
            Signal: signal,
            Environment: environment,
            Diagnostics: diagnostics);
    }

    /// <summary>Returns a snapshot of results while the session is still running, for a real-time display.
    /// Unlike <see cref="Stop"/>, this does not stop the reader thread; <see cref="BenchmarkResult.DurationSeconds"/>
    /// reflects elapsed time while the stopwatch continues. Can be called repeatedly during a session, typically
    /// from a UI timer.</summary>
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

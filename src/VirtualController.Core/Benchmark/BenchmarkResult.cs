using System.Runtime.InteropServices;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices;
using VirtualController.Core.Devices.Hid;
using VirtualController.Core.Devices.Usb;

namespace VirtualController.Core.Benchmark;

/// <summary>Device identification metrics from <see cref="HidDeviceInfo"/> (phase 2), augmented with the
/// user-facing/detected <see cref="PhysicalDeviceInfo"/> API and slot data, which are relevant to the rest
/// of the application configuration but are not known to HidDeviceInfo.</summary>
/// <param name="ReleaseNumberBcd">See <see cref="HidDeviceInfo.ReleaseNumberBcd"/>. This is only an estimate
/// of the firmware version and is not guaranteed to be accurate (see <see cref="HidDeviceInfoReader"/> docs).</param>
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

/// <summary>A USB endpoint in the transport metrics (see <see cref="UsbEndpointInfo"/>, phase 2), omitting its
/// raw <c>IntervalRaw</c> value because <see cref="NominalPollingIntervalMs"/> is the derived, directly
/// interpretable value in milliseconds.</summary>
public sealed record BenchmarkEndpointInfo(
    byte EndpointAddress,
    UsbEndpointDirection Direction,
    UsbTransferType TransferType,
    ushort MaxPacketSize,
    double NominalPollingIntervalMs);

/// <param name="Supported">False if USB hub topology lookup (phase 2, <see cref="UsbTopologyResolver"/>) failed,
/// e.g. because of missing permissions or because the device is not directly connected to a reachable hub.
/// In that case, the remaining fields contain default values and are not meaningful.</param>
public sealed record BenchmarkTransportInfo(
    bool Supported,
    UsbSpeed? Speed,
    ushort? DeviceAddress,
    byte? CurrentConfigurationValue,
    IReadOnlyList<BenchmarkEndpointInfo> Endpoints);

/// <summary>A HID interface with a matching vendor/product ID found during device resolution
/// (<see cref="HidDeviceInfoReader.TryResolveAll"/>; see <see cref="BenchmarkDiagnosticsInfo"/>). Composite
/// devices, such as joysticks with multiple HID top-level collections under one VID/PID, may produce several
/// entries, though only one may send input reports. This metric makes that case visible in the benchmark
/// result instead of silently reporting zero samples.</summary>
/// <param name="IsResolved">Whether this is the interface whose <see cref="IHidReportSource"/> was opened for
/// the session (see <see cref="BenchmarkSession.TryCreate"/>; currently the first suitable candidate).</param>
public sealed record BenchmarkHidCandidateInfo(
    string DevicePath,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength,
    bool IsResolved);

/// <summary>HID resolution diagnostics, always populated regardless of whether the rest of the measurement
/// succeeds. Helps troubleshoot sessions that return zero samples despite opening a report source; multiple
/// <see cref="Candidates"/> may indicate a composite device where the wrong interface was opened.</summary>
/// <param name="Candidates">All HID interfaces with matching vendor/product IDs found when the session was
/// created (see <see cref="HidDeviceInfoReader.TryResolveAll"/>).</param>
public sealed record BenchmarkDiagnosticsInfo(
    IReadOnlyList<BenchmarkHidCandidateInfo> Candidates);

/// <summary>Operating system details at measurement time, included at the user's request because the same
/// hardware can have different timing behavior across Windows versions/builds, e.g. due to changes in USB
/// drivers or the HID stack.</summary>
public sealed record BenchmarkEnvironmentInfo(
    string OsDescription,
    string OsVersion,
    Architecture OsArchitecture,
    string FrameworkDescription);

/// <summary>
/// Complete result of one benchmark session (see <see cref="BenchmarkSession"/>), exported as JSON to
/// "%AppData%\VirtualController\Benchmark\benchmark-device-{brand}-{name}.json" (see
/// <see cref="BenchmarkSession.BuildDefaultFilePath"/>).
/// </summary>
/// <param name="SchemaVersion">Export format version for future backward-compatible JSON changes. Currently 2;
/// version 2 adds <see cref="Diagnostics"/>.</param>
/// <param name="Signal">One entry per axis (see <see cref="SignalAxisResult"/>), exported as a list rather than
/// a dictionary keyed by <see cref="HidAxisUsage"/>. Each <see cref="SignalAxisResult"/> already contains its
/// <see cref="SignalAxisResult.Axis"/>, and the default JSON serializer does not produce readable enum-keyed dictionaries.</param>
/// <param name="Diagnostics">HID resolution diagnostics, always populated regardless of whether the rest of
/// the measurement succeeded (see <see cref="BenchmarkDiagnosticsInfo"/>).</param>
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

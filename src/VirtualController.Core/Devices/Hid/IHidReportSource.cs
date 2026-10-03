namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// One timestamped raw HID input report. The timestamp uses <see cref="System.Diagnostics.Stopwatch"/> ticks
/// (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>) rather than <see cref="DateTime"/> because timing
/// metrics (jitter and interval statistics) need only the relative, monotonic, high-resolution difference
/// between reports, not wall-clock time.
/// </summary>
/// <param name="Data">Raw report bytes, exactly as many as were read; see
/// <see cref="IHidReportSource.InputReportLength"/> for the nominal length reported by the device.</param>
/// <param name="TimestampTicks">Capture time in Stopwatch ticks, recorded immediately after the blocking read
/// returns (see <see cref="CaptureTimestampTicks"/>).</param>
public sealed record HidReport(byte[] Data, long TimestampTicks)
{
    /// <summary>Captures the timestamp for a newly read report in one place, in case the time source changes later.</summary>
    public static long CaptureTimestampTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
}

/// <summary>
/// Provides timestamped raw HID input reports from a physical device through a narrow abstraction over the
/// current access library (<c>HidSharp</c>; see <see cref="HidDeviceInfoReader"/>/<see cref="HidSharpReportSource"/>).
///
/// This separation keeps benchmark/metrics code (timing, latency, reliability in
/// <c>VirtualController.Core.Benchmark</c>) independent of HidSharp types. Replacing the underlying
/// implementation later, e.g. with native P/Invoke, would not require changes above this interface.
///
/// Implementations must support repeated <see cref="ReadReport"/> calls from one dedicated background thread
/// (like <see cref="IDeviceReader.Poll"/>), but need not be callable concurrently from multiple threads;
/// a benchmark always reads sequentially from one thread.
/// </summary>
public interface IHidReportSource : IDisposable
{
    /// <summary>Nominal input report length in bytes reported by the device (HidD_GetCaps/
    /// <c>MaxInputReportLength</c>), available before the first <see cref="ReadReport"/> call for the "Report Size" metric.</summary>
    int InputReportLength { get; }

    /// <summary>
    /// Blocks until the next input report arrives or <paramref name="cancellationToken"/> is canceled, then
    /// returns the report with a timestamp.
    /// </summary>
    /// <exception cref="OperationCanceledException">If canceled through <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="IOException">If the connection is lost while reading (device disconnected).</exception>
    HidReport ReadReport(CancellationToken cancellationToken);
}

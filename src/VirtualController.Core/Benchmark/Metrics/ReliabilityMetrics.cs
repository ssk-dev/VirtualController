namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="EstimatedDroppedReports">Approximate number of missed reports, estimated from time gaps much
/// larger than the nominal interval (see <see cref="ReliabilityMetrics"/> docs). This is a heuristic, not an exact count.</param>
/// <param name="ConsecutiveDuplicateReportCount">Number of consecutive byte-identical reports. Informational only:
/// this is normal for most HID gamepads/joysticks, which report continuously even when the state does not change.</param>
/// <param name="SequenceErrorsSupported">Always false. Generic, vendor-independent HID sequence numbers do not
/// exist, so this value is not calculated.</param>
/// <param name="UsbErrorsSupported">Always false. USB bus errors (CRC/STALL/etc.) are handled by the host
/// controller driver and cannot be read from user-mode HID.</param>
/// <param name="TotalReportCount">Total reports received during the measurement.</param>
public sealed record ReliabilityResult(
    long EstimatedDroppedReports,
    long ConsecutiveDuplicateReportCount,
    bool SequenceErrorsSupported,
    bool UsbErrorsSupported,
    long TotalReportCount);

/// <summary>
/// Computes reliability metrics from a stream of <see cref="Devices.Hid.HidReportSample"/> values.
///
/// Limitations (reported transparently rather than presenting unsupported values as real measurements):
///
/// - <b>Dropped reports</b>: HID report data has no generic, vendor-independent sequence number. Missed reports
///   are estimated heuristically from unusually large time gaps; a report interval much larger than nominal may
///   indicate one or more missed polls. This is an estimate, not an exact count, and is impossible without a
///   known nominal interval (e.g. when phase 2 USB topology lookup fails; see
///   <see cref="ReliabilityMetrics(double?, double)"/>).
///
/// - <b>Duplicate reports</b>: consecutive byte-identical reports are counted but are not an error for most HID
///   gamepads/joysticks. Many devices send one report per polling interval whether or not the state changed
///   (e.g. while an analog stick is at rest). This value is informational and must not be presented as an error.
///
/// - <b>Sequence errors</b>: not implemented (<see cref="ReliabilityResult.SequenceErrorsSupported"/> = false)
///   because HID has no standardized, vendor-independent sequence counter that can be read without knowing
///   each device model's report format.
///
/// - <b>USB errors</b>: not implemented (<see cref="ReliabilityResult.UsbErrorsSupported"/> = false). USB bus
///   errors (CRC errors, STALL conditions, timeout retries) are handled by the kernel-mode host controller
///   driver and cannot be read by a user-mode app without ETW kernel tracing or a custom driver.
/// </summary>
public sealed class ReliabilityMetrics
{
    private readonly double? _nominalIntervalMs;
    private readonly double _dropDetectionThresholdMultiplier;
    private long _estimatedDroppedReports;
    private long _consecutiveDuplicateReportCount;
    private long _totalReportCount;

    /// <param name="nominalIntervalMs">Nominal polling interval in milliseconds (see <see cref="LatencyMetrics"/>),
    /// or null when unavailable; in that case <see cref="ReliabilityResult.EstimatedDroppedReports"/> remains zero.</param>
    /// <param name="dropDetectionThresholdMultiplier">An observed interval must be at least this multiple of the
    /// nominal interval to count as one or more missed reports. Defaults to 1.5, deliberately above 1.0 so normal
    /// jitter (see <see cref="LatencyMetrics"/>) is not incorrectly counted as a drop.</param>
    public ReliabilityMetrics(double? nominalIntervalMs, double dropDetectionThresholdMultiplier = 1.5)
    {
        _nominalIntervalMs = nominalIntervalMs is > 0 ? nominalIntervalMs : null;
        _dropDetectionThresholdMultiplier = dropDetectionThresholdMultiplier;
    }

    public void Add(Devices.Hid.HidReportSample sample)
    {
        _totalReportCount++;

        if (sample.PreviousData is { } previous && previous.AsSpan().SequenceEqual(sample.Report.Data))
        {
            _consecutiveDuplicateReportCount++;
        }

        if (_nominalIntervalMs is { } nominal && sample.IntervalMs is { } interval
            && interval >= nominal * _dropDetectionThresholdMultiplier)
        {
            // Estimate how many additional polling cycles fit into the gap. Subtract one because the received
            // report is already counted.
            long estimatedMissed = (long)Math.Round(interval / nominal) - 1;
            if (estimatedMissed > 0)
            {
                _estimatedDroppedReports += estimatedMissed;
            }
        }
    }

    public ReliabilityResult ComputeResult() => new(
        EstimatedDroppedReports: _estimatedDroppedReports,
        ConsecutiveDuplicateReportCount: _consecutiveDuplicateReportCount,
        SequenceErrorsSupported: false,
        UsbErrorsSupported: false,
        TotalReportCount: _totalReportCount);
}

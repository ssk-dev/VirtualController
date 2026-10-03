using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="DeviationMs">Descriptive statistics for the absolute deviation of each observed interval
/// from the nominal/expected polling interval, in milliseconds. See <see cref="LatencyMetrics"/> docs.</param>
/// <param name="SampleCount">Number of included values.</param>
/// <param name="DeviationPercentilesApproximate">Siehe <see cref="StreamingStatisticsAccumulator.AreDistributionPercentilesApproximate"/>.</param>
public sealed record LatencyResult(DescriptiveStatisticsResult DeviationMs, long SampleCount, bool DeviationPercentilesApproximate);

/// <summary>
/// Estimates "latency" from a stream of <see cref="HidReportSample"/> values.
///
/// Important limitation: true end-to-end input latency (time from physically pressing a button/moving an axis
/// to the application receiving it) cannot be measured reliably in software alone without specialized reference
/// hardware, such as photodiode/relay test rigs used by dedicated latency devices. This software benchmark has
/// no independent timestamp for when a physical action actually occurred.
///
/// Instead, this class computes an honest, measurable estimate: the deviation of each observed report
/// interval from the nominal polling interval reported by the USB endpoint descriptor (see
/// <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>, phase 2). A device responding exactly
/// on schedule has a deviation near zero; irregular or delayed responses (e.g. due to USB bus load, driver
/// overhead, or internal processing delays) produce larger values. This is "jitter relative to specification"
/// as used in input-device tests, not end-to-end latency, and benchmark output must label it accordingly.
///
/// Without a known nominal interval (see <see cref="LatencyMetrics(double?)"/>),
/// <see cref="ComputeResult"/> returns <see cref="DescriptiveStatisticsResult.Empty"/> with
/// <see cref="LatencyResult.SampleCount"/> = 0. Benchmark output should label this as unavailable rather than
/// implying zero latency.
/// </summary>
public sealed class LatencyMetrics
{
    private readonly double? _nominalIntervalMs;
    private readonly StreamingStatisticsAccumulator _deviationAccumulator = new();

    /// <param name="nominalIntervalMs">Nominal polling interval in milliseconds (see
    /// <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>), or null if USB topology lookup
    /// (phase 2) failed; see the class documentation.</param>
    public LatencyMetrics(double? nominalIntervalMs)
    {
        _nominalIntervalMs = nominalIntervalMs is > 0 ? nominalIntervalMs : null;
    }

    public void Add(HidReportSample sample)
    {
        if (_nominalIntervalMs is not { } nominal || sample.IntervalMs is not { } interval)
        {
            return;
        }

        _deviationAccumulator.Add(Math.Abs(interval - nominal));
    }

    public LatencyResult ComputeResult()
    {
        var stats = _deviationAccumulator.ComputeResult();
        return new LatencyResult(
            DeviationMs: stats,
            SampleCount: _deviationAccumulator.Count,
            DeviationPercentilesApproximate: _deviationAccumulator.AreDistributionPercentilesApproximate);
    }
}

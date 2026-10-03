using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="ActualPollingRateHz">Observed mean polling rate in Hz, calculated as the reciprocal of
/// <see cref="IntervalMs"/>.Mean (1000 / mean interval). Unlike the nominal rate reported by the USB descriptor
/// (see <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>), this is measured behavior.</param>
/// <param name="CurrentPollingRateHz">"Instantaneous" rate calculated from the most recently observed interval
/// (1000 / last interval), for a real-time display that changes more quickly than the session-wide
/// <see cref="ActualPollingRateHz"/>. Zero until a second report is received.</param>
/// <param name="MinPollingRateHz">Lowest rate calculated from the largest observed interval
/// (<see cref="IntervalMs"/>.Max); note the inverse relationship between interval and rate.</param>
/// <param name="MaxPollingRateHz">Highest rate calculated from the smallest observed interval
/// (<see cref="IntervalMs"/>.Min); note the inverse relationship between interval and rate.</param>
/// <param name="MedianPollingRateHz">Rate calculated from the median interval (<see cref="IntervalMs"/>.Median).</param>
/// <param name="IntervalMs">Descriptive statistics for all observed intervals between consecutive reports,
/// in milliseconds.</param>
/// <param name="SampleCount">Number of intervals included (= report count - 1).</param>
/// <param name="IntervalPercentilesApproximate">See <see cref="StreamingStatisticsAccumulator.AreDistributionPercentilesApproximate"/>;
/// true when median/P95/P99 use a sample rather than the full value list (for very long sessions).</param>
public sealed record PollingRateResult(
    double ActualPollingRateHz,
    double CurrentPollingRateHz,
    double MinPollingRateHz,
    double MaxPollingRateHz,
    double MedianPollingRateHz,
    DescriptiveStatisticsResult IntervalMs,
    long SampleCount,
    bool IntervalPercentilesApproximate);

/// <summary>
/// Computes timing metrics (observed polling rate and interval statistics) from a stream of
/// <see cref="HidReportSample"/> values (see <see cref="RawHidReportReader"/>). This consumer knows neither
/// HidSharp nor the underlying report source; it uses only <see cref="HidReportSample.IntervalMs"/>, already
/// computed by the HID layer.
/// </summary>
public sealed class PollingRateMetrics
{
    private readonly StreamingStatisticsAccumulator _intervalAccumulator = new();
    private double? _lastIntervalMs;

    /// <summary>Must be called for each received <see cref="HidReportSample"/>, e.g. from the
    /// <see cref="RawHidReportReader.SampleReceived"/> handler.</summary>
    public void Add(HidReportSample sample)
    {
        if (sample.IntervalMs is { } interval)
        {
            _intervalAccumulator.Add(interval);
            _lastIntervalMs = interval;
        }
    }

    public PollingRateResult ComputeResult()
    {
        var stats = _intervalAccumulator.ComputeResult();

        static double ToHz(double intervalMs) => intervalMs > 0 ? 1000.0 / intervalMs : 0;

        return new PollingRateResult(
            ActualPollingRateHz: ToHz(stats.Mean),
            CurrentPollingRateHz: _lastIntervalMs is { } last ? ToHz(last) : 0,
            // Interval and rate are inversely related: the longest interval (Max) gives the lowest rate, and
            // the shortest interval (Min) gives the highest rate.
            MinPollingRateHz: ToHz(stats.Max),
            MaxPollingRateHz: ToHz(stats.Min),
            MedianPollingRateHz: ToHz(stats.Median),
            IntervalMs: stats,
            SampleCount: _intervalAccumulator.Count,
            IntervalPercentilesApproximate: _intervalAccumulator.AreDistributionPercentilesApproximate);
    }
}

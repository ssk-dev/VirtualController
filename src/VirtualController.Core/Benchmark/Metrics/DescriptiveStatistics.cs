namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Shared descriptive statistics for benchmark metrics (<see cref="PollingRateMetrics"/> and
/// <see cref="LatencyMetrics"/>). Keeps mean/median/standard deviation/percentile calculations in one place
/// so all metrics use the same methodology.
/// </summary>
/// <param name="Mean">Arithmetic mean.</param>
/// <param name="Median">50th percentile; see <see cref="DescriptiveStatistics.Percentile"/>.</param>
/// <param name="Min">Smallest observed value.</param>
/// <param name="Max">Largest observed value.</param>
/// <param name="StdDev">Sample standard deviation (Bessel-corrected, divided by n-1), treating collected
/// samples as a sample from the theoretical distribution of device behavior. Always zero with fewer than two values.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="P99">99th percentile.</param>
public sealed record DescriptiveStatisticsResult(double Mean, double Median, double Min, double Max, double StdDev, double P95, double P99)
{
    public static readonly DescriptiveStatisticsResult Empty = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Calculation logic only; see <see cref="DescriptiveStatisticsResult"/> for the meaning of each value.</summary>
public static class DescriptiveStatistics
{
    public static DescriptiveStatisticsResult Compute(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return DescriptiveStatisticsResult.Empty;
        }

        var sorted = values.OrderBy(v => v).ToList();
        double mean = values.Average();

        double stdDev = 0;
        if (values.Count > 1)
        {
            double sumSquaredDeviations = values.Sum(v => (v - mean) * (v - mean));
            stdDev = Math.Sqrt(sumSquaredDeviations / (values.Count - 1));
        }

        return new DescriptiveStatisticsResult(
            Mean: mean,
            Median: Percentile(sorted, 0.50),
            Min: sorted[0],
            Max: sorted[^1],
            StdDev: stdDev,
            P95: Percentile(sorted, 0.95),
            P99: Percentile(sorted, 0.99));
    }

    /// <summary>
    /// Computes a percentile through linear interpolation between the two nearest ranks, matching common
    /// methods such as NumPy's default or Excel's <c>PERCENTILE.INC</c>. <paramref name="sortedValues"/> must
    /// already be sorted in ascending order. Public so <see cref="StreamingStatisticsAccumulator"/> can apply
    /// the same method to a bounded sample (see <see cref="ReservoirSampler"/>) without duplicating logic.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double rank = percentile * (sortedValues.Count - 1);
        int lowerIndex = (int)Math.Floor(rank);
        int upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        double fraction = rank - lowerIndex;
        return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction;
    }
}

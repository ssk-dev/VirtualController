namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Continuously accumulates individual measurements (e.g. report intervals for <see cref="PollingRateMetrics"/>
/// or latency values for <see cref="LatencyMetrics"/>) during potentially long-running benchmarks (see
/// <see cref="ReservoirSampler"/> docs) and returns a complete <see cref="DescriptiveStatisticsResult"/>.
///
/// Mean/min/max/standard deviation are calculated exactly across all values using Welford's online algorithm,
/// which is numerically stable, uses constant memory, and requires no later correction. Median/P95/P99 are
/// estimated from a bounded random sample (<see cref="ReservoirSampler"/>), since exact percentiles require
/// retaining the full, unbounded value list. <see cref="AreDistributionPercentilesApproximate"/> exposes this
/// limitation so benchmark JSON output can label it transparently.
/// </summary>
public sealed class StreamingStatisticsAccumulator
{
    /// <summary>10,000 values (~80 KB as double[]); see the tradeoff described in
    /// <see cref="ReservoirSampler"/> documentation.</summary>
    private const int ReservoirCapacity = 10_000;

    private readonly ReservoirSampler _reservoir;
    private long _count;
    private double _mean;
    private double _sumSquaredDeviations; // "M2" in Welford's Algorithmus
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    public StreamingStatisticsAccumulator(int? randomSeedForTesting = null)
    {
        _reservoir = new ReservoirSampler(ReservoirCapacity, randomSeedForTesting);
    }

    /// <summary>Number of values collected so far.</summary>
    public long Count => _count;

    /// <summary>Whether median/P95/P99 in <see cref="ComputeResult"/> use a sample rather than the full value list;
    /// always true after more than <see cref="ReservoirCapacity"/> values.</summary>
    public bool AreDistributionPercentilesApproximate => _count > ReservoirCapacity;

    public void Add(double value)
    {
        _count++;

        // Welford's online algorithm for mean and variance (Knuth, TAOCP Vol. 2, 4.2.2) is more numerically
        // stable than naive summation over many values and needs only two running accumulators, not the full list.
        double delta = value - _mean;
        _mean += delta / _count;
        double delta2 = value - _mean;
        _sumSquaredDeviations += delta * delta2;

        if (value < _min)
        {
            _min = value;
        }

        if (value > _max)
        {
            _max = value;
        }

        _reservoir.Add(value);
    }

    public DescriptiveStatisticsResult ComputeResult()
    {
        if (_count == 0)
        {
            return DescriptiveStatisticsResult.Empty;
        }

        double stdDev = _count > 1 ? Math.Sqrt(_sumSquaredDeviations / (_count - 1)) : 0;
        var sortedSample = _reservoir.GetSortedSample();

        return new DescriptiveStatisticsResult(
            Mean: _mean,
            Median: DescriptiveStatistics.Percentile(sortedSample, 0.50),
            Min: _min,
            Max: _max,
            StdDev: stdDev,
            P95: DescriptiveStatistics.Percentile(sortedSample, 0.95),
            P99: DescriptiveStatistics.Percentile(sortedSample, 0.99));
    }
}

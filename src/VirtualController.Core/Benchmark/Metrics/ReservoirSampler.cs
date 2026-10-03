namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Reservoir sampling (Algorithm R, Jeffrey Vitter 1985) for estimating percentiles (median/P95/P99) from
/// potentially unbounded benchmark streams that run until stopped. Keeping every value for several hours at
/// 1000 Hz would consume substantial memory and make final sorting expensive.
///
/// Keeps a fixed maximum (<see cref="_capacity"/>) of values in memory. Each new value replaces a randomly
/// selected existing entry with probability 1/n, keeping the sample unbiased and uniformly drawn from all
/// values seen so far. With <see cref="_capacity"/> = 10,000 (see <see cref="StreamingStatisticsAccumulator"/>),
/// sampling error for P95/P99 is negligible in practice; mean/min/max/std. dev. are calculated exactly without sampling.
/// </summary>
internal sealed class ReservoirSampler
{
    private readonly double[] _reservoir;
    private readonly Random _random;
    private int _seenCount;
    private int _filledCount;

    public ReservoirSampler(int capacity, int? randomSeed = null)
    {
        _reservoir = new double[Math.Max(1, capacity)];
        // Use a random seed by default. A fixed seed is only needed for deterministic unit tests (see randomSeed).
        _random = randomSeed is { } seed ? new Random(seed) : new Random();
    }

    public void Add(double value)
    {
        _seenCount++;

        if (_filledCount < _reservoir.Length)
        {
            _reservoir[_filledCount] = value;
            _filledCount++;
            return;
        }

        // Classic Algorithm R: the nth value replaces an existing entry with probability capacity/n.
        // Random.Next(n) returns a value in [0, n), so this condition implements that probability exactly.
        int randomIndex = _random.Next(_seenCount);
        if (randomIndex < _reservoir.Length)
        {
            _reservoir[randomIndex] = value;
        }
    }

    /// <summary>Returns the current sample sorted in ascending order, for
    /// <see cref="DescriptiveStatistics.Percentile"/>.</summary>
    public IReadOnlyList<double> GetSortedSample()
    {
        return _reservoir.Take(_filledCount).OrderBy(v => v).ToList();
    }
}

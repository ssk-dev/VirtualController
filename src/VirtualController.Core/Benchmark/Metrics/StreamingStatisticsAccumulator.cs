namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Sammelt fortlaufend einzelne Messwerte (z.B. Intervalle zwischen Reports fuer
/// <see cref="PollingRateMetrics"/>, oder Latenzwerte fuer <see cref="LatencyMetrics"/>) waehrend eines
/// potenziell sehr lange laufenden Benchmarks (siehe <see cref="ReservoirSampler"/>-Dokumentation) und
/// liefert am Ende ein vollstaendiges <see cref="DescriptiveStatisticsResult"/>.
///
/// Mean/Min/Max/StdDev werden EXAKT ueber alle jemals gesehenen Werte berechnet (Welford's
/// Online-Algorithmus - numerisch stabil, konstanter Speicherbedarf, keine nachtraegliche Korrektur
/// noetig). Median/P95/P99 werden dagegen NUR NAEHERUNGSWEISE aus einer begrenzten Zufalls-Stichprobe
/// (<see cref="ReservoirSampler"/>) berechnet, da eine exakte Perzentil-Berechnung eine vollstaendige,
/// unbegrenzt wachsende Werteliste erfordern wuerde. Diese Einschraenkung wird ueber
/// <see cref="AreDistributionPercentilesApproximate"/> nach aussen sichtbar gemacht, damit eine
/// spaetere Benchmark-JSON-Ausgabe dies transparent kennzeichnen kann.
/// </summary>
public sealed class StreamingStatisticsAccumulator
{
    /// <summary>10.000 Werte (~80 KB als double[]) - siehe Abwaegung in <see cref="ReservoirSampler"/>-Dokumentation.</summary>
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

    /// <summary>Anzahl der bisher aufgenommenen Werte.</summary>
    public long Count => _count;

    /// <summary>Ob Median/P95/P99 in <see cref="ComputeResult"/> auf einer Stichprobe (statt der
    /// vollstaendigen Werteliste) beruhen - ab mehr als <see cref="ReservoirCapacity"/> Werten stets true.</summary>
    public bool AreDistributionPercentilesApproximate => _count > ReservoirCapacity;

    public void Add(double value)
    {
        _count++;

        // Welford's Online-Algorithmus fuer Mean und Varianz (Knuth, TAOCP Vol. 2, 4.2.2) - numerisch
        // stabiler als eine naive Summenbildung ueber sehr viele Werte, und benoetigt dabei nur zwei
        // laufend aktualisierte Akkumulatoren statt der vollstaendigen Werteliste.
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

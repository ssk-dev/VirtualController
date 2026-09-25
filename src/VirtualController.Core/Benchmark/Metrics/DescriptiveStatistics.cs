namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Gemeinsame deskriptive Statistik-Berechnung fuer die verschiedenen Benchmark-Kennzahlen
/// (<see cref="PollingRateMetrics"/>, <see cref="LatencyMetrics"/>) - bewusst als einzige Stelle,
/// an der Mean/Median/StdDev/Perzentile berechnet werden, damit alle Kennzahlen dieselbe Methodik
/// verwenden.
/// </summary>
/// <param name="Mean">Arithmetisches Mittel.</param>
/// <param name="Median">50.Perzentil, siehe <see cref="DescriptiveStatistics.Percentile"/>.</param>
/// <param name="Min">Kleinster beobachteter Wert.</param>
/// <param name="Max">Groesster beobachteter Wert.</param>
/// <param name="StdDev">Stichproben-Standardabweichung (Bessel-korrigiert, Division durch n-1) - der
/// ueblichen Interpretation folgend, dass die gesammelten Samples eine Stichprobe der tatsaechlichen,
/// theoretisch unendlichen Verteilung des Geraeteverhaltens darstellen. Bei weniger als 2 Werten stets 0.</param>
/// <param name="P95">95.Perzentil.</param>
/// <param name="P99">99.Perzentil.</param>
public sealed record DescriptiveStatisticsResult(double Mean, double Median, double Min, double Max, double StdDev, double P95, double P99)
{
    public static readonly DescriptiveStatisticsResult Empty = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Reine Berechnungslogik, siehe <see cref="DescriptiveStatisticsResult"/> fuer die Bedeutung der einzelnen Werte.</summary>
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
    /// Perzentil-Berechnung per linearer Interpolation zwischen den beiden naechstgelegenen
    /// Rangpositionen (identische Methodik wie z.B. numpy's Standardverfahren oder Excels
    /// <c>PERCENTILE.INC</c>) - <paramref name="sortedValues"/> MUSS bereits aufsteigend sortiert sein.
    /// Oeffentlich, damit <see cref="StreamingStatisticsAccumulator"/> dieselbe Methodik auf einer
    /// begrenzten Stichprobe (siehe <see cref="ReservoirSampler"/>) statt der vollstaendigen Werteliste
    /// anwenden kann, ohne die Logik zu duplizieren.
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

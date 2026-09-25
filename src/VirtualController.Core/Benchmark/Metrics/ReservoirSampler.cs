namespace VirtualController.Core.Benchmark.Metrics;

/// <summary>
/// Reservoir-Sampling (Algorithm R, Jeffrey Vitter 1985) fuer Perzentil-Naeherungen (Median/P95/P99)
/// auf einem potenziell unbegrenzt langen Datenstrom, wie ihn ein Benchmark liefert, der "laeuft bis
/// gestoppt" wird (siehe Anforderung des Nutzers) - eine vollstaendige Werteliste ueber z.B. mehrere
/// Stunden Laufzeit bei 1000 Hz waere sowohl speicher- als auch bei der abschliessenden Sortierung
/// rechenintensiv.
///
/// Haelt eine feste Obergrenze (<see cref="_capacity"/>) an Werten im Speicher; jeder neue Wert ersetzt
/// mit abnehmender Wahrscheinlichkeit (1/n) einen zufaellig gewaehlten vorhandenen Eintrag, sodass die
/// gehaltene Stichprobe stets eine unverzerrte (gleichverteilte) Teilmenge aller bisher gesehenen Werte
/// bleibt. Bei <see cref="_capacity"/> = 10.000 (siehe <see cref="StreamingStatisticsAccumulator"/>) ist
/// der Stichprobenfehler fuer die interessierenden P95/P99-Perzentile in der Praxis vernachlaessigbar
/// klein, waehrend Mean/Min/Max/StdDev ohnehin exakt (nicht ueber dieses Sampling) berechnet werden.
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
        // Standardmaessig ohne festen Seed (echte Zufaelligkeit) - ein fester Seed wird ausschliesslich
        // fuer deterministische Unit-Tests benoetigt (siehe randomSeed-Parameter).
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

        // Klassisches Algorithm R: der n-te Wert ersetzt einen zufaelligen vorhandenen Eintrag mit
        // Wahrscheinlichkeit capacity/n - Random.Next(n) liefert ein Ergebnis in [0, n), daher trifft
        // die Bedingung genau mit dieser Wahrscheinlichkeit zu.
        int randomIndex = _random.Next(_seenCount);
        if (randomIndex < _reservoir.Length)
        {
            _reservoir[randomIndex] = value;
        }
    }

    /// <summary>Liefert die aktuell gehaltene Stichprobe, aufsteigend sortiert - Grundlage fuer
    /// <see cref="DescriptiveStatistics.Percentile"/>.</summary>
    public IReadOnlyList<double> GetSortedSample()
    {
        return _reservoir.Take(_filledCount).OrderBy(v => v).ToList();
    }
}

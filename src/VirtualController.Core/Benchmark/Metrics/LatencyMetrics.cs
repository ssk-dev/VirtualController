using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="DeviationMs">Deskriptive Statistik ueber die absolute Abweichung jedes einzelnen
/// beobachteten Intervalls vom nominalen/erwarteten Polling-Intervall (in Millisekunden) - siehe
/// Klassendokumentation von <see cref="LatencyMetrics"/> fuer die Interpretation.</param>
/// <param name="SampleCount">Anzahl der eingeflossenen Werte.</param>
/// <param name="DeviationPercentilesApproximate">Siehe <see cref="StreamingStatisticsAccumulator.AreDistributionPercentilesApproximate"/>.</param>
public sealed record LatencyResult(DescriptiveStatisticsResult DeviationMs, long SampleCount, bool DeviationPercentilesApproximate);

/// <summary>
/// Berechnet eine Naeherung fuer "Latenz" aus einem Strom von <see cref="HidReportSample"/>.
///
/// WICHTIGE EINSCHRAENKUNG: Echte Ende-zu-Ende-Eingabelatenz (Zeit von der physischen Betaetigung
/// eines Buttons/einer Achse bis zum Empfang durch die Anwendung) ist rein softwareseitig, ohne
/// spezielle Referenzhardware (z.B. ein photodioden-/relaisbasiertes Mess-Rig, wie es dedizierte
/// Latenz-Messgeraete verwenden), NICHT zuverlaessig messbar - dieser Software-Benchmark hat keinen
/// unabhaengigen Zeitpunkt fuer "wann wurde tatsaechlich physisch etwas ausgeloest".
///
/// Diese Klasse berechnet daher stattdessen eine ehrliche, tatsaechlich messbare Naeherung: die
/// ABWEICHUNG jedes einzelnen beobachteten Report-Intervalls vom nominalen, vom USB-Endpoint-Deskriptor
/// gemeldeten Polling-Intervall (siehe <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>,
/// Phase 2). Ein Geraet, das exakt im nominalen Takt antwortet, hat eine Abweichung nahe 0; ein Geraet
/// mit unregelmaessiger/verzoegerter Antwort (z.B. durch USB-Bus-Auslastung, Treiber-Overhead oder
/// interne Verarbeitungsverzoegerung) zeigt hier hoehere Werte. Dies entspricht der in
/// Eingabegeraete-Tests gebraeuchlichen Grosse "Jitter relativ zur Spezifikation", nicht einer
/// End-zu-Ende-Latenzmessung - das Benchmark-Ergebnis muss dies entsprechend beschriften (siehe
/// zukuenftige Benchmark-JSON-Ausgabe).
///
/// Ohne bekanntes nominales Intervall (siehe <see cref="LatencyMetrics(double?)"/>) liefert
/// <see cref="ComputeResult"/> stets <see cref="DescriptiveStatisticsResult.Empty"/> mit
/// <see cref="LatencyResult.SampleCount"/> = 0 - die Benchmark-Ausgabe muss diesen Fall als
/// "nicht ermittelbar" kennzeichnen, statt eine Null-Latenz vorzutaeuschen.
/// </summary>
public sealed class LatencyMetrics
{
    private readonly double? _nominalIntervalMs;
    private readonly StreamingStatisticsAccumulator _deviationAccumulator = new();

    /// <param name="nominalIntervalMs">Nominales Polling-Intervall in Millisekunden (siehe
    /// <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>), oder null, falls die
    /// USB-Topologie-Abfrage (Phase 2) fehlgeschlagen ist - siehe Klassendokumentation.</param>
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

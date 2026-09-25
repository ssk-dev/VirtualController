using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="ActualPollingRateHz">Tatsaechlich beobachtete mittlere Abfragerate in Hz, aus dem
/// Kehrwert von <see cref="IntervalMs"/>.Mean berechnet (1000 / MittleresIntervall). Zu unterscheiden
/// von der NOMINALEN Polling-Rate, die der USB-Deskriptor meldet (siehe
/// <see cref="Devices.Usb.UsbEndpointInfo.NominalPollingIntervalMs"/>) - dieser Wert hier ist das
/// tatsaechlich waehrend der Messung gemessene Verhalten.</param>
/// <param name="CurrentPollingRateHz">Aus dem ZULETZT beobachteten einzelnen Intervall berechnete
/// "Momentan"-Rate (1000 / letztes Intervall) - fuer eine Echtzeit-Anzeige (siehe geplantes
/// Benchmark-Popup-Fenster), die sich staerker/schneller aendert als der ueber die gesamte Sitzung
/// gemittelte <see cref="ActualPollingRateHz"/>. 0, solange noch kein zweiter Report empfangen wurde.</param>
/// <param name="MinPollingRateHz">Aus dem GROESSTEN beobachteten Intervall (<see cref="IntervalMs"/>.Max)
/// berechnete niedrigste Rate - ACHTUNG Inversionsrichtung: das laengste Intervall ergibt die niedrigste Rate.</param>
/// <param name="MaxPollingRateHz">Aus dem KLEINSTEN beobachteten Intervall (<see cref="IntervalMs"/>.Min)
/// berechnete hoechste Rate - ACHTUNG Inversionsrichtung: das kuerzeste Intervall ergibt die hoechste Rate.</param>
/// <param name="MedianPollingRateHz">Aus dem Median-Intervall (<see cref="IntervalMs"/>.Median) berechnete Rate.</param>
/// <param name="IntervalMs">Deskriptive Statistik ueber alle beobachteten Zeitabstaende zwischen
/// aufeinanderfolgenden Reports, in Millisekunden.</param>
/// <param name="SampleCount">Anzahl der in diese Berechnung eingeflossenen Intervalle (= Report-Anzahl - 1).</param>
/// <param name="IntervalPercentilesApproximate">Siehe <see cref="StreamingStatisticsAccumulator.AreDistributionPercentilesApproximate"/> -
/// true, falls Median/P95/P99 auf einer Stichprobe statt der vollstaendigen Werteliste beruhen (bei sehr langen Sessions).</param>
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
/// Berechnet die Timing-Kennzahlen (tatsaechliche Polling-Rate, Intervall-Statistik) aus einem Strom
/// von <see cref="HidReportSample"/> (siehe <see cref="RawHidReportReader"/>). Reine Konsumenten-Klasse -
/// kennt weder HidSharp noch die zugrunde liegende Report-Quelle, ausschliesslich das bereits von der
/// HID-Ebene vorberechnete <see cref="HidReportSample.IntervalMs"/>.
/// </summary>
public sealed class PollingRateMetrics
{
    private readonly StreamingStatisticsAccumulator _intervalAccumulator = new();
    private double? _lastIntervalMs;

    /// <summary>Muss fuer jeden empfangenen <see cref="HidReportSample"/> aufgerufen werden (z.B. direkt
    /// im <see cref="RawHidReportReader.SampleReceived"/>-Handler).</summary>
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
            // Inversionsrichtung beachten: das laengste Intervall (Max) ergibt die niedrigste Rate,
            // das kuerzeste Intervall (Min) die hoechste Rate.
            MinPollingRateHz: ToHz(stats.Max),
            MaxPollingRateHz: ToHz(stats.Min),
            MedianPollingRateHz: ToHz(stats.Median),
            IntervalMs: stats,
            SampleCount: _intervalAccumulator.Count,
            IntervalPercentilesApproximate: _intervalAccumulator.AreDistributionPercentilesApproximate);
    }
}

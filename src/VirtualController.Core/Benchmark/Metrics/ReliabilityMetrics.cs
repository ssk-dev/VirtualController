namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="EstimatedDroppedReports">Naeherungsweise Anzahl "verpasster" Reports, geschaetzt aus
/// Zeitluecken, die deutlich groesser als das nominale Intervall sind (siehe Klassendokumentation von
/// <see cref="ReliabilityMetrics"/>). Eine HEURISTIK, keine exakte Zaehlung.</param>
/// <param name="ConsecutiveDuplicateReportCount">Anzahl aufeinanderfolgender, byte-identischer Reports -
/// REIN INFORMATIV, siehe Klassendokumentation: bei den meisten HID-Gamepads/Joysticks ist dies
/// NORMALES Verhalten (kontinuierlicher Report-Strom unabhaengig von Zustandsaenderung), kein Fehlerindikator.</param>
/// <param name="SequenceErrorsSupported">Immer false - siehe Klassendokumentation: echte HID-Sequenznummern
/// existieren nicht generisch/herstellerunabhaengig, daher wird dieser Wert nicht berechnet.</param>
/// <param name="UsbErrorsSupported">Immer false - siehe Klassendokumentation: USB-Bus-Fehler (CRC/STALL/etc.)
/// werden vom Host-Controller-Treiber behandelt und sind auf User-Mode-HID-Ebene nicht auslesbar.</param>
/// <param name="TotalReportCount">Gesamtzahl der waehrend der Messung empfangenen Reports.</param>
public sealed record ReliabilityResult(
    long EstimatedDroppedReports,
    long ConsecutiveDuplicateReportCount,
    bool SequenceErrorsSupported,
    bool UsbErrorsSupported,
    long TotalReportCount);

/// <summary>
/// Berechnet die Reliability-Kennzahlen aus einem Strom von <see cref="Devices.Hid.HidReportSample"/>.
///
/// EHRLICHE EINSCHRAENKUNGEN (bewusst nicht verschleiert, siehe Session-weite Vorgabe, reale statt
/// vorgetaeuschte Werte zu liefern):
///
/// - <b>Dropped Reports</b>: HID kennt keine generische, herstellerunabhaengige Sequenznummer in den
///   Report-Rohdaten. "Verpasste" Reports werden daher NUR HEURISTISCH aus auffaelligen Zeitluecken
///   erkannt (ein beobachtetes Intervall deutlich groesser als das nominale Polling-Intervall deutet
///   auf einen oder mehrere ausgebliebene Polls hin). Dies ist eine Naeherung, keine exakte Zaehlung -
///   und ohne bekanntes nominales Intervall (z.B. weil die USB-Topologie-Abfrage aus Phase 2 fehlschlug)
///   ueberhaupt nicht moeglich (siehe <see cref="ReliabilityMetrics(double?, double)"/>).
///
/// - <b>Duplicate Reports</b>: byte-identische aufeinanderfolgende Reports werden zwar gezaehlt, sind
///   bei den meisten HID-Gamepads/Joysticks aber KEIN Fehlerzustand: viele Geraete senden kontinuierlich
///   einen Report je Polling-Intervall, unabhaengig davon, ob sich der Zustand seit dem letzten Report
///   geaendert hat (z.B. ein ruhig liegender Analogstick). Dieser Wert ist daher rein informativ und
///   darf in der Benchmark-Anzeige NICHT als Fehlerindikator dargestellt werden.
///
/// - <b>Sequence Errors</b>: nicht implementiert (<see cref="ReliabilityResult.SequenceErrorsSupported"/> = false) -
///   es existiert kein standardisiertes, herstellerunabhaengiges HID-Sequenzzaehler-Feld, das zuverlaessig
///   ausgelesen werden koennte, ohne das konkrete Report-Format jedes einzelnen Geraetemodells zu kennen.
///
/// - <b>USB Errors</b>: nicht implementiert (<see cref="ReliabilityResult.UsbErrorsSupported"/> = false) -
///   USB-Bus-Fehler (CRC-Fehler, STALL-Bedingungen, Timeout-Retries) werden vollstaendig vom
///   USB-Host-Controller-Treiber im Kernel-Modus behandelt und sind fuer eine User-Mode-Anwendung ohne
///   ETW-Kernel-Tracing oder einen eigenen Treiber nicht auslesbar.
/// </summary>
public sealed class ReliabilityMetrics
{
    private readonly double? _nominalIntervalMs;
    private readonly double _dropDetectionThresholdMultiplier;
    private long _estimatedDroppedReports;
    private long _consecutiveDuplicateReportCount;
    private long _totalReportCount;

    /// <param name="nominalIntervalMs">Siehe <see cref="LatencyMetrics"/> - nominales Polling-Intervall
    /// in Millisekunden, oder null, falls nicht ermittelbar (dann bleibt <see cref="ReliabilityResult.EstimatedDroppedReports"/> stets 0).</param>
    /// <param name="dropDetectionThresholdMultiplier">Ein beobachtetes Intervall muss mindestens das
    /// so-vielfache des nominalen Intervalls betragen, um als (ein oder mehrere) verpasste Reports
    /// gewertet zu werden. Standardmaessig 1,5 - bewusst deutlich ueber 1,0, um normale Jitter-Schwankungen
    /// (siehe <see cref="LatencyMetrics"/>) nicht faelschlich als Drop zu werten.</param>
    public ReliabilityMetrics(double? nominalIntervalMs, double dropDetectionThresholdMultiplier = 1.5)
    {
        _nominalIntervalMs = nominalIntervalMs is > 0 ? nominalIntervalMs : null;
        _dropDetectionThresholdMultiplier = dropDetectionThresholdMultiplier;
    }

    public void Add(Devices.Hid.HidReportSample sample)
    {
        _totalReportCount++;

        if (sample.PreviousData is { } previous && previous.AsSpan().SequenceEqual(sample.Report.Data))
        {
            _consecutiveDuplicateReportCount++;
        }

        if (_nominalIntervalMs is { } nominal && sample.IntervalMs is { } interval
            && interval >= nominal * _dropDetectionThresholdMultiplier)
        {
            // Grobe Schaetzung, wie viele zusaetzliche Polling-Zyklen in diese Luecke gepasst haetten -
            // "-1", da der eine tatsaechlich empfangene Report bereits mitgezaehlt ist.
            long estimatedMissed = (long)Math.Round(interval / nominal) - 1;
            if (estimatedMissed > 0)
            {
                _estimatedDroppedReports += estimatedMissed;
            }
        }
    }

    public ReliabilityResult ComputeResult() => new(
        EstimatedDroppedReports: _estimatedDroppedReports,
        ConsecutiveDuplicateReportCount: _consecutiveDuplicateReportCount,
        SequenceErrorsSupported: false,
        UsbErrorsSupported: false,
        TotalReportCount: _totalReportCount);
}

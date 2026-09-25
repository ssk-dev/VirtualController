using System.Diagnostics;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Ein einzelner, bereits mit dem Vorgaenger-Report verglichener Messpunkt - siehe
/// <see cref="RawHidReportReader.SampleReceived"/>. Enthaelt bewusst nur rohe, unbewertete Fakten
/// (Zeitdifferenz, Rohdaten); jede Interpretation (Dropped/Duplicate/Latenz-Naeherung usw.) obliegt
/// der Benchmark-/Metrics-Schicht (siehe <c>VirtualController.Core.Benchmark</c>), nicht dieser HID-Ebene.
/// </summary>
/// <param name="Report">Der soeben gelesene Report.</param>
/// <param name="IntervalMs">Zeit seit dem unmittelbar vorherigen Report in Millisekunden, oder null
/// beim allerersten Report einer Session (kein Vorgaenger vorhanden).</param>
/// <param name="PreviousData">Rohdaten des unmittelbar vorherigen Reports, oder null beim ersten Report -
/// fuer eine spaetere Duplikat-Erkennung per Byte-Vergleich (siehe Benchmark-Schicht).</param>
public readonly record struct HidReportSample(HidReport Report, double? IntervalMs, byte[]? PreviousData);

/// <summary>
/// Liest fortlaufend Reports von einer <see cref="IHidReportSource"/> auf einem dedizierten
/// Hintergrund-Thread, bis <see cref="Stop"/> aufgerufen wird oder das Geraet die Verbindung verliert,
/// und meldet jeden gelesenen Report zeitnah per <see cref="SampleReceived"/>-Event. Reine
/// Erfassungs-/Weiterleitungslogik ohne jede statistische Auswertung (siehe Klassendokumentation von
/// <see cref="HidReportSample"/>) - die eigentliche Timing-/Latenz-/Reliability-Berechnung lebt bewusst
/// in einer eigenen, von HidSharp/HID vollstaendig unabhaengigen Schicht
/// (<c>VirtualController.Core.Benchmark.Metrics</c>), die ausschliesslich <see cref="HidReportSample"/>
/// konsumiert.
///
/// Laeuft unabhaengig von jeglicher UI-Sichtbarkeit (kein DispatcherTimer, kein Bezug zu einem
/// ausgewaehlten Tab) - passend zur Anforderung, dass ein gestartetes Benchmark/Log auch bei
/// Tab-Wechsel weiterlaeuft.
/// </summary>
public sealed class RawHidReportReader : IDisposable
{
    private readonly IHidReportSource _source;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Wird auf dem internen Lese-Thread ausgefuehrt (NICHT auf dem UI-Thread) - Abonnenten
    /// muessen ggf. selbst per Dispatcher auf den UI-Thread wechseln, falls sie UI-Elemente aktualisieren.</summary>
    public event Action<HidReportSample>? SampleReceived;

    /// <summary>Wird genau einmal ausgeloest, wenn die Lese-Schleife wegen eines Fehlers (z.B. Geraet
    /// getrennt) vorzeitig beendet wurde - NICHT bei regulaerem <see cref="Stop"/>-Aufruf.</summary>
    public event Action<Exception>? ReadFailed;

    /// <summary>Ob der Lese-Thread aktuell laeuft. Wird nach einem Fehler (siehe <see cref="ReadFailed"/>) automatisch false.</summary>
    public bool IsRunning => _running;

    public RawHidReportReader(IHidReportSource source)
    {
        _source = source;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(RunLoop)
        {
            Name = "RawHidReportReader",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void RunLoop()
    {
        byte[]? previousData = null;
        long? previousTimestampTicks = null;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                HidReport report;
                try
                {
                    report = _source.ReadReport(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Regulaerer Stop() - kein Fehler.
                    break;
                }
                catch (Exception ex)
                {
                    // Geraet vermutlich getrennt/Zugriff verloren - Schleife beenden und den Fehler
                    // dem Aufrufer melden, statt in einer Endlosschleife weitere Fehler zu produzieren.
                    _running = false;
                    ReadFailed?.Invoke(ex);
                    return;
                }

                double? intervalMs = null;
                if (previousTimestampTicks is { } previousTicks)
                {
                    intervalMs = (report.TimestampTicks - previousTicks) * 1000.0 / Stopwatch.Frequency;
                }

                SampleReceived?.Invoke(new HidReportSample(report, intervalMs, previousData));

                previousData = report.Data;
                previousTimestampTicks = report.TimestampTicks;
            }
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _cts.Dispose();
        _source.Dispose();
    }
}

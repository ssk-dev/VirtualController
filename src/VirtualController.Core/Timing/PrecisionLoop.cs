using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Timing;

/// <summary>
/// Fuehrt eine Callback-Aktion mit fester Zielfrequenz (z.B. 1000 Hz) auf einem dedizierten
/// Thread aus. Nutzt, sofern verfuegbar, einen High-Resolution Waitable-Timer (Sub-Millisekunden
/// Praezision, Windows 10 1809+), sonst einen Spin-Wait-Fallback basierend auf
/// <see cref="Stopwatch"/>/<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>. Beides
/// funktioniert rein im User-Mode ohne Treiber oder Admin-Rechte.
/// </summary>
public sealed class PrecisionLoop : IDisposable
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action _tick;
    private readonly int _targetHz;
    private volatile bool _running;

    public PrecisionLoop(string name, int targetHz, Action tick)
    {
        _targetHz = Math.Max(1, targetHz);
        _tick = tick;
        _thread = new Thread(Run)
        {
            Name = name,
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
    }

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _cts.Cancel();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // Loop-Thread reagiert nicht rechtzeitig; wird als Background-Thread beim
            // Prozessende ohnehin vom Betriebssystem beendet.
        }
    }

    private void Run()
    {
        long periodTicks = Stopwatch.Frequency / _targetHz;
        var timer = WaitableTimerNative.TryCreate();

        try
        {
            long nextTick = Stopwatch.GetTimestamp();

            while (_running)
            {
                _tick();

                nextTick += periodTicks;
                long now = Stopwatch.GetTimestamp();
                long remainingTicks = nextTick - now;

                if (remainingTicks <= 0)
                {
                    // Deadline bereits verpasst (Tick hat laenger gedauert als das Intervall) ->
                    // sofort weiter, Zeitbasis fuer den naechsten Tick bleibt am Soll-Raster.
                    continue;
                }

                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

                if (timer is not null)
                {
                    long due100ns = (long)(remainingMs * 10_000);
                    if (due100ns > 0)
                    {
                        WaitableTimerNative.WaitRelative(timer, due100ns);
                    }
                }
                else
                {
                    SpinWaitUntil(nextTick);
                }
            }
        }
        finally
        {
            timer?.Dispose();
        }
    }

    /// <summary>Fallback fuer Systeme ohne High-Resolution-Timer-Unterstuetzung (vor Windows 10 1809).</summary>
    private static void SpinWaitUntil(long targetTimestamp)
    {
        while (Stopwatch.GetTimestamp() < targetTimestamp)
        {
            Thread.SpinWait(50);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}

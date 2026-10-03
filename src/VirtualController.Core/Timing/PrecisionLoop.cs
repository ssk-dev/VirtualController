using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Timing;

/// <summary>
/// Runs a callback at a fixed target frequency (e.g. 1000 Hz) on a dedicated thread. Uses a high-resolution
/// waitable timer when available (sub-millisecond precision, Windows 10 1809+), otherwise a Stopwatch-based
/// spin-wait fallback. Both work in user mode without drivers or administrator privileges.
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
            // The loop thread did not respond in time; as a background thread, the OS will terminate it when
            // the process exits.
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
                    // Deadline already missed because the tick exceeded its interval; continue immediately
                    // while keeping the next tick aligned to the target schedule.
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

    /// <summary>Fallback for systems without high-resolution timer support (before Windows 10 1809).</summary>
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

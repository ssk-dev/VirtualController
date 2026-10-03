using System.Diagnostics;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// One sample already compared with the previous report (see <see cref="RawHidReportReader.SampleReceived"/>).
/// Contains only raw facts (time interval and data); interpretation such as dropped/duplicate reports or
/// latency estimates belongs to the benchmark/metrics layer (<c>VirtualController.Core.Benchmark</c>), not HID.
/// </summary>
/// <param name="Report">The report just read.</param>
/// <param name="IntervalMs">Time since the previous report in milliseconds, or null for the first report in a session.</param>
/// <param name="PreviousData">Raw data from the previous report, or null for the first report; used by the
/// benchmark layer for byte-wise duplicate detection.</param>
public readonly record struct HidReportSample(HidReport Report, double? IntervalMs, byte[]? PreviousData);

/// <summary>
/// Continuously reads reports from an <see cref="IHidReportSource"/> on a dedicated background thread until
/// <see cref="Stop"/> is called or the device disconnects, then promptly raises <see cref="SampleReceived"/>.
/// Performs capture/forwarding only, with no statistical analysis (see <see cref="HidReportSample"/> docs).
/// Timing/latency/reliability calculations live in the separate HidSharp-independent
/// <c>VirtualController.Core.Benchmark.Metrics</c> layer, which consumes <see cref="HidReportSample"/>.
///
/// Runs independently of UI visibility (no DispatcherTimer or selected-tab dependency), so a started
/// benchmark/log continues when the user switches tabs.
/// </summary>
public sealed class RawHidReportReader : IDisposable
{
    private readonly IHidReportSource _source;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Runs on the internal reader thread, not the UI thread. Subscribers must dispatch to the UI
    /// thread themselves when updating UI elements.</summary>
    public event Action<HidReportSample>? SampleReceived;

    /// <summary>Raised once if the read loop exits early due to an error (e.g. device disconnected), not during
    /// a normal <see cref="Stop"/> call.</summary>
    public event Action<Exception>? ReadFailed;

    /// <summary>Whether the reader thread is running. Automatically becomes false after an error (see
    /// <see cref="ReadFailed"/>).</summary>
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
                    // Normal Stop(); not an error.
                    break;
                }
                catch (Exception ex)
                {
                    // The device likely disconnected or access was lost. End the loop and report the error
                    // rather than producing errors indefinitely.
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

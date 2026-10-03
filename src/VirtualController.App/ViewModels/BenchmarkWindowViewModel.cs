using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Benchmark;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices.Hid;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel for the real-time hardware benchmark window for one device (see <see cref="Views.BenchmarkWindow"/>).
/// It deliberately does not control the session; start/stop remain owned by
/// <see cref="DeviceConfigDeviceViewModel"/> (<see cref="Device"/>, especially
/// <see cref="DeviceConfigDeviceViewModel.ToggleBenchmarkCommand"/> and
/// <see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/>). A started session therefore continues whether
/// this window is open, closed, or reopened. This view model observes
/// <see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/> and periodically polls
/// <see cref="DeviceConfigDeviceViewModel.GetLiveBenchmarkSnapshot"/> to display the metrics measured so far
/// (timing, latency, reliability, and signal).
/// </summary>
public sealed partial class BenchmarkWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Refresh rate for the live display. Intentionally much slower than the device's report rate
    /// (which can reach several hundred Hz), since this shows a readable snapshot of accumulated statistics,
    /// not a raw data stream.</summary>
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    /// <summary>Device whose benchmark is displayed in real time. The view's start/stop buttons bind directly
    /// to this instance's <see cref="DeviceConfigDeviceViewModel.ToggleBenchmarkCommand"/> and
    /// <see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/>, like the existing inline button in DeviceConfigTemplates.xaml.</summary>
    public DeviceConfigDeviceViewModel Device { get; }

    /// <summary>Whether at least one snapshot has been received. Until then, the view shows a placeholder
    /// instead of meaningless zero values.</summary>
    [ObservableProperty]
    private bool _hasSnapshot;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private double _actualPollingRateHz;

    [ObservableProperty]
    private double _currentPollingRateHz;

    [ObservableProperty]
    private double _minPollingRateHz;

    [ObservableProperty]
    private double _maxPollingRateHz;

    [ObservableProperty]
    private double _medianPollingRateHz;

    [ObservableProperty]
    private long _pollingSampleCount;

    [ObservableProperty]
    private double _intervalStdDevMs;

    /// <summary>See <see cref="LatencyResult"/>/<see cref="LatencyMetrics"/>. False while no nominal polling
    /// interval could be determined, in which case the latency estimate is unavailable.</summary>
    [ObservableProperty]
    private bool _latencySupported;

    /// <summary>Latency jitter: mean deviation of each observed report interval from the nominal USB polling
    /// interval (see <see cref="LatencyMetrics"/> documentation). This is not an end-to-end latency measurement,
    /// which cannot be measured here.</summary>
    [ObservableProperty]
    private double _latencyJitterMeanMs;

    [ObservableProperty]
    private double _latencyJitterMaxMs;

    [ObservableProperty]
    private long _estimatedDroppedReports;

    [ObservableProperty]
    private long _consecutiveDuplicateReportCount;

    [ObservableProperty]
    private long _totalReportCount;

    /// <summary>True when multiple HID interfaces with the matching vendor/product ID were found (see
    /// <see cref="HidCandidates"/>). This may indicate a composite device where the session opened an interface
    /// that produces no reports, especially if <see cref="PollingSampleCount"/> remains zero while the device is active.</summary>
    [ObservableProperty]
    private bool _hasMultipleHidCandidates;

    /// <summary>One entry per axis with its measured signal metrics (see <see cref="SignalAxisResult"/>); empty
    /// if the device has no detected axes or no session has run yet.</summary>
    public ObservableCollection<SignalAxisResult> SignalAxes { get; } = new();

    /// <summary>All HID interfaces with the matching vendor/product ID found when the session was created
    /// (see <see cref="BenchmarkDiagnosticsInfo"/>). Exposed for troubleshooting if a session reports zero
    /// samples despite opening a report source; multiple entries may indicate a composite device where the
    /// wrong interface was opened (marked by <see cref="BenchmarkHidCandidateInfo.IsResolved"/>).</summary>
    public ObservableCollection<BenchmarkHidCandidateInfo> HidCandidates { get; } = new();

    public BenchmarkWindowViewModel(DeviceConfigDeviceViewModel device)
    {
        Device = device;

        _timer = new DispatcherTimer { Interval = LiveRefreshInterval };
        _timer.Tick += OnTimerTick;

        Device.PropertyChanged += OnDevicePropertyChanged;

        // If the window opens or reopens during a running session, show the current snapshot and start live
        // updates immediately rather than waiting for the first timer tick.
        if (Device.IsBenchmarking)
        {
            RefreshSnapshot();
            _timer.Start();
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeviceConfigDeviceViewModel.IsBenchmarking))
        {
            return;
        }

        if (Device.IsBenchmarking)
        {
            // A new session started; clear any previous session's display so stale values are not confused
            // with the new results.
            SignalAxes.Clear();
            HasSnapshot = false;
            RefreshSnapshot();
            _timer.Start();
        }
        else
        {
            // The session ended, either normally or due to an error. Stop the timer but keep the displayed
            // values: they approximate the final result just exported as JSON (see Device.LastBenchmarkFilePath)
            // and remain visible until the user starts another session.
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => RefreshSnapshot();

    private void RefreshSnapshot()
    {
        var snapshot = Device.GetLiveBenchmarkSnapshot();
        if (snapshot is null)
        {
            // No session is running; keep the last displayed values (see OnDevicePropertyChanged).
            return;
        }

        HasSnapshot = true;
        DurationSeconds = snapshot.DurationSeconds;

        ActualPollingRateHz = snapshot.Timing.ActualPollingRateHz;
        CurrentPollingRateHz = snapshot.Timing.CurrentPollingRateHz;
        MinPollingRateHz = snapshot.Timing.MinPollingRateHz;
        MaxPollingRateHz = snapshot.Timing.MaxPollingRateHz;
        MedianPollingRateHz = snapshot.Timing.MedianPollingRateHz;
        PollingSampleCount = snapshot.Timing.SampleCount;
        IntervalStdDevMs = snapshot.Timing.IntervalMs.StdDev;

        LatencySupported = snapshot.Latency.SampleCount > 0;
        LatencyJitterMeanMs = snapshot.Latency.DeviationMs.Mean;
        LatencyJitterMaxMs = snapshot.Latency.DeviationMs.Max;

        EstimatedDroppedReports = snapshot.Reliability.EstimatedDroppedReports;
        ConsecutiveDuplicateReportCount = snapshot.Reliability.ConsecutiveDuplicateReportCount;
        TotalReportCount = snapshot.Reliability.TotalReportCount;

        SignalAxes.Clear();
        foreach (var axis in snapshot.Signal)
        {
            SignalAxes.Add(axis);
        }

        HidCandidates.Clear();
        foreach (var candidate in snapshot.Diagnostics.Candidates)
        {
            HidCandidates.Add(candidate);
        }
        HasMultipleHidCandidates = snapshot.Diagnostics.Candidates.Count > 1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        Device.PropertyChanged -= OnDevicePropertyChanged;
    }
}

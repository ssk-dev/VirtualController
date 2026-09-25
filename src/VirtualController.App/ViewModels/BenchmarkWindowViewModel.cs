using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Benchmark;
using VirtualController.Core.Benchmark.Metrics;
using VirtualController.Core.Devices.Hid;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel des Echtzeit-Anzeigefensters fuer den Hardware-Benchmark eines einzelnen Geraets (siehe
/// <see cref="Views.BenchmarkWindow"/>). Enthaelt bewusst KEINE eigene Sitzungssteuerung - Start/Stop
/// bleiben Sache des zugrunde liegenden <see cref="DeviceConfigDeviceViewModel"/> (<see cref="Device"/>,
/// insbesondere <see cref="DeviceConfigDeviceViewModel.ToggleBenchmarkCommand"/> und
/// <see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/>), damit eine einmal gestartete Sitzung -
/// wie bereits fuer den Inline-Button dokumentiert - unabhaengig davon weiterlaeuft, ob dieses Fenster
/// gerade geoeffnet, geschlossen oder erneut geoeffnet wird. Dieses ViewModel beobachtet lediglich
/// <see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/> und pollt waehrenddessen periodisch
/// <see cref="DeviceConfigDeviceViewModel.GetLiveBenchmarkSnapshot"/> fuer eine Echtzeit-Anzeige der
/// bisher gemessenen Kennzahlen (Timing/Latenz/Reliability/Signal).
/// </summary>
public sealed partial class BenchmarkWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Aktualisierungsrate der Live-Anzeige - bewusst deutlich langsamer als die tatsaechliche
    /// Report-Rate des Geraets (die kann mehrere hundert Hz betragen), da hier lediglich eine fuer
    /// Menschen lesbare Momentaufnahme der bisher akkumulierten Statistik gezeigt wird, kein Rohdatenstrom.</summary>
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    /// <summary>Das Geraet, dessen Benchmark hier in Echtzeit angezeigt wird - Start/Stop-Buttons der View
    /// binden direkt gegen <see cref="DeviceConfigDeviceViewModel.ToggleBenchmarkCommand"/>/<see cref="DeviceConfigDeviceViewModel.IsBenchmarking"/>
    /// dieser Instanz, analog zum bereits bestehenden Inline-Button in DeviceConfigTemplates.xaml.</summary>
    public DeviceConfigDeviceViewModel Device { get; }

    /// <summary>Ob bereits mindestens eine Momentaufnahme empfangen wurde - solange false, zeigt die View
    /// einen Platzhaltertext statt (noch) bedeutungsloser Nullwerte.</summary>
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

    /// <summary>Siehe <see cref="LatencyResult"/>/<see cref="LatencyMetrics"/> - false, solange kein
    /// nominales Polling-Intervall ermittelbar war (dann bleibt die Latenz-Naeherung "nicht ermittelbar").</summary>
    [ObservableProperty]
    private bool _latencySupported;

    /// <summary>Latenz-Jitter: mittlere Abweichung jedes einzelnen beobachteten Report-Intervalls vom
    /// nominalen USB-Polling-Intervall (siehe <see cref="LatencyMetrics"/>-Klassendokumentation) - NICHT
    /// zu verwechseln mit einer echten Ende-zu-Ende-Latenz (dort nicht messbar, siehe Dokumentation).</summary>
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

    /// <summary>Je Achse ein Eintrag mit den aktuell gemessenen Signal-Kennzahlen (siehe <see cref="SignalAxisResult"/>) -
    /// leer, falls das Geraet keine erkannten Achsen hat oder noch keine Sitzung lief.</summary>
    public ObservableCollection<SignalAxisResult> SignalAxes { get; } = new();

    public BenchmarkWindowViewModel(DeviceConfigDeviceViewModel device)
    {
        Device = device;

        _timer = new DispatcherTimer { Interval = LiveRefreshInterval };
        _timer.Tick += OnTimerTick;

        Device.PropertyChanged += OnDevicePropertyChanged;

        // Falls das Fenster (erneut) geoeffnet wird, waehrend bereits eine Sitzung laeuft, sofort die
        // aktuelle Momentaufnahme anzeigen und die Live-Aktualisierung starten, statt bis zum ersten
        // regulaeren Timer-Tick zu warten.
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
            // Neue Sitzung gestartet: Anzeige einer eventuell noch sichtbaren vorherigen Sitzung zuruecksetzen,
            // damit keine veralteten Werte mit denen der neuen Sitzung verwechselt werden koennen.
            SignalAxes.Clear();
            HasSnapshot = false;
            RefreshSnapshot();
            _timer.Start();
        }
        else
        {
            // Sitzung beendet (regulaer gestoppt oder Fehler) - Timer anhalten, aber die zuletzt
            // angezeigten Werte bewusst NICHT loeschen: sie entsprechen naeherungsweise dem soeben als
            // JSON exportierten Endergebnis (siehe Device.LastBenchmarkFilePath) und bleiben so als letzter
            // Stand sichtbar, bis der Nutzer eine neue Sitzung startet.
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => RefreshSnapshot();

    private void RefreshSnapshot()
    {
        var snapshot = Device.GetLiveBenchmarkSnapshot();
        if (snapshot is null)
        {
            // Keine laufende Sitzung (mehr) - letzte angezeigte Werte unveraendert stehen lassen (siehe
            // OnDevicePropertyChanged).
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

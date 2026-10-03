using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Benchmark;
using VirtualController.Core.Devices;
using VirtualController.Core.Logging;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Represents a complete physical device in the configuration dialog: a global enable/disable switch
/// (disabled devices are removed from every virtual controller's device selection) and an expandable
/// list of physical inputs with rename and per-input enable/disable controls.
/// </summary>
public sealed partial class DeviceConfigDeviceViewModel : ObservableObject, IDisposable
{
    /// <summary>Live highlight refresh rate, matching <see cref="DeviceSelectionViewModel"/>.</summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromMilliseconds(33);

    public PhysicalDeviceInfo Device { get; private set; }

    private readonly DeviceSettings _settings;
    private readonly Action _notifyAvailabilityChanged;
    private readonly Action _notifySettingsChanged;
    private bool _inputsBuilt;

    private DispatcherTimer? _liveTimer;
    private IDeviceReader? _liveReader;
    private DeviceConfigAxisGroupViewModel? _axisGroup;

    private DeviceStateLogger? _stateLogger;
    private BenchmarkSession? _benchmarkSession;

    /// <summary>Whether state logging is running for this device (see <see cref="DeviceStateLogger"/>). Controls
    /// the associated button label ("Start logging" / "Stop logging"; see DeviceConfigTemplates.xaml). Unlike
    /// live polling (<see cref="StartLiveMonitoring"/>), logging continues regardless of tab visibility or
    /// selection until the user explicitly stops it.</summary>
    [ObservableProperty]
    private bool _isLogging;

    /// <summary>Path to the most recently written or currently active log file, shown in the UI (e.g. in the
    /// logging button tooltip); null until logging has run.</summary>
    [ObservableProperty]
    private string? _lastLogFilePath;

    /// <summary>Whether a hardware benchmark session is running for this device (see <see cref="BenchmarkSession"/>).
    /// Like <see cref="IsLogging"/>, it continues regardless of tab visibility until explicitly stopped, when
    /// the result is exported as JSON (see <see cref="ToggleBenchmark"/>).</summary>
    [ObservableProperty]
    private bool _isBenchmarking;

    /// <summary>Path to the most recently exported benchmark JSON file, shown in the UI (e.g. in the benchmark
    /// button tooltip); null until a benchmark has completed.</summary>
    [ObservableProperty]
    private string? _lastBenchmarkFilePath;

    /// <summary>Whether the main window's Device Configuration tab is visible and the window is not minimized
    /// (set by <see cref="DeviceConfigViewModel"/> through <see cref="SetScreenActive"/> based on
    /// <see cref="MainViewModel"/>). Live polling runs only when this is true and <see cref="IsSelected"/> is
    /// true (see <see cref="RefreshLiveMonitoringState"/>); otherwise the highlight and axis preview are not visible.</summary>
    private bool _isScreenActive;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>Whether this device is physically connected. Updated by <see cref="UpdateConnectionState"/> on
    /// each device scan (see MainViewModel.RefreshDevices, including periodic hot-plug polling). Disconnected
    /// devices remain visible but dimmed and can still be configured (renamed or calibrated) so their settings
    /// are not lost.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Whether this device is currently selected as a tab in the configuration dialog. Replaces the
    /// former "IsExpanded" Expander state now that devices are shown as tabs; input creation remains lazy and
    /// live monitoring still runs only for the visible tab.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the user has manually hidden this device (see <see cref="DeviceSettings.Hidden"/>).
    /// Hidden devices are removed from this tab's main list (see the separate "Hidden devices" list in
    /// MainWindow.xaml) and from every virtual controller's device selection. They can be restored at any
    /// time through <see cref="ToggleHiddenCommand"/>. This is useful for this app's ViGEmBus-emulated virtual
    /// controllers, which cannot otherwise be distinguished from physical hardware.</summary>
    [ObservableProperty]
    private bool _hidden;

    public ObservableCollection<IDeviceConfigGroup> InputGroups { get; } = new();

    public string DisplayName => Device.DisplayName;

    /// <summary>Controls the green/gray status indicator in the device list (see the Device Configuration tab
    /// in MainWindow.xaml): green only when the device is both enabled and connected, otherwise gray (see
    /// BoolToStatusBrushConverter).</summary>
    public bool IsActiveIndicator => Enabled && IsConnected;

    public DeviceConfigDeviceViewModel(PhysicalDeviceInfo device, DeviceSettings settings, Action notifyAvailabilityChanged, Action notifySettingsChanged, bool isConnected)
    {
        Device = device;
        _settings = settings;
        _notifyAvailabilityChanged = notifyAvailabilityChanged;
        _notifySettingsChanged = notifySettingsChanged;
        _enabled = settings.Enabled;
        _hidden = settings.Hidden;
        _isConnected = isConnected;
    }

    /// <summary>Updates this existing device view model's connection state without replacing it (see
    /// DeviceConfigViewModel.UpdateDevices, called on every device scan including periodic hot-plug polling).
    /// This avoids disrupting active edits. The underlying <see cref="Device"/> description is refreshed only
    /// until the input list is built (<see cref="EnsureInputsBuilt"/>), so the initial build uses the most
    /// accurate available capabilities (detected live rather than reconstructed from DeviceSettings.LastKnown*).
    /// A device that is already selected or built remains unchanged.</summary>
    public void UpdateConnectionState(PhysicalDeviceInfo device, bool isConnected)
    {
        if (!_inputsBuilt)
        {
            Device = device;
        }

        IsConnected = isConnected;
    }

    partial void OnEnabledChanged(bool value)
    {
        _settings.Enabled = value;
        _notifyAvailabilityChanged();
        OnPropertyChanged(nameof(IsActiveIndicator));
    }

    partial void OnHiddenChanged(bool value)
    {
        _settings.Hidden = value;
        _notifyAvailabilityChanged();
    }

    /// <summary>Hides or restores this device by moving it to or from the "Hidden devices" list. Called by the
    /// Hide button in the main list and the Show button in the hidden list (see MainWindow.xaml).</summary>
    [RelayCommand]
    private void ToggleHidden() => Hidden = !Hidden;

    /// <summary>Starts or stops state logging for this device (see <see cref="DeviceStateLogger"/>). Logging
    /// intentionally runs independently of <see cref="IsSelected"/> and tab visibility, continuing when the
    /// user switches devices or tabs until explicitly stopped here.</summary>
    [RelayCommand]
    private void ToggleLogging()
    {
        if (IsLogging)
        {
            StopLogging();
            return;
        }

        if (!IsConnected)
        {
            System.Windows.MessageBox.Show(
                $"\"{DisplayName}\" is not connected. Logging can start after the device is connected.",
                "Logging unavailable", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            _stateLogger = new DeviceStateLogger(Device);
            _stateLogger.LogFailed += OnStateLoggerFailed;
            LastLogFilePath = _stateLogger.FilePath;
            _stateLogger.Start();
            IsLogging = true;
        }
        catch (Exception ex)
        {
            // File or directory access can fail (e.g. due to missing permissions). This must not affect
            // the rest of the configuration; just inform the user.
            _stateLogger?.Dispose();
            _stateLogger = null;
            System.Windows.MessageBox.Show(
                $"Could not start logging for \"{DisplayName}\":\n{ex.Message}",
                "Logging failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>Raised on the logging background thread (see <see cref="DeviceStateLogger.LogFailed"/>), for
    /// example when the device disconnects during logging. Dispatches to the UI thread before changing the
    /// bound <see cref="IsLogging"/> property.</summary>
    private void OnStateLoggerFailed(Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsLogging = false;
            System.Windows.MessageBox.Show(
                $"Logging for \"{DisplayName}\" stopped because of an error:\n{ex.Message}",
                "Logging stopped", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }

    private void StopLogging()
    {
        if (_stateLogger is null)
        {
            IsLogging = false;
            return;
        }

        _stateLogger.LogFailed -= OnStateLoggerFailed;
        _stateLogger.Dispose();
        _stateLogger = null;
        IsLogging = false;
    }

    /// <summary>Opens or activates the real-time hardware benchmark window for this device (see
    /// <see cref="Views.BenchmarkWindow"/>). The window is non-modal and does not own the session lifecycle:
    /// closing it does not stop a running benchmark. As documented by <see cref="IsBenchmarking"/>, the
    /// session continues regardless of UI visibility until explicitly stopped from the window or through
    /// <see cref="ToggleBenchmarkCommand"/>.</summary>
    [RelayCommand]
    private void OpenBenchmarkWindow()
        => Views.BenchmarkWindow.ShowFor(this, System.Windows.Application.Current?.MainWindow);

    /// <summary>Returns a snapshot of benchmark results while a session is still running (see
    /// <see cref="BenchmarkSession.GetSnapshot"/>), for the real-time display in <see cref="Views.BenchmarkWindow"/>.
    /// Returns null while <see cref="IsBenchmarking"/> is false.</summary>
    public BenchmarkResult? GetLiveBenchmarkSnapshot() => _benchmarkSession?.GetSnapshot();

    /// <summary>Starts or stops a hardware benchmark session for this device (see <see cref="BenchmarkSession"/>).
    /// Like <see cref="ToggleLogging"/>, it runs independently of <see cref="IsSelected"/> and tab visibility.
    /// When stopped, the result is immediately exported as JSON (see <see cref="BenchmarkJsonExporter"/>).</summary>
    [RelayCommand]
    private void ToggleBenchmark()
    {
        if (IsBenchmarking)
        {
            StopBenchmark();
            return;
        }

        if (!IsConnected)
        {
            System.Windows.MessageBox.Show(
                $"\"{DisplayName}\" is not connected. The benchmark can start after the device is connected.",
                "Benchmark unavailable", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            _benchmarkSession = BenchmarkSession.TryCreate(Device);
            if (_benchmarkSession is null)
            {
                System.Windows.MessageBox.Show(
                    $"Could not find an associated HID device for \"{DisplayName}\". The hardware benchmark " +
                    "requires a device with a detectable HID path and is not available for some XInput-only devices.",
                    "Benchmark unavailable", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            _benchmarkSession.BenchmarkFailed += OnBenchmarkFailed;
            _benchmarkSession.Start();
            IsBenchmarking = true;
        }
        catch (Exception ex)
        {
            // As with ToggleLogging, a startup failure (e.g. the device is already open exclusively by
            // another app) must not affect the rest of the configuration.
            _benchmarkSession?.Dispose();
            _benchmarkSession = null;
            System.Windows.MessageBox.Show(
                $"Could not start the benchmark for \"{DisplayName}\":\n{ex.Message}",
                "Benchmark failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>Raised on the benchmark background thread (see <see cref="BenchmarkSession.BenchmarkFailed"/>),
    /// for example when the device disconnects during a session. Dispatches to the UI thread before changing
    /// the bound <see cref="IsBenchmarking"/> property. Exports any partial results collected so far.</summary>
    private void OnBenchmarkFailed(Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            FinishBenchmark();
            System.Windows.MessageBox.Show(
                $"Benchmark for \"{DisplayName}\" stopped because of an error:\n{ex.Message}",
                "Benchmark stopped", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }

    private void StopBenchmark() => FinishBenchmark();

    /// <summary>Shared cleanup for a normal stop (<see cref="StopBenchmark"/>) and failure
    /// (<see cref="OnBenchmarkFailed"/>): stops the session, exports its JSON result, and disposes the session.
    /// An export error (e.g. missing write permissions) is reported but must not prevent cleanup.</summary>
    private void FinishBenchmark()
    {
        if (_benchmarkSession is null)
        {
            IsBenchmarking = false;
            return;
        }

        _benchmarkSession.BenchmarkFailed -= OnBenchmarkFailed;

        try
        {
            var result = _benchmarkSession.Stop();
            var path = BenchmarkJsonExporter.BuildDefaultFilePath(Device);
            BenchmarkJsonExporter.Write(path, result);
            LastBenchmarkFilePath = path;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not export the benchmark result for \"{DisplayName}\":\n{ex.Message}",
                "Export failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _benchmarkSession.Dispose();
            _benchmarkSession = null;
            IsBenchmarking = false;
        }
    }

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsActiveIndicator));

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            EnsureInputsBuilt();
        }

        RefreshLiveMonitoringState();
    }

    /// <summary>Whether the main window's Device Configuration tab is visible and the window is not minimized.
    /// Live polling is allowed only in that state (see <see cref="RefreshLiveMonitoringState"/>). Updated by
    /// <see cref="DeviceConfigViewModel.SetScreenActive"/> for every relevant change (tab switch or minimize/restore).</summary>
    public void SetScreenActive(bool value)
    {
        if (_isScreenActive == value)
        {
            return;
        }

        _isScreenActive = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Starts or stops live polling based on the combined condition that the screen is active and
    /// this device is selected in the details pane. Both must be true for the live highlight and axis preview
    /// to be visible.</summary>
    private void RefreshLiveMonitoringState()
    {
        if (_isScreenActive && IsSelected)
        {
            StartLiveMonitoring();
        }
        else
        {
            StopLiveMonitoring();
        }
    }

    /// <summary>Assigns a physical input (button or D-pad direction) to a display group so users can find
    /// categories quickly on devices with many inputs. Axes are handled separately (see
    /// <see cref="EnsureInputsBuilt"/>) because positive and negative entries are displayed as pairs within
    /// a shared frame (<see cref="DeviceConfigAxisGroupViewModel"/>).</summary>
    private static string GetGroupName(PhysicalInputKind kind) => kind switch
    {
        PhysicalInputKind.Button => "Buttons",
        PhysicalInputKind.DPad or PhysicalInputKind.DPadUp or PhysicalInputKind.DPadDown
            or PhysicalInputKind.DPadLeft or PhysicalInputKind.DPadRight => "D-Pad",
        _ => "Other"
    };

    private void EnsureInputsBuilt()
    {
        if (_inputsBuilt)
        {
            return;
        }

        _inputsBuilt = true;

        var groupsByName = new Dictionary<string, DeviceConfigInputGroupViewModel>();
        var axisPairsByIndex = new Dictionary<int, DeviceConfigAxisPairViewModel>();
        var orderedAxisIndices = new List<int>();
        var axisGroup = new DeviceConfigAxisGroupViewModel();

        foreach (var inputRef in PhysicalInputCatalog.BuildInputs(Device))
        {
            var key = PhysicalInputCatalog.BuildStorageKey(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
            if (!_settings.Inputs.TryGetValue(key, out var inputSettings))
            {
                inputSettings = new InputSettings();
                _settings.Inputs[key] = inputSettings;
            }

            var row = new DeviceConfigInputRowViewModel(Device, inputRef, inputSettings, _notifySettingsChanged);

            if (inputRef.Kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative)
            {
                // The positive and negative entries of one physical axis share an index (see
                // PhysicalInputCatalog.AddAxisPair). Combine them into a framed pair instead of showing
                // unrelated rows. BuildAxisGroupItems uses GetStickAxisPairs to decide whether multiple
                // pairs should then be combined into a complete stick.
                if (!axisPairsByIndex.TryGetValue(inputRef.Index, out var pair))
                {
                    pair = new DeviceConfigAxisPairViewModel();
                    axisPairsByIndex[inputRef.Index] = pair;
                    orderedAxisIndices.Add(inputRef.Index);
                }

                if (inputRef.Kind == PhysicalInputKind.AxisPositive)
                {
                    pair.Positive = row;
                }
                else
                {
                    pair.Negative = row;
                }

                continue;
            }

            string groupName = GetGroupName(inputRef.Kind);
            if (!groupsByName.TryGetValue(groupName, out var group))
            {
                group = new DeviceConfigInputGroupViewModel(groupName);
                groupsByName[groupName] = group;
                InputGroups.Add(group);
            }

            group.Inputs.Add(row);
        }

        foreach (var pair in axisPairsByIndex.Values)
        {
            // Build the visualization only after both positive and, if present, negative entries have been
            // assigned so it can determine the correct bidirectional or unidirectional range (see
            // DeviceConfigAxisPairViewModel.BuildVisualization).
            pair.BuildVisualization();
        }

        BuildAxisGroupItems(axisGroup, orderedAxisIndices, axisPairsByIndex);
        _axisGroup = axisGroup;

        if (axisGroup.Items.Count > 0)
        {
            // Insert after the Buttons group, or first if there is no such group, to preserve the order
            // "Buttons -> Axes -> D-Pad -> Other".
            int insertIndex = groupsByName.TryGetValue("Buttons", out var buttonGroup) ? InputGroups.IndexOf(buttonGroup) + 1 : 0;
            InputGroups.Insert(insertIndex, axisGroup);
        }
    }

    /// <summary>
    /// Combines two related axis pairs (X and Y of the same stick) into one
    /// <see cref="DeviceConfigStickGroupViewModel"/>, using the same pairing logic as the live preview
    /// (<see cref="AxisVisualizationFactory"/>). This lets users disable all four positive/negative stick rows
    /// with one switch. Axes without a recognized stick partner (triggers, sliders, or unpaired rotation axes)
    /// remain individual <see cref="DeviceConfigAxisPairViewModel"/> instances. Items in <paramref name="axisGroup"/>
    /// retain the original catalog order (<paramref name="orderedAxisIndices"/>).
    /// </summary>
    private void BuildAxisGroupItems(
        DeviceConfigAxisGroupViewModel axisGroup,
        List<int> orderedAxisIndices,
        Dictionary<int, DeviceConfigAxisPairViewModel> axisPairsByIndex)
    {
        var stickByFirstIndex = new Dictionary<int, (int YIndex, string Name, bool InvertYForDisplay)>();
        var consumedAsStickY = new HashSet<int>();

        foreach (var (xIndex, yIndex, name, invertYForDisplay) in GetStickAxisPairs(Device.Api))
        {
            if (axisPairsByIndex.ContainsKey(xIndex) && axisPairsByIndex.ContainsKey(yIndex))
            {
                stickByFirstIndex[xIndex] = (yIndex, name, invertYForDisplay);
                consumedAsStickY.Add(yIndex);
            }
        }

        foreach (var index in orderedAxisIndices)
        {
            if (stickByFirstIndex.TryGetValue(index, out var stickInfo))
            {
                var xPair = axisPairsByIndex[index];
                var yPair = axisPairsByIndex[stickInfo.YIndex];
                axisGroup.Items.Add(new DeviceConfigStickGroupViewModel(
                    stickInfo.Name, xPair, yPair, Device, _settings, _notifySettingsChanged, stickInfo.InvertYForDisplay));
                continue;
            }

            if (consumedAsStickY.Contains(index))
            {
                continue; // Already processed above as the Y axis of a combined stick.
            }

            axisGroup.Items.Add(axisPairsByIndex[index]);
        }
    }

    /// <summary>Known X/Y axis index pairs that form a complete 2D stick, using the same mapping as
    /// <see cref="AxisVisualizationFactory"/> for the live preview and, here, shared enable/disable grouping
    /// and the embedded 2D visualization. For XInput, Z/RotationX also represent the right stick. DirectInput
    /// assigns no fixed meaning to those slots, so they must not be combined automatically. For both APIs,
    /// <c>InvertYForDisplay</c> is always true: <see cref="DirectInputDeviceReader"/> negates the raw Y value at
    /// the source so positive consistently means forward/up for XInput and DirectInput.</summary>
    private static IEnumerable<(int XIndex, int YIndex, string Name, bool InvertYForDisplay)> GetStickAxisPairs(InputApi api)
    {
        yield return (
            (int)PhysicalAxisId.X, (int)PhysicalAxisId.Y,
            api == InputApi.XInput ? "Left stick" : "Stick (X/Y)",
            true);

        if (api == InputApi.XInput)
        {
            yield return ((int)PhysicalAxisId.Z, (int)PhysicalAxisId.RotationX, "Right stick", true);
        }
    }

    private void StartLiveMonitoring()
    {
        if (_liveTimer is not null || !IsConnected)
        {
            return;
        }

        try
        {
            _liveReader = DeviceEnumerator.OpenReader(Device);
        }
        catch
        {
            // The device cannot currently be opened (e.g. it was just disconnected); skip live highlighting.
            _liveReader = null;
            return;
        }

        _liveTimer = new DispatcherTimer { Interval = LivePollInterval };
        _liveTimer.Tick += OnLiveTimerTick;
        _liveTimer.Start();
    }

    private void StopLiveMonitoring()
    {
        if (_liveTimer is not null)
        {
            _liveTimer.Tick -= OnLiveTimerTick;
            _liveTimer.Stop();
            _liveTimer = null;
        }

        _liveReader?.Dispose();
        _liveReader = null;

        foreach (var group in InputGroups)
        {
            foreach (var row in group.AllRows)
            {
                row.IsActive = false;
            }
        }

        _axisGroup?.Reset();
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        if (_liveReader is null)
        {
            return;
        }

        if (!_liveReader.Poll(out var state))
        {
            // The device was disconnected; stop monitoring until the user selects it again.
            StopLiveMonitoring();
            return;
        }

        foreach (var group in InputGroups)
        {
            foreach (var row in group.AllRows)
            {
                row.UpdateActiveState(state);
            }
        }

        _axisGroup?.UpdateFromState(state);
    }

    public void Dispose()
    {
        StopLiveMonitoring();
        StopLogging();
        StopBenchmark();
    }
}

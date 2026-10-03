using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.App.Diagnostics;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Represents one virtual controller in the UI: its name, selected layout, target polling rate, and complete
/// mapping table (which physical controllers/inputs affect it). Property changes are written directly to
/// the underlying <see cref="VirtualControllerProfile"/>, which is persisted when saved.
/// </summary>
public sealed partial class VirtualControllerViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Runtime refresh rate for synchronizing <see cref="VirtualControllerProfile.ActiveModeId"/> with
    /// the UI (selected tab and green active indicator) while the controller is running; see
    /// <see cref="_activeModeSyncTimer"/>. Kept low because this is only for smooth visual feedback, not the
    /// actual 1000 Hz mapping poll.</summary>
    private static readonly TimeSpan ActiveModeSyncInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Polls <see cref="VirtualControllerProfile.ActiveModeId"/> while the controller is running.
    /// <see cref="Engine.ControllerSession"/> changes it on its high-frequency polling thread when a toggle or
    /// switch trigger fires, and the UI uses it to keep each tab's green active indicator
    /// (<see cref="ModeViewModel.IsActive"/>) up to date. The selected tab (<see cref="SelectedMode"/> and its
    /// mapping table) follows automatically only when <see cref="VirtualControllerProfile.ActiveModeId"/>
    /// actually changes since the previous tick (see <see cref="_lastObservedActiveModeId"/>). A manual tab
    /// selection remains in place until a trigger activates another mode (see <see cref="OnActiveModeSyncTimerTick"/>).</summary>
    private DispatcherTimer? _activeModeSyncTimer;

    /// <summary>Last <see cref="VirtualControllerProfile.ActiveModeId"/> observed by
    /// <see cref="OnActiveModeSyncTimerTick"/>. Distinguishes an actual trigger switch (the value changes)
    /// from a manual tab selection (only <see cref="SelectedMode"/> differs) so the selected tab follows only
    /// real trigger switches and users can freely inspect other tabs between them.</summary>
    private Guid? _lastObservedActiveModeId;

    public VirtualControllerProfile Profile { get; }

    /// <summary>Options for ComboBox bindings in the view.</summary>
    public static IReadOnlyList<ControllerLayout> LayoutOptions { get; } = Enum.GetValues<ControllerLayout>();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private ControllerLayout _layout;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private int _pollingRateHz;

    [ObservableProperty]
    private string _statusText = "Stopped";

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Currently resolved ViGEmBus backend (Xbox 360/DualShock 4), derived from the layout.</summary>
    public VirtualBackend ResolvedBackend => LayoutBackendMap.Resolve(Layout);

    public ObservableCollection<ModeViewModel> Modes { get; } = new();

    [ObservableProperty]
    private ModeViewModel? _selectedMode;

    /// <summary>Options for ComboBox/RadioButton bindings in the view.</summary>
    public static IReadOnlyList<ModeSwitchMechanism> ModeSwitchMechanismOptions { get; } = Enum.GetValues<ModeSwitchMechanism>();

    [ObservableProperty]
    private ModeSwitchMechanism _modeSwitchMechanism;

    /// <summary>Whether to show a brief on-screen notification whenever the active mode changes
    /// (see <see cref="ModeActivated"/>, handled by <see cref="MainViewModel"/>).</summary>
    [ObservableProperty]
    private bool _notifyOnModeChange;

    /// <summary>Whether to automatically block this controller's assigned physical devices through HidHide
    /// while it is running (see <see cref="Core.Mapping.VirtualControllerProfile.HidHideEnabled"/>). Effective
    /// only when HidHide is installed and ready; otherwise the associated checkbox in
    /// <see cref="Views.MainWindow"/> is disabled (see <see cref="MainViewModel.IsHidHideAvailable"/>).</summary>
    [ObservableProperty]
    private bool _hidHideEnabled;

    /// <summary>Whether this controller should start and stop automatically based on whether the program at
    /// <see cref="AutoStartExecutablePath"/> is running (see <see cref="Core.Mapping.VirtualControllerProfile.AutoStartEnabled"/>).
    /// Evaluated through periodic polling in <see cref="MainViewModel"/>.</summary>
    [ObservableProperty]
    private bool _autoStartEnabled;

    /// <summary>Full path to the .exe whose process is monitored (see <see cref="AutoStartEnabled"/> and
    /// <see cref="Core.Mapping.VirtualControllerProfile.AutoStartExecutablePath"/>). Set through a file picker
    /// opened by <see cref="ChooseAutoStartExecutableCommand"/>.</summary>
    [ObservableProperty]
    private string? _autoStartExecutablePath;

    /// <summary>Display name of the selected .exe (filename only), shown beside the "Choose program" button,
    /// or a placeholder when no program is selected.</summary>
    public string AutoStartExecutableDisplayName => string.IsNullOrWhiteSpace(AutoStartExecutablePath)
        ? "(no program selected)"
        : System.IO.Path.GetFileName(AutoStartExecutablePath);

    /// <summary>Used only with <see cref="Core.Mapping.ModeSwitchMechanism.Toggle"/>: display name of the physical
    /// input that advances to the next enabled mode on each rising edge.</summary>
    [ObservableProperty]
    private string _toggleTriggerDisplayName = "(no input assigned)";

    [ObservableProperty]
    private bool _isCapturingToggleTrigger;

    /// <summary>Seconds remaining while Capture waits for a physical input for the controller-wide toggle
    /// trigger, counting down from <see cref="CaptureTimeout"/> to zero. Shown beside the Capture button.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    /// <summary>Connected physical controllers with a checkbox to include them for this virtual controller's
    /// capture function and active mapping.</summary>
    public ObservableCollection<DeviceSelectionViewModel> AvailableDeviceSelections { get; } = new();

    /// <summary>Devices assigned to this virtual controller (see
    /// <see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>) that are currently disconnected.
    /// Shown in a separate dimmed "Assigned devices" list below <see cref="AvailableDeviceSelections"/> so the
    /// assignment remains visible. Recomputed by <see cref="RefreshDeviceSelections"/> (see
    /// <see cref="UpdateAssignedDisconnectedDeviceSelections"/>).</summary>
    public ObservableCollection<AssignedDisconnectedDeviceViewModel> AssignedDisconnectedDeviceSelections { get; } = new();

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;
    private readonly Func<PhysicalInputRef, string?> _getCustomInputName;
    private readonly Action<PhysicalInputRef, string> _setCustomInputName;

    public event Action<VirtualControllerViewModel>? StartRequested;
    public event Action<VirtualControllerViewModel>? StopRequested;
    public event Action<VirtualControllerViewModel>? RemoveRequested;
    public event Action<VirtualControllerViewModel>? ProfileChanged;

    /// <summary>Raised when this controller's active mode actually changes (by tab click while stopped or by
    /// a toggle/switch trigger while running) and <see cref="NotifyOnModeChange"/> is enabled. Forwarded by
    /// <see cref="MainViewModel"/> so <see cref="Views.MainWindow"/> can show a brief on-screen notification
    /// (see <see cref="Views.ModeChangeToast"/>).</summary>
    public event Action<VirtualControllerViewModel, ModeViewModel>? ModeActivated;

    public VirtualControllerViewModel(
        VirtualControllerProfile profile,
        Func<IReadOnlyList<PhysicalDeviceInfo>> getAvailableDevices,
        Func<IReadOnlyDictionary<string, DeviceSettings>> getDeviceSettings,
        Func<PhysicalInputRef, string?> getCustomInputName,
        Action<PhysicalInputRef, string> setCustomInputName)
    {
        Profile = profile;
        _getAvailableDevices = getAvailableDevices;
        _getDeviceSettings = getDeviceSettings;
        _getCustomInputName = getCustomInputName;
        _setCustomInputName = setCustomInputName;

        _name = profile.Name;
        _layout = profile.Layout;
        _enabled = profile.Enabled;
        _pollingRateHz = profile.PollingRateHz;
        _modeSwitchMechanism = profile.ModeSwitchMechanism;
        _notifyOnModeChange = profile.NotifyOnModeChange;
        _hidHideEnabled = profile.HidHideEnabled;
        _autoStartEnabled = profile.AutoStartEnabled;
        _autoStartExecutablePath = profile.AutoStartExecutablePath;

        var knownDevices = getAvailableDevices();

        // For a new profile with no device selection, include all currently connected devices by default.
        if (Profile.AssignedDeviceIds.Count == 0 && knownDevices.Count > 0)
        {
            Profile.AssignedDeviceIds = knownDevices.Select(d => d.DeviceId).ToList();
        }

        RefreshDeviceSelections(knownDevices);

        if (profile.ToggleTrigger is { } toggleTrigger)
        {
            _toggleTriggerDisplayName = PhysicalInputDisplayNameHelper.Build(
                toggleTrigger.DeviceId, toggleTrigger.Kind, toggleTrigger.Index, knownDevices, getDeviceSettings(), out _);
        }

        foreach (var mode in profile.Modes)
        {
            AddModeViewModel(mode, knownDevices);
        }

        // RefreshDeviceSelections() ran before this loop while Modes was still empty, so it could not detect
        // existing mapping sources. Run it again now so devices referenced by loaded mappings are monitored
        // from the start, keeping mapping row highlights active even when "Available devices" is collapsed.
        UpdateMappingSourceLiveMonitoring();

        SelectedMode = Modes.FirstOrDefault(m => m.Mode.Id == profile.ActiveModeId) ?? Modes.FirstOrDefault();
    }

    /// <summary>Incrementally reconciles the device selection list with the currently available physical
    /// controllers: removes devices no longer in <paramref name="devices"/> and adds only new devices, keeping
    /// existing instances intact. This method now runs every few seconds through the automatic device detection
    /// hot-plug timer in <see cref="MainViewModel"/>. Rebuilding the list would reset each device's expanded
    /// state (<see cref="DeviceSelectionViewModel.IsExpanded"/>), input rows, and live highlights, causing an
    /// expanded list to collapse while the user is testing an input.</summary>
    public void RefreshDeviceSelections(IReadOnlyList<PhysicalDeviceInfo> devices)
    {
        var incomingIds = devices.Select(d => d.DeviceId).ToHashSet();

        foreach (var stale in AvailableDeviceSelections.Where(s => !incomingIds.Contains(s.Device.DeviceId)).ToList())
        {
            stale.SelectionChanged -= OnDeviceSelectionChanged;
            stale.AssignInputRequested -= OnAssignInputRequested;
            stale.LiveStateChanged -= OnDeviceLiveStateChanged;
            stale.Dispose();
            AvailableDeviceSelections.Remove(stale);
        }

        var existingIds = AvailableDeviceSelections.Select(s => s.Device.DeviceId).ToHashSet();

        foreach (var device in devices)
        {
            if (existingIds.Contains(device.DeviceId))
            {
                continue; // Keep the existing view model (including expanded/selected state and inputs) intact.
            }

            bool isSelected = Profile.AssignedDeviceIds.Contains(device.DeviceId);

            var selection = new DeviceSelectionViewModel(device, isSelected, _getCustomInputName, _setCustomInputName, _getDeviceSettings);
            selection.SelectionChanged += OnDeviceSelectionChanged;
            selection.AssignInputRequested += OnAssignInputRequested;
            selection.LiveStateChanged += OnDeviceLiveStateChanged;
            selection.SetScreenActive(_isScreenActive);
            AvailableDeviceSelections.Add(selection);
        }

        UpdateAssignedDisconnectedDeviceSelections(devices);

        // Existing mapping rows display their physical source's connection status (see
        // MappingRowViewModel.IsSourceConnected), so refresh it whenever the device list changes, e.g. when a
        // device disconnects or reconnects while the app is running.
        foreach (var mode in Modes)
        {
            foreach (var row in mode.Mappings)
            {
                row.RefreshSourceConnectionState(devices);
            }

            mode.RefreshSwitchTriggerDisplayName(devices);
        }

        if (Profile.ToggleTrigger is { } toggleTrigger)
        {
            ToggleTriggerDisplayName = PhysicalInputDisplayNameHelper.Build(
                toggleTrigger.DeviceId, toggleTrigger.Kind, toggleTrigger.Index, devices, _getDeviceSettings(), out _);
        }

        UpdateMappingSourceLiveMonitoring();
    }

    /// <summary>Rebuilds <see cref="AssignedDisconnectedDeviceSelections"/> from the current assignment
    /// (<see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>), including exactly the assigned
    /// device IDs missing from <paramref name="availableDevices"/> (disconnected, disabled, or hidden). Uses
    /// the last known display name (see <see cref="Core.Devices.DeviceSettings.LastKnownDisplayName"/>), as in
    /// <see cref="MainViewModel.GetAllKnownDevices"/>.</summary>
    private void UpdateAssignedDisconnectedDeviceSelections(IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        var availableIds = availableDevices.Select(d => d.DeviceId).ToHashSet();
        var deviceSettings = _getDeviceSettings();

        AssignedDisconnectedDeviceSelections.Clear();

        foreach (var deviceId in Profile.AssignedDeviceIds)
        {
            if (availableIds.Contains(deviceId))
            {
                continue; // Connected; already represented in AvailableDeviceSelections.
            }

            string displayName = deviceSettings.TryGetValue(deviceId, out var settings)
                ? settings.LastKnownDisplayName ?? deviceId
                : deviceId;

            AssignedDisconnectedDeviceSelections.Add(new AssignedDisconnectedDeviceViewModel(deviceId, displayName));
        }
    }

    /// <summary>Returns only the physical devices currently selected for this virtual controller, e.g. as
    /// sources for mapping row capture.</summary>
    public IReadOnlyList<PhysicalDeviceInfo> GetFilteredDevices()
        => AvailableDeviceSelections.Where(s => s.IsSelected).Select(s => s.Device).ToList();

    /// <summary>Ensures every physical device used as a source by any mapping row in any mode is monitored
    /// live (see <see cref="DeviceSelectionViewModel.SetMappingSourceMonitoringRequested"/>), regardless of
    /// whether its input list in "Available devices" is expanded. This keeps assigned mapping rows
    /// (<see cref="MappingRowViewModel.IsSourceActive"/>) highlighted even when the user never opens that list.
    /// Call again whenever the set of source devices may change: mapping rows or modes are added/removed, a
    /// row's source changes through Capture/Assign, or device selections are rebuilt
    /// (<see cref="RefreshDeviceSelections"/>).</summary>
    public void UpdateMappingSourceLiveMonitoring()
    {
        var sourceDeviceIds = Modes
            .SelectMany(mode => mode.Mappings)
            .Select(row => row.Entry.SourceDeviceId)
            .Where(deviceId => !string.IsNullOrEmpty(deviceId))
            .ToHashSet();

        foreach (var selection in AvailableDeviceSelections)
        {
            selection.SetMappingSourceMonitoringRequested(sourceDeviceIds.Contains(selection.Device.DeviceId));
        }
    }

    /// <summary>Whether the main window's Mapping tab is visible, this controller is selected, and the window
    /// is not minimized (see <see cref="SetScreenActive"/>, set by <see cref="MainViewModel"/>). Propagated to
    /// each <see cref="DeviceSelectionViewModel"/> in <see cref="AvailableDeviceSelections"/> so live polling
    /// (see <see cref="DeviceSelectionViewModel.SetScreenActive"/>) runs only when this condition is true;
    /// otherwise the highlight cannot be seen.</summary>
    private bool _isScreenActive;

    /// <summary>Sets whether this controller is currently visible: the Mapping tab is active, this controller
    /// is selected (see <see cref="MainViewModel.SelectedController"/>), and the window is not minimized.
    /// <see cref="MainViewModel"/> recalculates this for every controller whenever the tab, selection, or
    /// window state changes; only the visible controller receives <c>true</c>.</summary>
    public void SetScreenActive(bool value)
    {
        _isScreenActive = value;

        foreach (var selection in AvailableDeviceSelections)
        {
            selection.SetScreenActive(value);
        }
    }

    private void OnDeviceSelectionChanged(DeviceSelectionViewModel selection)
    {
        // Do not replace the assignment with only the visible selection. Assigned but disconnected devices
        // (see AssignedDisconnectedDeviceSelections) are absent from AvailableDeviceSelections and would
        // otherwise be incorrectly removed whenever the checkbox for another connected device changes.
        var selectedVisibleIds = AvailableDeviceSelections.Where(s => s.IsSelected).Select(s => s.Device.DeviceId);
        var stillAssignedDisconnectedIds = AssignedDisconnectedDeviceSelections.Select(d => d.DeviceId);
        Profile.AssignedDeviceIds = selectedVisibleIds.Union(stillAssignedDisconnectedIds).ToList();
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Mirrors the live highlight from the expandable device input list
    /// (<see cref="DeviceSelectionViewModel.LiveStateChanged"/>, using the same mechanism as
    /// <see cref="PhysicalInputRowViewModel.IsActive"/>) to every mapping row across all modes, not only
    /// <see cref="SelectedMode"/>. Rows sourced from this device are highlighted while their physical input is
    /// active, just as in the available devices list. When <paramref name="state"/> is null (collapsed or
    /// disconnected), clears the highlight.</summary>
    private void OnDeviceLiveStateChanged(DeviceSelectionViewModel selection, DeviceState? state)
    {
        string deviceId = selection.Device.DeviceId;

        foreach (var mode in Modes)
        {
            foreach (var row in mode.Mappings)
            {
                if (state is { } value)
                {
                    row.UpdateSourceActiveState(deviceId, value);
                }
                else
                {
                    row.ResetSourceActiveState();
                }
            }
        }
    }

    [RelayCommand]
    private void AddMode()
    {
        var mode = new ControllerMode
        {
            Id = Guid.NewGuid(),
            Name = $"Mode {Modes.Count + 1}"
        };

        Profile.Modes.Add(mode);
        var viewModel = AddModeViewModel(mode, _getAvailableDevices());
        SelectedMode = viewModel;

        // Activate the first mode automatically so the controller has a mapping to evaluate without requiring
        // the user to activate it manually.
        if (Profile.ActiveModeId is null)
        {
            Profile.ActiveModeId = mode.Id;
        }

        ProfileChanged?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanAddMapping))]
    private void AddMapping()
    {
        // Create an unassigned row; its source will be set through Capture on the row.
        CreateAndAddMapping(sourceDeviceId: string.Empty, sourceKind: PhysicalInputKind.Button, sourceIndex: 0);
    }

    /// <summary>Every mapping entry belongs to a mode, so a new mapping cannot be added without a selected
    /// mode (see <see cref="CreateAndAddMapping"/>). Controls the Add mapping row button in the view.</summary>
    private bool CanAddMapping() => SelectedMode is not null;

    /// <summary>Called when the user clicks Assign on a physical input in the expandable device list. Creates
    /// a mapping row with its physical source already set, leaving only the target type and value to choose.</summary>
    private void OnAssignInputRequested(PhysicalInputRef inputRef)
        => CreateAndAddMapping(inputRef.DeviceId, inputRef.Kind, inputRef.Index);

    private void CreateAndAddMapping(string sourceDeviceId, PhysicalInputKind sourceKind, int sourceIndex)
    {
        // Every mapping entry belongs to exactly one mode. Without a selected mode, there is no target table
        // to add it to; a mode must exist before mappings can be captured or assigned.
        if (SelectedMode is not { } mode)
        {
            return;
        }

        var entry = new MappingEntry
        {
            SourceDeviceId = sourceDeviceId,
            SourceKind = sourceKind,
            SourceIndex = sourceIndex,
            TargetKind = MappingTargetKind.Button,
            TargetButton = VirtualButton.South
        };

        mode.Mode.Mappings.Add(entry);
        mode.AddRowViewModel(entry, _getAvailableDevices(), GetFilteredDevices, () => Layout);
        UpdateMappingSourceLiveMonitoring();
        ProfileChanged?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => StartRequested?.Invoke(this);

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => StopRequested?.Invoke(this);

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    public void SetRunningState(bool running, string? statusText = null)
    {
        DebugLog.Write($"[VCVM:{Name}] SetRunningState called: running={running} statusText='{statusText}' (previous IsRunning={IsRunning}).");
        IsRunning = running;
        StatusText = statusText ?? (running ? "Running" : "Stopped");
    }

    partial void OnIsRunningChanged(bool value)
    {
        // Disable Start while the controller is running (and vice versa for Stop). This prevents starting a
        // second virtual controller without stopping the first, which previously left an orphaned device
        // registered with ViGEmBus/Windows until the process exited.
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();

        if (value)
        {
            StartActiveModeSync();
        }
        else
        {
            StopActiveModeSync();
        }
    }

    /// <summary>Starts periodic synchronization of <see cref="VirtualControllerProfile.ActiveModeId"/> with
    /// the UI while the controller is running (see <see cref="_activeModeSyncTimer"/>).</summary>
    private void StartActiveModeSync()
    {
        if (_activeModeSyncTimer is not null)
        {
            return;
        }

        _activeModeSyncTimer = new DispatcherTimer { Interval = ActiveModeSyncInterval };
        _activeModeSyncTimer.Tick += OnActiveModeSyncTimerTick;
        _lastObservedActiveModeId = Profile.ActiveModeId;
        _activeModeSyncTimer.Start();
    }

    private void StopActiveModeSync()
    {
        if (_activeModeSyncTimer is null)
        {
            return;
        }

        _activeModeSyncTimer.Tick -= OnActiveModeSyncTimerTick;
        _activeModeSyncTimer.Stop();
        _activeModeSyncTimer = null;
    }

    private void OnActiveModeSyncTimerTick(object? sender, EventArgs e)
    {
        // Always refresh the green active indicators. Update SelectedMode (and its mapping table) only when
        // Profile.ActiveModeId actually changes since the previous tick, meaning a toggle/switch trigger fired.
        // A manual tab click leaves that value unchanged (see OnSelectedModeChanged), so users can inspect
        // another mode while the controller runs without the next 100 ms tick resetting the view. When a
        // trigger activates another mode, the view follows immediately.
        RefreshActiveModeIndicators();

        bool triggerSwitchedMode = Profile.ActiveModeId != _lastObservedActiveModeId;
        _lastObservedActiveModeId = Profile.ActiveModeId;

        if (triggerSwitchedMode)
        {
            var newlyActiveMode = Modes.FirstOrDefault(mode => mode.Mode.Id == Profile.ActiveModeId);
            SelectedMode = newlyActiveMode;

            if (NotifyOnModeChange && newlyActiveMode is not null)
            {
                ModeActivated?.Invoke(this, newlyActiveMode);
            }
        }
    }

    private ModeViewModel AddModeViewModel(ControllerMode mode, IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        var viewModel = new ModeViewModel(mode, knownDevices, _getAvailableDevices, _getDeviceSettings, () => Layout, GetFilteredDevices);
        viewModel.RemoveRequested += OnModeRemoveRequested;
        viewModel.Changed += OnModeChanged;
        viewModel.SwitchTriggerCaptured += OnModeSwitchTriggerCaptured;
        RefreshModeActiveState(viewModel);
        Modes.Add(viewModel);
        return viewModel;
    }

    private void OnModeChanged(ModeViewModel mode)
    {
        UpdateMappingSourceLiveMonitoring();
        ProfileChanged?.Invoke(this);
    }

    private void OnModeRemoveRequested(ModeViewModel mode)
    {
        mode.RemoveRequested -= OnModeRemoveRequested;
        mode.Changed -= OnModeChanged;
        mode.SwitchTriggerCaptured -= OnModeSwitchTriggerCaptured;
        mode.Dispose();

        Profile.Modes.Remove(mode.Mode);
        Modes.Remove(mode);

        if (Profile.ActiveModeId == mode.Mode.Id)
        {
            var fallback = Modes.FirstOrDefault();
            Profile.ActiveModeId = fallback?.Mode.Id;
        }

        if (SelectedMode == mode)
        {
            SelectedMode = Modes.FirstOrDefault();
        }

        UpdateMappingSourceLiveMonitoring();
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Called when the user sets a mode's switch trigger through Capture/Assign. Ensures that the
    /// same physical input is not already used as a switch trigger by another mode in this controller before
    /// accepting it.</summary>
    private void OnModeSwitchTriggerCaptured(ModeViewModel mode, PhysicalInputRef trigger)
    {
        bool isAlreadyUsedElsewhere = Modes.Any(other => other != mode
            && other.Mode.SwitchTrigger is { } otherTrigger
            && otherTrigger.DeviceId == trigger.DeviceId
            && otherTrigger.Kind == trigger.Kind
            && otherTrigger.Index == trigger.Index);

        if (isAlreadyUsedElsewhere)
        {
            mode.SwitchTriggerValidationError = "This input is already used by another mode on this controller.";
            return;
        }

        mode.SetSwitchTrigger(trigger, _getAvailableDevices());
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Updates every mode's green active indicator (<see cref="ModeViewModel.IsActive"/>) from
    /// <see cref="VirtualControllerProfile.ActiveModeId"/>.</summary>
    private void RefreshActiveModeIndicators()
    {
        foreach (var mode in Modes)
        {
            RefreshModeActiveState(mode);
        }
    }

    private void RefreshModeActiveState(ModeViewModel mode) => mode.IsActive = mode.Mode.Id == Profile.ActiveModeId;

    partial void OnNameChanged(string value)
    {
        Profile.Name = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnLayoutChanged(ControllerLayout value)
    {
        Profile.Layout = value;
        OnPropertyChanged(nameof(ResolvedBackend));

        // Existing mapping rows display layout-dependent target labels (e.g. "A" on Xbox vs. "Cross" on
        // PlayStation), so refresh them when the layout changes even though the stored values remain the same.
        foreach (var mode in Modes)
        {
            foreach (var row in mode.Mappings)
            {
                row.RefreshForLayoutChange();
            }
        }

        ProfileChanged?.Invoke(this);
    }

    partial void OnEnabledChanged(bool value)
    {
        Profile.Enabled = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnPollingRateHzChanged(int value)
    {
        Profile.PollingRateHz = value;
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Raised when the user selects another mode in the tab bar or when
    /// <see cref="OnActiveModeSyncTimerTick"/> follows a mode activated by a trigger. While the controller is
    /// stopped, selecting a tab activates that mode directly. While it is running, only toggle/switch triggers
    /// change the active mode (see <see cref="Engine.ControllerSession.EvaluateModeSwitching"/>); a manual tab
    /// click changes only the view and leaves Profile.ActiveModeId unchanged until a trigger activates another mode.</summary>
    partial void OnSelectedModeChanged(ModeViewModel? value)
    {
        DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged called: new value='{value?.Name ?? "null"}' (Id={value?.Mode.Id}) IsRunning={IsRunning}");

        AddMappingCommand.NotifyCanExecuteChanged();

        if (IsRunning)
        {
            DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged: IsRunning=true -> Profile.ActiveModeId is unchanged; only the view (SelectedMode.Mappings) changes.");
            RefreshActiveModeIndicators();
            return;
        }

        Profile.ActiveModeId = value?.Mode.Id;
        DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged: IsRunning=false -> Profile.ActiveModeId set to {Profile.ActiveModeId}.");
        // Update only after assigning ActiveModeId; otherwise the green active indicator and tab highlight
        // would lag one click behind.
        RefreshActiveModeIndicators();

        if (NotifyOnModeChange && value is not null)
        {
            ModeActivated?.Invoke(this, value);
        }

        ProfileChanged?.Invoke(this);
    }

    partial void OnModeSwitchMechanismChanged(ModeSwitchMechanism value)
    {
        Profile.ModeSwitchMechanism = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnNotifyOnModeChangeChanged(bool value)
    {
        Profile.NotifyOnModeChange = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnHidHideEnabledChanged(bool value)
    {
        Profile.HidHideEnabled = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnAutoStartEnabledChanged(bool value)
    {
        Profile.AutoStartEnabled = value;
        ProfileChanged?.Invoke(this);
    }

    partial void OnAutoStartExecutablePathChanged(string? value)
    {
        Profile.AutoStartExecutablePath = value;
        OnPropertyChanged(nameof(AutoStartExecutableDisplayName));
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Opens an .exe-filtered file picker so the user can choose the program whose process triggers
    /// automatic start/stop for this controller (see <see cref="AutoStartEnabled"/> and
    /// <see cref="AutoStartExecutablePath"/>). Stores the full path, not just the filename, to distinguish
    /// programs with the same name in different locations.</summary>
    [RelayCommand]
    private void ChooseAutoStartExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose program to start controller automatically",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AutoStartExecutablePath = dialog.FileName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCaptureToggleTrigger))]
    private async Task CaptureToggleTriggerAsync()
    {
        IsCapturingToggleTrigger = true;
        try
        {
            var devices = _getAvailableDevices();
            var captured = await CaptureCountdownHelper.CaptureWithCountdownAsync(
                devices, CaptureTimeout, _getDeviceSettings(), seconds => CaptureCountdownSeconds = seconds).ConfigureAwait(true);
            if (captured is not null)
            {
                ApplyToggleTrigger(captured, devices);
            }
        }
        finally
        {
            IsCapturingToggleTrigger = false;
        }
    }

    private bool CanCaptureToggleTrigger() => !IsCapturingToggleTrigger;

    partial void OnIsCapturingToggleTriggerChanged(bool value) => CaptureToggleTriggerCommand.NotifyCanExecuteChanged();

    /// <summary>Builds the complete list for the modal Assign dialog for the controller-wide toggle trigger
    /// (an alternative to Capture), like <see cref="ModeViewModel.BuildAssignableInputs"/> and
    /// <see cref="MappingRowViewModel.BuildAssignableInputs"/>.</summary>
    public IReadOnlyList<AssignableInputOption> BuildAssignableToggleTriggerInputs()
    {
        var deviceSettings = _getDeviceSettings();
        var options = new List<AssignableInputOption>();

        foreach (var device in _getAvailableDevices())
        {
            foreach (var inputRef in PhysicalInputCatalog.BuildInputs(device))
            {
                if (!deviceSettings.IsInputEnabled(inputRef.DeviceId, inputRef.Kind, inputRef.Index))
                {
                    continue;
                }

                var storageKey = PhysicalInputCatalog.BuildStorageKey(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
                string? customName = deviceSettings.TryGetValue(inputRef.DeviceId, out var settingsEntry)
                    && settingsEntry.Inputs.TryGetValue(storageKey, out var inputSettings)
                    ? inputSettings.CustomName
                    : null;

                options.Add(new AssignableInputOption(device, inputRef, customName ?? inputRef.DisplayName));
            }
        }

        return options;
    }

    /// <summary>Called when the user confirms a physical input as the controller-wide toggle trigger in the
    /// modal Assign dialog; an alternative to physical Capture.</summary>
    public void AssignToggleTrigger(AssignableInputOption selected)
        => ApplyToggleTrigger(selected.InputRef, _getAvailableDevices());

    private void ApplyToggleTrigger(PhysicalInputRef captured, IReadOnlyList<PhysicalDeviceInfo> devices)
    {
        Profile.ToggleTrigger = new PhysicalInputTrigger
        {
            DeviceId = captured.DeviceId,
            Kind = captured.Kind,
            Index = captured.Index
        };
        ToggleTriggerDisplayName = PhysicalInputDisplayNameHelper.Build(
            captured.DeviceId, captured.Kind, captured.Index, devices, _getDeviceSettings(), out _);
        ProfileChanged?.Invoke(this);
    }

    public void Dispose()
    {
        StopActiveModeSync();

        foreach (var selection in AvailableDeviceSelections)
        {
            selection.SelectionChanged -= OnDeviceSelectionChanged;
            selection.AssignInputRequested -= OnAssignInputRequested;
            selection.LiveStateChanged -= OnDeviceLiveStateChanged;
            selection.Dispose();
        }

        foreach (var mode in Modes)
        {
            mode.RemoveRequested -= OnModeRemoveRequested;
            mode.Changed -= OnModeChanged;
            mode.SwitchTriggerCaptured -= OnModeSwitchTriggerCaptured;
            mode.Dispose();
        }
    }
}


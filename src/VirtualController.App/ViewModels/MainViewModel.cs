using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.App.Services;
using VirtualController.Core.Devices;
using VirtualController.Core.Engine;
using VirtualController.Core.Mapping;
using VirtualController.Core.Profiles;
using VirtualController.Core.Updates;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Root view model for the main window. Manages the <see cref="ControllerManager"/> (ViGEmBus connection
/// and running sessions), the configured virtual controllers, and loading/saving the complete profile.
/// Changes to a virtual controller are automatically forwarded to its associated, possibly running
/// <see cref="ControllerSession"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ControllerManager _manager = new();

    /// <summary>Device-wide settings (enabled state, rename, calibration, deadzone, and curve per physical
    /// input), keyed by <see cref="PhysicalDeviceInfo.DeviceId"/>. Saved and loaded with the controller
    /// profiles in <see cref="AppProfile"/>.</summary>
    private Dictionary<string, DeviceSettings> _deviceSettings = new();

    /// <summary>Periodic hot-plug scan interval (see <see cref="StartHotplugPolling"/>), balancing prompt
    /// detection of connected/disconnected devices against background work from repeatedly enumerating
    /// every input device.</summary>
    private static readonly TimeSpan HotplugPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Interval for scanning target processes used by "Start controller automatically"
    /// (see <see cref="StartAutoStartPolling"/>). Uses the same balance as the hot-plug scan
    /// (<see cref="HotplugPollInterval"/>) between prompt detection and unnecessary background work
    /// from repeatedly enumerating all running processes.</summary>
    private static readonly TimeSpan AutoStartPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Index of the Mapping tab in the main TabControl (the first TabItem in MainWindow.xaml).
    /// Used by <see cref="RefreshScreenActiveStates"/> to enable live polling for mapping table highlights
    /// only while this tab is visible.</summary>
    public const int MappingTabIndex = 0;

    /// <summary>Index of the Device Configuration tab in the main TabControl (the second TabItem in
    /// MainWindow.xaml). Used by <see cref="RefreshScreenActiveStates"/> to enable live polling for the
    /// axis preview only while this tab is visible.</summary>
    public const int DeviceConfigTabIndex = 1;

    private DispatcherTimer? _hotplugTimer;

    /// <summary>Periodically checks whether the target program for any controller with
    /// <see cref="VirtualControllerViewModel.AutoStartEnabled"/> is running, then starts or stops that
    /// controller accordingly (see <see cref="OnAutoStartTimerTick"/>). Unlike hot-plug polling, this timer
    /// always runs because each controller has its own checkbox. With no controllers enabled, the scan is
    /// inexpensive: it only enumerates processes and does not access devices or drivers.</summary>
    private DispatcherTimer? _autoStartTimer;

    /// <summary>Whether connected or disconnected physical devices are detected automatically through
    /// background polling, without restarting the app or manually clicking "Refresh devices" (see the
    /// Settings tab in <see cref="Views.MainWindow"/>). Disabling this stops the timer entirely
    /// (<see cref="StopHotplugPolling"/>).</summary>
    [ObservableProperty]
    private bool _autoDeviceDetectionEnabled = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _alwaysOnTop = true;

    [ObservableProperty]
    private string _selectedUiLanguage = "system";

    public ObservableCollection<UiLanguageOption> UiLanguageOptions { get; } = new(TranslationService.Instance.GetAvailableLanguages());

    public string SettingsTabHeaderText => TranslationService.Instance.GetText("settings.tab");
    public string SettingsLanguageLabelText => TranslationService.Instance.GetText("settings.language.label");
    public string SettingsAutoDeviceDetectionText => TranslationService.Instance.GetText("settings.auto_device_detection");
    public string SettingsAutoDeviceDetectionHelpText => TranslationService.Instance.GetText("settings.auto_device_detection_help");
    public string SettingsStartWithWindowsText => TranslationService.Instance.GetText("settings.start_with_windows");
    public string SettingsStartWithWindowsHelpText => TranslationService.Instance.GetText("settings.start_with_windows_help");
    public string SettingsStartMinimizedText => TranslationService.Instance.GetText("settings.start_minimized");
    public string SettingsStartMinimizedHelpText => TranslationService.Instance.GetText("settings.start_minimized_help");
    public string SettingsAlwaysOnTopText => TranslationService.Instance.GetText("settings.always_on_top");
    public string SettingsAlwaysOnTopHelpText => TranslationService.Instance.GetText("settings.always_on_top_help");

    public ObservableCollection<VirtualControllerViewModel> Controllers { get; } = new();

    /// <summary>Root view model for the Device Configuration tab (see <see cref="Views.MainWindow"/>).
    /// Replaces the former separate device configuration dialog. It lives for the lifetime of the main
    /// window and incrementally reconciles devices on each scan (manual, triggered by a settings change,
    /// or periodic hot-plug polling) instead of being destroyed and rebuilt (see
    /// <see cref="RefreshDevices"/> and <see cref="DeviceConfigViewModel.UpdateDevices"/>).</summary>
    [ObservableProperty]
    private DeviceConfigViewModel _deviceConfig = null!;

    [ObservableProperty]
    private IReadOnlyList<PhysicalDeviceInfo> _availableDevices = Array.Empty<PhysicalDeviceInfo>();

    /// <summary>Main text of the ViGEmBus status pill in the header. Computed from <see cref="DriverReady"/>
    /// through <see cref="TranslationService"/> so it switches language together with the rest of the UI;
    /// change notifications are raised by <see cref="OnDriverReadyChanged"/> and after language changes.</summary>
    public string DriverStatusText => TranslationService.Instance.GetText(DriverReady ? "driver.status_connected" : "driver.status_unavailable");

    [ObservableProperty]
    private bool _driverReady;

    /// <summary>Whether the HidHide driver is installed and ready (see <see cref="ControllerManager.IsHidHideAvailable"/>).
    /// Controls the HidHide checkbox beside each virtual controller's start/stop button, which is disabled
    /// when HidHide is unavailable (see <see cref="Views.MainWindow"/>).</summary>
    public bool IsHidHideAvailable => _manager.IsHidHideAvailable;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    private VirtualControllerViewModel? _selectedController;

    /// <summary>Index of the currently visible tab in the main TabControl (MainWindow.xaml), two-way bound
    /// to <c>TabControl.SelectedIndex</c>. Used with <see cref="SelectedController"/> and
    /// <see cref="IsWindowMinimized"/> to restrict expensive physical device polling (mapping highlights and
    /// the device configuration axis preview) to cases where the visual feedback can be seen (see
    /// <see cref="RefreshScreenActiveStates"/>). Values 0 ("Mapping") and 1 ("Device Configuration") match
    /// the TabItem order in MainWindow.xaml (see <see cref="MappingTabIndex"/> and <see cref="DeviceConfigTabIndex"/>).</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Whether the main window is minimized. Kept up to date by the <c>StateChanged</c> event in
    /// <see cref="Views.MainWindow"/> because <c>Window.WindowState</c> cannot be bound directly as a
    /// <c>bool</c>. Live polling is paused while the window is minimized because no highlight can be seen
    /// (see <see cref="RefreshScreenActiveStates"/>).</summary>
    [ObservableProperty]
    private bool _isWindowMinimized;

    /// <summary>Whether there are unsaved changes since the last successful load or save (mapping table,
    /// controller properties, or device input/output settings). Used to color the save button red and show
    /// a warning in the UI.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>Raised when a managed controller's active mode changes and that controller has notifications
    /// enabled (see <see cref="VirtualControllerViewModel.ModeActivated"/>). <see cref="Views.MainWindow"/>
    /// uses this to show a brief on-screen notification.</summary>
    public event Action<VirtualControllerViewModel, ModeViewModel>? ModeActivated;

    /// <summary>Manages automatic startup and manual update checks from the Settings tab, including the
    /// "Check for updates automatically" setting (see <see cref="Views.MainWindow"/>). The main window
    /// subscribes to <see cref="UpdateViewModel.UpdateAvailable"/> to show the update dialog when a newer
    /// version is available (see <see cref="Views.UpdateAvailableDialog"/>).</summary>
    public UpdateViewModel Update { get; } = new();

    /// <summary>Window title including the app version derived from the Git tag at build time (see
    /// <see cref="AppVersionProvider"/>), e.g. "Virtual Controller - Version 1.4.2". Bound directly to
    /// <c>Window.Title</c> (see MainWindow.xaml).</summary>
    public string WindowTitle => $"Virtual Controller - Version {AppVersionProvider.RawVersion}";

    public MainViewModel()
    {
        // Subscribe early: ConnectDriverCommand calls Initialize(), which can find and remove stale HidHide
        // blocks from a previous crash. A later controller start/update can also report that HidHide is
        // enabled in a profile but the driver is unavailable. Either event can fire before construction ends.
        _manager.HidHideWarning += message => LastErrorMessage = message;

        RefreshDevices();
        LoadProfiles();
        RefreshTranslatableTexts();

        if (AutoDeviceDetectionEnabled)
        {
            StartHotplugPolling();
        }

        StartAutoStartPolling();

        // Update explicitly instead of relying only on SelectedController change notifications. If no profile
        // is loaded, Controllers remains empty and SelectedController remains null, so
        // OnSelectedControllerChanged would not fire and the initial screen-active state (e.g. for DeviceConfig)
        // would remain at its default instead of being calculated correctly.
        RefreshScreenActiveStates();
    }

    /// <summary>
    /// Connects to the ViGEmBus driver. Must succeed before a virtual controller can start. Catches
    /// <see cref="Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException"/> if the driver is not yet
    /// installed and displays the error as status text.
    /// </summary>
    [RelayCommand]
    private void ConnectDriver()
    {
        try
        {
            _manager.Initialize();
            DriverReady = true;
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            DriverReady = false;
            LastErrorMessage = ex.Message;
        }
    }

    partial void OnDriverReadyChanged(bool value) => OnPropertyChanged(nameof(DriverStatusText));

    partial void OnAutoDeviceDetectionEnabledChanged(bool value)
    {
        HasUnsavedChanges = true;

        if (value)
        {
            StartHotplugPolling();
        }
        else
        {
            StopHotplugPolling();
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        HasUnsavedChanges = true;
        UpdateWindowsStartup();
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        HasUnsavedChanges = true;
        if (StartWithWindows)
        {
            UpdateWindowsStartup();
        }
    }

    partial void OnAlwaysOnTopChanged(bool value) => HasUnsavedChanges = true;

    partial void OnSelectedUiLanguageChanged(string value)
    {
        HasUnsavedChanges = true;
        TranslationService.Instance.ApplyLanguage(value);
        RefreshTranslatableTexts();
    }

    private void RefreshTranslatableTexts()
    {
        OnPropertyChanged(nameof(SettingsTabHeaderText));
        OnPropertyChanged(nameof(SettingsLanguageLabelText));
        OnPropertyChanged(nameof(SettingsAutoDeviceDetectionText));
        OnPropertyChanged(nameof(SettingsAutoDeviceDetectionHelpText));
        OnPropertyChanged(nameof(SettingsStartWithWindowsText));
        OnPropertyChanged(nameof(SettingsStartWithWindowsHelpText));
        OnPropertyChanged(nameof(SettingsStartMinimizedText));
        OnPropertyChanged(nameof(SettingsStartMinimizedHelpText));
        OnPropertyChanged(nameof(SettingsAlwaysOnTopText));
        OnPropertyChanged(nameof(SettingsAlwaysOnTopHelpText));
        OnPropertyChanged(nameof(DriverStatusText));
    }

    private void UpdateWindowsStartup()
    {
        try
        {
            WindowsStartupManager.SetEnabled(StartWithWindows, StartMinimized);
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Could not update Windows startup settings: {ex.Message}";
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => RefreshScreenActiveStates();

    partial void OnSelectedControllerChanged(VirtualControllerViewModel? value) => RefreshScreenActiveStates();

    partial void OnIsWindowMinimizedChanged(bool value) => RefreshScreenActiveStates();

    /// <summary>Central decision point for whether physical devices may be polled for each virtual controller
    /// on the Mapping tab or for the Device Configuration tab. Poll only when the corresponding visual
    /// feedback (mapping highlight or axis preview) can be seen: the tab is visible, the relevant controller
    /// is selected for Mapping, and the window is not minimized. Called after relevant changes (tab switch,
    /// controller selection, minimize/restore) and after the controller list is first loaded.</summary>
    private void RefreshScreenActiveStates()
    {
        bool mappingTabVisible = !IsWindowMinimized && SelectedTabIndex == MappingTabIndex;
        bool deviceConfigTabVisible = !IsWindowMinimized && SelectedTabIndex == DeviceConfigTabIndex;

        foreach (var controller in Controllers)
        {
            controller.SetScreenActive(mappingTabVisible && controller == SelectedController);
        }

        DeviceConfig?.SetScreenActive(deviceConfigTabVisible);
    }

    /// <summary>Starts the periodic hot-plug scan (see <see cref="HotplugPollInterval"/>) so newly connected
    /// or disconnected devices are detected without restarting the app or clicking "Refresh devices".
    /// Called only when <see cref="AutoDeviceDetectionEnabled"/> is enabled; otherwise the timer remains
    /// stopped and no background work is spent re-enumerating input devices.</summary>
    private void StartHotplugPolling()
    {
        if (_hotplugTimer is not null)
        {
            return;
        }

        _hotplugTimer = new DispatcherTimer { Interval = HotplugPollInterval };
        _hotplugTimer.Tick += OnHotplugTimerTick;
        _hotplugTimer.Start();
    }

    private void StopHotplugPolling()
    {
        if (_hotplugTimer is null)
        {
            return;
        }

        _hotplugTimer.Tick -= OnHotplugTimerTick;
        _hotplugTimer.Stop();
        _hotplugTimer = null;
    }

    private void OnHotplugTimerTick(object? sender, EventArgs e) => RefreshDevices();

    /// <summary>Starts the periodic process scan for "Start controller automatically" (see
    /// <see cref="AutoStartPollInterval"/>). Runs from app startup because the feature is enabled per
    /// controller rather than globally.</summary>
    private void StartAutoStartPolling()
    {
        if (_autoStartTimer is not null)
        {
            return;
        }

        _autoStartTimer = new DispatcherTimer { Interval = AutoStartPollInterval };
        _autoStartTimer.Tick += OnAutoStartTimerTick;
        _autoStartTimer.Start();
    }

    private void StopAutoStartPolling()
    {
        if (_autoStartTimer is null)
        {
            return;
        }

        _autoStartTimer.Tick -= OnAutoStartTimerTick;
        _autoStartTimer.Stop();
        _autoStartTimer = null;
    }

    /// <summary>For each controller with <see cref="VirtualControllerViewModel.AutoStartEnabled"/> enabled,
    /// checks whether the program at <see cref="VirtualControllerViewModel.AutoStartExecutablePath"/> is running
    /// by comparing its full path (see <see cref="Core.Mapping.VirtualControllerProfile.AutoStartExecutablePath"/>).
    /// Starts an inactive controller when the program starts and stops an active controller when it exits
    /// (see <see cref="OnStartRequested"/> and <see cref="OnStopRequested"/>). A manual start or stop while the
    /// target program is running is overridden on the next tick because the checkbox creates a persistent link,
    /// not a one-time trigger.</summary>
    private void OnAutoStartTimerTick(object? sender, EventArgs e)
    {
        var candidates = Controllers.Where(c => c.AutoStartEnabled && !string.IsNullOrWhiteSpace(c.AutoStartExecutablePath)).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var runningExecutablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } fileName)
                    {
                        runningExecutablePaths.Add(fileName);
                    }
                }
                catch
                {
                    // Some processes (system/elevated processes or processes that have already exited) deny
                    // access to MainModule. Ignore an inaccessible process so it cannot abort the entire scan.
                }
            }
        }

        foreach (var controller in candidates)
        {
            bool isTargetRunning = runningExecutablePaths.Contains(controller.AutoStartExecutablePath!);

            if (isTargetRunning && !controller.IsRunning)
            {
                OnStartRequested(controller);
            }
            else if (!isTargetRunning && controller.IsRunning)
            {
                OnStopRequested(controller);
            }
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        var allDevices = _manager.GetAvailablePhysicalDevices();

        // Cache each device's last known name and capabilities so the mapping table and Device Configuration
        // tab can still show useful information after disconnection, rather than a raw device ID or nothing.
        foreach (var device in allDevices)
        {
            var settings = GetOrCreateDeviceSettings(device.DeviceId);
            settings.LastKnownDisplayName = device.DisplayName;
            settings.LastKnownApi = device.Api;
            settings.LastKnownApiSlot = device.ApiSlot;
            settings.LastKnownButtonCount = device.ButtonCount;
            settings.LastKnownHasPov = device.HasPov;
            settings.LastKnownAvailableAxes = device.AvailableAxes.ToList();
        }

        AvailableDevices = allDevices
            .Where(d => !_deviceSettings.TryGetValue(d.DeviceId, out var settings) || (settings.Enabled && !settings.Hidden))
            .ToList();

        foreach (var controller in Controllers)
        {
            controller.RefreshDeviceSelections(AvailableDevices);
        }

        if (DeviceConfig is null)
        {
            DeviceConfig = new DeviceConfigViewModel(this);
        }
        else
        {
            DeviceConfig.UpdateDevices();
        }

        // Update explicitly rather than relying only on change notifications such as SelectedController.
        // Newly created DeviceConfigViewModel or DeviceSelectionViewModel instances would otherwise retain
        // the default false screen-active flag until another change happens to call RefreshScreenActiveStates.
        // That may never happen when SelectedController does not actually change (e.g. it remains null because
        // no profile is loaded). IsHidHideAvailable is computed rather than observable, and driver installation
        // can change at runtime. RefreshDevices already runs on the hot-plug timer, so update it here to
        // automatically re-enable the associated checkbox after HidHide is installed.
        OnPropertyChanged(nameof(IsHidHideAvailable));

        RefreshScreenActiveStates();
    }

    /// <summary>Returns every physical device known to <see cref="MainViewModel"/> for the Device Configuration
    /// tab: currently connected devices, even if disabled through <see cref="DeviceSettings.Enabled"/>, and
    /// previously detected but now disconnected devices reconstructed from their last known capabilities
    /// (see <see cref="DeviceSettings.LastKnownButtonCount"/>). Their settings (name, calibration, and enabled
    /// state) remain visible and editable while disconnected, unlike <see cref="AvailableDevices"/>, which is
    /// filtered for mapping selection.</summary>
    public IReadOnlyList<KnownDeviceInfo> GetAllKnownDevices()
    {
        var connectedDevices = _manager.GetAvailablePhysicalDevices();
        var connectedIds = connectedDevices.Select(d => d.DeviceId).ToHashSet();

        var result = connectedDevices.Select(d => new KnownDeviceInfo(d, IsConnected: true)).ToList();

        foreach (var (deviceId, settings) in _deviceSettings)
        {
            if (connectedIds.Contains(deviceId) || settings.LastKnownButtonCount is not { } buttonCount)
            {
                continue; // Already connected and added above, or never fully detected.
            }

            var offlineDevice = new PhysicalDeviceInfo(
                deviceId,
                settings.LastKnownDisplayName ?? deviceId,
                settings.LastKnownApi ?? InputApi.DirectInput,
                settings.LastKnownApiSlot ?? 0,
                buttonCount,
                settings.LastKnownHasPov ?? false,
                settings.LastKnownAvailableAxes ?? new List<PhysicalAxisId>());

            result.Add(new KnownDeviceInfo(offlineDevice, IsConnected: false));
        }

        return result;
    }

    [RelayCommand]
    private void AddController()
    {
        var profile = new VirtualControllerProfile
        {
            Id = Guid.NewGuid(),
            Name = $"Controller {Controllers.Count + 1}",
            Layout = ControllerLayout.Xbox
        };

        AddControllerViewModel(profile);
        SelectedController = Controllers.LastOrDefault();
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void SaveProfiles()
    {
        try
        {
            var appProfile = new AppProfile
            {
                Controllers = Controllers.Select(c => c.Profile).ToList(),
                DeviceSettings = new Dictionary<string, DeviceSettings>(_deviceSettings),
                AutoDeviceDetectionEnabled = AutoDeviceDetectionEnabled,
                StartWithWindows = StartWithWindows,
                StartMinimized = StartMinimized,
                AlwaysOnTop = AlwaysOnTop,
                UiLanguage = SelectedUiLanguage
            };
            ProfileStore.Save(appProfile);
            LastErrorMessage = null;
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>Opens the native Windows Game Controllers control panel ("joy.cpl") so users can quickly
    /// inspect the axes and buttons detected by Windows for comparison or troubleshooting without leaving
    /// the app to find the dialog manually.</summary>
    [RelayCommand]
    private void OpenWindowsGameControllers()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "joy.cpl",
                UseShellExecute = true
            };
            Process.Start(startInfo);
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Could not open Windows Game Controllers settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadProfiles()
    {
        try
        {
            foreach (var vm in Controllers.ToList())
            {
                DetachAndRemove(vm);
            }

            var appProfile = ProfileStore.Load();
            foreach (var profile in appProfile.Controllers)
            {
                AddControllerViewModel(profile);
            }

            _deviceSettings = new Dictionary<string, DeviceSettings>(appProfile.DeviceSettings);
            AutoDeviceDetectionEnabled = appProfile.AutoDeviceDetectionEnabled;
            StartWithWindows = appProfile.StartWithWindows;
            StartMinimized = appProfile.StartMinimized;
            AlwaysOnTop = appProfile.AlwaysOnTop;
            SelectedUiLanguage = appProfile.UiLanguage;

            // Rebuild DeviceConfig here instead of incrementally reconciling it as RefreshDevices/UpdateDevices
            // normally do. _deviceSettings was just replaced with new instances, so an existing
            // DeviceConfigDeviceViewModel would otherwise keep referencing discarded DeviceSettings objects.
            DeviceConfig?.Dispose();
            DeviceConfig = null!;

            RefreshDevices();
            SelectedController = Controllers.FirstOrDefault();
            LastErrorMessage = null;
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Load failed: {ex.Message}";
        }
    }

    private void AddControllerViewModel(VirtualControllerProfile profile)
    {
        var vm = new VirtualControllerViewModel(profile, () => AvailableDevices, () => _deviceSettings, GetCustomInputName, SetCustomInputName);
        vm.StartRequested += OnStartRequested;
        vm.StopRequested += OnStopRequested;
        vm.RemoveRequested += OnRemoveRequested;
        vm.ProfileChanged += OnProfileChanged;
        vm.ModeActivated += OnModeActivated;
        Controllers.Add(vm);
    }

    private string? GetCustomInputName(PhysicalInputRef inputRef)
    {
        var key = PhysicalInputCatalog.BuildStorageKey(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
        return _deviceSettings.TryGetValue(inputRef.DeviceId, out var settings)
            && settings.Inputs.TryGetValue(key, out var inputSettings)
            ? inputSettings.CustomName
            : null;
    }

    private void SetCustomInputName(PhysicalInputRef inputRef, string name)
    {
        var key = PhysicalInputCatalog.BuildStorageKey(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
        var deviceSettings = GetOrCreateDeviceSettings(inputRef.DeviceId);

        if (!deviceSettings.Inputs.TryGetValue(key, out var inputSettings))
        {
            inputSettings = new InputSettings();
            deviceSettings.Inputs[key] = inputSettings;
        }

        inputSettings.CustomName = string.IsNullOrWhiteSpace(name) ? null : name;
        HasUnsavedChanges = true;
    }

    /// <summary>Returns the settings for a physical device, creating an empty entry if needed. Used by the
    /// configuration dialog to read and update enabled state, calibration, deadzone, and response curve
    /// for each input.</summary>
    public DeviceSettings GetOrCreateDeviceSettings(string deviceId)
    {
        if (!_deviceSettings.TryGetValue(deviceId, out var settings))
        {
            settings = new DeviceSettings();
            _deviceSettings[deviceId] = settings;
        }

        return settings;
    }

    /// <summary>Called by the configuration dialog after the user changes a device's overall availability
    /// (enable/disable or hide/show). Refreshes the device list so disabled or hidden devices immediately
    /// disappear from virtual controller selections, then broadcasts the change to running sessions.
    /// <see cref="RefreshDevices"/> performs full hardware detection (XInput/DirectInput enumeration), so do
    /// not use this for settings-only changes (rename, calibration, deadzone, curve, or per-input enabled state).
    /// Those controls update on every keystroke/value change through UpdateSourceTrigger=PropertyChanged;
    /// synchronously enumerating hardware each time would cause noticeable delays. Use the lightweight
    /// <see cref="NotifyDeviceSettingsChanged"/> path for those changes.</summary>
    public void NotifyDeviceAvailabilityChanged()
    {
        RefreshDevices();
        _manager.BroadcastDeviceSettings(_deviceSettings);
        HasUnsavedChanges = true;
    }

    /// <summary>Called by the configuration dialog after a settings-only change that does not affect device
    /// availability or capabilities (e.g. renaming an input/stick, calibration, deadzone, curve, or changing
    /// one input's enabled state). Immediately broadcasts the change to running sessions without performing
    /// the expensive hardware detection in <see cref="NotifyDeviceAvailabilityChanged"/>. Kept separate because
    /// these controls commonly use UpdateSourceTrigger=PropertyChanged and invoke this on every keystroke;
    /// repeatedly enumerating devices would cause noticeable input delays.</summary>
    public void NotifyDeviceSettingsChanged()
    {
        _manager.BroadcastDeviceSettings(_deviceSettings);
        HasUnsavedChanges = true;
    }

    private void OnStartRequested(VirtualControllerViewModel vm)
    {
        if (!DriverReady)
        {
            vm.SetRunningState(false, "Driver not connected");
            return;
        }

        try
        {
            _manager.AddController(vm.Profile, _deviceSettings);
            vm.SetRunningState(true);
        }
        catch (Exception ex)
        {
            vm.SetRunningState(false, "Start failed");
            LastErrorMessage = ex.Message;
        }
    }

    private void OnStopRequested(VirtualControllerViewModel vm)
    {
        _manager.RemoveController(vm.Profile.Id);
        vm.SetRunningState(false);
    }

    private void OnRemoveRequested(VirtualControllerViewModel vm) => DetachAndRemove(vm);

    private void OnProfileChanged(VirtualControllerViewModel vm)
    {
        HasUnsavedChanges = true;

        if (vm.IsRunning)
        {
            _manager.UpdateController(vm.Profile, _deviceSettings);
        }
    }

    private void OnModeActivated(VirtualControllerViewModel vm, ModeViewModel mode) => ModeActivated?.Invoke(vm, mode);

    private void DetachAndRemove(VirtualControllerViewModel vm)
    {
        _manager.RemoveController(vm.Profile.Id);
        vm.StartRequested -= OnStartRequested;
        vm.StopRequested -= OnStopRequested;
        vm.RemoveRequested -= OnRemoveRequested;
        vm.ProfileChanged -= OnProfileChanged;
        vm.ModeActivated -= OnModeActivated;
        vm.Dispose();
        Controllers.Remove(vm);
        HasUnsavedChanges = true;

        if (SelectedController == vm)
        {
            SelectedController = null;
        }
    }

    public void Dispose()
    {
        StopHotplugPolling();
        StopAutoStartPolling();

        foreach (var vm in Controllers)
        {
            vm.Dispose();
        }
        DeviceConfig?.Dispose();
        _manager.Dispose();
    }
}

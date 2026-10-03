using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Represents a connected physical controller in a virtual controller's selection list. The user chooses
/// whether to include it in that virtual controller's mapping and capture actions. It can also be expanded
/// to show all physical inputs with live highlighting and assign them directly to a mapping row.
/// </summary>
public sealed partial class DeviceSelectionViewModel : ObservableObject, IDisposable
{
    /// <summary>Live highlight refresh rate. Intentionally much lower than mapping polling (1000 Hz), since this
    /// only needs to provide smooth visual feedback.</summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromMilliseconds(33);

    public PhysicalDeviceInfo Device { get; }

    private readonly Func<PhysicalInputRef, string?> _getCustomName;
    private readonly Action<PhysicalInputRef, string> _setCustomName;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;

    private DispatcherTimer? _liveTimer;
    private IDeviceReader? _liveReader;
    private bool _inputsBuilt;

    /// <summary>Whether any mapping row in any mode currently uses this device as a physical source (see
    /// <see cref="VirtualControllerViewModel.UpdateMappingSourceLiveMonitoring"/>). Keeps live polling active
    /// independently of <see cref="IsExpanded"/> so the mapping table highlight
    /// (<see cref="MappingRowViewModel.IsSourceActive"/>) works even when this device's input list is collapsed.</summary>
    private bool _mappingSourceMonitoringRequested;

    /// <summary>Whether the main window's Mapping tab is visible, this device's virtual controller is selected,
    /// and the window is not minimized (see <see cref="VirtualControllerViewModel.SetScreenActive"/>, set by
    /// <see cref="MainViewModel"/>). Otherwise every device, including those belonging to hidden controllers,
    /// would be polled by a timer even though its highlight cannot be seen, wasting CPU time and device access.
    /// Starts as <c>false</c>; polling begins only after <see cref="MainViewModel"/> explicitly enables it during setup.</summary>
    private bool _isScreenActive;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<PhysicalInputRowViewModel> Inputs { get; } = new();

    /// <summary>Raised when the user changes the checkbox selection.</summary>
    public event Action<DeviceSelectionViewModel>? SelectionChanged;

    /// <summary>Raised when the user requests a new mapping row through "Assign" on a physical input.</summary>
    public event Action<PhysicalInputRef>? AssignInputRequested;

    /// <summary>Raised on each live polling tick with the newly read <see cref="DeviceState"/> while the device
    /// list is expanded, or with <c>null</c> when monitoring stops (collapsed or disconnected). Lets
    /// <see cref="VirtualControllerViewModel"/> apply the same live highlight shown in <see cref="Inputs"/> to
    /// matching mapping rows without adding a redundant polling mechanism.</summary>
    public event Action<DeviceSelectionViewModel, DeviceState?>? LiveStateChanged;

    public DeviceSelectionViewModel(
        PhysicalDeviceInfo device,
        bool isSelected,
        Func<PhysicalInputRef, string?> getCustomName,
        Action<PhysicalInputRef, string> setCustomName,
        Func<IReadOnlyDictionary<string, DeviceSettings>> getDeviceSettings)
    {
        Device = device;
        _isSelected = isSelected;
        _getCustomName = getCustomName;
        _setCustomName = setCustomName;
        _getDeviceSettings = getDeviceSettings;
    }

    public string DisplayName => Device.DisplayName;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureInputsBuilt();
        }

        RefreshLiveMonitoringState();
    }

    /// <summary>Sets whether this device must be monitored for the mapping table's live highlight
    /// (<see cref="MappingRowViewModel.IsSourceActive"/>) because a mapping row in any mode uses it as a source,
    /// regardless of whether its input list (<see cref="IsExpanded"/>) is expanded. Updated by
    /// <see cref="VirtualControllerViewModel.UpdateMappingSourceLiveMonitoring"/> whenever mappings or modes
    /// are added, removed, or changed.</summary>
    public void SetMappingSourceMonitoringRequested(bool value)
    {
        if (_mappingSourceMonitoringRequested == value)
        {
            return;
        }

        _mappingSourceMonitoringRequested = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Sets whether the main window's Mapping tab is visible, this device's virtual controller is
    /// selected, and the window is not minimized. Live polling is allowed only in that state (see
    /// <see cref="RefreshLiveMonitoringState"/>). Updated by <see cref="VirtualControllerViewModel.SetScreenActive"/>
    /// for every tab switch, controller selection, or minimize/restore event.</summary>
    public void SetScreenActive(bool value)
    {
        if (_isScreenActive == value)
        {
            return;
        }

        _isScreenActive = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Starts or stops live polling based on whether the screen is active and either the input list
    /// is expanded or the device is needed as a mapping source. Polling therefore continues when the list is
    /// collapsed if a mapping still references this device, and starts when a new mapping uses it even if the
    /// list is collapsed. The screen-active condition (<see cref="_isScreenActive"/>) always takes precedence:
    /// no polling occurs when the Mapping tab is hidden, its controller is not selected, or the window is minimized.</summary>
    private void RefreshLiveMonitoringState()
    {
        if (_isScreenActive && (IsExpanded || _mappingSourceMonitoringRequested))
        {
            StartLiveMonitoring();
        }
        else
        {
            StopLiveMonitoring();
        }
    }

    private void EnsureInputsBuilt()
    {
        if (_inputsBuilt)
        {
            return;
        }

        _inputsBuilt = true;
        var deviceSettings = _getDeviceSettings();
        foreach (var inputRef in PhysicalInputCatalog.BuildInputs(Device))
        {
            string displayName = _getCustomName(inputRef) ?? inputRef.DisplayName;
            bool isEnabled = deviceSettings.IsInputEnabled(inputRef.DeviceId, inputRef.Kind, inputRef.Index);
            var row = new PhysicalInputRowViewModel(inputRef, displayName, isEnabled, _setCustomName);
            row.AssignRequested += OnRowAssignRequested;
            Inputs.Add(row);
        }
    }

    private void OnRowAssignRequested(PhysicalInputRef inputRef) => AssignInputRequested?.Invoke(inputRef);

    private void StartLiveMonitoring()
    {
        if (_liveTimer is not null)
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

        foreach (var row in Inputs)
        {
            row.IsActive = false;
        }

        LiveStateChanged?.Invoke(this, null);
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        if (_liveReader is null)
        {
            return;
        }

        if (!_liveReader.Poll(out var state))
        {
            // The device was disconnected; stop monitoring until the user expands the list or refreshes devices.
            StopLiveMonitoring();
            return;
        }

        foreach (var row in Inputs)
        {
            row.UpdateActiveState(state);
        }

        LiveStateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        StopLiveMonitoring();
        foreach (var row in Inputs)
        {
            row.AssignRequested -= OnRowAssignRequested;
        }
    }
}

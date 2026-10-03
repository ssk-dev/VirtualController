using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.App.Diagnostics;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Combines a target value (<see cref="VirtualButton"/>, <see cref="VirtualAxis"/>,
/// <see cref="VirtualTrigger"/>, or <see cref="DPadDirection"/>) with the layout-specific label used by the
/// selected <see cref="ControllerLayout"/> (see <see cref="VirtualControllerLabels"/>). Used as an item in
/// the target value ComboBox (DisplayMemberPath = Label, SelectedValuePath = Value).
/// </summary>
public sealed record TargetOptionItem(object Value, string Label);

/// <summary>
/// One entry in the "Assign" selection list (see <see cref="MappingRowViewModel.AssignableInputs"/>): a
/// physical input on a specific device that the user can select as the source for this mapping row without
/// physically pressing or moving it. This is an alternative to Capture, useful for triggers that are already
/// slightly engaged or inputs that are difficult to activate individually.
/// </summary>
public sealed record AssignableInputOption(PhysicalDeviceInfo Device, PhysicalInputRef InputRef, string Label)
{
    /// <summary>Convenience property for grouping by device in the "Assign" dialog
    /// (<see cref="Views.AssignInputDialog"/>), allowing a simple <c>PropertyGroupDescription</c> instead of
    /// relying on the more error-prone dotted binding path "Device.DisplayName".</summary>
    public string DeviceDisplayName => Device.DisplayName;
}

/// <summary>
/// One row in a virtual controller's mapping table. Shows and edits which physical input (controller,
/// button, axis, or D-pad) affects which virtual controller element. All bindable properties write directly
/// to the underlying <see cref="MappingEntry"/>, which is part of the saved profile.
/// </summary>
public sealed partial class MappingRowViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    /// <summary>An axis direction is considered active at this absolute deflection. Matches
    /// <see cref="PhysicalInputRowViewModel.AxisActiveThreshold"/> so this mapping row's live highlight
    /// (<see cref="IsSourceActive"/>) responds at the same point as the corresponding device input row.</summary>
    private const float AxisActiveThreshold = 0.3f;

    /// <summary>All target categories, for ComboBox bindings in the view.</summary>
    public static IReadOnlyList<MappingTargetKind> TargetKindOptions { get; } = Enum.GetValues<MappingTargetKind>();
    public static IReadOnlyList<VirtualButton> ButtonOptions { get; } = Enum.GetValues<VirtualButton>();
    public static IReadOnlyList<VirtualAxis> AxisOptions { get; } = Enum.GetValues<VirtualAxis>();
    public static IReadOnlyList<VirtualTrigger> TriggerOptions { get; } = Enum.GetValues<VirtualTrigger>();
    public static IReadOnlyList<DPadDirection> DPadOptions { get; } = Enum.GetValues<DPadDirection>();

    public MappingEntry Entry { get; }

    /// <summary>Short, instance-unique ID for debug logging, making it clear which row instance triggered an
    /// action in <see cref="DebugLog"/>. Useful for diagnosing unintended interactions between rows.</summary>
    public string RowId { get; } = Guid.NewGuid().ToString("N")[..8];

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;
    private readonly Func<ControllerLayout> _getLayout;

    /// <summary>Raised when the user removes this row through the Remove button.</summary>
    public event Action<MappingRowViewModel>? RemoveRequested;

    /// <summary>Raised when this row's underlying <see cref="MappingEntry"/> changes (captured physical source,
    /// target type/value, or inversion), so the parent <see cref="VirtualControllerViewModel"/> can update
    /// running sessions and track unsaved changes.</summary>
    public event Action<MappingRowViewModel>? Changed;

    [ObservableProperty]
    private string _sourceDisplayName;

    /// <summary>Whether this row's physical source device is currently connected. The view shows a red
    /// "disconnected" indicator when it is not, rather than displaying only an unhelpful device ID.</summary>
    [ObservableProperty]
    private bool _isSourceConnected;

    /// <summary>Whether a physical source has been assigned to this row through Capture or Assign. New rows
    /// have no source (<see cref="MappingEntry.SourceDeviceId"/> is empty), so
    /// <see cref="ModeViewModel.RebuildMappingGroups"/> displays them in a separate ungrouped section at the
    /// top of the mapping table until a source is assigned.</summary>
    [ObservableProperty]
    private bool _isSourceAssigned;

    /// <summary>Whether this row's physical source is currently active (button pressed, axis deflected, or
    /// D-pad held). Like <see cref="PhysicalInputRowViewModel.IsActive"/>, it drives the mapping table's row
    /// highlight. Updated by <see cref="VirtualControllerViewModel"/> through live polling of selected or
    /// expanded devices (see <see cref="UpdateSourceActiveState"/>); remains false until a matching device is monitored.</summary>
    [ObservableProperty]
    private bool _isSourceActive;

    [ObservableProperty]
    private string _targetDisplayName;

    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Seconds remaining while Capture waits for a physical input, counting down from
    /// <see cref="CaptureTimeout"/> to zero. Shown beside the Capture button so users know the mode ends
    /// automatically if no new input is detected in time.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    [ObservableProperty]
    private MappingTargetKind _selectedTargetKind;

    private object? _selectedTargetValue;

    /// <summary>
    /// Unified target value for the view's single target-value ComboBox. Its type depends on
    /// <see cref="SelectedTargetKind"/>: <see cref="VirtualButton"/>, <see cref="VirtualAxis"/>,
    /// <see cref="VirtualTrigger"/>, or <see cref="DPadDirection"/>. Replaces four overlapping ComboBoxes
    /// (one per target type) whose dropdowns could overlap and display stale values when switching quickly.
    /// </summary>
    public object? SelectedTargetValue
    {
        get => _selectedTargetValue;
        set
        {
            DebugLog.Write($"[Row {RowId}] SelectedTargetValue setter called: old='{_selectedTargetValue}' new='{value}' TargetKind={SelectedTargetKind}");

            if (!SetProperty(ref _selectedTargetValue, value))
            {
                DebugLog.Write($"[Row {RowId}] SelectedTargetValue: SetProperty made no change (value was already equal); aborting.");
                return;
            }

            switch (SelectedTargetKind)
            {
                case MappingTargetKind.Button:
                    Entry.TargetButton = value as VirtualButton?;
                    break;
                case MappingTargetKind.Axis:
                    Entry.TargetAxis = value as VirtualAxis?;
                    break;
                case MappingTargetKind.Trigger:
                    Entry.TargetTrigger = value as VirtualTrigger?;
                    break;
                case MappingTargetKind.DPad:
                    Entry.TargetDPadDirection = value as DPadDirection?;
                    break;
            }

            TargetDisplayName = BuildTargetDisplayName(Entry, _getLayout());
            DebugLog.Write($"[Row {RowId}] SelectedTargetValue applied -> Entry.TargetButton={Entry.TargetButton} Entry.TargetAxis={Entry.TargetAxis} Entry.TargetTrigger={Entry.TargetTrigger} Entry.TargetDPadDirection={Entry.TargetDPadDirection} TargetDisplayName='{TargetDisplayName}'");
            Changed?.Invoke(this);
        }
    }

    /// <summary>Valid options for the target-value ComboBox for the current <see cref="SelectedTargetKind"/>,
    /// labeled according to the virtual controller's current <see cref="ControllerLayout"/> (see
    /// <see cref="VirtualControllerLabels"/>).</summary>
    public IReadOnlyList<TargetOptionItem> CurrentTargetOptions
    {
        get
        {
            var layout = _getLayout();
            return SelectedTargetKind switch
            {
                MappingTargetKind.Button => ButtonOptions.Select(b => new TargetOptionItem(b, VirtualControllerLabels.GetButtonLabel(layout, b))).ToList(),
                MappingTargetKind.Axis => AxisOptions.Select(a => new TargetOptionItem(a, VirtualControllerLabels.GetAxisLabel(a))).ToList(),
                MappingTargetKind.Trigger => TriggerOptions.Select(t => new TargetOptionItem(t, VirtualControllerLabels.GetTriggerLabel(layout, t))).ToList(),
                MappingTargetKind.DPad => DPadOptions.Select(d => new TargetOptionItem(d, VirtualControllerLabels.GetDPadLabel(d))).ToList(),
                _ => Array.Empty<TargetOptionItem>()
            };
        }
    }

    /// <summary>Inversion is meaningful only for analog axes and is shown in the view only for those targets.</summary>
    public bool IsAxisTarget => SelectedTargetKind == MappingTargetKind.Axis;

    [ObservableProperty]
    private bool _invert;

    /// <summary>For axis targets, use only the half of the physical axis specified by the captured source
    /// (SourceKind: AxisPositive/AxisNegative) instead of the full bidirectional range. This allows the two
    /// halves of a physical axis to map to separate virtual axes with independent inversion settings.</summary>
    [ObservableProperty]
    private bool _directionalOnly;

    public MappingRowViewModel(
        MappingEntry entry,
        IReadOnlyList<PhysicalDeviceInfo> knownDevices,
        Func<IReadOnlyList<PhysicalDeviceInfo>> getAvailableDevices,
        Func<IReadOnlyDictionary<string, DeviceSettings>> getDeviceSettings,
        Func<ControllerLayout> getLayout)
    {
        Entry = entry;
        _getAvailableDevices = getAvailableDevices;
        _getDeviceSettings = getDeviceSettings;
        _getLayout = getLayout;
        _sourceDisplayName = BuildSourceDisplayName(entry, knownDevices, getDeviceSettings(), out _isSourceConnected);
        _isSourceAssigned = !string.IsNullOrEmpty(entry.SourceDeviceId);
        _targetDisplayName = BuildTargetDisplayName(entry, getLayout());

        _selectedTargetKind = entry.TargetKind;
        _selectedTargetValue = entry.TargetKind switch
        {
            MappingTargetKind.Button => entry.TargetButton,
            MappingTargetKind.Axis => entry.TargetAxis,
            MappingTargetKind.Trigger => entry.TargetTrigger,
            MappingTargetKind.DPad => entry.TargetDPadDirection,
            _ => null
        };
        _invert = entry.Invert;
        _directionalOnly = entry.DirectionalOnly;

        DebugLog.Write($"[Row {RowId}] Constructor: Source={entry.SourceDeviceId}|{entry.SourceKind}|{entry.SourceIndex} TargetKind={entry.TargetKind} TargetValue={_selectedTargetValue}");
    }

    public void ApplyCapturedInput(PhysicalInputRef captured, IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        Entry.SourceDeviceId = captured.DeviceId;
        Entry.SourceKind = captured.Kind;
        Entry.SourceIndex = captured.Index;

        SourceDisplayName = BuildSourceDisplayName(Entry, knownDevices, _getDeviceSettings(), out bool isConnected);
        IsSourceConnected = isConnected;
        IsSourceAssigned = true;
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Called by <see cref="VirtualControllerViewModel"/> when the list of connected physical devices changes
    /// (e.g. a device disconnects or reconnects). Refreshes this row's display name and connection status
    /// without requiring the user to capture the source again.
    /// </summary>
    public void RefreshSourceConnectionState(IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        SourceDisplayName = BuildSourceDisplayName(Entry, knownDevices, _getDeviceSettings(), out bool isConnected);
        IsSourceConnected = isConnected;
    }

    /// <summary>Updates <see cref="IsSourceActive"/> from a freshly polled <see cref="DeviceState"/>, like
    /// <see cref="PhysicalInputRowViewModel.UpdateActiveState"/>. Called by
    /// <see cref="VirtualControllerViewModel"/> for rows whose <see cref="MappingEntry.SourceDeviceId"/> matches
    /// <paramref name="deviceId"/>; rows for other devices remain unchanged.</summary>
    public void UpdateSourceActiveState(string deviceId, DeviceState state)
    {
        if (Entry.SourceDeviceId != deviceId)
        {
            return;
        }

        IsSourceActive = Entry.SourceKind switch
        {
            PhysicalInputKind.Button => Entry.SourceIndex < state.Buttons.Length && state.Buttons[Entry.SourceIndex],
            PhysicalInputKind.AxisPositive => state.GetAxisRaw(Entry.SourceIndex) >= AxisActiveThreshold,
            PhysicalInputKind.AxisNegative => state.GetAxisRaw(Entry.SourceIndex) <= -AxisActiveThreshold,
            PhysicalInputKind.DPad => state.PovDirectionDegrees >= 0,
            PhysicalInputKind.DPadUp => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasUp(),
            PhysicalInputKind.DPadDown => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasDown(),
            PhysicalInputKind.DPadLeft => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasLeft(),
            PhysicalInputKind.DPadRight => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasRight(),
            _ => false
        };
    }

    /// <summary>Resets <see cref="IsSourceActive"/> to false when the physical device is no longer monitored
    /// (its list is collapsed or the device is disconnected), preventing a stale highlight.</summary>
    public void ResetSourceActiveState() => IsSourceActive = false;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task CaptureAsync()
    {
        IsCapturing = true;
        try
        {
            var devices = _getAvailableDevices();
            var captured = await CaptureCountdownHelper.CaptureWithCountdownAsync(
                devices, CaptureTimeout, _getDeviceSettings(), seconds => CaptureCountdownSeconds = seconds).ConfigureAwait(true);
            if (captured is not null)
            {
                ApplyCapturedInput(captured, devices);
            }
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private bool CanCapture() => !IsCapturing;

    /// <summary>Builds the complete list for the modal Assign dialog: all physical inputs (buttons, axis
    /// directions, and D-pad) on devices selected for this virtual controller, using the same display names
    /// as the expandable device input list. Inputs disabled in the configuration dialog are excluded, just as
    /// they are during physical capture in <see cref="InputCaptureService"/>, because disabled inputs are never evaluated.</summary>
    public IReadOnlyList<AssignableInputOption> BuildAssignableInputs()
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

    /// <summary>Called by the modal Assign dialog (<see cref="Views.AssignInputDialog"/>) when the user confirms
    /// a physical input. Assigns it just like a physically captured input, without requiring the user to press
    /// or move it.</summary>
    public void AssignInput(AssignableInputOption selected)
    {
        DebugLog.Write($"[Row {RowId}] AssignInput: using '{selected.Label}' (device '{selected.Device.DisplayName}') as the new physical source (assign instead of capture).");
        ApplyCapturedInput(selected.InputRef, _getAvailableDevices());
    }

    partial void OnIsCapturingChanged(bool value) => CaptureCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTargetKindChanged(MappingTargetKind value)
    {
        DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged: new TargetKind={value} (previous Entry.TargetKind={Entry.TargetKind})");
        Entry.TargetKind = value;
        Changed?.Invoke(this);

        // Defer resetting the target value and its dependent CurrentTargetOptions/IsAxisTarget updates because
        // they affect the DataGrid row layout (the target ComboBox changes its ItemsSource and Invert appears
        // or disappears, changing row height). Rebuilding the row while the clicked target-type ComboBox is
        // processing its SelectionChanged/binding update would destroy and recreate that control mid-operation,
        // discarding the selection. Dispatcher.BeginInvoke lets the selection finish before the layout changes.
        // IMPORTANT: use DispatcherPriority.Input rather than Background. Background is below Input in WPF's
        // priority order; continuous mouse movement toward the target-value dropdown could starve the action
        // indefinitely, leaving the old options visible. Input priority still runs after the current selection
        // processing and is not overtaken by later input events at the same priority (FIFO).
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged deferred BeginInvoke running (resetting target value to null).");

                Entry.TargetButton = null;
                Entry.TargetAxis = null;
                Entry.TargetTrigger = null;
                Entry.TargetDPadDirection = null;
                _selectedTargetValue = null;

                OnPropertyChanged(nameof(SelectedTargetValue));
                OnPropertyChanged(nameof(CurrentTargetOptions));
                OnPropertyChanged(nameof(IsAxisTarget));
                TargetDisplayName = BuildTargetDisplayName(Entry, _getLayout());

                DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged deferred BeginInvoke complete: SelectedTargetValue={SelectedTargetValue} TargetDisplayName='{TargetDisplayName}'");
            }));
    }

    partial void OnInvertChanged(bool value)
    {
        DebugLog.Write($"[Row {RowId}] OnInvertChanged raised: value={value} (previous Entry.Invert={Entry.Invert})");
        Entry.Invert = value;
        Changed?.Invoke(this);
    }

    partial void OnDirectionalOnlyChanged(bool value)
    {
        DebugLog.Write($"[Row {RowId}] OnDirectionalOnlyChanged raised: value={value} (previous Entry.DirectionalOnly={Entry.DirectionalOnly})");
        Entry.DirectionalOnly = value;
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Called by <see cref="VirtualControllerViewModel"/> when the virtual controller layout changes. Stored
    /// target values remain unchanged, but their labels (e.g. "South" -> "A" on Xbox or "Cross" on PlayStation)
    /// must be refreshed.
    /// </summary>
    public void RefreshForLayoutChange()
    {
        OnPropertyChanged(nameof(CurrentTargetOptions));
        TargetDisplayName = BuildTargetDisplayName(Entry, _getLayout());
    }

    private static string BuildSourceDisplayName(MappingEntry entry, IReadOnlyList<PhysicalDeviceInfo> knownDevices, IReadOnlyDictionary<string, DeviceSettings> deviceSettings, out bool isConnected)
        => PhysicalInputDisplayNameHelper.Build(entry.SourceDeviceId, entry.SourceKind, entry.SourceIndex, knownDevices, deviceSettings, out isConnected);

    private static string BuildTargetDisplayName(MappingEntry entry, ControllerLayout layout) => entry.TargetKind switch
    {
        MappingTargetKind.Button => entry.TargetButton is { } b ? VirtualControllerLabels.GetButtonLabel(layout, b) : "-",
        MappingTargetKind.Axis => entry.TargetAxis is { } a ? VirtualControllerLabels.GetAxisLabel(a) : "-",
        MappingTargetKind.Trigger => entry.TargetTrigger is { } t ? VirtualControllerLabels.GetTriggerLabel(layout, t) : "-",
        MappingTargetKind.DPad => $"D-Pad {VirtualControllerLabels.GetDPadLabel(entry.TargetDPadDirection ?? DPadDirection.None)}",
        _ => "-"
    };
}


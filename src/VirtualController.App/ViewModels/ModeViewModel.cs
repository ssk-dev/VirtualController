using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Represents one mode ("Flight", "Racing", etc.) of a virtual controller in the UI: name, enabled state,
/// whether it is currently active (shown by the green indicator in the tab label), its mapping table, and,
/// for <see cref="ModeSwitchMechanism.Switch"/> only, the physical input that activates it directly.
/// Property changes are written directly to the underlying <see cref="ControllerMode"/>.
/// </summary>
public sealed partial class ModeViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    public ControllerMode Mode { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>True when this is the controller's active mode; controls the green indicator in the tab label.</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _switchTriggerDisplayName = "(no input assigned)";

    [ObservableProperty]
    private bool _isCapturingSwitchTrigger;

    /// <summary>Seconds remaining while Capture waits for a physical input for the switch trigger, counting
    /// down from <see cref="CaptureTimeout"/> to zero. Shown beside the Capture button.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    /// <summary>Error shown when the most recently captured/assigned physical input is already used as a
    /// switch trigger by another mode on the same controller (see
    /// <see cref="VirtualControllerViewModel.TryAssignSwitchTrigger"/>). Displayed beside the trigger controls
    /// and cleared after a successful assignment.</summary>
    [ObservableProperty]
    private string? _switchTriggerValidationError;

    public ObservableCollection<MappingRowViewModel> Mappings { get; } = new();

    /// <summary>The same rows as <see cref="Mappings"/>, grouped by target type (see
    /// <see cref="MappingGroupViewModel"/>) in the order used by each row's target-type ComboBox
    /// (<see cref="MappingRowViewModel.TargetKindOptions"/>). Only non-empty groups are shown
    /// (see <see cref="RebuildMappingGroups"/>). The view displays a heading for each group without relying
    /// on the native WPF DataGrid grouping and its custom default group header.</summary>
    public ObservableCollection<MappingGroupViewModel> MappingGroups { get; } = new();

    /// <summary>Mapping rows without a physical source assigned (<see cref="MappingRowViewModel.IsSourceAssigned"/>
    /// is false), typically newly added rows before the user selects Capture or Assign. They cannot yet be
    /// meaningfully grouped by target type because <see cref="MappingRowViewModel.SelectedTargetKind"/> is
    /// only a placeholder value, so they appear ungrouped at the top of the mapping table. Once a source is
    /// assigned, the row moves automatically into its target-type group (see <see cref="RebuildMappingGroups"/>).</summary>
    public ObservableCollection<MappingRowViewModel> UnassignedMappings { get; } = new();

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;

    /// <summary>Raised when the user removes this mode through the Remove button.</summary>
    public event Action<ModeViewModel>? RemoveRequested;

    /// <summary>Raised when the underlying <see cref="ControllerMode"/> changes (name, enabled state, mapping
    /// rows, or switch trigger), so unsaved changes can be tracked.</summary>
    public event Action<ModeViewModel>? Changed;

    /// <summary>Raised when the user captures a physical input as a switch trigger. The calling
    /// <see cref="VirtualControllerViewModel"/> checks that it is unique across the other modes before
    /// accepting it (see <see cref="VirtualControllerViewModel.TryAssignSwitchTrigger"/>).</summary>
    public event Action<ModeViewModel, PhysicalInputRef>? SwitchTriggerCaptured;

    public ModeViewModel(
        ControllerMode mode,
        IReadOnlyList<PhysicalDeviceInfo> knownDevices,
        Func<IReadOnlyList<PhysicalDeviceInfo>> getAvailableDevices,
        Func<IReadOnlyDictionary<string, DeviceSettings>> getDeviceSettings,
        Func<Core.Virtual.ControllerLayout> getLayout,
        Func<IReadOnlyList<PhysicalDeviceInfo>> getFilteredDevices)
    {
        Mode = mode;
        _getAvailableDevices = getAvailableDevices;
        _getDeviceSettings = getDeviceSettings;

        _name = mode.Name;
        _enabled = mode.Enabled;

        RefreshSwitchTriggerDisplayName(knownDevices);

        foreach (var entry in mode.Mappings)
        {
            AddRowViewModel(entry, knownDevices, getFilteredDevices, getLayout);
        }
    }

    public void AddRowViewModel(
        MappingEntry entry,
        IReadOnlyList<PhysicalDeviceInfo> knownDevices,
        Func<IReadOnlyList<PhysicalDeviceInfo>> getFilteredDevices,
        Func<Core.Virtual.ControllerLayout> getLayout)
    {
        var row = new MappingRowViewModel(entry, knownDevices, getFilteredDevices, _getDeviceSettings, getLayout);
        row.RemoveRequested += OnRowRemoveRequested;
        row.Changed += OnRowChanged;
        row.PropertyChanged += OnRowPropertyChanged;
        Mappings.Add(row);
        RebuildMappingGroups();
    }

    /// <summary>A row's target type (<see cref="MappingRowViewModel.SelectedTargetKind"/>) can change while it
    /// belongs to a group. Reassigning it in place would require searching all groups for each change; since
    /// target type changes are infrequent user actions, rebuilding all groups is simpler and sufficient
    /// (see <see cref="RebuildMappingGroups"/>).</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MappingRowViewModel.SelectedTargetKind)
            || e.PropertyName == nameof(MappingRowViewModel.IsSourceAssigned))
        {
            RebuildMappingGroups();
        }
    }

    /// <summary>Rebuilds <see cref="UnassignedMappings"/> and <see cref="MappingGroups"/> from the current
    /// <see cref="Mappings"/>. Rows without an assigned physical source
    /// (<see cref="MappingRowViewModel.IsSourceAssigned"/> == false) go into
    /// <see cref="UnassignedMappings"/>; all others are grouped by <see cref="MappingTargetKind"/> in the
    /// order shown by the target-type ComboBox (<see cref="MappingRowViewModel.TargetKindOptions"/>). Only
    /// target types with at least one assigned row receive a group; empty groups are omitted.</summary>
    private void RebuildMappingGroups()
    {
        UnassignedMappings.Clear();
        foreach (var row in Mappings.Where(row => !row.IsSourceAssigned))
        {
            UnassignedMappings.Add(row);
        }

        MappingGroups.Clear();

        var assignedRows = Mappings.Where(row => row.IsSourceAssigned).ToList();
        foreach (var kind in MappingRowViewModel.TargetKindOptions)
        {
            var rowsForKind = assignedRows.Where(row => row.SelectedTargetKind == kind).ToList();
            if (rowsForKind.Count == 0)
            {
                continue;
            }

            var group = new MappingGroupViewModel(kind, kind.ToString());
            foreach (var row in rowsForKind)
            {
                group.Rows.Add(row);
            }

            MappingGroups.Add(group);
        }
    }

    private void OnRowChanged(MappingRowViewModel row) => Changed?.Invoke(this);

    private void OnRowRemoveRequested(MappingRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.Changed -= OnRowChanged;
        row.PropertyChanged -= OnRowPropertyChanged;
        Mode.Mappings.Remove(row.Entry);
        Mappings.Remove(row);
        RebuildMappingGroups();
        Changed?.Invoke(this);
    }

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanCaptureSwitchTrigger))]
    private async Task CaptureSwitchTriggerAsync()
    {
        IsCapturingSwitchTrigger = true;
        try
        {
            var devices = _getAvailableDevices();
            var captured = await CaptureCountdownHelper.CaptureWithCountdownAsync(
                devices, CaptureTimeout, _getDeviceSettings(), seconds => CaptureCountdownSeconds = seconds).ConfigureAwait(true);
            if (captured is not null)
            {
                SwitchTriggerCaptured?.Invoke(this, captured);
            }
        }
        finally
        {
            IsCapturingSwitchTrigger = false;
        }
    }

    private bool CanCaptureSwitchTrigger() => !IsCapturingSwitchTrigger;

    /// <summary>Called by the Assign dialog as an alternative to Capture for the switch trigger, like
    /// <see cref="MappingRowViewModel.AssignInput"/>.</summary>
    public void AssignSwitchTrigger(AssignableInputOption selected)
        => SwitchTriggerCaptured?.Invoke(this, selected.InputRef);

    /// <summary>Builds the complete list for the modal Assign dialog, like
    /// <see cref="MappingRowViewModel.BuildAssignableInputs"/>.</summary>
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

    /// <summary>Applies a switch trigger that has already passed the uniqueness check to the underlying
    /// <see cref="ControllerMode"/> and refreshes its display. Called only by the owning
    /// <see cref="VirtualControllerViewModel"/> after validation succeeds.</summary>
    public void SetSwitchTrigger(PhysicalInputRef trigger, IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        Mode.SwitchTrigger = new PhysicalInputTrigger
        {
            DeviceId = trigger.DeviceId,
            Kind = trigger.Kind,
            Index = trigger.Index
        };
        SwitchTriggerValidationError = null;
        RefreshSwitchTriggerDisplayName(knownDevices);
        Changed?.Invoke(this);
    }

    public void RefreshSwitchTriggerDisplayName(IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        if (Mode.SwitchTrigger is not { } trigger)
        {
            SwitchTriggerDisplayName = "(no input assigned)";
            return;
        }

        SwitchTriggerDisplayName = PhysicalInputDisplayNameHelper.Build(
            trigger.DeviceId, trigger.Kind, trigger.Index, knownDevices, _getDeviceSettings(), out _);
    }

    partial void OnIsCapturingSwitchTriggerChanged(bool value) => CaptureSwitchTriggerCommand.NotifyCanExecuteChanged();

    partial void OnNameChanged(string value)
    {
        Mode.Name = value;
        Changed?.Invoke(this);
    }

    partial void OnEnabledChanged(bool value)
    {
        Mode.Enabled = value;
        Changed?.Invoke(this);
    }

    public void Dispose()
    {
        foreach (var row in Mappings)
        {
            row.RemoveRequested -= OnRowRemoveRequested;
            row.Changed -= OnRowChanged;
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }
}

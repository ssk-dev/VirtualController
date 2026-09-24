using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Repraesentiert genau einen Modus ("Flugmodus", "Rennen", ...) eines virtuellen Controllers in der
/// UI: Name, Enabled-Flag, ob dieser Modus aktuell aktiv ist (fuer den gruenen Kreis im Tab-Label),
/// die eigene Mapping-Tabelle dieses Modus sowie - nur relevant bei
/// <see cref="ModeSwitchMechanism.Switch"/> - der physische Ausloeser, der diesen Modus direkt aktiviert.
/// Aenderungen an den Properties schreiben direkt in das zugrunde liegende <see cref="ControllerMode"/>.
/// </summary>
public sealed partial class ModeViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    public ControllerMode Mode { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>True, wenn dieser Modus aktuell der aktive Modus des Controllers ist - steuert den
    /// gruenen Aktiv-Indikator im Tab-Label.</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _switchTriggerDisplayName = "(keine Eingabe zugewiesen)";

    [ObservableProperty]
    private bool _isCapturingSwitchTrigger;

    /// <summary>Verbleibende Sekunden, waehrend "Erfassen" auf eine physische Eingabe fuer den
    /// Switch-Trigger wartet - zaehlt vom Erfassen-Timeout (<see cref="CaptureTimeout"/>) bis 0 herunter.
    /// Wird von der View als Countdown neben dem "Erfassen"-Button angezeigt.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    /// <summary>Fehlermeldung, falls die zuletzt erfasste/zugewiesene physische Eingabe fuer den
    /// Switch-Trigger bereits von einem anderen Modus desselben Controllers verwendet wird - siehe
    /// <see cref="VirtualControllerViewModel.TryAssignSwitchTrigger"/>. Wird in der View direkt neben
    /// dem Trigger-Bereich angezeigt und bei erfolgreicher Zuweisung wieder geleert.</summary>
    [ObservableProperty]
    private string? _switchTriggerValidationError;

    public ObservableCollection<MappingRowViewModel> Mappings { get; } = new();

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;

    /// <summary>Wird ausgeloest, wenn der Nutzer diesen Modus ueber den "Entfernen"-Button loeschen moechte.</summary>
    public event Action<ModeViewModel>? RemoveRequested;

    /// <summary>Wird ausgeloest, wenn sich am zugrunde liegenden <see cref="ControllerMode"/> etwas
    /// aendert (Name, Enabled, Mapping-Zeilen, Switch-Trigger) - fuer "ungespeicherte Aenderungen".</summary>
    public event Action<ModeViewModel>? Changed;

    /// <summary>Wird ausgeloest, wenn der Nutzer per "Erfassen" eine physische Eingabe als Switch-Trigger
    /// erfasst hat: der aufrufende <see cref="VirtualControllerViewModel"/> prueft die Eindeutigkeit
    /// gegenueber den uebrigen Modi und uebernimmt den Wert nur bei Erfolg (siehe
    /// <see cref="VirtualControllerViewModel.TryAssignSwitchTrigger"/>).</summary>
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
        Mappings.Add(row);
    }

    private void OnRowChanged(MappingRowViewModel row) => Changed?.Invoke(this);

    private void OnRowRemoveRequested(MappingRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.Changed -= OnRowChanged;
        Mode.Mappings.Remove(row.Entry);
        Mappings.Remove(row);
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

    /// <summary>Wird vom "Zuweisen"-Dialog aufgerufen (Alternative zu "Erfassen" fuer den Switch-Trigger,
    /// analog zu <see cref="MappingRowViewModel.AssignInput"/>).</summary>
    public void AssignSwitchTrigger(AssignableInputOption selected)
        => SwitchTriggerCaptured?.Invoke(this, selected.InputRef);

    /// <summary>Baut die vollstaendige Auswahlliste fuer den modalen "Zuweisen"-Dialog auf, analog zu
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

    /// <summary>Uebernimmt einen bereits als eindeutig geprueften Switch-Trigger in das zugrunde liegende
    /// <see cref="ControllerMode"/> und aktualisiert die Anzeige. Wird ausschliesslich vom besitzenden
    /// <see cref="VirtualControllerViewModel"/> nach erfolgreicher Eindeutigkeitspruefung aufgerufen.</summary>
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
            SwitchTriggerDisplayName = "(keine Eingabe zugewiesen)";
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
        }
    }
}

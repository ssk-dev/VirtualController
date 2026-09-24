using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Repraesentiert einen angeschlossenen physischen Controller in der Auswahlliste eines
/// virtuellen Controllers: der Nutzer entscheidet per Checkbox, ob dieses Geraet fuer das
/// Mapping (und die "Erfassen"-Funktion) dieses virtuellen Controllers beruecksichtigt wird.
/// Kann zusaetzlich aufgeklappt werden, um alle physischen Eingaben des Geraets mit Live-
/// Hervorhebung anzuzeigen und direkt einer Mapping-Zeile zuzuweisen.
/// </summary>
public sealed partial class DeviceSelectionViewModel : ObservableObject, IDisposable
{
    /// <summary>Aktualisierungsrate der Live-Hervorhebung. Bewusst deutlich niedriger als das Mapping-Polling (1000Hz),
    /// da es hier nur um eine fuer das menschliche Auge fluessige visuelle Rueckmeldung geht.</summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromMilliseconds(33);

    public PhysicalDeviceInfo Device { get; }

    private readonly Func<PhysicalInputRef, string?> _getCustomName;
    private readonly Action<PhysicalInputRef, string> _setCustomName;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;

    private DispatcherTimer? _liveTimer;
    private IDeviceReader? _liveReader;
    private bool _inputsBuilt;

    /// <summary>Ob mindestens eine Mapping-Zeile eines beliebigen Modus dieses Geraet aktuell als
    /// physische Quelle verwendet (siehe <see cref="VirtualControllerViewModel.UpdateMappingSourceLiveMonitoring"/>).
    /// Haelt das Live-Polling dieses Geraets unabhaengig von <see cref="IsExpanded"/> am Laufen, damit die
    /// Hervorhebung der Mapping-Tabelle (<see cref="MappingRowViewModel.IsSourceActive"/>) auch dann
    /// funktioniert, wenn die Eingabeliste dieses Geraets gerade nicht aufgeklappt ist.</summary>
    private bool _mappingSourceMonitoringRequested;

    /// <summary>Ob der "Mapping"-Tab des Hauptfensters aktuell tatsaechlich sichtbar ist, der virtuelle
    /// Controller, zu dem dieses Geraet gehoert, der aktuell ausgewaehlte Controller ist UND das Fenster
    /// nicht minimiert ist (siehe <see cref="VirtualControllerViewModel.SetScreenActive"/>, gesetzt durch
    /// <see cref="MainViewModel"/>). Ohne diese Bedingung wuerde jedes Geraet jedes (auch gerade nicht
    /// sichtbaren) virtuellen Controllers weiterhin per Timer gepollt, obwohl die zugehoerige Hervorhebung
    /// gar nicht angezeigt werden kann - reine Verschwendung von CPU-Zeit und Geraetezugriffen. Startet
    /// bewusst mit <c>false</c>: erst der explizite Aufruf durch <see cref="MainViewModel"/> (unmittelbar
    /// nach dem Aufbau) aktiviert das Polling tatsaechlich.</summary>
    private bool _isScreenActive;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<PhysicalInputRowViewModel> Inputs { get; } = new();

    /// <summary>Wird ausgeloest, wenn der Nutzer die Auswahl per Checkbox aendert.</summary>
    public event Action<DeviceSelectionViewModel>? SelectionChanged;

    /// <summary>Wird ausgeloest, wenn der Nutzer ueber "Zuweisen" bei einer physischen Eingabe eine neue Mapping-Zeile anlegen moechte.</summary>
    public event Action<PhysicalInputRef>? AssignInputRequested;

    /// <summary>Wird bei jedem Live-Poll-Tick dieses Geraets mit dem frisch gelesenen <see cref="DeviceState"/>
    /// ausgeloest (waehrend die Geraeteliste aufgeklappt ist), bzw. mit <c>null</c>, sobald die Live-
    /// Ueberwachung stoppt (Einklappen, Geraet getrennt). Ermoeglicht es <see cref="VirtualControllerViewModel"/>,
    /// dieselbe Live-Hervorhebung wie in <see cref="Inputs"/> zusaetzlich auf die passenden Zeilen der
    /// Mapping-Tabelle anzuwenden, ohne einen eigenen, redundanten Polling-Mechanismus zu benoetigen.</summary>
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

    /// <summary>Legt fest, ob dieses Geraet fuer die Live-Hervorhebung der Mapping-Tabelle
    /// (<see cref="MappingRowViewModel.IsSourceActive"/>) ueberwacht werden muss, weil mindestens eine
    /// Mapping-Zeile eines beliebigen Modus dieses Geraet als physische Quelle verwendet - unabhaengig
    /// davon, ob die Eingabeliste dieses Geraets (<see cref="IsExpanded"/>) aktuell aufgeklappt ist. Wird
    /// von <see cref="VirtualControllerViewModel.UpdateMappingSourceLiveMonitoring"/> bei jeder relevanten
    /// Aenderung (Mapping hinzugefuegt/entfernt/Quelle geaendert, Modus hinzugefuegt/entfernt) neu gesetzt.</summary>
    public void SetMappingSourceMonitoringRequested(bool value)
    {
        if (_mappingSourceMonitoringRequested == value)
        {
            return;
        }

        _mappingSourceMonitoringRequested = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Legt fest, ob der "Mapping"-Tab des Hauptfensters aktuell tatsaechlich sichtbar ist, der
    /// virtuelle Controller, zu dem dieses Geraet gehoert, der aktuell ausgewaehlte Controller ist UND das
    /// Fenster nicht minimiert ist - nur dann darf ueberhaupt live gepollt werden (siehe
    /// <see cref="RefreshLiveMonitoringState"/>). Wird von <see cref="VirtualControllerViewModel.SetScreenActive"/>
    /// bei jeder relevanten Aenderung (Tab-Wechsel, Controller-Auswahl, Minimieren/Wiederherstellen des
    /// Fensters) fuer alle seine <see cref="AvailableDeviceSelections"/> neu gesetzt.</summary>
    public void SetScreenActive(bool value)
    {
        if (_isScreenActive == value)
        {
            return;
        }

        _isScreenActive = value;
        RefreshLiveMonitoringState();
    }

    /// <summary>Startet bzw. stoppt das Live-Polling dieses Geraets anhand der kombinierten Bedingung
    /// "Bildschirm aktiv UND (Eingabeliste aufgeklappt ODER als Mapping-Quelle benoetigt)" - so bleibt das
    /// Polling z.B. beim Einklappen der Eingabeliste bestehen, solange noch eine Mapping-Zeile dieses
    /// Geraet referenziert, und umgekehrt startet es automatisch, sobald der Nutzer eine neue Mapping-Zeile
    /// mit diesem Geraet als Quelle anlegt, ohne dass die Eingabeliste dafuer aufgeklappt sein muss. Die
    /// Bedingung "Bildschirm aktiv" (<see cref="_isScreenActive"/>) hat dabei stets Vorrang: solange der
    /// Mapping-Tab nicht sichtbar ist, der zugehoerige Controller nicht ausgewaehlt ist oder das Fenster
    /// minimiert ist, wird ueberhaupt nicht gepollt - unabhaengig davon, wie die beiden anderen Bedingungen
    /// stehen.</summary>
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
            // Geraet aktuell nicht oeffenbar (z.B. gerade getrennt) -> Live-Hervorhebung einfach ueberspringen.
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
            // Geraet wurde getrennt -> Ueberwachung stoppen, bis der Nutzer erneut aufklappt/aktualisiert.
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

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
/// Repraesentiert genau einen virtuellen Controller in der UI: Name, gewaehltes Layout,
/// Ziel-Abtastrate sowie die komplette Mapping-Tabelle (welche physischen Controller/Eingaben
/// auf ihn wirken). Aenderungen an den Properties schreiben direkt in das zugrunde liegende
/// <see cref="VirtualControllerProfile"/>, das beim Speichern persistiert wird.
/// </summary>
public sealed partial class VirtualControllerViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Aktualisierungsrate der Laufzeit-Synchronisation von <see cref="VirtualControllerProfile.ActiveModeId"/>
    /// in die UI (ausgewaehlter Tab, gruener Aktiv-Indikator), waehrend der Controller laeuft - siehe
    /// <see cref="_activeModeSyncTimer"/>. Bewusst niedrig, da es hier nur um eine fuer das menschliche
    /// Auge fluessige visuelle Rueckmeldung geht, nicht um das eigentliche 1000Hz-Mapping-Polling.</summary>
    private static readonly TimeSpan ActiveModeSyncInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Pollt <see cref="VirtualControllerProfile.ActiveModeId"/>, waehrend der Controller laeuft:
    /// <see cref="Engine.ControllerSession"/> aendert diesen Wert bei ausgeloestem Toggle-/Switch-Trigger
    /// auf seinem eigenen Hochfrequenz-Polling-Thread - die UI muss daraus den gruenen Aktiv-Indikator
    /// (<see cref="ModeViewModel.IsActive"/>) jedes Tabs aktuell halten. Der ausgewaehlte Tab
    /// (<see cref="SelectedMode"/>, und damit die angezeigte Mapping-Tabelle) wird dabei NUR dann
    /// automatisch nachgezogen, wenn sich <see cref="VirtualControllerProfile.ActiveModeId"/> seit dem
    /// letzten Tick tatsaechlich geaendert hat (siehe <see cref="_lastObservedActiveModeId"/>) - eine rein
    /// manuelle Tab-Auswahl des Nutzers bleibt also bestehen, bis der Trigger wirklich einen anderen
    /// Modus aktiviert - siehe <see cref="OnActiveModeSyncTimerTick"/>.</summary>
    private DispatcherTimer? _activeModeSyncTimer;

    /// <summary>Letzter von <see cref="OnActiveModeSyncTimerTick"/> beobachteter Wert von
    /// <see cref="VirtualControllerProfile.ActiveModeId"/> - wird benoetigt, um eine tatsaechliche
    /// Trigger-Umschaltung (dieser Wert aendert sich) von einer rein manuellen Tab-Auswahl des Nutzers
    /// (nur <see cref="SelectedMode"/> weicht ab, ActiveModeId bleibt gleich) zu unterscheiden - nur bei
    /// einer echten Trigger-Umschaltung soll <see cref="SelectedMode"/> automatisch nachgezogen werden,
    /// damit der Nutzer zwischenzeitlich frei einen beliebigen Tab anschauen kann.</summary>
    private Guid? _lastObservedActiveModeId;

    public VirtualControllerProfile Profile { get; }

    /// <summary>Fuer ComboBox-Bindings in der View.</summary>
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
    private string _statusText = "Gestoppt";

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Anzeige des tatsaechlich verwendeten ViGEmBus-Backends (Xbox360/DualShock4), abgeleitet aus dem Layout.</summary>
    public VirtualBackend ResolvedBackend => LayoutBackendMap.Resolve(Layout);

    public ObservableCollection<ModeViewModel> Modes { get; } = new();

    [ObservableProperty]
    private ModeViewModel? _selectedMode;

    /// <summary>Fuer ComboBox/RadioButton-Bindings in der View.</summary>
    public static IReadOnlyList<ModeSwitchMechanism> ModeSwitchMechanismOptions { get; } = Enum.GetValues<ModeSwitchMechanism>();

    [ObservableProperty]
    private ModeSwitchMechanism _modeSwitchMechanism;

    /// <summary>Ob bei jedem tatsaechlichen Wechsel des aktiven Modus eine kurze Bildschirmbenachrichtigung
    /// (siehe <see cref="ModeActivated"/>, ausgewertet in <see cref="MainViewModel"/>) angezeigt werden soll.</summary>
    [ObservableProperty]
    private bool _notifyOnModeChange;

    /// <summary>Ob die diesem Controller aktuell zugeordneten physischen Geraete automatisch per HidHide
    /// gesperrt werden sollen, waehrend dieser Controller laeuft (siehe <see cref="Core.Mapping.VirtualControllerProfile.HidHideEnabled"/>).
    /// Nur wirksam, wenn HidHide installiert/betriebsbereit ist (siehe <see cref="Views.MainWindow"/>, dort
    /// wird die zugehoerige Checkbox andernfalls ausgegraut, siehe <see cref="MainViewModel.IsHidHideAvailable"/>).</summary>
    [ObservableProperty]
    private bool _hidHideEnabled;

    /// <summary>Ob dieser Controller automatisch gestartet/gestoppt werden soll, je nachdem ob das unter
    /// <see cref="AutoStartExecutablePath"/> hinterlegte Programm laeuft (siehe <see cref="Core.Mapping.VirtualControllerProfile.AutoStartEnabled"/>).
    /// Wird per periodischem Polling in <see cref="MainViewModel"/> ausgewertet.</summary>
    [ObservableProperty]
    private bool _autoStartEnabled;

    /// <summary>Vollstaendiger Pfad der .exe, deren Laufen ueberwacht wird (siehe <see cref="AutoStartEnabled"/>
    /// und <see cref="Core.Mapping.VirtualControllerProfile.AutoStartExecutablePath"/>). Wird per
    /// <see cref="ChooseAutoStartExecutableCommand"/> ueber einen Dateiauswahl-Dialog gesetzt.</summary>
    [ObservableProperty]
    private string? _autoStartExecutablePath;

    /// <summary>Anzeigename der ausgewaehlten .exe (nur Dateiname ohne Pfad) fuer die Beschriftung neben dem
    /// "Programm auswaehlen"-Button, oder ein Platzhaltertext, solange noch kein Programm ausgewaehlt wurde.</summary>
    public string AutoStartExecutableDisplayName => string.IsNullOrWhiteSpace(AutoStartExecutablePath)
        ? "(kein Programm ausgewaehlt)"
        : System.IO.Path.GetFileName(AutoStartExecutablePath);

    /// <summary>Nur relevant bei <see cref="Core.Mapping.ModeSwitchMechanism.Toggle"/>: Anzeigename der
    /// physischen Eingabe, die bei jeder steigenden Flanke zum naechsten aktivierten Modus weiterschaltet.</summary>
    [ObservableProperty]
    private string _toggleTriggerDisplayName = "(keine Eingabe zugewiesen)";

    [ObservableProperty]
    private bool _isCapturingToggleTrigger;

    /// <summary>Verbleibende Sekunden, waehrend "Erfassen" auf eine physische Eingabe fuer den
    /// controller-weiten Toggle-Trigger wartet - zaehlt vom Erfassen-Timeout (<see cref="CaptureTimeout"/>)
    /// bis 0 herunter. Wird von der View als Countdown neben dem "Erfassen"-Button angezeigt.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    /// <summary>Angeschlossene physische Controller mit Checkbox, ob sie fuer diesen virtuellen Controller
    /// beruecksichtigt werden sollen (Filter fuer die "Erfassen"-Funktion und das aktive Mapping).
    /// </summary>
    public ObservableCollection<DeviceSelectionViewModel> AvailableDeviceSelections { get; } = new();

    /// <summary>Physische Geraete, die diesem virtuellen Controller zugeordnet sind (siehe
    /// <see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>), aber aktuell NICHT
    /// angeschlossen sind - werden in der View unterhalb von <see cref="AvailableDeviceSelections"/> in
    /// einer eigenen, ausgegrauten Liste "Zugewiesene Geräte" angezeigt, damit die bestehende Zuweisung
    /// auch waehrend das Geraet getrennt ist sichtbar bleibt. Wird bei jedem Aufruf von
    /// <see cref="RefreshDeviceSelections"/> neu ermittelt (siehe <see cref="UpdateAssignedDisconnectedDeviceSelections"/>).
    /// </summary>
    public ObservableCollection<AssignedDisconnectedDeviceViewModel> AssignedDisconnectedDeviceSelections { get; } = new();

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;
    private readonly Func<PhysicalInputRef, string?> _getCustomInputName;
    private readonly Action<PhysicalInputRef, string> _setCustomInputName;

    public event Action<VirtualControllerViewModel>? StartRequested;
    public event Action<VirtualControllerViewModel>? StopRequested;
    public event Action<VirtualControllerViewModel>? RemoveRequested;
    public event Action<VirtualControllerViewModel>? ProfileChanged;

    /// <summary>Wird ausgeloest, wenn sich der aktive Modus dieses Controllers tatsaechlich geaendert hat
    /// (manuell per Tab-Klick waehrend der Controller gestoppt ist, oder per Toggle-/Switch-Trigger
    /// waehrend der Controller laeuft) UND <see cref="NotifyOnModeChange"/> aktiviert ist - wird von
    /// <see cref="MainViewModel"/> weitergereicht, damit <see cref="Views.MainWindow"/> eine kurze
    /// Bildschirmbenachrichtigung anzeigen kann (siehe <see cref="Views.ModeChangeToast"/>).</summary>
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

        // Neues Profil ohne Auswahl -> standardmaessig alle aktuell angeschlossenen Geraete beruecksichtigen.
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

        // RefreshDeviceSelections() oben lief bereits VOR dieser Schleife (Modes war zu diesem Zeitpunkt
        // noch leer) und konnte daher noch keine vorhandenen Mapping-Quellen erkennen - erneuter Aufruf
        // jetzt, damit Geraete, die bereits geladene Mapping-Eintraege als Quelle referenzieren, von
        // Anfang an live ueberwacht werden (Mapping-Tabellen-Highlight unabhaengig von "Verfügbare Geräte").
        UpdateMappingSourceLiveMonitoring();

        SelectedMode = Modes.FirstOrDefault(m => m.Mode.Id == profile.ActiveModeId) ?? Modes.FirstOrDefault();
    }

    /// <summary>Baut die Geraete-Auswahlliste anhand der aktuell verfuegbaren physischen Controller neu auf.
    /// Arbeitet bewusst inkrementell (statt alles zu verwerfen und neu zu erzeugen): nur Geraete, die
    /// nicht mehr in <paramref name="devices"/> enthalten sind, werden entfernt, und nur wirklich neue
    /// Geraete werden als zusaetzliche <see cref="DeviceSelectionViewModel"/> angelegt. Bereits vorhandene
    /// Instanzen bleiben unveraendert erhalten. Das ist notwendig, da diese Methode inzwischen (durch die
    /// automatische Geraeteerkennung, siehe <see cref="MainViewModel"/>-Hotplug-Timer) alle paar Sekunden
    /// aufgerufen wird - ein vollstaendiger Neuaufbau wuerde sonst bei jedem Aufruf den aufgeklappten
    /// Zustand (<see cref="DeviceSelectionViewModel.IsExpanded"/>) sowie die bereits aufgebaute
    /// Eingabeliste und laufende Live-Hervorhebung jedes Geraets verwerfen, sodass eine vom Nutzer
    /// aufgeklappte Geraeteliste sich waehrend der Nutzung (z.B. beim Testen einer Eingabe) von selbst
    /// wieder einklappte.</summary>
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
                continue; // Bereits vorhanden -> ViewModel (inkl. IsExpanded/IsSelected/Inputs) unangetastet lassen.
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

        // Bereits vorhandene Mapping-Zeilen zeigen den Verbindungsstatus ihrer physischen Quelle an
        // (siehe MappingRowViewModel.IsSourceConnected) - muss bei jeder Aenderung der Geraeteliste
        // neu ermittelt werden, z.B. wenn ein Geraet waehrend der Laufzeit getrennt/wieder verbunden wird.
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

    /// <summary>Baut <see cref="AssignedDisconnectedDeviceSelections"/> anhand der aktuellen Zuweisung
    /// (<see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>) neu auf: enthaelt genau
    /// jene zugeordneten Geraete-Ids, die NICHT in <paramref name="availableDevices"/> (also aktuell nicht
    /// angeschlossen bzw. deaktiviert/ausgeblendet) enthalten sind. Der Anzeigename wird dabei aus dem
    /// zuletzt bekannten Geraetenamen (siehe <see cref="Core.Devices.DeviceSettings.LastKnownDisplayName"/>)
    /// ermittelt, analog zu <see cref="MainViewModel.GetAllKnownDevices"/>.</summary>
    private void UpdateAssignedDisconnectedDeviceSelections(IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        var availableIds = availableDevices.Select(d => d.DeviceId).ToHashSet();
        var deviceSettings = _getDeviceSettings();

        AssignedDisconnectedDeviceSelections.Clear();

        foreach (var deviceId in Profile.AssignedDeviceIds)
        {
            if (availableIds.Contains(deviceId))
            {
                continue; // Aktuell angeschlossen -> bereits in AvailableDeviceSelections vertreten.
            }

            string displayName = deviceSettings.TryGetValue(deviceId, out var settings)
                ? settings.LastKnownDisplayName ?? deviceId
                : deviceId;

            AssignedDisconnectedDeviceSelections.Add(new AssignedDisconnectedDeviceViewModel(deviceId, displayName));
        }
    }

    /// <summary>Liefert nur die aktuell fuer diesen virtuellen Controller ausgewaehlten physischen Geraete,
    /// z.B. als Quelle fuer die "Erfassen"-Funktion der Mapping-Zeilen.</summary>
    public IReadOnlyList<PhysicalDeviceInfo> GetFilteredDevices()
        => AvailableDeviceSelections.Where(s => s.IsSelected).Select(s => s.Device).ToList();

    /// <summary>Stellt sicher, dass jedes physische Geraet, das aktuell als Quelle mindestens einer
    /// Mapping-Zeile (irgendeines Modus) dient, live ueberwacht wird (siehe
    /// <see cref="DeviceSelectionViewModel.SetMappingSourceMonitoringRequested"/>) - unabhaengig davon,
    /// ob die Eingabeliste dieses Geraets in "Verfügbare Geräte" aktuell aufgeklappt ist. Damit hebt sich
    /// eine zugewiesene Mapping-Zeile (<see cref="MappingRowViewModel.IsSourceActive"/>) auch dann farblich
    /// hervor, wenn der Nutzer die zugehoerige Geraeteliste nie oeffnet. Muss bei jeder Aenderung, die die
    /// Menge der als Quelle verwendeten Geraete beeinflussen kann, erneut aufgerufen werden: neue/entfernte
    /// Mapping-Zeile, geaenderte physische Quelle einer Zeile (Erfassen/Zuweisen), neuer/entfernter Modus,
    /// sowie nach jedem Neuaufbau der Geraeteauswahl (<see cref="RefreshDeviceSelections"/>).</summary>
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

    /// <summary>Ob der "Mapping"-Tab des Hauptfensters aktuell tatsaechlich sichtbar ist, DIESER Controller
    /// der aktuell ausgewaehlte Controller ist UND das Fenster nicht minimiert ist (siehe
    /// <see cref="SetScreenActive"/>, gesetzt durch <see cref="MainViewModel"/>). Wird an jede
    /// <see cref="DeviceSelectionViewModel"/>-Instanz in <see cref="AvailableDeviceSelections"/>
    /// weitergereicht, damit deren Live-Polling (siehe <see cref="DeviceSelectionViewModel.SetScreenActive"/>)
    /// nur laeuft, waehrend diese Bedingung erfuellt ist - andernfalls waere jedes Polling reine
    /// Verschwendung, da die zugehoerige Live-Hervorhebung ohnehin nicht sichtbar sein kann.</summary>
    private bool _isScreenActive;

    /// <summary>Legt fest, ob dieser Controller aktuell "auf dem Bildschirm" sichtbar ist - d.h. der
    /// Mapping-Tab des Hauptfensters ist der aktive Tab, DIESER Controller ist der aktuell ausgewaehlte
    /// Controller (siehe <see cref="MainViewModel.SelectedController"/>) UND das Fenster ist nicht
    /// minimiert. Wird von <see cref="MainViewModel"/> bei jeder dieser drei Bedingungen (Tab-Wechsel,
    /// Controller-Auswahl, Minimieren/Wiederherstellen) fuer ALLE verwalteten Controller neu berechnet und
    /// gesetzt - nur der jeweils tatsaechlich sichtbare Controller erhaelt dabei <c>true</c>.</summary>
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
        // Bewusst NICHT einfach durch die aktuell sichtbare Auswahl ersetzen: aktuell getrennte, aber
        // weiterhin zugeordnete Geraete (siehe AssignedDisconnectedDeviceSelections) sind hier nicht
        // vertreten (sie tauchen ja gar nicht in AvailableDeviceSelections auf) und wuerden sonst bei
        // jeder Checkbox-Aenderung eines beliebigen ANDEREN, gerade angeschlossenen Geraets faelschlich
        // aus der Zuweisung entfernt.
        var selectedVisibleIds = AvailableDeviceSelections.Where(s => s.IsSelected).Select(s => s.Device.DeviceId);
        var stillAssignedDisconnectedIds = AssignedDisconnectedDeviceSelections.Select(d => d.DeviceId);
        Profile.AssignedDeviceIds = selectedVisibleIds.Union(stillAssignedDisconnectedIds).ToList();
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Spiegelt die Live-Hervorhebung der aufklappbaren Geraete-Eingabeliste
    /// (<see cref="DeviceSelectionViewModel.LiveStateChanged"/>, gleicher Mechanismus wie
    /// <see cref="PhysicalInputRowViewModel.IsActive"/>) zusaetzlich auf alle Mapping-Zeilen (ueber alle
    /// Modi hinweg, nicht nur den aktuell angezeigten <see cref="SelectedMode"/>), deren physische Quelle
    /// von diesem Geraet stammt - dadurch hebt sich die zugewiesene Zeile der Mapping-Tabelle farblich
    /// hervor, waehrend die zugehoerige physische Eingabe gerade aktiv ist, analog zum Verhalten in der
    /// Liste der verfuegbaren Geraete. Bei <paramref name="state"/> == null (Geraet eingeklappt/getrennt)
    /// wird die Hervorhebung stattdessen zurueckgesetzt.</summary>
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
            Name = $"Modus {Modes.Count + 1}"
        };

        Profile.Modes.Add(mode);
        var viewModel = AddModeViewModel(mode, _getAvailableDevices());
        SelectedMode = viewModel;

        // Erster angelegter Modus wird automatisch aktiv, damit der Controller ueberhaupt ein
        // ausgewertetes Mapping besitzt, ohne dass der Nutzer zusaetzlich manuell aktivieren muss.
        if (Profile.ActiveModeId is null)
        {
            Profile.ActiveModeId = mode.Id;
        }

        ProfileChanged?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanAddMapping))]
    private void AddMapping()
    {
        // Neuer, noch nicht zugeordneter Eintrag - Quelle wird ueber "Erfassen" in der Zeile gesetzt.
        CreateAndAddMapping(sourceDeviceId: string.Empty, sourceKind: PhysicalInputKind.Button, sourceIndex: 0);
    }

    /// <summary>Ein Mapping-Eintrag gehoert immer zu einem Modus - ohne angelegten/ausgewaehlten Modus
    /// kann kein neues Mapping hinzugefuegt werden (siehe <see cref="CreateAndAddMapping"/>). Steuert die
    /// Aktivierung des "+ Mapping-Zeile hinzufuegen"-Buttons in der View.</summary>
    private bool CanAddMapping() => SelectedMode is not null;

    /// <summary>Wird aufgerufen, wenn der Nutzer in der aufklappbaren Eingabeliste eines Geraets bei einer
    /// physischen Eingabe auf "Zuweisen" klickt: legt eine neue Mapping-Zeile mit bereits gesetzter
    /// physischer Quelle an, sodass der Nutzer nur noch Ziel-Typ/Ziel-Wert waehlen muss.</summary>
    private void OnAssignInputRequested(PhysicalInputRef inputRef)
        => CreateAndAddMapping(inputRef.DeviceId, inputRef.Kind, inputRef.Index);

    private void CreateAndAddMapping(string sourceDeviceId, PhysicalInputKind sourceKind, int sourceIndex)
    {
        // Ein Mapping-Eintrag gehoert immer zu genau einem Modus - ohne angelegten/ausgewaehlten Modus
        // gibt es keine Zieltabelle, in die der Eintrag geschrieben werden koennte (siehe Anforderung:
        // ein Modus muss angelegt werden, bevor Mappings erfasst/zugewiesen werden koennen).
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
        DebugLog.Write($"[VCVM:{Name}] SetRunningState aufgerufen: running={running} statusText='{statusText}' (vorher IsRunning={IsRunning}).");
        IsRunning = running;
        StatusText = statusText ?? (running ? "Laeuft" : "Gestoppt");
    }

    partial void OnIsRunningChanged(bool value)
    {
        // Start-Button muss gesperrt werden, sobald der Controller laeuft (und umgekehrt fuer
        // Stop) - verhindert den Bug, dass "Start" erneut geklickt werden konnte (z.B. nach einem
        // Layout-Wechsel waehrend der Laufzeit), wodurch ein neuer virtueller Controller erzeugt
        // wurde, ohne den vorherigen zu beenden. Der alte blieb dadurch dauerhaft (bis Prozessende)
        // als verwaistes Geraet beim ViGEmBus-Treiber/in Windows angemeldet.
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

    /// <summary>Startet die periodische Uebernahme von <see cref="VirtualControllerProfile.ActiveModeId"/>
    /// in die UI, solange der Controller laeuft (siehe <see cref="_activeModeSyncTimer"/>).</summary>
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
        // Aktualisiert die gruenen Aktiv-Indikatoren immer. SelectedMode (und damit die angezeigte
        // Mapping-Tabelle, siehe MainWindow.xaml MappingsDataGrid.ItemsSource) wird jedoch NUR dann
        // nachgezogen, wenn sich Profile.ActiveModeId seit dem letzten Tick tatsaechlich geaendert hat
        // (also der Toggle-/Switch-Trigger wirklich umgeschaltet hat) - nicht schon deshalb, weil der
        // Nutzer manuell einen anderen Tab angeklickt hat, um sich dessen Mappings anzusehen (dabei
        // bleibt Profile.ActiveModeId unveraendert, siehe OnSelectedModeChanged). So kann der Nutzer
        // waehrend der Laufzeit frei zwischen Tabs wechseln, ohne dass diese Ansicht durch den naechsten
        // 100ms-Tick sofort wieder zurueckgesetzt wird - sobald der Trigger jedoch tatsaechlich einen
        // anderen Modus aktiviert, folgt die Ansicht dem sofort.
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

    /// <summary>Wird aufgerufen, wenn der Nutzer fuer einen Modus per "Erfassen"/"Zuweisen" einen
    /// Switch-Trigger festlegen moechte: stellt sicher, dass dieselbe physische Eingabe innerhalb
    /// desselben Controllers nicht bereits von einem anderen (aktivierten) Modus als Switch-Trigger
    /// verwendet wird, bevor der Wert tatsaechlich uebernommen wird.</summary>
    private void OnModeSwitchTriggerCaptured(ModeViewModel mode, PhysicalInputRef trigger)
    {
        bool isAlreadyUsedElsewhere = Modes.Any(other => other != mode
            && other.Mode.SwitchTrigger is { } otherTrigger
            && otherTrigger.DeviceId == trigger.DeviceId
            && otherTrigger.Kind == trigger.Kind
            && otherTrigger.Index == trigger.Index);

        if (isAlreadyUsedElsewhere)
        {
            mode.SwitchTriggerValidationError = "Diese Eingabe wird bereits von einem anderen Modus dieses Controllers verwendet.";
            return;
        }

        mode.SetSwitchTrigger(trigger, _getAvailableDevices());
        ProfileChanged?.Invoke(this);
    }

    /// <summary>Aktualisiert den gruenen Aktiv-Indikator (<see cref="ModeViewModel.IsActive"/>) aller Modi
    /// anhand von <see cref="VirtualControllerProfile.ActiveModeId"/>.</summary>
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

        // Bereits vorhandene Mapping-Zeilen zeigen ihre "Ziel-Wert"-Beschriftung layoutabhaengig an
        // (z.B. "A" bei Xbox vs. "Kreuz" bei PlayStation) - bei Layoutwechsel muessen alle bestehenden
        // Zeilen ihre Anzeige aktualisieren, obwohl sich der zugrunde liegende gespeicherte Wert nicht aendert.
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

    /// <summary>Wird ausgeloest, wenn der Nutzer in der Tab-Leiste einen anderen Modus auswaehlt oder wenn
    /// <see cref="OnActiveModeSyncTimerTick"/> den ausgewaehlten Tab an einen tatsaechlich per Trigger neu
    /// aktivierten Modus anpasst. Waehrend der Controller GESTOPPT ist, aktiviert die Tab-Auswahl den
    /// Modus direkt (Klick = Aktivierung) - der Nutzer soll so bequem per Klick zwischen Modi wechseln
    /// koennen. Waehrend der Controller LAEUFT, wird die Aktivierung ausschliesslich durch den
    /// Toggle-/Switch-Trigger bestimmt (siehe <see cref="Engine.ControllerSession.EvaluateModeSwitching"/>);
    /// ein manueller Tab-Klick aendert dann nur die Ansicht (Profile.ActiveModeId bleibt unveraendert) und
    /// bleibt bestehen, bis der Trigger tatsaechlich einen anderen Modus aktiviert - erst dann zieht
    /// <see cref="OnActiveModeSyncTimerTick"/> die Ansicht automatisch nach.</summary>
    partial void OnSelectedModeChanged(ModeViewModel? value)
    {
        DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged aufgerufen: neuer Wert='{value?.Name ?? "null"}' (Id={value?.Mode.Id}) IsRunning={IsRunning}");

        AddMappingCommand.NotifyCanExecuteChanged();

        if (IsRunning)
        {
            DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged: IsRunning=true -> Profile.ActiveModeId wird NICHT geaendert, nur Ansicht (SelectedMode.Mappings) wechselt.");
            RefreshActiveModeIndicators();
            return;
        }

        Profile.ActiveModeId = value?.Mode.Id;
        DebugLog.Write($"[VCVM:{Name}] OnSelectedModeChanged: IsRunning=false -> Profile.ActiveModeId gesetzt auf {Profile.ActiveModeId}.");
        // Muss ERST NACH der ActiveModeId-Zuweisung erfolgen, sonst haengt der gruene Aktiv-Indikator
        // bzw. das Tab-Highlighting einen Klick hinterher (zeigt noch den vorherigen statt des gerade
        // ausgewaehlten Modus als aktiv an).
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

    /// <summary>Oeffnet einen Dateiauswahl-Dialog (gefiltert auf .exe), damit der Nutzer das Programm
    /// festlegen kann, dessen Laufen ueber <see cref="AutoStartEnabled"/> automatisches Starten/Stoppen
    /// dieses Controllers ausloest (siehe <see cref="AutoStartExecutablePath"/>). Speichert bewusst den
    /// vollstaendigen Pfad (nicht nur den Dateinamen), um Verwechslungen mit gleichnamigen Programmen an
    /// anderer Stelle zu vermeiden.</summary>
    [RelayCommand]
    private void ChooseAutoStartExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Programm fuer automatischen Start auswaehlen",
            Filter = "Programme (*.exe)|*.exe",
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

    /// <summary>Baut die vollstaendige Auswahlliste fuer den modalen "Zuweisen"-Dialog des controller-weiten
    /// Toggle-Triggers auf (Alternative zu "Erfassen"), analog zu <see cref="ModeViewModel.BuildAssignableInputs"/>
    /// bzw. <see cref="MappingRowViewModel.BuildAssignableInputs"/>.</summary>
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

    /// <summary>Wird vom modalen "Zuweisen"-Dialog aufgerufen, wenn der Nutzer dort eine physische Eingabe
    /// als controller-weiten Toggle-Trigger bestaetigt hat - Alternative zum physischen "Erfassen".</summary>
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


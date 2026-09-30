using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Engine;
using VirtualController.Core.Mapping;
using VirtualController.Core.Profiles;
using VirtualController.Core.Updates;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Wurzel-ViewModel des Hauptfensters: verwaltet den <see cref="ControllerManager"/> (ViGEmBus-
/// Verbindung + laufende Sessions), die Liste der konfigurierten virtuellen Controller sowie
/// das Laden/Speichern des gesamten Profils. Jede Aenderung an einem virtuellen Controller wird
/// automatisch an die zugehoerige, ggf. laufende <see cref="ControllerSession"/> weitergereicht.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ControllerManager _manager = new();

    /// <summary>Geraeteweite Einstellungen (Enable/Disable, Umbenennung, Kalibrierung, Deadzone, Kurve je
    /// physischer Eingabe), Key = <see cref="PhysicalDeviceInfo.DeviceId"/>. Wird zusammen mit den
    /// Controller-Profilen in <see cref="AppProfile"/> gespeichert/geladen.</summary>
    private Dictionary<string, DeviceSettings> _deviceSettings = new();

    /// <summary>Intervall des periodischen Hotplug-Scans (siehe <see cref="StartHotplugPolling"/>) - ein
    /// Kompromiss zwischen zuegiger Erkennung neu angeschlossener/getrennter Geraete und unnoetiger
    /// Hintergrundlast durch zu haeufiges Neu-Enumerieren aller Eingabegeraete.</summary>
    private static readonly TimeSpan HotplugPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Intervall des periodischen Scans nach Zielprozessen fuer "Controller automatisch starten"
    /// (siehe <see cref="StartAutoStartPolling"/>) - bewusst derselbe Kompromiss wie beim Hotplug-Scan
    /// (<see cref="HotplugPollInterval"/>) zwischen zuegiger Erkennung und unnoetiger Hintergrundlast durch
    /// zu haeufiges Enumerieren aller laufenden Prozesse.</summary>
    private static readonly TimeSpan AutoStartPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Index des "Mapping"-Tabs im Haupt-TabControl (siehe MainWindow.xaml, erstes TabItem) -
    /// verwendet von <see cref="RefreshScreenActiveStates"/>, um Live-Polling physischer Geraete fuer die
    /// Mapping-Tabellen-Hervorhebung nur dann zu aktivieren, wenn dieser Tab tatsaechlich sichtbar ist.</summary>
    public const int MappingTabIndex = 0;

    /// <summary>Index des "Gerätekonfiguration"-Tabs im Haupt-TabControl (siehe MainWindow.xaml, zweites
    /// TabItem) - verwendet von <see cref="RefreshScreenActiveStates"/>, um Live-Polling physischer
    /// Geraete fuer die Achsen-Live-Vorschau nur dann zu aktivieren, wenn dieser Tab tatsaechlich sichtbar
    /// ist.</summary>
    public const int DeviceConfigTabIndex = 1;

    private DispatcherTimer? _hotplugTimer;

    /// <summary>Pollt periodisch, ob fuer irgendeinen Controller mit aktiviertem
    /// <see cref="VirtualControllerViewModel.AutoStartEnabled"/> das hinterlegte Zielprogramm laeuft bzw.
    /// nicht mehr laeuft, und startet/stoppt den betroffenen Controller entsprechend automatisch (siehe
    /// <see cref="OnAutoStartTimerTick"/>). Im Gegensatz zum Hotplug-Timer laeuft dieser Timer immer
    /// (kein globaler Ein-/Ausschalter), da jeder Controller die Funktion einzeln ueber seine eigene
    /// Checkbox aktiviert/deaktiviert - ohne aktivierte Controller ist der periodische Scan sehr
    /// kostenguenstig (reines Enumerieren, kein Geraete-/Treiberzugriff).</summary>
    private DispatcherTimer? _autoStartTimer;

    /// <summary>Ob neu angeschlossene/getrennte physische Geraete automatisch per Hintergrund-Polling
    /// erkannt werden, ohne dass die App neu gestartet oder "Geraete aktualisieren" manuell geklickt
    /// werden muss (siehe <see cref="Views.MainWindow"/>, "Einstellungen"-Tab). Deaktivieren stoppt den
    /// Timer vollstaendig (<see cref="StopHotplugPolling"/>), sodass keine zusaetzliche Hintergrundlast
    /// mehr entsteht.</summary>
    [ObservableProperty]
    private bool _autoDeviceDetectionEnabled = true;

    public ObservableCollection<VirtualControllerViewModel> Controllers { get; } = new();

    /// <summary>Wurzel-ViewModel des "Gerätekonfiguration"-Tabs (siehe <see cref="Views.MainWindow"/>).
    /// Ersetzt den frueheren separaten "Geraete konfigurieren"-Dialog: lebt nun dauerhaft ueber die
    /// gesamte Laufzeit des Hauptfensters und wird bei jedem Geraete-Scan (manuell, durch eine
    /// Einstellungs-Aenderung oder periodisch per Hotplug-Polling) inkrementell abgeglichen, statt
    /// zerstoert und neu aufgebaut zu werden (siehe <see cref="RefreshDevices"/> und
    /// <see cref="DeviceConfigViewModel.UpdateDevices"/>).</summary>
    [ObservableProperty]
    private DeviceConfigViewModel _deviceConfig = null!;

    [ObservableProperty]
    private IReadOnlyList<PhysicalDeviceInfo> _availableDevices = Array.Empty<PhysicalDeviceInfo>();

    [ObservableProperty]
    private string _driverStatusText = "Nicht verbunden";

    [ObservableProperty]
    private bool _driverReady;

    /// <summary>Ob der HidHide-Treiber installiert und betriebsbereit ist (siehe <see cref="ControllerManager.IsHidHideAvailable"/>).
    /// Fuer die HidHide-Checkbox neben dem Start/Stop-Button jedes virtuellen Controllers: wird diese
    /// Checkbox ausgegraut, falls HidHide nicht verfuegbar ist (siehe <see cref="Views.MainWindow"/>).</summary>
    public bool IsHidHideAvailable => _manager.IsHidHideAvailable;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    private VirtualControllerViewModel? _selectedController;

    /// <summary>Index des aktuell sichtbaren Tabs im Haupt-TabControl (siehe MainWindow.xaml), zwei-Wege
    /// gebunden an <c>TabControl.SelectedIndex</c>. Wird zusammen mit <see cref="SelectedController"/> und
    /// <see cref="IsWindowMinimized"/> genutzt, um teures Live-Polling physischer Geraete (Mapping-Tabellen-
    /// Hervorhebung, Achsen-Live-Vorschau der Geraetekonfiguration) auf genau die Faelle zu beschraenken,
    /// in denen die zugehoerige visuelle Rueckmeldung ueberhaupt sichtbar sein kann (siehe
    /// <see cref="RefreshScreenActiveStates"/>). Die Werte 0 ("Mapping") und 1 ("Gerätekonfiguration")
    /// entsprechen der Reihenfolge der TabItems in MainWindow.xaml (siehe <see cref="MappingTabIndex"/>/
    /// <see cref="DeviceConfigTabIndex"/>).</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Ob das Hauptfenster aktuell minimiert ist. Wird von <see cref="Views.MainWindow"/> ueber
    /// dessen <c>StateChanged</c>-Ereignis aktuell gehalten (kein direktes XAML-Binding an
    /// <c>Window.WindowState</c> moeglich, da dieses kein <c>bool</c> ist). Waehrend das Fenster minimiert
    /// ist, kann keine Live-Hervorhebung sichtbar sein - das gesamte Live-Polling wird daher fuer diese
    /// Zeit komplett angehalten (siehe <see cref="RefreshScreenActiveStates"/>).</summary>
    [ObservableProperty]
    private bool _isWindowMinimized;

    /// <summary>Ob seit dem letzten erfolgreichen Speichern/Laden ungespeicherte Aenderungen vorliegen
    /// (Mapping-Tabelle, Controller-Eigenschaften, Geraete-Ein-/Ausgabeeinstellungen). Wird in der UI
    /// genutzt, um den Speichern-Button rot einzufaerben und einen Hinweistext anzuzeigen.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>Wird ausgeloest, wenn sich der aktive Modus irgendeines verwalteten Controllers
    /// tatsaechlich geaendert hat und dieser Controller Benachrichtigungen aktiviert hat (siehe
    /// <see cref="VirtualControllerViewModel.ModeActivated"/>) - <see cref="Views.MainWindow"/> nutzt
    /// dies, um eine kurze Bildschirmbenachrichtigung anzuzeigen.</summary>
    public event Action<VirtualControllerViewModel, ModeViewModel>? ModeActivated;

    /// <summary>Verwaltet die Update-Pruefung (automatisch beim Start und manuell ueber den
    /// "Einstellungen"-Tab) sowie die Einstellung "Automatisch auf Updates pruefen" - siehe
    /// <see cref="Views.MainWindow"/>, "Einstellungen"-Tab. <see cref="Views.MainWindow"/> abonniert
    /// <see cref="UpdateViewModel.UpdateAvailable"/>, um bei einer neu verfuegbaren Version das
    /// Update-Popup anzuzeigen (siehe <see cref="Views.UpdateAvailableDialog"/>).</summary>
    public UpdateViewModel Update { get; } = new();

    /// <summary>Fenstertitel inkl. der zur Build-Zeit aus dem Git-Tag ermittelten App-Version (siehe
    /// <see cref="AppVersionProvider"/>), z.B. "Virtual Controller - Version 1.4.2" - direkt an
    /// <c>Window.Title</c> gebunden (siehe MainWindow.xaml).</summary>
    public string WindowTitle => $"Virtual Controller - Version {AppVersionProvider.RawVersion}";

    public MainViewModel()
    {
        // Fruehzeitig abonnieren: ConnectDriverCommand (ruft Initialize() auf, das verwaiste HidHide-Sperren
        // eines vorherigen Absturzes erkennen/entfernen kann) sowie ein spaeteres Controller-Start/Update
        // (das melden kann, dass HidHide fuer ein Profil aktiviert, der Treiber aber nicht verfuegbar ist)
        // koennen dieses Event bereits ausloesen, bevor der Konstruktor fertig durchlaufen ist.
        _manager.HidHideWarning += message => LastErrorMessage = message;

        RefreshDevices();
        LoadProfiles();

        if (AutoDeviceDetectionEnabled)
        {
            StartHotplugPolling();
        }

        StartAutoStartPolling();

        // Explizit statt sich allein auf die Change-Notification von SelectedController zu verlassen:
        // falls kein Profil geladen wurde (Controllers bleibt leer, SelectedController bleibt null), wuerde
        // OnSelectedControllerChanged sonst gar nicht feuern und der initiale Bildschirm-Status (z.B. fuer
        // DeviceConfig) bliebe implizit auf dem Default-Wert stehen, statt explizit korrekt berechnet zu sein.
        RefreshScreenActiveStates();
    }

    /// <summary>
    /// Verbindet zum ViGEmBus-Treiber. Muss vor dem Start eines virtuellen Controllers erfolgreich
    /// gewesen sein. Faengt <see cref="Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException"/>
    /// ab, falls der Treiber (noch) nicht installiert ist, und zeigt das als Statustext an.
    /// </summary>
    [RelayCommand]
    private void ConnectDriver()
    {
        try
        {
            _manager.Initialize();
            DriverReady = true;
            DriverStatusText = "ViGEmBus verbunden";
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            DriverReady = false;
            DriverStatusText = "ViGEmBus nicht verfuegbar";
            LastErrorMessage = ex.Message;
        }
    }

    partial void OnAutoDeviceDetectionEnabledChanged(bool value)
    {
        if (value)
        {
            StartHotplugPolling();
        }
        else
        {
            StopHotplugPolling();
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => RefreshScreenActiveStates();

    partial void OnSelectedControllerChanged(VirtualControllerViewModel? value) => RefreshScreenActiveStates();

    partial void OnIsWindowMinimizedChanged(bool value) => RefreshScreenActiveStates();

    /// <summary>Zentrale Stelle, die anhand von <see cref="SelectedTabIndex"/>, <see cref="SelectedController"/>
    /// und <see cref="IsWindowMinimized"/> entscheidet, fuer welchen virtuellen Controller (Mapping-Tab)
    /// bzw. ob fuer den "Gerätekonfiguration"-Tab ueberhaupt Live-Polling physischer Geraete laufen darf -
    /// naemlich nur dann, wenn die zugehoerige visuelle Rueckmeldung (Mapping-Tabellen-Hervorhebung bzw.
    /// Achsen-Live-Vorschau) tatsaechlich sichtbar sein kann: der jeweilige Tab ist aktiv sichtbar, im Fall
    /// des Mapping-Tabs zusaetzlich genau der betroffene Controller ist ausgewaehlt, und das Fenster ist
    /// nicht minimiert. Wird bei jeder relevanten Aenderung (Tab-Wechsel, Controller-Auswahl, Minimieren/
    /// Wiederherstellen) sowie initial nach dem Laden/Aufbau der Controller-Liste aufgerufen.</summary>
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

    /// <summary>Startet den periodischen Hotplug-Scan (siehe <see cref="HotplugPollInterval"/>), damit
    /// neu angeschlossene/getrennte physische Geraete ohne App-Neustart oder manuellen Klick auf
    /// "Geraete aktualisieren" erkannt werden. Wird nur aufgerufen, wenn <see cref="AutoDeviceDetectionEnabled"/>
    /// aktiv ist; bei deaktivierter Erkennung bleibt der Timer ungestartet, sodass keinerlei zusaetzliche
    /// Hintergrundlast durch periodisches Neu-Enumerieren aller Eingabegeraete entsteht.</summary>
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

    /// <summary>Startet den periodischen Scan nach Zielprozessen fuer "Controller automatisch starten"
    /// (siehe <see cref="AutoStartPollInterval"/>). Laeuft unconditional ab dem Start der Anwendung, da die
    /// Funktion pro Controller einzeln (nicht global) aktiviert wird.</summary>
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

    /// <summary>Prueft fuer jeden Controller mit aktiviertem <see cref="VirtualControllerViewModel.AutoStartEnabled"/>,
    /// ob das unter <see cref="VirtualControllerViewModel.AutoStartExecutablePath"/> hinterlegte Programm
    /// aktuell laeuft (Abgleich ueber den vollstaendigen Pfad, siehe <see cref="Core.Mapping.VirtualControllerProfile.AutoStartExecutablePath"/>),
    /// und startet bzw. stoppt den betroffenen Controller entsprechend automatisch - laeuft das Programm und
    /// der Controller ist noch nicht aktiv, wird er gestartet (<see cref="OnStartRequested"/>); laeuft es nicht
    /// (mehr) und der Controller ist noch aktiv, wird er gestoppt (<see cref="OnStopRequested"/>). Ein manueller
    /// Stop/Start durch den Nutzer waehrend das Zielprogramm laeuft wird beim naechsten Tick wieder ueberschrieben -
    /// das ist bewusst so (die Checkbox ist eine dauerhafte Kopplung, kein einmaliger Ausloeser).</summary>
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
                    // Manche Prozesse (System-/erhoehte Prozesse, bereits beendete Prozesse) verweigern den
                    // Zugriff auf MainModule - ein einzelner nicht abfragbarer Prozess darf den gesamten Scan
                    // nicht abbrechen, daher hier bewusst ignoriert.
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

        // Zuletzt bekannte Anzeigename + Faehigkeiten je Geraet merken, damit sowohl die Mapping-Tabelle
        // (Anzeigename) als auch der "Gerätekonfiguration"-Tab (komplette Faehigkeiten, um das Geraet bei
        // Bedarf als getrennt/synthetisch weiterhin darstellen zu koennen) auch nach dem Trennen noch
        // sinnvolle Informationen anzeigen koennen, statt der rohen DeviceId bzw. gar nichts.
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

        // Explizit statt sich allein auf Change-Notifications (z.B. von SelectedController) zu verlassen:
        // ein hier frisch angelegtes DeviceConfigViewModel bzw. neu hinzugekommene DeviceSelectionViewModel-
        // Instanzen (siehe VirtualControllerViewModel.RefreshDeviceSelections) starten sonst mit dem
        // Default-Wert false fuer ihr Bildschirm-Aktiv-Flag, bis irgendeine andere Aenderung zufaellig
        // RefreshScreenActiveStates auslöst - das kann insbesondere dann ausbleiben, wenn SelectedController
        // sich dabei gar nicht tatsaechlich aendert (z.B. bleibt null, da kein Profil geladen ist).
        // IsHidHideAvailable ist eine berechnete (nicht observable) Property, deren zugrunde liegender
        // Installationsstatus sich waehrend der Laufzeit aendern kann (Nutzer installiert HidHide
        // nachtraeglich) - RefreshDevices laeuft bereits periodisch per Hotplug-Timer, daher hier
        // mitgenommen, damit die zugehoerige Checkbox in der View sich automatisch entsperrt.
        OnPropertyChanged(nameof(IsHidHideAvailable));

        RefreshScreenActiveStates();
    }

    /// <summary>Liefert saemtliche dem <see cref="MainViewModel"/> bekannten physischen Geraete fuer den
    /// "Gerätekonfiguration"-Tab: sowohl aktuell tatsaechlich angeschlossene (unabhaengig davon, ob sie
    /// per <see cref="DeviceSettings.Enabled"/> deaktiviert wurden - im Gegensatz zu <see cref="AvailableDevices"/>,
    /// die fuer die Mapping-Auswahl bereits gefiltert ist) als auch zuvor bereits erkannte, aber aktuell
    /// getrennte Geraete, rekonstruiert aus deren zuletzt bekannten Faehigkeiten (<see cref="DeviceSettings.LastKnownButtonCount"/>
    /// etc.) - so bleiben deren Einstellungen (Name, Kalibrierung, Enable/Disable) im Konfigurationsdialog
    /// weiterhin sichtbar und bearbeitbar, auch waehrend das Geraet nicht angeschlossen ist.</summary>
    public IReadOnlyList<KnownDeviceInfo> GetAllKnownDevices()
    {
        var connectedDevices = _manager.GetAvailablePhysicalDevices();
        var connectedIds = connectedDevices.Select(d => d.DeviceId).ToHashSet();

        var result = connectedDevices.Select(d => new KnownDeviceInfo(d, IsConnected: true)).ToList();

        foreach (var (deviceId, settings) in _deviceSettings)
        {
            if (connectedIds.Contains(deviceId) || settings.LastKnownButtonCount is not { } buttonCount)
            {
                continue; // Aktuell angeschlossen (bereits oben erfasst) oder noch nie vollstaendig erkannt.
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
                AutoDeviceDetectionEnabled = AutoDeviceDetectionEnabled
            };
            ProfileStore.Save(appProfile);
            LastErrorMessage = null;
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Speichern fehlgeschlagen: {ex.Message}";
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

            // DeviceConfig muss hier komplett neu aufgebaut werden (statt inkrementell abgeglichen zu
            // werden, wie es RefreshDevices/UpdateDevices sonst tun): _deviceSettings wurde eben komplett
            // durch neu geladene Instanzen ersetzt, ein bereits vorhandenes DeviceConfigDeviceViewModel
            // wuerde sonst weiterhin auf die verworfenen, alten DeviceSettings-Objekte zeigen.
            DeviceConfig?.Dispose();
            DeviceConfig = null!;

            RefreshDevices();
            SelectedController = Controllers.FirstOrDefault();
            LastErrorMessage = null;
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Laden fehlgeschlagen: {ex.Message}";
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

    /// <summary>Liefert die Einstellungen eines physischen Geraets, legt bei Bedarf einen neuen, leeren
    /// Eintrag an. Wird vom Konfigurationsdialog genutzt, um Enable/Disable, Kalibrierung, Deadzone und
    /// Antwortkurve je Eingabe zu lesen und zu aendern.</summary>
    public DeviceSettings GetOrCreateDeviceSettings(string deviceId)
    {
        if (!_deviceSettings.TryGetValue(deviceId, out var settings))
        {
            settings = new DeviceSettings();
            _deviceSettings[deviceId] = settings;
        }

        return settings;
    }

    /// <summary>Wird vom Konfigurationsdialog aufgerufen, nachdem der Nutzer die Verfuegbarkeit eines
    /// gesamten Geraets geaendert hat (Enable/Disable oder Ausblenden/Einblenden): filtert die
    /// Geraeteliste neu (deaktivierte/ausgeblendete Geraete verschwinden sofort aus der Auswahl fuer
    /// virtuelle Controller) und verteilt die Aenderung an alle laufenden Sessions. <see cref="RefreshDevices"/>
    /// fuehrt dabei eine vollstaendige Hardware-Neuerkennung (XInput/DirectInput-Enumeration) durch - diese
    /// Methode darf deshalb NICHT fuer reine Einstellungsaenderungen (Umbenennung, Kalibrierung, Deadzone,
    /// Kurve, Enable/Disable einzelner Eingaben) verwendet werden, da die zugehoerigen Steuerelemente per
    /// UpdateSourceTrigger=PropertyChanged bei jedem Tastendruck/jeder Wertaenderung binden - eine dabei
    /// jedesmal synchron auf dem UI-Thread ausgefuehrte Hardware-Enumeration wuerde zu spuerbaren
    /// Verzoegerungen fuehren (siehe <see cref="NotifyDeviceSettingsChanged"/> fuer den dafuer vorgesehenen,
    /// leichtgewichtigen Pfad).</summary>
    public void NotifyDeviceAvailabilityChanged()
    {
        RefreshDevices();
        _manager.BroadcastDeviceSettings(_deviceSettings);
        HasUnsavedChanges = true;
    }

    /// <summary>Wird vom Konfigurationsdialog aufgerufen, nachdem der Nutzer eine reine Einstellung
    /// geaendert hat, die weder die Verfuegbarkeit eines Geraets noch dessen Faehigkeiten beeinflusst
    /// (z.B. Umbenennung einer Eingabe/eines Sticks, Kalibrierung, Deadzone, Kurve, Enable/Disable einer
    /// einzelnen Eingabe statt des gesamten Geraets): verteilt die Aenderung sofort an alle laufenden
    /// Sessions, OHNE die teure Hardware-Neuerkennung aus <see cref="NotifyDeviceAvailabilityChanged"/>
    /// auszufuehren. Bewusst getrennt, da die zugehoerigen Steuerelemente ueblicherweise per
    /// UpdateSourceTrigger=PropertyChanged binden (z.B. das Umbenennungs-Textfeld) und daher bei jedem
    /// Tastendruck aufgerufen werden - eine dabei staendig wiederholte Geraete-Enumeration wuerde
    /// spuerbare Eingabeverzoegerungen verursachen.</summary>
    public void NotifyDeviceSettingsChanged()
    {
        _manager.BroadcastDeviceSettings(_deviceSettings);
        HasUnsavedChanges = true;
    }

    private void OnStartRequested(VirtualControllerViewModel vm)
    {
        if (!DriverReady)
        {
            vm.SetRunningState(false, "Treiber nicht verbunden");
            return;
        }

        try
        {
            _manager.AddController(vm.Profile, _deviceSettings);
            vm.SetRunningState(true);
        }
        catch (Exception ex)
        {
            vm.SetRunningState(false, "Fehler beim Start");
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

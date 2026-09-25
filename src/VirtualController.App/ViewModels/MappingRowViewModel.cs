using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.App.Diagnostics;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Kombiniert einen Ziel-Wert (<see cref="VirtualButton"/>, <see cref="VirtualAxis"/>,
/// <see cref="VirtualTrigger"/> oder <see cref="DPadDirection"/>) mit dem layoutabhaengigen
/// Anzeigenamen, wie er auf dem aktuell gewaehlten <see cref="ControllerLayout"/> tatsaechlich
/// beschriftet ist (siehe <see cref="VirtualControllerLabels"/>). Wird als Item der "Ziel-Wert"-
/// ComboBox verwendet (DisplayMemberPath = Label, SelectedValuePath = Value).
/// </summary>
public sealed record TargetOptionItem(object Value, string Label);

/// <summary>
/// Ein einzelner Eintrag der "Zuweisen"-Auswahlliste (siehe <see cref="MappingRowViewModel.AssignableInputs"/>):
/// eine physische Eingabe eines bestimmten Geraets, die der Nutzer als Quelle dieser Mapping-Zeile
/// uebernehmen kann, ohne sie tatsaechlich druecken/bewegen zu muessen (Alternative zu "Erfassen",
/// z.B. sinnvoll bei Triggern, die bereits leicht ausgeloest sind, oder Eingaben, die sich nur schwer
/// gezielt einzeln ausloesen lassen).
/// </summary>
public sealed record AssignableInputOption(PhysicalDeviceInfo Device, PhysicalInputRef InputRef, string Label)
{
    /// <summary>Bequemlichkeits-Eigenschaft fuer die Gruppierung nach Geraet im "Zuweisen"-Dialog
    /// (<see cref="Views.AssignInputDialog"/>), damit dort per einfacher <c>PropertyGroupDescription</c>
    /// gruppiert werden kann, ohne auf einen (fehleranfälligeren) gepunkteten Bindungspfad "Device.DisplayName"
    /// angewiesen zu sein.</summary>
    public string DeviceDisplayName => Device.DisplayName;
}

/// <summary>
/// Eine einzelne Zeile der Mapping-Tabelle eines virtuellen Controllers: zeigt und bearbeitet,
/// welche physische Eingabe (welcher angeschlossene Controller, welcher Button/Achse/DPad) auf
/// welches Element des virtuellen Controllers wirkt. Alle bindbaren Properties schreiben direkt
/// in das zugrunde liegende <see cref="MappingEntry"/> zurueck, das Teil des gespeicherten Profils ist.
/// </summary>
public sealed partial class MappingRowViewModel : ObservableObject
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Ab diesem absoluten Achsenausschlag gilt eine Achsen-Richtung als "aktiv" - identischer
    /// Schwellwert wie <see cref="PhysicalInputRowViewModel.AxisActiveThreshold"/>, damit die Live-
    /// Hervorhebung dieser Mapping-Zeile (siehe <see cref="IsSourceActive"/>) exakt im selben Moment
    /// reagiert wie die entsprechende Zeile in der aufklappbaren Geraete-Eingabeliste.</summary>
    private const float AxisActiveThreshold = 0.3f;

    /// <summary>Fuer ComboBox-Bindings in der View: alle moeglichen Ziel-Kategorien.</summary>
    public static IReadOnlyList<MappingTargetKind> TargetKindOptions { get; } = Enum.GetValues<MappingTargetKind>();
    public static IReadOnlyList<VirtualButton> ButtonOptions { get; } = Enum.GetValues<VirtualButton>();
    public static IReadOnlyList<VirtualAxis> AxisOptions { get; } = Enum.GetValues<VirtualAxis>();
    public static IReadOnlyList<VirtualTrigger> TriggerOptions { get; } = Enum.GetValues<VirtualTrigger>();
    public static IReadOnlyList<DPadDirection> DPadOptions { get; } = Enum.GetValues<DPadDirection>();

    public MappingEntry Entry { get; }

    /// <summary>Kurze, pro Instanz eindeutige ID (fuer Debug-Logging), damit sich in <see cref="DebugLog"/>
    /// unzweifelhaft nachvollziehen laesst, welche konkrete Zeilen-Instanz eine Aktion ausgeloest hat -
    /// wichtig, um zu erkennen, ob sich mehrere Zeilen gegenseitig ungewollt beeinflussen.</summary>
    public string RowId { get; } = Guid.NewGuid().ToString("N")[..8];

    private readonly Func<IReadOnlyList<PhysicalDeviceInfo>> _getAvailableDevices;
    private readonly Func<IReadOnlyDictionary<string, DeviceSettings>> _getDeviceSettings;
    private readonly Func<ControllerLayout> _getLayout;

    /// <summary>Wird ausgeloest, wenn der Nutzer diese Zeile ueber den "Entfernen"-Button loeschen moechte.</summary>
    public event Action<MappingRowViewModel>? RemoveRequested;

    /// <summary>Wird ausgeloest, wenn sich das zugrunde liegende <see cref="MappingEntry"/> dieser Zeile
    /// aendert (Erfassen einer neuen physischen Quelle, Ziel-Typ/Ziel-Wert, Invertieren, Deadzone) - damit
    /// der uebergeordnete <see cref="VirtualControllerViewModel"/> laufende Sessions aktualisieren und
    /// ungespeicherte Aenderungen erkennen kann.</summary>
    public event Action<MappingRowViewModel>? Changed;

    [ObservableProperty]
    private string _sourceDisplayName;

    /// <summary>Ob das physische Quellgeraet dieser Zeile aktuell tatsaechlich angeschlossen ist. Wird von
    /// der View genutzt, um bei getrennten Geraeten einen roten "nicht verbunden"-Hinweis anzuzeigen,
    /// statt stillschweigend nur die bedeutungslose DeviceId zu zeigen.</summary>
    [ObservableProperty]
    private bool _isSourceConnected;

    /// <summary>Ob dieser Zeile bereits eine physische Quelle zugewiesen wurde (per "Erfassen" oder
    /// "Zuweisen"). Frisch angelegte Zeilen haben noch keine Quelle (<see cref="MappingEntry.SourceDeviceId"/>
    /// ist leer) - solche Zeilen werden von <see cref="ModeViewModel.RebuildMappingGroups"/> keiner
    /// Ziel-Typ-Gruppe zugeordnet, sondern in einem eigenen, gruppierungslosen Bereich ganz oben in der
    /// Mapping-Tabelle angezeigt, bis eine Quelle zugewiesen wurde.</summary>
    [ObservableProperty]
    private bool _isSourceAssigned;

    /// <summary>Ob die physische Quelle dieser Mapping-Zeile aktuell tatsaechlich aktiv ist (Taste
    /// gedrueckt, Achse ausgeschlagen, D-Pad-Richtung gehalten) - analog zu
    /// <see cref="PhysicalInputRowViewModel.IsActive"/> in der aufklappbaren Geraete-Eingabeliste, nur
    /// hier fuer die Zeilen-Hervorhebung der Mapping-Tabelle. Wird von <see cref="VirtualControllerViewModel"/>
    /// per Live-Polling der ausgewaehlten/aufgeklappten Geraete aktualisiert (siehe
    /// <see cref="UpdateSourceActiveState"/>) - solange kein passendes Geraet aufgeklappt ist, bleibt
    /// dieser Wert unveraendert false.</summary>
    [ObservableProperty]
    private bool _isSourceActive;

    [ObservableProperty]
    private string _targetDisplayName;

    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Verbleibende Sekunden, waehrend "Erfassen" auf eine physische Eingabe wartet - zaehlt vom
    /// Erfassen-Timeout (<see cref="CaptureTimeout"/>) bis 0 herunter. Wird von der View als Countdown
    /// neben dem "Erfassen"-Button angezeigt, damit der Nutzer sieht, dass der Erfassen-Modus automatisch
    /// beendet wird, falls innerhalb dieser Zeit keine neue Eingabe erkannt wird.</summary>
    [ObservableProperty]
    private int _captureCountdownSeconds;

    [ObservableProperty]
    private MappingTargetKind _selectedTargetKind;

    private object? _selectedTargetValue;

    /// <summary>
    /// Einheitlicher Ziel-Wert fuer die (einzige) Ziel-Wert-ComboBox in der View. Je nach
    /// <see cref="SelectedTargetKind"/> handelt es sich um einen <see cref="VirtualButton"/>,
    /// <see cref="VirtualAxis"/>, <see cref="VirtualTrigger"/> oder <see cref="DPadDirection"/>.
    /// Ersetzt vier fruehere, im gleichen Zellenbereich uebereinander liegende ComboBoxen
    /// (eine pro Ziel-Typ), deren Dropdown-Popups sich beim schnellen Wechsel des Ziel-Typs
    /// gegenseitig ueberlagern konnten und dadurch falsche/veraltete Werte anzeigten.
    /// </summary>
    public object? SelectedTargetValue
    {
        get => _selectedTargetValue;
        set
        {
            DebugLog.Write($"[Row {RowId}] SelectedTargetValue SETTER aufgerufen: alt='{_selectedTargetValue}' neu='{value}' TargetKind={SelectedTargetKind}");

            if (!SetProperty(ref _selectedTargetValue, value))
            {
                DebugLog.Write($"[Row {RowId}] SelectedTargetValue: SetProperty hat NICHT geaendert (Wert war bereits gleich) -> Abbruch.");
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
            DebugLog.Write($"[Row {RowId}] SelectedTargetValue: uebernommen -> Entry.TargetButton={Entry.TargetButton} Entry.TargetAxis={Entry.TargetAxis} Entry.TargetTrigger={Entry.TargetTrigger} Entry.TargetDPadDirection={Entry.TargetDPadDirection} TargetDisplayName='{TargetDisplayName}'");
            Changed?.Invoke(this);
        }
    }

    /// <summary>Die fuer den aktuellen <see cref="SelectedTargetKind"/> gueltigen Auswahlwerte fuer die Ziel-Wert-ComboBox,
    /// mit layoutabhaengigen Anzeigenamen (siehe <see cref="VirtualControllerLabels"/>) passend zum aktuell gewaehlten
    /// <see cref="ControllerLayout"/> des virtuellen Controllers.</summary>
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

    /// <summary>Invertieren/Deadzone sind nur fuer analoge Achsen sinnvoll und werden nur dann in der View eingeblendet.</summary>
    public bool IsAxisTarget => SelectedTargetKind == MappingTargetKind.Axis;

    [ObservableProperty]
    private bool _invert;

    /// <summary>Bei Achsen-Zielen: nur die durch die erfasste Quelle (SourceKind: AxisPositive/AxisNegative)
    /// festgelegte Haelfte der physischen Achse verwenden, statt des vollen bidirektionalen Bereichs -
    /// ermoeglicht z.B. das Aufteilen von zwei unabhaengigen physischen Achsenhaelften auf zwei
    /// unterschiedliche virtuelle Achsen mit jeweils eigenem Invertieren-Vorzeichen.</summary>
    [ObservableProperty]
    private bool _directionalOnly;

    [ObservableProperty]
    private float _deadzone;

    /// <summary>Text-Zwischenspeicher fuer das Deadzone-Eingabefeld: wird von der View statt der reinen
    /// <see cref="Deadzone"/>-Zahl gebunden, damit auch unvollstaendige Zwischenzustaende beim Tippen
    /// (z.B. "0." oder "0,0") im Textfeld stehen bleiben koennen, ohne sofort auf den zuletzt gueltigen
    /// Wert zurueckgesetzt zu werden. Ein direktes Binding von TextBox.Text an den float-Wert mit
    /// UpdateSourceTrigger=PropertyChanged versucht bei jedem einzelnen Tastendruck sofort zu konvertieren;
    /// scheitert dies (z.B. weil noch keine Nachkommastelle nach dem Trennzeichen folgt), verwirft WPF die
    /// Eingabe augenblicklich - dadurch liess sich nie ein Dezimaltrennzeichen eintippen. Erst wenn der
    /// Text vollstaendig zu einem float parsebar ist, wird <see cref="Deadzone"/> (und damit Entry.Deadzone)
    /// tatsaechlich aktualisiert; unvollstaendiger/ungueltiger Text bleibt bis dahin unangetastet im Feld.
    /// Akzeptiert sowohl Punkt als auch Komma als Dezimaltrennzeichen (invariante bzw. aktuelle Kultur).</summary>
    [ObservableProperty]
    private string _deadzoneText;

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
        _deadzone = entry.Deadzone;
        _deadzoneText = _deadzone.ToString("0.####", CultureInfo.InvariantCulture);

        DebugLog.Write($"[Row {RowId}] Konstruktor: Source={entry.SourceDeviceId}|{entry.SourceKind}|{entry.SourceIndex} TargetKind={entry.TargetKind} TargetValue={_selectedTargetValue}");
    }

    public void ApplyCapturedInput(PhysicalInputRef captured, IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        Entry.SourceDeviceId = captured.DeviceId;
        Entry.SourceKind = captured.Kind;
        Entry.SourceIndex = captured.Index;

        // Frisch erfasste/zugewiesene Achse: die geraeteweite Kalibrierung (falls vorhanden) als
        // Deadzone-Vorgabe fuer diesen Mapping-Eintrag uebernehmen, statt des reinen Compile-Time-
        // Standardwerts aus MappingEntry.Deadzone - vermeidet, dass derselbe Wert (Stickdrift etc.)
        // doppelt gepflegt werden muss. Der Nutzer kann den Wert danach weiterhin frei ueberschreiben.
        if (captured.Kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative)
        {
            Deadzone = _getDeviceSettings().ResolveDefaultAxisDeadzone(captured.DeviceId, captured.Index);
        }

        SourceDisplayName = BuildSourceDisplayName(Entry, knownDevices, _getDeviceSettings(), out bool isConnected);
        IsSourceConnected = isConnected;
        IsSourceAssigned = true;
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Wird von <see cref="VirtualControllerViewModel"/> aufgerufen, wenn sich die Liste der aktuell
    /// angeschlossenen physischen Geraete aendert (z.B. Geraet getrennt/wieder verbunden): aktualisiert
    /// den Anzeigenamen und den "nicht verbunden"-Status dieser Zeile, ohne dass der Nutzer dafuer
    /// erneut die "Erfassen"-Funktion benutzen muss.
    /// </summary>
    public void RefreshSourceConnectionState(IReadOnlyList<PhysicalDeviceInfo> knownDevices)
    {
        SourceDisplayName = BuildSourceDisplayName(Entry, knownDevices, _getDeviceSettings(), out bool isConnected);
        IsSourceConnected = isConnected;
    }

    /// <summary>Aktualisiert <see cref="IsSourceActive"/> anhand eines frisch gepollten <see cref="DeviceState"/>
    /// eines bestimmten physischen Geraets - analog zu <see cref="PhysicalInputRowViewModel.UpdateActiveState"/>.
    /// Wird von <see cref="VirtualControllerViewModel"/> fuer jede Mapping-Zeile aufgerufen, deren
    /// <see cref="MappingEntry.SourceDeviceId"/> mit <paramref name="deviceId"/> uebereinstimmt; bei
    /// abweichender DeviceId bleibt <see cref="IsSourceActive"/> unveraendert (ein anderes, gerade
    /// gepolltes Geraet betrifft diese Zeile nicht).</summary>
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

    /// <summary>Setzt <see cref="IsSourceActive"/> zurueck auf false - wird aufgerufen, wenn das
    /// zugehoerige physische Geraet nicht (mehr) live ueberwacht wird (Geraeteliste eingeklappt,
    /// Geraet getrennt), damit keine veraltete Hervorhebung stehen bleibt.</summary>
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

    /// <summary>Baut die vollstaendige Auswahlliste fuer den modalen "Zuweisen"-Dialog auf: alle
    /// physischen Eingaben (Buttons, Achsen-Richtungen, D-Pad) der aktuell fuer diesen virtuellen
    /// Controller ausgewaehlten Geraete, mit denselben (ggf. vom Nutzer umbenannten) Anzeigenamen wie
    /// in der aufklappbaren Geraete-Eingabeliste. Im Konfigurationsdialog deaktivierte Eingaben werden -
    /// analog zu <see cref="InputCaptureService"/> beim physischen Erfassen - konsequent ausgeschlossen,
    /// da eine deaktivierte Eingabe ohnehin nie ausgewertet wird.</summary>
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

    /// <summary>Wird vom modalen "Zuweisen"-Dialog (<see cref="Views.AssignInputDialog"/>) aufgerufen,
    /// wenn der Nutzer dort eine physische Eingabe bestaetigt hat: uebernimmt sie exakt wie eine per
    /// "Erfassen" physisch ausgeloeste Eingabe (inkl. Uebernahme der geraeteweiten Achsen-Kalibrierung
    /// als Deadzone-Vorgabe), ohne dass der Nutzer die Eingabe tatsaechlich druecken/bewegen muss.</summary>
    public void AssignInput(AssignableInputOption selected)
    {
        DebugLog.Write($"[Row {RowId}] AssignInput: uebernehme '{selected.Label}' (Geraet '{selected.Device.DisplayName}') als neue physische Quelle (Zuweisen statt Erfassen).");
        ApplyCapturedInput(selected.InputRef, _getAvailableDevices());
    }

    partial void OnIsCapturingChanged(bool value) => CaptureCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTargetKindChanged(MappingTargetKind value)
    {
        DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged: neuer TargetKind={value} (Entry.TargetKind vorher={Entry.TargetKind})");
        Entry.TargetKind = value;
        Changed?.Invoke(this);

        // Die eigentliche Zuruecksetzung des Ziel-Werts (und die davon abhaengigen Aenderungen an
        // CurrentTargetOptions/IsAxisTarget) wird bewusst NICHT synchron hier ausgefuehrt: Diese
        // Aenderungen wirken sich auf das Layout der DataGrid-Zeile aus (Ziel-Wert-ComboBox tauscht
        // ihre ItemsSource, Invertieren/Deadzone werden ein-/ausgeblendet -> Zeilenhoehe aendert sich).
        // Wuerde das DataGrid dadurch die Zeile noch WAEHREND die vom Nutzer angeklickte
        // "Ziel-Typ"-ComboBox ihren eigenen SelectionChanged/Binding-Update-Vorgang verarbeitet neu
        // aufbauen, wird genau diese ComboBox mitten im Vorgang zerstoert und neu erzeugt - das
        // fuehrt dazu, dass die gerade getroffene Auswahl verworfen wird und sichtbar auf den alten
        // Wert zurueckspringt. Durch Verzoegern via Dispatcher.BeginInvoke laeuft der Auswahlvorgang
        // der ComboBox vollstaendig zu Ende, bevor sich das Zeilenlayout aendert.
        // WICHTIG: DispatcherPriority.Input statt .Background verwenden - Background liegt in der
        // WPF-Prioritaetsreihenfolge UNTER Input. Bewegt der Nutzer nach der Auswahl die Maus direkt
        // zum "Ziel-Wert"-Dropdown (der ueblichste naechste Schritt), erzeugt das fortlaufend
        // Mausbewegungs-Ereignisse mit Input-Prioritaet, die eine Background-Aktion beliebig lange
        // verhungern lassen koennen - die Ziel-Wert-ComboBox zeigt dann dauerhaft die alten Optionen
        // des vorherigen Ziel-Typs. Input-Prioritaet laeuft weiterhin garantiert NACH der aktuellen
        // Selektionsverarbeitung der Ziel-Typ-ComboBox, wird aber nicht mehr von neu eintreffenden
        // Maus-Events gleicher Prioritaet ueberholt (FIFO innerhalb derselben Prioritaetsstufe).
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged deferred BeginInvoke laeuft jetzt (setzt Ziel-Wert auf null zurueck).");

                Entry.TargetButton = null;
                Entry.TargetAxis = null;
                Entry.TargetTrigger = null;
                Entry.TargetDPadDirection = null;
                _selectedTargetValue = null;

                OnPropertyChanged(nameof(SelectedTargetValue));
                OnPropertyChanged(nameof(CurrentTargetOptions));
                OnPropertyChanged(nameof(IsAxisTarget));
                TargetDisplayName = BuildTargetDisplayName(Entry, _getLayout());

                DebugLog.Write($"[Row {RowId}] OnSelectedTargetKindChanged deferred BeginInvoke fertig: SelectedTargetValue={SelectedTargetValue} TargetDisplayName='{TargetDisplayName}'");
            }));
    }

    partial void OnInvertChanged(bool value)
    {
        Entry.Invert = value;
        Changed?.Invoke(this);
    }

    partial void OnDirectionalOnlyChanged(bool value)
    {
        Entry.DirectionalOnly = value;
        Changed?.Invoke(this);
    }

    partial void OnDeadzoneChanged(float value)
    {
        Entry.Deadzone = value;

        // DeadzoneText mit dem (ggf. programmatisch, z.B. per Kalibrierungs-Uebernahme in
        // ApplyCapturedInput, geaenderten) float-Wert synchron halten, aber Rueckkopplung ueber
        // OnDeadzoneTextChanged vermeiden (siehe _suppressDeadzoneTextSync).
        _suppressDeadzoneTextSync = true;
        DeadzoneText = value.ToString("0.####", CultureInfo.InvariantCulture);
        _suppressDeadzoneTextSync = false;

        Changed?.Invoke(this);
    }

    private bool _suppressDeadzoneTextSync;

    partial void OnDeadzoneTextChanged(string value)
    {
        if (_suppressDeadzoneTextSync)
        {
            return;
        }

        // Waehrend der Nutzer tippt, sind Zwischenzustaende wie "0." oder ein einzelnes "," normal und
        // duerfen NICHT sofort verworfen werden (siehe Doku an DeadzoneText) - deshalb wird hier nur bei
        // erfolgreichem Parsen tatsaechlich durchgeschrieben; unvollstaendiger/ungueltiger Text bleibt
        // im Feld stehen, bis er entweder vollstaendig wird oder das Feld den Fokus verliert.
        string normalized = value.Trim().Replace(',', '.');
        if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            Deadzone = parsed;
        }
    }

    /// <summary>
    /// Wird von <see cref="VirtualControllerViewModel"/> aufgerufen, wenn der Nutzer das Layout des
    /// virtuellen Controllers aendert: die gespeicherten Ziel-Werte bleiben unveraendert, aber ihre
    /// Anzeigenamen (z.B. "South" -> "A" bei Xbox bzw. "Kreuz" bei PlayStation) muessen neu ermittelt werden.
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


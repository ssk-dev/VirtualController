using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>Ziel eines einzelnen Mapping-Eintrags: worauf im virtuellen Controller die physische Eingabe wirkt.</summary>
public enum MappingTargetKind
{
    Button,
    Axis,
    Trigger,
    DPad
}

/// <summary>
/// Legt fest, wie zwischen den Modi eines virtuellen Controllers umgeschaltet wird (siehe
/// <see cref="VirtualControllerProfile.ModeSwitchMechanism"/>). Die beiden Mechanismen schliessen
/// sich pro Controller gegenseitig aus (nur einer ist zu einem Zeitpunkt aktiv), beide werden aber
/// von der Anwendung unterstuetzt und koennen ueber einen Schalter in der UI gewaehlt werden.
/// </summary>
public enum ModeSwitchMechanism
{
    /// <summary>Ein einzelner, dem Controller zugeordneter Ausloeser (<see cref="VirtualControllerProfile.ToggleTrigger"/>)
    /// schaltet bei jeder steigenden Flanke zum naechsten aktivierten Modus weiter (zyklisch, mit Umlauf).</summary>
    Toggle,

    /// <summary>Jeder Modus kann seinen eigenen Ausloeser (<see cref="ControllerMode.SwitchTrigger"/>) besitzen;
    /// eine steigende Flanke auf diesem Ausloeser aktiviert direkt genau diesen Modus. Ein Ausloeser darf
    /// innerhalb desselben Controllers nur von genau einem Modus verwendet werden.</summary>
    Switch
}

/// <summary>
/// Referenz auf eine physische Eingabe, die als Ausloeser fuer einen Moduswechsel dient (Umschalten/Toggle
/// oder direktes Aktivieren eines bestimmten Modus). Bewusst getrennt von <see cref="MappingEntry"/>, da hier
/// kein Ziel im virtuellen Controller existiert, sondern nur ein Ereignis (steigende Flanke) ausgewertet wird.
/// </summary>
public sealed class PhysicalInputTrigger
{
    public required string DeviceId { get; set; }
    public required PhysicalInputKind Kind { get; set; }
    public required int Index { get; set; }
}

/// <summary>
/// Ein einzelner Mapping-Eintrag: "Diese physische Eingabe (Button/Achse/DPad eines bestimmten
/// angeschlossenen Controllers) steuert dieses Element des virtuellen Controllers."
/// Mehrere physische Eingaben (auch von unterschiedlichen Geraeten) koennen auf dasselbe
/// virtuelle Ziel gemappt werden (z.B. zwei Controller teilen sich "Start").
/// </summary>
public sealed class MappingEntry
{
    /// <summary>Quelle: welches physische Geraet, welcher Button/welche Achse/DPad-Index.</summary>
    public required string SourceDeviceId { get; set; }
    public required PhysicalInputKind SourceKind { get; set; }
    public required int SourceIndex { get; set; }

    /// <summary>Ziel: welches Element des virtuellen Controllers angesteuert wird.</summary>
    public required MappingTargetKind TargetKind { get; set; }
    public VirtualButton? TargetButton { get; set; }
    public VirtualAxis? TargetAxis { get; set; }
    public VirtualTrigger? TargetTrigger { get; set; }
    public DPadDirection? TargetDPadDirection { get; set; }

    /// <summary>Bei Achsen: Ausschlagsrichtung invertieren.</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Bei Achsen-Zielen: Wenn true, wird nur die durch <see cref="SourceKind"/> festgelegte Haelfte
    /// der physischen Quellachse (z.B. nur "Y+") isoliert auf 0..1 normalisiert und als Ziel-Achsenwert
    /// verwendet (Vorzeichen weiterhin unabhaengig ueber <see cref="Invert"/> steuerbar) - ermoeglicht
    /// z.B. "physische Achse Y+ -&gt; virtuelle Achse X-", also ein Aufteilen zweier unabhaengiger
    /// physischer Achsenhaelften auf dieselbe oder unterschiedliche virtuelle Achsen. Wenn false
    /// (Standard, auch fuer alte Profile ohne dieses Feld), wird wie bisher der volle bidirektionale
    /// Bereich der Quellachse durchgereicht.
    /// </summary>
    public bool DirectionalOnly { get; set; }

    /// <summary>Bei Achsen: Werte innerhalb dieses Radius um 0 werden als 0 behandelt (0.0 .. 1.0).
    /// Vorgabewert entspricht <see cref="Devices.DeviceSettingsExtensions.DefaultAxisDeadzoneWithoutCalibration"/>
    /// (Rueckfall ohne Kalibrierung); beim Erfassen/Zuweisen einer physischen Achse wird dieser Wert in der
    /// App-Schicht nach Moeglichkeit durch die geraeteweite Kalibrierung ueberschrieben.</summary>
    public float Deadzone { get; set; } = 0.025f;

    /// <summary>Anzeigename fuer die UI-Tabelle, z.B. "Controller 1 - Button A" -> "South".</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Ein einzelner, vom Nutzer frei benannter Modus eines virtuellen Controllers (z.B. "Flugmodus",
/// "Rennen"), mit eigener, unabhaengiger Mapping-Tabelle. Ein virtueller Controller kann mehrere Modi
/// besitzen, von denen zu jedem Zeitpunkt genau einer aktiv ist (<see cref="VirtualControllerProfile.ActiveModeId"/>).
/// Ein Modus muss zunaechst angelegt werden, bevor ihm Mapping-Eintraege hinzugefuegt werden koennen.
/// </summary>
public sealed class ControllerMode
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Nur aktivierte Modi koennen ueberhaupt aktiv werden (siehe Toggle-/Switch-Mechanismus in
    /// <see cref="VirtualControllerProfile"/>); ein deaktivierter Modus wird beim Umschalten uebersprungen.</summary>
    public bool Enabled { get; set; } = true;

    public List<MappingEntry> Mappings { get; set; } = new();

    /// <summary>Nur relevant, wenn <see cref="VirtualControllerProfile.ModeSwitchMechanism"/> auf
    /// <see cref="Mapping.ModeSwitchMechanism.Switch"/> steht: physische Eingabe, deren steigende Flanke
    /// diesen Modus direkt aktiviert. Muss innerhalb desselben Controllers eindeutig sein (wird beim
    /// Zuweisen gegen die uebrigen Modi geprueft).</summary>
    public PhysicalInputTrigger? SwitchTrigger { get; set; }
}

/// <summary>
/// Ein einzelner virtueller Controller inklusive seiner kompletten Mapping-Tabelle
/// (Liste der physischen Controller/Eingaben, die auf ihn wirken).
/// </summary>
public sealed class VirtualControllerProfile
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required ControllerLayout Layout { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Zielabtastrate des Polling-/Submit-Loops in Hz (z.B. 1000).</summary>
    public int PollingRateHz { get; set; } = 1000;

    /// <summary>Alle Modi dieses virtuellen Controllers (siehe <see cref="ControllerMode"/>). Ein neu
    /// angelegter Controller startet ohne Modus - der Nutzer muss zunaechst einen Modus anlegen, bevor
    /// Mapping-Eintraege erfasst/zugewiesen werden koennen.</summary>
    public List<ControllerMode> Modes { get; set; } = new();

    /// <summary>Id des aktuell aktiven Modus (siehe <see cref="ActiveMode"/>). Null, solange noch kein
    /// Modus angelegt oder aktiviert wurde.</summary>
    public Guid? ActiveModeId { get; set; }

    /// <summary>Legt fest, ueber welchen Mechanismus zwischen den Modi umgeschaltet wird - siehe
    /// <see cref="Mapping.ModeSwitchMechanism"/>.</summary>
    public ModeSwitchMechanism ModeSwitchMechanism { get; set; } = ModeSwitchMechanism.Toggle;

    /// <summary>Nur relevant bei <see cref="ModeSwitchMechanism.Toggle"/>: einzelner, dem gesamten
    /// Controller zugeordneter Ausloeser, der bei jeder steigenden Flanke zum naechsten aktivierten
    /// Modus weiterschaltet (zyklisch, mit Umlauf).</summary>
    public PhysicalInputTrigger? ToggleTrigger { get; set; }

    /// <summary>Ob bei jedem tatsaechlichen Wechsel des aktiven Modus (manuell per Tab-Klick waehrend
    /// der Controller gestoppt ist, oder per Toggle-/Switch-Trigger waehrend der Controller laeuft) eine
    /// kurze Bildschirmbenachrichtigung mit Controller- und Modusnamen angezeigt werden soll.</summary>
    public bool NotifyOnModeChange { get; set; }

    /// <summary>Ob die diesem Controller aktuell zugeordneten physischen Geraete (siehe
    /// <see cref="Engine.ControllerSession.NeededDeviceIds"/>) automatisch per HidHide (siehe
    /// <see cref="Devices.HidHideController"/>) fuer alle anderen Anwendungen gesperrt werden sollen, solange
    /// dieser virtuelle Controller laeuft - damit z.B. ein Spiel nicht gleichzeitig auf das physische UND das
    /// davon abgeleitete virtuelle Geraet reagiert. Opt-in (Standard: deaktiviert), da HidHide ein separat zu
    /// installierender Treiber ist. Ohne Wirkung, solange HidHide nicht installiert/betriebsbereit ist (siehe
    /// <see cref="Devices.HidHideController.IsAvailable"/>).</summary>
    public bool HidHideEnabled { get; set; }

    /// <summary>Der aktuell aktive Modus, oder null, falls noch keiner angelegt/aktiviert wurde bzw.
    /// die hinterlegte <see cref="ActiveModeId"/> auf keinen (mehr) vorhandenen Modus verweist.</summary>
    public ControllerMode? ActiveMode => ActiveModeId is { } id ? Modes.FirstOrDefault(m => m.Id == id) : null;

    /// <summary>
    /// Physische Geraete (per <see cref="Devices.PhysicalDeviceInfo.DeviceId"/>), die diesem virtuellen
    /// Controller zugeordnet sind. Wird verwendet, um die "Erfassen"-Funktion und das aktive Mapping auf
    /// diese Geraete zu beschraenken. Leer = alle angeschlossenen Geraete werden beruecksichtigt (Standard).
    /// </summary>
    public List<string> AssignedDeviceIds { get; set; } = new();

    /// <summary>Das tatsaechliche ViGEmBus-Backend, abgeleitet aus dem Layout.</summary>
    public VirtualBackend Backend => LayoutBackendMap.Resolve(Layout);
}

/// <summary>Die gesamte gespeicherte Konfiguration: alle definierten virtuellen Controller.</summary>
public sealed class AppProfile
{
    public List<VirtualControllerProfile> Controllers { get; set; } = new();

    /// <summary>
    /// Vom Nutzer vergebene Anzeigenamen fuer einzelne physische Eingaben (z.B. "Sniper-Taste"
    /// statt "Button 4"), Key-Format "{DeviceId}|{PhysicalInputKind}|{Index}". Geraeteuebergreifend
    /// gueltig, unabhaengig vom virtuellen Controller, dem das Geraet aktuell zugeordnet ist.
    /// Veraltet: wird nur noch zum Laden alter Profile benutzt und beim Laden automatisch nach
    /// <see cref="DeviceSettings"/> migriert (siehe <see cref="Profiles.ProfileStore.Load"/>).
    /// </summary>
    public Dictionary<string, string> CustomInputNames { get; set; } = new();

    /// <summary>
    /// Einstellungen je physischem Geraet (Enable/Disable, sowie je physischer Eingabe: Umbenennung,
    /// Enable/Disable, Kalibrierung, Deadzone, Antwortkurve), Key = <see cref="PhysicalDeviceInfo.DeviceId"/>.
    /// Geraeteuebergreifend gueltig, unabhaengig vom virtuellen Controller, dem das Geraet aktuell
    /// zugeordnet ist.
    /// </summary>
    public Dictionary<string, DeviceSettings> DeviceSettings { get; set; } = new();

    /// <summary>Ob neu angeschlossene/getrennte physische Geraete automatisch (per Hintergrund-Polling,
    /// siehe MainViewModel) erkannt werden sollen, ohne dass die App neu gestartet oder "Geraete
    /// aktualisieren" manuell geklickt werden muss. Kann im "Einstellungen"-Tab deaktiviert werden, falls
    /// der dafuer notwendige periodische Geraete-Scan unerwuenscht ist (z.B. um jegliche zusaetzliche
    /// Hintergrundlast zu vermeiden). Standard: aktiviert.</summary>
    public bool AutoDeviceDetectionEnabled { get; set; } = true;
}

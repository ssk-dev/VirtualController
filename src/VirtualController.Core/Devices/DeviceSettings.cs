using System.Text.Json.Serialization;

namespace VirtualController.Core.Devices;

/// <summary>Antwortkurve, mit der ein kalibrierter Achsenwert (-1.0 .. 1.0 bzw. 0.0 .. 1.0) vor der
/// Ausgabe transformiert wird. Beeinflusst nur die "Feinfuehligkeit" entlang des Ausschlags, nicht
/// den kalibrierten Wertebereich selbst (siehe <see cref="InputSettings.CalibratedMin"/> etc.).</summary>
public enum AxisCurveType
{
    /// <summary>Unveraendert, 1:1-Uebertragung des kalibrierten Werts (Standard).</summary>
    Linear,

    /// <summary>output = sign(x) * |x|^Exponent. Exponent &gt; 1 macht die Mitte unempfindlicher
    /// (feinfuehliger nahe 0, steiler an den Extremen); Exponent &lt; 1 macht die Mitte empfindlicher.</summary>
    Exponential,

    /// <summary>Sanfter S-foermiger Uebergang um die Mitte (kubische Hermite-Interpolation),
    /// unempfindlicher nahe 0, dafuer steiler kurz vor den Extremen.</summary>
    SCurve
}

/// <summary>
/// Einstellungen fuer ein einzelnes physisches Eingabeelement (Button, Achsen-Richtung, Slider,
/// D-Pad-Richtung) eines konkreten Geraets. Key im uebergeordneten <see cref="DeviceSettings.Inputs"/>
/// ist <see cref="PhysicalInputCatalog.BuildStorageKey"/>, damit die Einstellungen unabhaengig davon
/// gueltig bleiben, welchem virtuellen Controller das Geraet aktuell zugeordnet ist.
/// </summary>
public sealed class InputSettings
{
    /// <summary>Vom Nutzer vergebener Anzeigename, ersetzt den Standardnamen aus <see cref="PhysicalInputCatalog"/>.
    /// Null/leer = Standardname wird verwendet.</summary>
    public string? CustomName { get; set; }

    /// <summary>Wenn false, wird diese Eingabe von der automatischen Erfassung (<see cref="InputCaptureService"/>)
    /// und der Mapping-Auswertung (<see cref="Mapping.MappingEngine"/>) vollstaendig ignoriert, bleibt aber
    /// im Konfigurationsdialog sichtbar (ausgegraut) fuer Umbenennung/Kalibrierung.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nur fuer Achsen/Slider: per Kalibrierungs-Assistent ermittelter minimaler Rohwert.
    /// Null = keine Kalibrierung vorgenommen, es gilt der vom jeweiligen <see cref="IDeviceReader"/>
    /// bereits normalisierte Standardbereich.</summary>
    public float? CalibratedMin { get; set; }

    /// <summary>Nur fuer Achsen/Slider: per Kalibrierungs-Assistent ermittelter maximaler Rohwert.</summary>
    public float? CalibratedMax { get; set; }

    /// <summary>Nur fuer zentrierte Achsen (Sticks): per Kalibrierungs-Assistent ermittelte Ruheposition
    /// (Mittelpunkt), falls das physische Zentrum nicht exakt bei 0 liegt (Bauteiltoleranz).</summary>
    public float? CalibratedCenter { get; set; }

    /// <summary>Nur fuer Achsen/Slider: Ausschlag innerhalb dieses Radius um die Mitte wird als 0 behandelt
    /// (0.0 .. 1.0, geraeteweit, zusaetzlich zur ggf. mapping-spezifischen <see cref="Mapping.MappingEntry.Deadzone"/>).
    /// Kann manuell gesetzt oder per Deadzone-Kalibrierung (Stickdrift-Erkennung) automatisch ermittelt werden.</summary>
    public float Deadzone { get; set; }

    /// <summary>Nur fuer Achsen/Slider: Antwortkurve, die auf den kalibrierten, deadzone-bereinigten Wert
    /// angewendet wird, bevor er an das Mapping weitergegeben wird.</summary>
    public AxisCurveType CurveType { get; set; } = AxisCurveType.Linear;

    /// <summary>Exponent fuer <see cref="AxisCurveType.Exponential"/> (typisch 1.5 .. 3.0) bzw. Staerke
    /// der S-Kurve bei <see cref="AxisCurveType.SCurve"/> (typisch 1.0 .. 3.0). Bei <see cref="AxisCurveType.Linear"/>
    /// ohne Wirkung.</summary>
    public float CurveStrength { get; set; } = 1f;
}

/// <summary>
/// Einstellungen fuer ein komplettes physisches Geraet, unabhaengig davon, welchem virtuellen
/// Controller es aktuell zugeordnet ist. Key im uebergeordneten <see cref="Mapping.AppProfile.DeviceSettings"/>
/// ist die <see cref="PhysicalDeviceInfo.DeviceId"/>.
/// </summary>
public sealed class DeviceSettings
{
    /// <summary>Wenn false, taucht dieses Geraet in keiner Geraeteauswahl fuer virtuelle Controller mehr auf
    /// (wird von der App-Schicht beim Aufbau der Auswahllisten entsprechend gefiltert), unabhaengig vom
    /// aktuellen Verbindungsstatus.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Wenn true, wird dieses Geraet vom Nutzer manuell komplett ausgeblendet: es verschwindet
    /// sowohl aus der Hauptliste des "Gerätekonfiguration"-Tabs (landet dort in einer separaten, einklappbaren
    /// Liste ausgeblendeter Geraete zum spaeteren Wiedereinblenden) als auch aus der Geraeteauswahl fuer
    /// virtuelle Controller (wie <see cref="Enabled"/> = false). Unabhaengig von <see cref="Enabled"/>, damit
    /// der zuvor gewaehlte Enabled-Zustand beim erneuten Einblenden erhalten bleibt. Gedacht fuer Geraete, die
    /// dauerhaft nicht relevant sind (z.B. die eigenen, per ViGEmBus emulierten virtuellen Controller dieser
    /// Anwendung, die von XInput/DirectInput nicht von echter Hardware unterschieden werden koennen).</summary>
    public bool Hidden { get; set; }

    /// <summary>Zuletzt bekannter Anzeigename dieses Geraets (aktualisiert bei jeder erfolgreichen
    /// Geraete-Erkennung). Wird verwendet, um in der Mapping-Tabelle einen sinnvollen Namen statt der
    /// bedeutungslosen, rohen <see cref="PhysicalDeviceInfo.DeviceId"/> anzuzeigen, wenn das Geraet
    /// aktuell nicht angeschlossen ist (z.B. beim Laden eines Profils).</summary>
    public string? LastKnownDisplayName { get; set; }

    /// <summary>Zuletzt bekannte Geraete-Fähigkeiten (API, Slot, Buttons, POV, Achsen), aktualisiert bei
    /// jeder erfolgreichen Geraete-Erkennung (siehe <see cref="MainViewModel.RefreshDevices"/>). Ermoeglicht
    /// die Rekonstruktion eines synthetischen, "getrennten" <see cref="PhysicalDeviceInfo"/> fuer den
    /// Geraetekonfiguration-Tab, damit dort auch aktuell nicht angeschlossene, aber bereits bekannte
    /// Geraete weiterhin sichtbar und konfigurierbar bleiben (siehe <see cref="MainViewModel.GetAllKnownDevices"/>).
    /// <see cref="LastKnownButtonCount"/> dient dabei als Marker, ob ueberhaupt schon einmal vollstaendige
    /// Faehigkeiten erfasst wurden - fehlt er, wird das Geraet nicht als "bekannt, aber getrennt" gefuehrt.</summary>
    public InputApi? LastKnownApi { get; set; }

    public int? LastKnownApiSlot { get; set; }

    public int? LastKnownButtonCount { get; set; }

    public bool? LastKnownHasPov { get; set; }

    public List<PhysicalAxisId>? LastKnownAvailableAxes { get; set; }

    /// <summary>Einstellungen je physischem Eingabeelement, Key = <see cref="PhysicalInputCatalog.BuildStorageKey"/>.
    /// Die Serialisierung blendet fuer rein digitale Eingaben (Buttons, D-Pad-Richtungen) die nur fuer
    /// Achsen/Slider relevanten Felder (Kalibrierung, Deadzone, Antwortkurve) aus - siehe
    /// <see cref="InputSettingsDictionaryConverter"/>.</summary>
    [JsonConverter(typeof(InputSettingsDictionaryConverter))]
    public Dictionary<string, InputSettings> Inputs { get; set; } = new();

    /// <summary>Vom Nutzer vergebene Anzeigenamen fuer kombinierte 2D-Sticks (z.B. "Linker Stick" -> "Flugstick"),
    /// Key = <see cref="PhysicalInputCatalog.BuildStickStorageKey"/> (Achsen-Index der X-Achse dieses Sticks).
    /// Getrennt von <see cref="InputSettings.CustomName"/>, da ein Stick kein eigenes physisches
    /// Eingabeelement, sondern eine reine Buendelung zweier Achsen im Konfigurationsdialog ist
    /// (siehe <see cref="DeviceConfigStickGroupViewModel"/>).</summary>
    public Dictionary<string, string> StickNames { get; set; } = new();
}

/// <summary>
/// Erweiterungsmethoden fuer die Auswertung von <see cref="DeviceSettings"/>, die sowohl von
/// <see cref="Mapping.MappingEngine"/> (laufende Mapping-Auswertung) als auch von
/// <see cref="InputCaptureService"/> (\"Erfassen\"-Funktion) benoetigt werden, um eine vom Nutzer
/// im Konfigurationsdialog deaktivierte physische Eingabe konsistent an beiden Stellen zu ignorieren.
/// </summary>
public static class DeviceSettingsExtensions
{
    /// <summary>Prueft, ob eine physische Eingabe eines Geraets aktiviert ist. Fehlende Eintraege
    /// (Geraet oder Eingabe noch nie konfiguriert) gelten als aktiviert (Standardverhalten), damit
    /// bestehende Profile ohne <see cref="DeviceSettings"/> unveraendert weiterlaufen.</summary>
    public static bool IsInputEnabled(
        this IReadOnlyDictionary<string, DeviceSettings>? deviceSettings,
        string deviceId,
        PhysicalInputKind kind,
        int index)
    {
        if (deviceSettings is null || !deviceSettings.TryGetValue(deviceId, out var settings))
        {
            return true;
        }

        var key = PhysicalInputCatalog.BuildStorageKey(deviceId, kind, index);
        return !settings.Inputs.TryGetValue(key, out var inputSettings) || inputSettings.Enabled;
    }

    /// <summary>Liefert die <see cref="InputSettings"/> einer physischen Eingabe (Kalibrierung, Deadzone,
    /// Antwortkurve, Umbenennung, Enabled), oder null, falls das Geraet bzw. diese Eingabe noch nie
    /// konfiguriert wurde. Wird von <see cref="Mapping.MappingEngine"/> genutzt, um Kalibrierung und
    /// Antwortkurve auf einen Achsen-Rohwert anzuwenden, bevor er in die Mapping-Auswertung einfliesst.</summary>
    public static InputSettings? TryGetInputSettings(
        this IReadOnlyDictionary<string, DeviceSettings>? deviceSettings,
        string deviceId,
        PhysicalInputKind kind,
        int index)
    {
        if (deviceSettings is null || !deviceSettings.TryGetValue(deviceId, out var settings))
        {
            return null;
        }

        var key = PhysicalInputCatalog.BuildStorageKey(deviceId, kind, index);
        return settings.Inputs.TryGetValue(key, out var inputSettings) ? inputSettings : null;
    }

    /// <summary>Rueckfallwert fuer <see cref="Mapping.MappingEntry.Deadzone"/>, wenn fuer die neu gemappte
    /// physische Achse noch keine geraeteweite Kalibrierung (<see cref="InputSettings.Deadzone"/>) vorliegt.</summary>
    public const float DefaultAxisDeadzoneWithoutCalibration = 0.025f;

    /// <summary>Ermittelt den sinnvollen Vorgabewert fuer <see cref="Mapping.MappingEntry.Deadzone"/>, wenn
    /// der Nutzer eine physische Achse neu erfasst/zuweist ("Erfassen"/"Zuweisen"): Existiert bereits eine
    /// geraeteweite Kalibrierung (<see cref="InputSettings.Deadzone"/>) fuer diese Achse, wird deren Wert
    /// uebernommen - Kalibrierung und Mapping sollen dann konsistent sein, ohne dass der Nutzer denselben
    /// Wert zweimal pflegen muss. Andernfalls wird <see cref="DefaultAxisDeadzoneWithoutCalibration"/> verwendet.
    /// Nutzt wie <see cref="Mapping.MappingEngine.ApplyAxisSource"/> stets den kanonischen AxisPositive-Schluessel
    /// derselben Achsen-Nummer, unabhaengig davon, ob die erfasste Richtung positiv oder negativ ist.</summary>
    public static float ResolveDefaultAxisDeadzone(
        this IReadOnlyDictionary<string, DeviceSettings>? deviceSettings,
        string deviceId,
        int axisIndex)
    {
        var axisSettings = deviceSettings.TryGetInputSettings(deviceId, PhysicalInputKind.AxisPositive, axisIndex);
        return axisSettings?.Deadzone ?? DefaultAxisDeadzoneWithoutCalibration;
    }

    /// <summary>Liefert die <see cref="InputSettings"/> einer physischen Eingabe eines konkreten Geraets
    /// und legt sie bei Bedarf neu an (im Gegensatz zu <see cref="TryGetInputSettings"/>, das bei fehlendem
    /// Eintrag null liefert). Wird u.a. von der Geraete-Konfiguration genutzt, um sicherzustellen, dass jede
    /// im Katalog gelistete Eingabe (inkl. Achsen fuer die Live-Visualisierung) stets eine persistierbare
    /// <see cref="InputSettings"/>-Instanz besitzt, sobald der Konfigurationsdialog sie erstmals anzeigt.</summary>
    public static InputSettings GetOrCreateInputSettings(
        this DeviceSettings settings,
        string deviceId,
        PhysicalInputKind kind,
        int index)
    {
        var key = PhysicalInputCatalog.BuildStorageKey(deviceId, kind, index);
        if (!settings.Inputs.TryGetValue(key, out var inputSettings))
        {
            inputSettings = new InputSettings();
            settings.Inputs[key] = inputSettings;
        }

        return inputSettings;
    }
}

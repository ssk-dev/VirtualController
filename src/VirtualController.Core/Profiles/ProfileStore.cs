using System.Text.Json;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Speichert und laedt die komplette Konfiguration (alle virtuellen Controller + Mapping-Tabellen +
/// Geraete-Einstellungen), aufgeteilt in mehrere, sprechend benannte Dateien statt einer einzigen
/// profiles.json:
/// <list type="bullet">
/// <item><description>Ein virtueller Controller je Datei: "Controllers\controller-{name}.json"
/// (siehe <see cref="ControllerStore"/>).</description></item>
/// <item><description>Ein physisches Geraet je Datei: "Devices\device-{marke}-{name}.json"
/// (siehe <see cref="DeviceSettingsStore"/>).</description></item>
/// <item><description>Allgemeine Einstellungen in einer einzigen kleinen "settings.json"
/// (siehe <see cref="SettingsStore"/>).</description></item>
/// </list>
/// Jede Einzeldatei wird von ihrem jeweiligen Teilspeicher weiterhin atomar geschrieben (siehe
/// <see cref="AtomicJsonWriter"/>), damit ein Absturz oder Stromausfall waehrend des Schreibens niemals
/// eine bereits vorhandene, gueltige Datei beschaedigt. Existiert noch eine alte, kombinierte
/// profiles.json aus einer fruehen Version dieser App, wird sie beim ersten <see cref="Load"/> einmalig
/// automatisch in dieses neue Format aufgeteilt (siehe <see cref="LoadLegacyAndMigrate"/>).
/// </summary>
public static class ProfileStore
{
    /// <summary>Basisordner aller Profildateien, standardmaessig %AppData%\VirtualController. Ueber den
    /// optionalen Parameter von <see cref="Load"/>/<see cref="Save"/> ueberschreibbar, z.B. fuer Tests.</summary>
    public static string BaseDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualController");

    private static string ControllersDirectory(string baseDirectory) => Path.Combine(baseDirectory, "Controllers");

    private static string DevicesDirectory(string baseDirectory) => Path.Combine(baseDirectory, "Devices");

    private static string SettingsFilePath(string baseDirectory) => Path.Combine(baseDirectory, "settings.json");

    /// <summary>Pfad der alten, kombinierten Profildatei aus fruehen Versionen dieser App - wird nur noch
    /// fuer die einmalige Migration nach dem Aufteilen in mehrere Dateien benoetigt (siehe
    /// <see cref="LoadLegacyAndMigrate"/>).</summary>
    private static string LegacyFilePath(string baseDirectory) => Path.Combine(baseDirectory, "profiles.json");

    public static AppProfile Load(string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? BaseDirectory;
        var legacyPath = LegacyFilePath(baseDir);

        if (File.Exists(legacyPath) && !HasAnySplitFiles(baseDir))
        {
            return LoadLegacyAndMigrate(legacyPath, baseDir);
        }

        var profile = new AppProfile
        {
            Controllers = ControllerStore.LoadAll(ControllersDirectory(baseDir)),
            DeviceSettings = DeviceSettingsStore.LoadAll(DevicesDirectory(baseDir))
        };

        var settings = SettingsStore.Load(SettingsFilePath(baseDir));
        profile.AutoDeviceDetectionEnabled = settings.AutoDeviceDetectionEnabled;
        profile.CustomInputNames = settings.CustomInputNames;

        MigrateLegacyCustomInputNames(profile);

        return profile;
    }

    /// <summary>Ob bereits mindestens eine der neuen, aufgeteilten Dateien existiert - dann gilt diese
    /// Installation als bereits migriert und eine evtl. noch vorhandene alte profiles.json wird
    /// ignoriert (verhindert, dass laengst geloeschte Controller/Geraete durch eine erneute Migration
    /// wieder auftauchen).</summary>
    private static bool HasAnySplitFiles(string baseDirectory)
    {
        var controllersDir = ControllersDirectory(baseDirectory);
        if (Directory.Exists(controllersDir) && Directory.EnumerateFiles(controllersDir, "controller-*.json").Any())
        {
            return true;
        }

        var devicesDir = DevicesDirectory(baseDirectory);
        if (Directory.Exists(devicesDir) && Directory.EnumerateFiles(devicesDir, "device-*.json").Any())
        {
            return true;
        }

        return File.Exists(SettingsFilePath(baseDirectory));
    }

    /// <summary>Liest eine alte, kombinierte profiles.json (inkl. aller bisherigen Legacy-Migrationen),
    /// speichert das Ergebnis einmalig im neuen, aufgeteilten Format und benennt die alte Datei zu
    /// "profiles.json.migrated" um (statt sie zu loeschen, als Sicherheitsnetz), damit sie beim naechsten
    /// <see cref="Load"/> nicht erneut als Migrationsquelle erkannt wird.</summary>
    private static AppProfile LoadLegacyAndMigrate(string legacyPath, string baseDirectory)
    {
        var jsonBytes = File.ReadAllBytes(legacyPath);
        var legacyOptions = ProfileJsonOptions.Create();

        // Muss vor der typisierten Deserialisierung ausgewertet werden: aeltere Profile speicherten die
        // Mapping-Tabelle als flaches "Mappings"-Array direkt am Controller-Objekt - diese Eigenschaft
        // existiert seit Einfuehrung der Modi nicht mehr auf VirtualControllerProfile, wuerde also von
        // JsonSerializer.Deserialize<AppProfile> stillschweigend verworfen (unbekannte Property).
        var legacyMappingsByControllerId = ExtractLegacyMappingsByControllerId(jsonBytes, legacyOptions);

        var loaded = JsonSerializer.Deserialize<AppProfile>(jsonBytes, legacyOptions);
        var profile = loaded ?? new AppProfile();

        MigrateLegacyDPadMappings(legacyMappingsByControllerId);
        MigrateLegacyModes(profile, legacyMappingsByControllerId);
        MigrateLegacyCustomInputNames(profile);

        Save(profile, baseDirectory);

        var migratedPath = legacyPath + ".migrated";
        try
        {
            File.Delete(migratedPath); // Falls von einem vorherigen, abgebrochenen Migrationsversuch uebrig.
            File.Move(legacyPath, migratedPath);
        }
        catch (IOException)
        {
            // Umbenennen fehlgeschlagen (z.B. Datei gesperrt) -> unkritisch, HasAnySplitFiles verhindert
            // beim naechsten Load ohnehin eine erneute Migration.
        }

        return profile;
    }

    /// <summary>
    /// Liest aus dem rohen JSON die veraltete, flache "Mappings"-Liste jedes Controllers aus (Key = dessen
    /// <see cref="VirtualControllerProfile.Id"/>), bevor diese durch die typisierte Deserialisierung
    /// verloren geht. Liefert nur Eintraege fuer Controller, die tatsaechlich ein solches Legacy-Array
    /// besitzen (neuere Profile ohne dieses Feld liefern hier nichts).
    /// </summary>
    private static Dictionary<Guid, List<MappingEntry>> ExtractLegacyMappingsByControllerId(
        byte[] jsonBytes, JsonSerializerOptions options)
    {
        var result = new Dictionary<Guid, List<MappingEntry>>();

        using var document = JsonDocument.Parse(jsonBytes);
        if (!document.RootElement.TryGetProperty(nameof(AppProfile.Controllers), out var controllersElement)
            || controllersElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var controllerElement in controllersElement.EnumerateArray())
        {
            if (!controllerElement.TryGetProperty(nameof(VirtualControllerProfile.Id), out var idElement)
                || !idElement.TryGetGuid(out var controllerId)
                || !controllerElement.TryGetProperty("Mappings", out var mappingsElement)
                || mappingsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var mappings = mappingsElement.Deserialize<List<MappingEntry>>(options);
            if (mappings is { Count: > 0 })
            {
                result[controllerId] = mappings;
            }
        }

        return result;
    }

    /// <summary>
    /// Ueberfuehrt fuer jeden Controller, der noch keine Modi besitzt, aber ueber
    /// <paramref name="legacyMappingsByControllerId"/> eine alte, flache Mapping-Liste mitbringt, diese in
    /// einen einzigen, automatisch angelegten "Standard"-Modus - so bleiben bereits vorhandene Zuordnungen
    /// nach dem Umstieg auf mehrere Modi pro Controller erhalten und sind sofort aktiv.
    /// </summary>
    private static void MigrateLegacyModes(AppProfile profile, Dictionary<Guid, List<MappingEntry>> legacyMappingsByControllerId)
    {
        foreach (var controller in profile.Controllers)
        {
            if (controller.Modes.Count > 0)
            {
                continue; // Bereits im neuen Format gespeichert -> nichts zu tun.
            }

            legacyMappingsByControllerId.TryGetValue(controller.Id, out var legacyMappings);

            var standardMode = new ControllerMode
            {
                Id = Guid.NewGuid(),
                Name = "Standard",
                Mappings = legacyMappings ?? new List<MappingEntry>()
            };

            controller.Modes.Add(standardMode);
            controller.ActiveModeId = standardMode.Id;
        }
    }

    /// <summary>
    /// Wandelt Mapping-Eintraege mit dem veralteten, kombinierten <see cref="PhysicalInputKind.DPad"/>
    /// (aus Profilen, die vor der Aufteilung des D-Pads in ein echtes 4-Wege-Kreuz gespeichert wurden)
    /// in vier gleichwertige Eintraege um - je einen pro Richtung (Hoch/Runter/Links/Rechts). Das
    /// entspricht exakt dem alten Laufzeitverhalten: die alte Logik reagierte bei Button-/Trigger-Zielen
    /// auf jede POV-Bewegung und gab bei DPad-Zielen die kombinierte Richtung direkt weiter - vier
    /// unabhaengige Digital-Eintraege mit demselben Ziel bilden das 1:1 nach, auch bei Diagonalen
    /// (dort werden einfach zwei der vier Eintraege gleichzeitig aktiv).
    /// </summary>
    private static void MigrateLegacyDPadMappings(Dictionary<Guid, List<MappingEntry>> legacyMappingsByControllerId)
    {
        foreach (var mappings in legacyMappingsByControllerId.Values)
        {
            var legacyEntries = mappings
                .Where(m => m.SourceKind == PhysicalInputKind.DPad)
                .ToList();

            if (legacyEntries.Count == 0)
            {
                continue;
            }

            foreach (var legacy in legacyEntries)
            {
                int insertIndex = mappings.IndexOf(legacy);
                mappings.RemoveAt(insertIndex);
                mappings.InsertRange(insertIndex, BuildDirectionalReplacements(legacy));
            }
        }
    }

    private static IEnumerable<MappingEntry> BuildDirectionalReplacements(MappingEntry legacy)
    {
        (PhysicalInputKind Kind, int Index)[] directions =
        {
            (PhysicalInputKind.DPadUp, 0),
            (PhysicalInputKind.DPadDown, 1),
            (PhysicalInputKind.DPadLeft, 2),
            (PhysicalInputKind.DPadRight, 3)
        };

        foreach (var (kind, index) in directions)
        {
            yield return new MappingEntry
            {
                SourceDeviceId = legacy.SourceDeviceId,
                SourceKind = kind,
                SourceIndex = index,
                TargetKind = legacy.TargetKind,
                TargetButton = legacy.TargetButton,
                TargetAxis = legacy.TargetAxis,
                TargetTrigger = legacy.TargetTrigger,
                TargetDPadDirection = legacy.TargetDPadDirection,
                Invert = legacy.Invert,
                Deadzone = legacy.Deadzone,
                Description = legacy.Description
            };
        }
    }

    /// <summary>
    /// Ueberfuehrt die veraltete, flache <see cref="AppProfile.CustomInputNames"/>-Liste (vor der
    /// Einfuehrung von <see cref="Devices.DeviceSettings"/> die einzige Persistenz fuer benutzerdefinierte
    /// Eingabenamen) in die neue, reichhaltigere Struktur, damit bereits vergebene Namen nach dem Umstieg
    /// erhalten bleiben. Bereits in <see cref="AppProfile.DeviceSettings"/> vorhandene Namen haben Vorrang
    /// vor der Legacy-Liste, falls beide (theoretisch) denselben Eintrag beschreiben.
    /// </summary>
    private static void MigrateLegacyCustomInputNames(AppProfile profile)
    {
        foreach (var (key, customName) in profile.CustomInputNames)
        {
            if (!PhysicalInputCatalog.TryParseStorageKey(key, out var deviceId, out _, out _))
            {
                continue; // Unerwartetes/fehlerhaftes Key-Format -> Eintrag einfach ignorieren statt zu werfen.
            }

            if (!profile.DeviceSettings.TryGetValue(deviceId, out var deviceSettings))
            {
                deviceSettings = new DeviceSettings();
                profile.DeviceSettings[deviceId] = deviceSettings;
            }

            if (!deviceSettings.Inputs.TryGetValue(key, out var inputSettings))
            {
                inputSettings = new InputSettings();
                deviceSettings.Inputs[key] = inputSettings;
            }

            inputSettings.CustomName ??= customName;
        }
    }

    public static void Save(AppProfile profile, string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? BaseDirectory;

        ControllerStore.SaveAll(profile.Controllers, ControllersDirectory(baseDir));
        DeviceSettingsStore.SaveAll(profile.DeviceSettings, DevicesDirectory(baseDir));
        SettingsStore.Save(
            new AppSettings
            {
                AutoDeviceDetectionEnabled = profile.AutoDeviceDetectionEnabled,
                CustomInputNames = profile.CustomInputNames
            },
            SettingsFilePath(baseDir));
    }
}


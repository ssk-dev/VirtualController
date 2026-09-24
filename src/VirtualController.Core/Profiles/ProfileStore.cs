using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Speichert und laedt die komplette Konfiguration (alle virtuellen Controller + Mapping-Tabellen)
/// als lesbare JSON-Datei, standardmaessig unter %AppData%\VirtualController\profiles.json.
/// Save() schreibt atomar (ueber eine temporaere Datei + Replace), damit ein Absturz oder
/// Stromausfall waehrend des Schreibens niemals eine bereits vorhandene, gueltige Profildatei
/// beschaedigt.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VirtualController", "profiles.json");

    public static AppProfile Load(string? filePath = null)
    {
        var targetPath = filePath ?? DefaultFilePath;

        if (!File.Exists(targetPath))
        {
            return new AppProfile();
        }

        var jsonBytes = File.ReadAllBytes(targetPath);

        // Muss vor der typisierten Deserialisierung ausgewertet werden: aeltere Profile speicherten die
        // Mapping-Tabelle als flaches "Mappings"-Array direkt am Controller-Objekt - diese Eigenschaft
        // existiert seit Einfuehrung der Modi nicht mehr auf VirtualControllerProfile, wuerde also von
        // JsonSerializer.Deserialize<AppProfile> stillschweigend verworfen (unbekannte Property).
        var legacyMappingsByControllerId = ExtractLegacyMappingsByControllerId(jsonBytes);

        var loaded = JsonSerializer.Deserialize<AppProfile>(jsonBytes, SerializerOptions);
        var profile = loaded ?? new AppProfile();

        MigrateLegacyDPadMappings(legacyMappingsByControllerId);
        MigrateLegacyModes(profile, legacyMappingsByControllerId);
        MigrateLegacyCustomInputNames(profile);

        return profile;
    }

    /// <summary>
    /// Liest aus dem rohen JSON die veraltete, flache "Mappings"-Liste jedes Controllers aus (Key = dessen
    /// <see cref="VirtualControllerProfile.Id"/>), bevor diese durch die typisierte Deserialisierung
    /// verloren geht. Liefert nur Eintraege fuer Controller, die tatsaechlich ein solches Legacy-Array
    /// besitzen (neuere Profile ohne dieses Feld liefern hier nichts).
    /// </summary>
    private static Dictionary<Guid, List<MappingEntry>> ExtractLegacyMappingsByControllerId(byte[] jsonBytes)
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

            var mappings = mappingsElement.Deserialize<List<MappingEntry>>(SerializerOptions);
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
            if (!TryParseStorageKey(key, out var deviceId, out var kind, out var index))
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

    /// <summary>Parst einen Storage-Key im Format "{DeviceId}|{PhysicalInputKind}|{Index}" (siehe
    /// <see cref="PhysicalInputCatalog.BuildStorageKey"/>) zurueck in seine Bestandteile. Die letzten
    /// zwei durch '|' getrennten Segmente muessen Kind bzw. Index sein, alle davor liegenden Segmente
    /// werden wieder zur DeviceId zusammengefuegt (falls diese selbst jemals ein '|' enthalten sollte).</summary>
    private static bool TryParseStorageKey(string key, out string deviceId, out PhysicalInputKind kind, out int index)
    {
        deviceId = string.Empty;
        kind = default;
        index = 0;

        var segments = key.Split('|');
        if (segments.Length < 3)
        {
            return false;
        }

        if (!Enum.TryParse(segments[^2], out kind) || !int.TryParse(segments[^1], out index))
        {
            return false;
        }

        deviceId = string.Join('|', segments[..^2]);
        return true;
    }

    public static void Save(AppProfile profile, string? filePath = null)
    {
        var targetPath = filePath ?? DefaultFilePath;
        EnsureDirectoryExists(targetPath);

        var tempPath = targetPath + ".tmp";

        using (var writeStream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(writeStream, profile, SerializerOptions);
        }

        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static void EnsureDirectoryExists(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}


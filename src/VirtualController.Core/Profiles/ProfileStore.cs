using System.Text.Json;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Saves and loads the complete configuration (virtual controllers, mapping tables, and device settings) as
/// several descriptively named files instead of one profiles.json:
/// <list type="bullet">
/// <item><description>One virtual controller per file: "Controllers\controller-{name}.json"
/// (see <see cref="ControllerStore"/>).</description></item>
/// <item><description>One physical device per file: "Devices\device-{brand}-{name}.json"
/// (see <see cref="DeviceSettingsStore"/>).</description></item>
/// <item><description>General settings in one small "settings.json" file (see <see cref="SettingsStore"/>).</description></item>
/// </list>
/// Each file is still written atomically by its store (see <see cref="AtomicJsonWriter"/>), so a crash or
/// power loss cannot corrupt an existing valid file. If a combined profiles.json from an older app version
/// still exists, the first <see cref="Load"/> automatically migrates it to this format
/// (see <see cref="LoadLegacyAndMigrate"/>).
/// </summary>
public static class ProfileStore
{
    /// <summary>Base directory for all profile files, defaulting to %AppData%\VirtualController. Can be
    /// overridden through the optional <see cref="Load"/>/<see cref="Save"/> parameter, e.g. in tests.</summary>
    public static string BaseDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualController");

    private static string ControllersDirectory(string baseDirectory) => Path.Combine(baseDirectory, "Controllers");

    private static string DevicesDirectory(string baseDirectory) => Path.Combine(baseDirectory, "Devices");

    private static string SettingsFilePath(string baseDirectory) => Path.Combine(baseDirectory, "settings.json");

    /// <summary>Path to the combined profile file used by older app versions, retained only for one-time
    /// migration to split files (see <see cref="LoadLegacyAndMigrate"/>).</summary>
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
        profile.StartWithWindows = settings.StartWithWindows;
        profile.StartMinimized = settings.StartMinimized;
        profile.AlwaysOnTop = settings.AlwaysOnTop;
        profile.UiLanguage = settings.UiLanguage;
        profile.CustomInputNames = settings.CustomInputNames;

        MigrateLegacyCustomInputNames(profile);

        return profile;
    }

    /// <summary>Whether any new split files exist. If so, the installation is considered migrated and any
    /// remaining legacy profiles.json is ignored, preventing deleted controllers/devices from reappearing.</summary>
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

    /// <summary>Reads a legacy combined profiles.json (including existing legacy migrations), saves its contents
    /// once in the new split format, and renames the old file to "profiles.json.migrated" rather than deleting
    /// it. This prevents it from being treated as a migration source on the next <see cref="Load"/>.</summary>
    private static AppProfile LoadLegacyAndMigrate(string legacyPath, string baseDirectory)
    {
        var jsonBytes = File.ReadAllBytes(legacyPath);
        var legacyOptions = ProfileJsonOptions.Create();

        // Extract this before typed deserialization: older profiles stored the mapping table as a flat
        // "Mappings" array directly on the controller object. VirtualControllerProfile no longer has that
        // property since modes were introduced, so JsonSerializer would silently discard it as unknown.
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
            File.Delete(migratedPath); // Remove a leftover from a previously interrupted migration attempt.
            File.Move(legacyPath, migratedPath);
        }
        catch (IOException)
        {
            // Rename failure (e.g. file locked) is harmless; HasAnySplitFiles prevents another migration on
            // the next load.
        }

        return profile;
    }

    /// <summary>
    /// Reads each controller's legacy flat "Mappings" list (keyed by <see cref="VirtualControllerProfile.Id"/>)
    /// from raw JSON before typed deserialization discards it. Returns entries only for controllers that have
    /// this legacy array; newer profiles without the field produce no entries.
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
    /// For each controller without modes that has a legacy flat mapping list in
    /// <paramref name="legacyMappingsByControllerId"/>, creates one "Default" mode containing those mappings.
    /// Existing mappings are preserved and active after migration to multiple modes per controller.
    /// </summary>
    private static void MigrateLegacyModes(AppProfile profile, Dictionary<Guid, List<MappingEntry>> legacyMappingsByControllerId)
    {
        foreach (var controller in profile.Controllers)
        {
            if (controller.Modes.Count > 0)
            {
                continue; // Already saved in the new format; nothing to migrate.
            }

            legacyMappingsByControllerId.TryGetValue(controller.Id, out var legacyMappings);

            var standardMode = new ControllerMode
            {
                Id = Guid.NewGuid(),
                Name = "Default",
                Mappings = legacyMappings ?? new List<MappingEntry>()
            };

            controller.Modes.Add(standardMode);
            controller.ActiveModeId = standardMode.Id;
        }
    }

    /// <summary>
    /// Converts legacy mappings with the combined <see cref="PhysicalInputKind.DPad"/> source (from profiles
    /// saved before D-pad was split into four directions) into four equivalent entries, one per direction.
    /// This exactly preserves the old behavior: button/trigger targets reacted to any POV movement, while
    /// D-pad targets passed through the combined direction. Four independent digital entries with the same
    /// target reproduce that behavior, including diagonals where two entries are active at once.
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
                Description = legacy.Description
            };
        }
    }

    /// <summary>
    /// Migrates the legacy flat <see cref="AppProfile.CustomInputNames"/> list (the only storage for custom
    /// input names before <see cref="Devices.DeviceSettings"/> was introduced) into the richer structure so
    /// existing names are preserved. Names already present in <see cref="AppProfile.DeviceSettings"/> take
    /// precedence if both sources describe the same entry.
    /// </summary>
    private static void MigrateLegacyCustomInputNames(AppProfile profile)
    {
        foreach (var (key, customName) in profile.CustomInputNames)
        {
            if (!PhysicalInputCatalog.TryParseStorageKey(key, out var deviceId, out _, out _))
            {
                continue; // Ignore unexpected/malformed key formats instead of throwing.
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
                StartWithWindows = profile.StartWithWindows,
                StartMinimized = profile.StartMinimized,
                AlwaysOnTop = profile.AlwaysOnTop,
                UiLanguage = profile.UiLanguage,
                CustomInputNames = profile.CustomInputNames
            },
            SettingsFilePath(baseDir));
    }
}

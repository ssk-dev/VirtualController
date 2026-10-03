using System.Text.Json;

namespace VirtualController.Core.Profiles;

/// <summary>
/// General settings that are not specific to a device or controller, stored in settings.json
/// (see <see cref="SettingsStore"/>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Whether connected/disconnected physical devices are detected automatically through background
    /// polling (see MainViewModel), without restarting the app or manually clicking Refresh devices. Enabled by default.</summary>
    public bool AutoDeviceDetectionEnabled { get; set; } = true;

    /// <summary>Whether the application starts automatically when signing in to Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Whether the main window starts minimized when launched with Windows.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Whether the main window stays on top. Enabled by default.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>UI language selection. Supported values are "system", "en", and "de". "system" uses the
    /// current OS language when the app starts.</summary>
    public string UiLanguage { get; set; } = "system";

    /// <summary>Deprecated: before <see cref="Devices.DeviceSettings"/> was introduced, this was the only storage
    /// for custom input names, keyed by "{DeviceId}|{PhysicalInputKind}|{Index}". Retained temporarily so very
    /// old, already-migrated profiles can be migrated idempotently to <see cref="Devices.DeviceSettings"/> on
    /// subsequent loads (see <see cref="ProfileStore"/>).</summary>
    public Dictionary<string, string> CustomInputNames { get; set; } = new();
}

/// <summary>
/// Loads/saves general <see cref="AppSettings"/> in a small settings.json file, separate from the per-device
/// (<see cref="DeviceSettingsStore"/>) and per-controller (<see cref="ControllerStore"/>) files.
/// </summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    public static AppSettings Load(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var bytes = File.ReadAllBytes(settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Fall back to defaults if settings.json is corrupted rather than failing to load the entire
            // configuration (controllers/devices).
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string settingsFilePath) =>
        AtomicJsonWriter.Write(settingsFilePath, settings, SerializerOptions);
}

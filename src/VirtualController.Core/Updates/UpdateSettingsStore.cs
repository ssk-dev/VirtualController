using System.Text.Json;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Updates;

/// <summary>
/// Persisted update settings: whether to check automatically at app startup (<see cref="AutoCheckEnabled"/>)
/// and which version the user last permanently skipped (<see cref="SkippedVersion"/>). Stored in a separate,
/// small update-settings.json rather than the batch-saved <see cref="AppProfile"/>/settings.json (see
/// <see cref="SettingsStore"/>), so changes to either value are persisted immediately when toggled in Settings
/// or when the user skips an update, regardless of unsaved mapping/controller changes.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Whether to automatically check for a newer version at every app startup. Enabled by default.</summary>
    public bool AutoCheckEnabled { get; set; } = true;

    /// <summary>Whether update checks should include prereleases (tags ending in -alpha/-beta/-nightly; see
    /// release.yml) instead of stable releases only. Disabled by default to avoid prompting users to install
    /// unstable versions unexpectedly.</summary>
    public bool IncludePreReleases { get; set; }

    /// <summary>Version (e.g. "1.5.0") the user last permanently skipped, or null if no version has been skipped.</summary>
    public string? SkippedVersion { get; set; }
}

/// <summary>
/// Loads/saves <see cref="UpdateSettings"/> in a separate update-settings.json under
/// <see cref="ProfileStore.BaseDirectory"/>, like <see cref="SettingsStore"/>. Kept separate because these
/// values are persisted immediately, independently of the explicit Save profiles operation.
/// </summary>
public static class UpdateSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    private static string FilePath(string? baseDirectory = null) =>
        Path.Combine(baseDirectory ?? ProfileStore.BaseDirectory, "update-settings.json");

    public static UpdateSettings Load(string? baseDirectory = null)
    {
        var path = FilePath(baseDirectory);

        if (!File.Exists(path))
        {
            return new UpdateSettings();
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<UpdateSettings>(bytes, SerializerOptions) ?? new UpdateSettings();
        }
        catch (JsonException)
        {
            // Fall back to defaults if update-settings.json is corrupted rather than failing to load the
            // entire configuration.
            return new UpdateSettings();
        }
    }

    public static void Save(UpdateSettings settings, string? baseDirectory = null) =>
        AtomicJsonWriter.Write(FilePath(baseDirectory), settings, SerializerOptions);
}

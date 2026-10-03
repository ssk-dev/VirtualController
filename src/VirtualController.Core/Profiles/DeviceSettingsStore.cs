using System.Text.Json;
using VirtualController.Core.Devices;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Stores device-wide <see cref="DeviceSettings"/> in one descriptive file per physical device
/// (device-{brand}-{name}.json, e.g. "device-logitech-x56.json") under a Devices directory instead of one
/// combined file. This makes files easier to inspect and lets users back up, share, or delete settings for
/// one device independently.
/// </summary>
internal static class DeviceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    /// <summary>One device file. Stores <see cref="DeviceId"/> alongside the settings so the device can be
    /// identified later; the filename is only an approximate, potentially non-unique derivative of the
    /// display name (see <see cref="ResolveFileName"/>). Files are matched by this field, not by filename.</summary>
    private sealed class DeviceFile
    {
        public required string DeviceId { get; set; }
        public required DeviceSettings Settings { get; set; }
    }

    public static Dictionary<string, DeviceSettings> LoadAll(string devicesDirectory)
    {
        var result = new Dictionary<string, DeviceSettings>();
        if (!Directory.Exists(devicesDirectory))
        {
            return result;
        }

        foreach (var filePath in Directory.EnumerateFiles(devicesDirectory, "device-*.json"))
        {
            var file = TryReadDeviceFile(filePath);
            if (file is not null)
            {
                result[file.DeviceId] = file.Settings;
            }
        }

        return result;
    }

    public static void SaveAll(IReadOnlyDictionary<string, DeviceSettings> deviceSettings, string devicesDirectory)
    {
        Directory.CreateDirectory(devicesDirectory);

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (deviceId, settings) in deviceSettings)
        {
            var fileName = ResolveFileName(devicesDirectory, deviceId, settings, usedFileNames);
            usedFileNames.Add(fileName);

            var fullPath = Path.Combine(devicesDirectory, fileName);
            expectedFiles.Add(fullPath);

            var file = new DeviceFile { DeviceId = deviceId, Settings = settings };
            AtomicJsonWriter.Write(fullPath, file, SerializerOptions);
        }

        // Delete files for orphaned devices (e.g. removed/hidden in configuration) so stale files do not
        // remain permanently and reappear on the next load.
        foreach (var existing in Directory.EnumerateFiles(devicesDirectory, "device-*.json"))
        {
            if (!expectedFiles.Contains(existing))
            {
                TryDelete(existing);
            }
        }
    }

    private static DeviceFile? TryReadDeviceFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return JsonSerializer.Deserialize<DeviceFile>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            // Skip this device's corrupted file rather than making the entire configuration unusable.
            return null;
        }
    }

    private static string ResolveFileName(
        string devicesDirectory, string deviceId, DeviceSettings settings, HashSet<string> usedInThisSave)
    {
        var (brand, name) = FileNaming.SplitBrandAndName(settings.LastKnownDisplayName ?? deviceId);
        var baseName = $"device-{brand}-{name}";

        var candidate = baseName + ".json";
        int suffix = 2;
        while (usedInThisSave.Contains(candidate)
               || BelongsToDifferentDevice(devicesDirectory, candidate, deviceId))
        {
            candidate = $"{baseName}-{suffix}.json";
            suffix++;
        }

        return candidate;
    }

    /// <summary>Different devices can produce the same derived filename (e.g. two identical joysticks). Before
    /// assigning a filename, check whether an existing file belongs to a different device (choose another name)
    /// or to this device (overwrite it normally).</summary>
    private static bool BelongsToDifferentDevice(string devicesDirectory, string fileName, string deviceId)
    {
        var path = Path.Combine(devicesDirectory, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        var file = TryReadDeviceFile(path);
        return file is null || file.DeviceId != deviceId;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

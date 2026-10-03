using System.Text.Json;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Atomically writes an object as JSON through a temporary file and <see cref="File.Replace"/>, so a crash or
/// power loss during writing cannot corrupt an existing valid file. Shared by all profile stores
/// (<see cref="DeviceSettingsStore"/>, <see cref="ControllerStore"/>, <see cref="SettingsStore"/>); originally
/// used by <see cref="ProfileStore.Save"/> before profiles.json was split into multiple files.
/// </summary>
internal static class AtomicJsonWriter
{
    public static void Write<T>(string targetPath, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = targetPath + ".tmp";
        using (var writeStream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(writeStream, value, options);
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
}

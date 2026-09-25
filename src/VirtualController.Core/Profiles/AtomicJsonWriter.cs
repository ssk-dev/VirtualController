using System.Text.Json;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Schreibt ein Objekt atomar als JSON in eine Datei (ueber eine temporaere Datei + <see cref="File.Replace"/>),
/// damit ein Absturz oder Stromausfall waehrend des Schreibens niemals eine bereits vorhandene, gueltige
/// Datei beschaedigt. Wird von allen Profil-Teilspeichern (<see cref="DeviceSettingsStore"/>,
/// <see cref="ControllerStore"/>, <see cref="SettingsStore"/>) gemeinsam genutzt - urspruenglich das
/// Verhalten von <see cref="ProfileStore.Save"/>, bevor die einzelne profiles.json in mehrere Dateien
/// aufgeteilt wurde.
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

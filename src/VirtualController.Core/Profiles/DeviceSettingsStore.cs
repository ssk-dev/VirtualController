using System.Text.Json;
using VirtualController.Core.Devices;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Speichert die geraeteweiten Einstellungen (<see cref="DeviceSettings"/>) nicht mehr gesammelt in
/// einer einzigen Datei, sondern je physischem Geraet in einer eigenen, sprechend benannten Datei
/// (device-{marke}-{name}.json, z.B. "device-logitech-x56.json") unterhalb eines "Devices"-Ordners.
/// Das macht die einzelnen Dateien uebersichtlicher und erlaubt es, die Einstellungen eines einzelnen
/// Geraets unabhaengig von allen anderen zu sichern, zu teilen oder zu loeschen.
/// </summary>
internal static class DeviceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    /// <summary>Eine einzelne Geraetedatei: die <see cref="DeviceId"/> wird zusaetzlich zu den eigentlichen
    /// Einstellungen mitgespeichert, damit spaeter erkannt werden kann, welches Geraet gemeint ist (der
    /// Dateiname selbst ist nur eine vom Anzeigenamen abgeleitete, nicht zwingend eindeutige Naeherung -
    /// siehe <see cref="ResolveFileName"/>) und Dateien beim Laden nicht anhand ihres Namens, sondern
    /// anhand dieses Felds zugeordnet werden.</summary>
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

        // Dateien verwaister Geraete (z.B. nach dem Entfernen/Ausblenden in der Konfiguration) wieder
        // loeschen, damit alte Stand-Dateien nicht dauerhaft liegen bleiben und beim naechsten Laden
        // faelschlich wieder auftauchen.
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
            // Beschaedigte Einzeldatei -> dieses eine Geraet wird beim Laden uebersprungen statt die
            // gesamte Konfiguration unbrauchbar zu machen.
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

    /// <summary>Zwei unterschiedliche Geraete koennen denselben abgeleiteten Dateinamen ergeben (z.B.
    /// zwei baugleiche Joysticks). Bevor ein Dateiname fuer ein Geraet vergeben wird, muss daher
    /// geprueft werden, ob eine bereits existierende Datei mit diesem Namen tatsaechlich zu einem
    /// ANDEREN Geraet gehoert (dann muss ausgewichen werden) oder ohnehin schon zu diesem Geraet
    /// gehoert (dann wird sie regulaer ueberschrieben).</summary>
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

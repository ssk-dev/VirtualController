using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Benchmark;

/// <summary>
/// Schreibt ein <see cref="BenchmarkResult"/> als JSON-Datei nach
/// "%AppData%\VirtualController\Benchmark\benchmark-device-{marke}-{name}.json" - bewusst eine eigene,
/// schlanke Kopie des Atomar-Schreib-Musters von <see cref="AtomicJsonWriter"/>/<see cref="ProfileJsonOptions"/>
/// statt deren Wiederverwendung, da beide Typen als <see langword="internal"/> ausschliesslich fuer die
/// Profil-Teilspeicher (<see cref="Profiles.ControllerStore"/> usw.) gedacht sind und das Benchmark-Feature
/// bewusst als eigenstaendiges, von der Profil-Persistenz unabhaengiges Feature entworfen wurde (siehe
/// Klassendokumentation von <see cref="BenchmarkSession"/>).
/// </summary>
public static class BenchmarkJsonExporter
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Leitet den Standard-Dateipfad aus dem Anzeigenamen des Geraets ab - dieselbe Marke/Name-
    /// Aufteilung wie bei den Log-Dateien (siehe <see cref="Logging.DeviceStateLogger.BuildDefaultFilePath"/>),
    /// damit beide Dateiarten fuer denselben Geraetenamen konsistent benannt sind.</summary>
    public static string BuildDefaultFilePath(Devices.PhysicalDeviceInfo device)
    {
        var (brand, name) = FileNaming.SplitBrandAndName(device.DisplayName);
        var directory = Path.Combine(ProfileStore.BaseDirectory, "Benchmark");
        return Path.Combine(directory, $"benchmark-device-{brand}-{name}.json");
    }

    /// <summary>Schreibt <paramref name="result"/> atomar (ueber eine temporaere Datei + <see cref="File.Replace"/>)
    /// nach <paramref name="targetPath"/>, damit ein Absturz waehrend des Schreibens niemals eine bereits
    /// vorhandene, gueltige Datei beschaedigt - identisches Vorgehen wie <see cref="AtomicJsonWriter.Write{T}"/>.</summary>
    public static void Write(string targetPath, BenchmarkResult result)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = targetPath + ".tmp";
        using (var writeStream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(writeStream, result, Options);
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

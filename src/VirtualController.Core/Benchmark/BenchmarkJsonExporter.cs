using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Benchmark;

/// <summary>
/// Writes a <see cref="BenchmarkResult"/> to
/// "%AppData%\VirtualController\Benchmark\benchmark-device-{brand}-{name}.json". Uses a small, dedicated
/// copy of the atomic-write pattern from <see cref="AtomicJsonWriter"/>/<see cref="ProfileJsonOptions"/>
/// rather than reusing those internal types, which are intended only for profile stores such as
/// <see cref="Profiles.ControllerStore"/>. Benchmarking is intentionally independent of profile persistence
/// (see <see cref="BenchmarkSession"/> documentation).
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

    /// <summary>Builds the default file path from the device display name, using the same brand/name split as
    /// log files (see <see cref="Logging.DeviceStateLogger.BuildDefaultFilePath"/>) so both file types are
    /// named consistently for the same device.</summary>
    public static string BuildDefaultFilePath(Devices.PhysicalDeviceInfo device)
    {
        var (brand, name) = FileNaming.SplitBrandAndName(device.DisplayName);
        var directory = Path.Combine(ProfileStore.BaseDirectory, "Benchmark");
        return Path.Combine(directory, $"benchmark-device-{brand}-{name}.json");
    }

    /// <summary>Atomically writes <paramref name="result"/> to <paramref name="targetPath"/> through a temporary
    /// file and <see cref="File.Replace"/>, so a crash during writing cannot corrupt an existing valid file.
    /// Uses the same approach as <see cref="AtomicJsonWriter.Write{T}"/>.</summary>
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

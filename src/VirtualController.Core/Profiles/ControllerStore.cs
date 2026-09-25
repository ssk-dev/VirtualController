using System.Text.Json;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Speichert jeden virtuellen Controller (<see cref="VirtualControllerProfile"/>) in einer eigenen,
/// nach seinem Namen benannten Datei (controller-{name}.json) unterhalb eines "Controllers"-Ordners,
/// statt gesammelt in einer einzigen Profildatei. Macht einzelne Controller-Konfigurationen
/// uebersichtlicher und erlaubt es, sie unabhaengig voneinander zu sichern, zu teilen oder zu loeschen.
/// </summary>
internal static class ControllerStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    public static List<VirtualControllerProfile> LoadAll(string controllersDirectory)
    {
        var result = new List<VirtualControllerProfile>();
        if (!Directory.Exists(controllersDirectory))
        {
            return result;
        }

        foreach (var filePath in Directory.EnumerateFiles(controllersDirectory, "controller-*.json"))
        {
            var profile = TryReadControllerFile(filePath);
            if (profile is not null)
            {
                result.Add(profile);
            }
        }

        return result;
    }

    public static void SaveAll(IReadOnlyList<VirtualControllerProfile> controllers, string controllersDirectory)
    {
        Directory.CreateDirectory(controllersDirectory);

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in controllers)
        {
            var fileName = ResolveFileName(controllersDirectory, controller, usedFileNames);
            usedFileNames.Add(fileName);

            var fullPath = Path.Combine(controllersDirectory, fileName);
            expectedFiles.Add(fullPath);

            AtomicJsonWriter.Write(fullPath, controller, SerializerOptions);
        }

        // Dateien entfernter Controller wieder loeschen, damit alte Stand-Dateien nicht dauerhaft liegen
        // bleiben und beim naechsten Laden faelschlich wieder auftauchen.
        foreach (var existing in Directory.EnumerateFiles(controllersDirectory, "controller-*.json"))
        {
            if (!expectedFiles.Contains(existing))
            {
                TryDelete(existing);
            }
        }
    }

    private static VirtualControllerProfile? TryReadControllerFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return JsonSerializer.Deserialize<VirtualControllerProfile>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            // Beschaedigte Einzeldatei -> dieser eine Controller wird beim Laden uebersprungen statt die
            // gesamte Konfiguration unbrauchbar zu machen.
            return null;
        }
    }

    private static string ResolveFileName(
        string controllersDirectory, VirtualControllerProfile controller, HashSet<string> usedInThisSave)
    {
        var baseName = $"controller-{FileNaming.Sanitize(controller.Name)}";

        var candidate = baseName + ".json";
        int suffix = 2;
        while (usedInThisSave.Contains(candidate)
               || BelongsToDifferentController(controllersDirectory, candidate, controller.Id))
        {
            candidate = $"{baseName}-{suffix}.json";
            suffix++;
        }

        return candidate;
    }

    /// <summary>Zwei unterschiedliche Controller koennen denselben Namen (und damit denselben
    /// abgeleiteten Dateinamen) tragen. Bevor ein Dateiname vergeben wird, muss daher geprueft werden,
    /// ob eine bereits existierende Datei mit diesem Namen tatsaechlich zu einem ANDEREN Controller
    /// (per Id) gehoert (dann muss ausgewichen werden) oder ohnehin schon zu diesem Controller gehoert
    /// (dann wird sie regulaer ueberschrieben).</summary>
    private static bool BelongsToDifferentController(string controllersDirectory, string fileName, Guid controllerId)
    {
        var path = Path.Combine(controllersDirectory, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        var existing = TryReadControllerFile(path);
        return existing is null || existing.Id != controllerId;
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

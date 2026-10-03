using System.Text.Json;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Stores each virtual controller (<see cref="VirtualControllerProfile"/>) in its own name-based file
/// (controller-{name}.json) under a Controllers directory instead of combining them in one profile file.
/// This makes controller configurations easier to inspect and lets users back up, share, or delete them independently.
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

        // Delete files for removed controllers so stale files do not remain and reappear on the next load.
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
            // Skip this controller's corrupted file rather than making the entire configuration unusable.
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

    /// <summary>Different controllers can have the same name and derived filename. Before assigning a filename,
    /// check whether an existing file belongs to another controller (by ID; choose another name) or to this
    /// controller (overwrite it normally).</summary>
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

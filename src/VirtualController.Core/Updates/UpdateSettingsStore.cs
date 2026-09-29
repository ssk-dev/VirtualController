using System.Text.Json;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Updates;

/// <summary>
/// Persistierte Einstellungen der Update-Funktion: ob automatisch beim App-Start geprueft werden soll
/// (<see cref="AutoCheckEnabled"/>) und welche Version der Nutzer zuletzt dauerhaft uebersprungen hat
/// (<see cref="SkippedVersion"/>). Bewusst als eigene, kleine "update-settings.json" getrennt von der
/// batch-gespeicherten <see cref="AppProfile"/>/"settings.json" (siehe <see cref="SettingsStore"/>):
/// beide hier gespeicherten Werte sollen sofort bei jeder Aenderung dauerhaft uebernommen werden (Toggle
/// im "Einstellungen"-Tab bzw. Klick auf "Update ueberspringen"), unabhaengig davon, ob der Nutzer
/// zusaetzlich ungespeicherte Mapping-/Controller-Aenderungen hat und den expliziten "Profile
/// speichern"-Button noch gar nicht angeklickt hat.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Ob bei jedem App-Start automatisch geprueft werden soll, ob eine neuere Version verfuegbar
    /// ist. Standard: aktiviert.</summary>
    public bool AutoCheckEnabled { get; set; } = true;

    /// <summary>Ob bei der Update-Pruefung auch als "Pre-release" markierte Versionen (Tags mit Suffix wie
    /// "-alpha"/"-beta"/"-nightly", siehe release.yml) beruecksichtigt werden sollen, statt ausschliesslich
    /// vollwertige, stabile Releases. Standard: deaktiviert, damit Nutzer nicht ungewollt zu instabilen
    /// Vorabversionen aufgefordert werden.</summary>
    public bool IncludePreReleases { get; set; }

    /// <summary>Versionsnummer (z.B. "1.5.0"), die der Nutzer zuletzt ueber "Update ueberspringen"
    /// dauerhaft uebersprungen hat, oder null, falls noch keine Version uebersprungen wurde.</summary>
    public string? SkippedVersion { get; set; }
}

/// <summary>
/// Laedt/speichert die <see cref="UpdateSettings"/> als eigene, kleine "update-settings.json" unter
/// <see cref="ProfileStore.BaseDirectory"/> - analog zu <see cref="SettingsStore"/>, jedoch bewusst in
/// einer eigenen Datei, da diese Werte (siehe <see cref="UpdateSettings"/>) unabhaengig vom expliziten
/// "Profile speichern"-Vorgang sofort persistiert werden.
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
            // Beschaedigte update-settings.json -> auf Standardwerte zurueckfallen statt das Laden der
            // gesamten Konfiguration daran scheitern zu lassen.
            return new UpdateSettings();
        }
    }

    public static void Save(UpdateSettings settings, string? baseDirectory = null) =>
        AtomicJsonWriter.Write(FilePath(baseDirectory), settings, SerializerOptions);
}

using System.Text.Json;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Allgemeine, nicht geraete- oder controllerspezifische Einstellungen, gespeichert in settings.json
/// (siehe <see cref="SettingsStore"/>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Ob neu angeschlossene/getrennte physische Geraete automatisch (per Hintergrund-Polling,
    /// siehe MainViewModel) erkannt werden sollen, ohne dass die App neu gestartet oder "Geraete
    /// aktualisieren" manuell geklickt werden muss. Standard: aktiviert.</summary>
    public bool AutoDeviceDetectionEnabled { get; set; } = true;

    /// <summary>Ob die Anwendung bei der Windows-Anmeldung automatisch gestartet wird.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Ob das Hauptfenster beim Windows-Autostart minimiert angezeigt wird.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Ob das Hauptfenster immer im Vordergrund bleibt. Standard: aktiviert.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Veraltet: vor Einfuehrung von <see cref="Devices.DeviceSettings"/> die einzige Persistenz
    /// fuer benutzerdefinierte Eingabenamen, Key-Format "{DeviceId}|{PhysicalInputKind}|{Index}". Bleibt
    /// hier nur uebergangsweise erhalten, damit sehr alte, bereits einmal migrierte Profile beim
    /// wiederholten Laden weiterhin idempotent nach <see cref="Devices.DeviceSettings"/> ueberfuehrt
    /// werden koennen (siehe <see cref="ProfileStore"/>).</summary>
    public Dictionary<string, string> CustomInputNames { get; set; } = new();
}

/// <summary>
/// Laedt/speichert die allgemeinen Einstellungen (<see cref="AppSettings"/>) als eigene, kleine
/// settings.json - getrennt von den pro Geraet (<see cref="DeviceSettingsStore"/>) und pro Controller
/// (<see cref="ControllerStore"/>) aufgeteilten Dateien.
/// </summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = ProfileJsonOptions.Create();

    public static AppSettings Load(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var bytes = File.ReadAllBytes(settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Beschaedigte settings.json -> auf Standardwerte zurueckfallen statt das Laden der gesamten
            // Konfiguration (Controller/Geraete) daran scheitern zu lassen.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string settingsFilePath) =>
        AtomicJsonWriter.Write(settingsFilePath, settings, SerializerOptions);
}

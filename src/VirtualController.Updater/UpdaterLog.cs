using System.IO;
using System.Text;

namespace VirtualController.Updater;

/// <summary>
/// Einfaches, robustes Datei-Logging fuer diesen eigenstaendigen Updater-Prozess - schreibt in
/// dieselbe Datei wie <c>VirtualController.Core.Logging.UpdateLog</c> ("%AppData%\VirtualController\update.log"),
/// damit der gesamte Update-Vorgang (Vorbereitung durch die Hauptanwendung, anschliessendes Kopieren
/// durch diesen Prozess) chronologisch in einer einzigen Datei nachvollziehbar bleibt. Bewusst als
/// eigenstaendige, minimale Kopie statt einer Abhaengigkeit auf das Core-Projekt implementiert (siehe
/// VirtualController.Updater.csproj) - dieses Projekt soll unabhaengig von Core's nativen
/// Treiber-Abhaengigkeiten (ViGEmBus/HidHide/Vortice/HidSharp) bleiben, die fuer den reinen
/// Dateikopiervorgang hier nicht benoetigt werden.
/// </summary>
internal static class UpdaterLog
{
    private static readonly object Lock = new();

    /// <summary>Vollstaendiger Pfad der Update-Log-Datei - identisch zu
    /// <c>VirtualController.Core.Logging.UpdateLog.FilePath</c>.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualController", "update.log");

    /// <summary>Schreibt eine neue Zeile mit Zeitstempel an das Ende der Log-Datei, im selben Format wie
    /// die Hauptanwendung, damit beide Seiten des Update-Vorgangs chronologisch nachvollziehbar
    /// bleiben.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Updater] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf diesen Prozess niemals zum Absturz bringen.
        }
    }

    /// <summary>Schreibt eine gut sichtbare Trennzeile fuer den Start dieses eigenstaendigen
    /// Updater-Prozesses, analog zu <c>VirtualController.Core.Logging.UpdateLog.WriteSessionStart</c> -
    /// die Hauptanwendung hat beim Start des Update-Vorgangs bereits eine eigene Trennzeile
    /// geschrieben, diese hier markiert den Beginn des Kopiervorgangs in demselben Log.</summary>
    public static void WriteSessionStart()
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(
                    FilePath,
                    $"{Environment.NewLine}===== Eigenstaendiger Updater-Prozess gestartet @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf diesen Prozess niemals zum Absturz bringen.
        }
    }
}

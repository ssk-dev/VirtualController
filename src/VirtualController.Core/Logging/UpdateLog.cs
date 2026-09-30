using System.Text;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Logging;

/// <summary>
/// Einfaches, robustes Datei-Logging speziell fuer den Update-Installationsvorgang (siehe
/// <see cref="Updates.UpdateInstaller"/> und das dort generierte PowerShell-Updater-Skript): schreibt
/// zeitgestempelte Zeilen sofort (kein Buffering, kein async) in eine Log-Datei unter
/// "%AppData%\VirtualController\update.log". Bewusst als eigene Datei getrennt von <c>DebugLog</c>
/// (App-Projekt) angelegt, da <see cref="Updates.UpdateInstaller"/> im WPF-unabhaengigen Core-Projekt
/// liegt und ausserdem NACH dem eigenen Beenden der Anwendung noch aus dem separaten,
/// generierten PowerShell-Prozess heraus weiter protokollieren muss - genau der Zeitraum
/// (Kopieren der neuen Dateien, Neustart der Anwendung), in dem ein Fehlschlag bislang vollstaendig
/// unsichtbar war (siehe Klassendokumentation von <see cref="Updates.UpdateInstaller"/>).
///
/// Schreibt bewusst NICHT bei jedem Aufruf/App-Start neu (kein <c>Reset</c> wie bei <c>DebugLog</c>),
/// sondern haengt fortlaufend an: ein fehlgeschlagener Update-Versuch (App startet nicht neu) darf durch
/// den naechsten manuellen Neustart/Update-Versuch nicht ueberschrieben werden, bevor der Nutzer die
/// Datei einsehen konnte. Um dennoch nicht unbegrenzt zu wachsen, wird die Datei automatisch verworfen
/// und neu begonnen, sobald sie eine Groesse von <see cref="MaxFileSizeBytes"/> überschreitet (siehe
/// <see cref="TrimIfTooLarge"/>). Fehler beim Schreiben werden verschluckt - Logging darf weder die
/// Anwendung noch das Updater-Skript jemals zum Absturz bringen.
/// </summary>
public static class UpdateLog
{
    /// <summary>Ab dieser Dateigroesse wird die Log-Datei bei der naechsten <see cref="WriteSessionStart"/>
    /// verworfen und neu begonnen, damit sie bei vielen Update-Pruefungen ueber lange Zeit nicht
    /// unbegrenzt waechst.</summary>
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly object Lock = new();

    /// <summary>Vollstaendiger Pfad der Update-Log-Datei - wird bewusst zusaetzlich in die Datei selbst
    /// geschrieben (siehe <see cref="WriteSessionStart"/>) und kann z.B. im "Einstellungen"-Tab
    /// angezeigt werden, damit der Nutzer die Datei nach einem fehlgeschlagenen Update-Versuch ohne
    /// Suchen wiederfindet.</summary>
    public static string FilePath { get; } = Path.Combine(ProfileStore.BaseDirectory, "update.log");

    /// <summary>Schreibt eine neue Zeile mit Zeitstempel an das Ende der Log-Datei. Kann sowohl aus der
    /// laufenden Anwendung (C#) als auch - ueber die vom generierten PowerShell-Skript nachgebildete
    /// Zeilenform (siehe <see cref="Updates.UpdateInstaller"/>) - aus dem separaten Updater-Prozess
    /// aufgerufen werden, damit beide Seiten des Update-Vorgangs in derselben Datei chronologisch
    /// nachvollziehbar sind.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                EnsureDirectory();

                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf die Anwendung niemals zum Absturz bringen.
        }
    }

    /// <summary>Schreibt eine gut sichtbare Trennzeile fuer den Beginn eines neuen Update-Versuchs, ohne
    /// vorherige Versuche zu loeschen (siehe Klassendokumentation) - ausser die Datei ist bereits
    /// unerwuenscht gross geworden (siehe <see cref="MaxFileSizeBytes"/>), dann wird sie an dieser Stelle
    /// verworfen.</summary>
    public static void WriteSessionStart(string sessionLabel)
    {
        try
        {
            lock (Lock)
            {
                EnsureDirectory();
                TrimIfTooLarge();

                File.AppendAllText(
                    FilePath,
                    $"{Environment.NewLine}===== {sessionLabel} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf die Anwendung niemals zum Absturz bringen.
        }
    }

    private static void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void TrimIfTooLarge()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > MaxFileSizeBytes)
            {
                File.WriteAllText(
                    FilePath,
                    $"===== Vorherige Update-Log-Datei wegen Groesse verworfen @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Best-effort - ein fehlgeschlagenes Kuerzen darf das eigentliche Logging nicht verhindern.
        }
    }
}

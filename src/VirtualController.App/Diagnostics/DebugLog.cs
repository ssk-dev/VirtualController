using System;
using System.IO;
using System.Text;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Sehr einfaches, robustes Datei-Logging fuer die gezielte Fehlersuche des "Ziel-Typ/Ziel-Wert
/// springt in einer anderen Zeile auf einen falschen Wert"-Bugs: schreibt zeitgestempelte Zeilen
/// sofort (kein Buffering, kein async) in eine Log-Datei unter %AppData%\VirtualController\debug.log.
/// Nach einer Reproduktion in der UI kann die Datei eingesehen werden, um die exakte Abfolge aller
/// relevanten ViewModel-Aenderungen nachzuvollziehen (welche Mapping-Zeile hat wann welchen Wert
/// gesetzt/empfangen). Bewusst als simple statische Klasse ohne Dependency Injection, damit sie an
/// jeder beliebigen Stelle im Code ohne Konstruktor-Aenderungen genutzt werden kann. Fehler beim
/// Schreiben werden verschluckt - Logging darf die Anwendung niemals zum Absturz bringen.
/// </summary>
public static class DebugLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualController", "debug.log");

    private static readonly object Lock = new();

    /// <summary>Schreibt eine neue Zeile mit Zeitstempel und Thread-Id an das Ende der Log-Datei.</summary>
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
                    $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf die Anwendung niemals zum Absturz bringen.
        }
    }

    /// <summary>Leert die Log-Datei und schreibt eine Trennzeile, z.B. beim Start einer neuen Diagnose-Sitzung.</summary>
    public static void Reset(string sessionLabel)
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

                File.WriteAllText(FilePath, $"===== {sessionLabel} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf die Anwendung niemals zum Absturz bringen.
        }
    }
}

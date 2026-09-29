namespace VirtualController.Core.Updates;

/// <summary>
/// Ergebnis von <see cref="UpdateInstaller.PrepareAsync"/>: das ZIP-Archiv der neuen Version wurde
/// erfolgreich heruntergeladen und in ein temporaeres Staging-Verzeichnis entpackt. Enthaelt alle
/// Informationen, die der separate Updater-Prozess (siehe <see cref="UpdateInstaller.LaunchUpdaterProcess"/>)
/// benoetigt, um nach Beenden dieser Anwendung die neuen Dateien an die Installation zu kopieren und die
/// Anwendung anschliessend neu zu starten.
/// </summary>
/// <param name="StagingDirectory">Temporaeres Verzeichnis mit den bereits entpackten neuen Dateien.</param>
/// <param name="InstallDirectory">Zielverzeichnis der laufenden Installation (enthaelt die EXE sowie die
/// benoetigten nativen WPF-DLLs), in das der Updater-Prozess die neuen Dateien kopiert.</param>
/// <param name="ExecutableFileName">Dateiname der Haupt-EXE (z.B. "VirtualController.exe"), mit dem der
/// Updater-Prozess die Anwendung nach dem Kopieren neu startet.</param>
/// <param name="ProcessId">Prozess-ID dieser noch laufenden Anwendungsinstanz - der Updater-Prozess wartet
/// auf deren Beendigung, bevor er die Zieldateien ueberschreibt (sie sind waehrend der Laufzeit gesperrt).</param>
public sealed record UpdateInstallPreparation(
    string StagingDirectory,
    string InstallDirectory,
    string ExecutableFileName,
    int ProcessId);

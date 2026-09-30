using System.IO;

namespace VirtualController.Updater;

/// <summary>Ergebnis von <see cref="UpdateApplier.ApplyAsync"/> - entweder erfolgreich oder mit einer
/// verstaendlichen Fehlermeldung fuer die Anzeige im Updater-Fenster.</summary>
/// <param name="Succeeded">Ob das Kopieren erfolgreich abgeschlossen wurde.</param>
/// <param name="ErrorMessage">Falls <paramref name="Succeeded"/> <c>false</c> ist: eine
/// verstaendliche, fuer die Anzeige im Updater-Fenster geeignete Fehlermeldung.</param>
internal sealed record ApplyResult(bool Succeeded, string? ErrorMessage);

/// <summary>
/// Enthaelt die eigentliche Update-Anwendungslogik dieses eigenstaendigen Updater-Prozesses: wartet auf
/// die Beendigung der Hauptanwendung, kopiert anschliessend die im Staging-Verzeichnis bereits
/// entpackten neuen Dateien in das Installationsverzeichnis (mit mehreren Versuchen, siehe
/// <see cref="ApplyAsync"/>) und kann danach die neu installierte Anwendung starten. Bewusst als reine,
/// von WPF unabhaengige Logik implementiert (arbeitet nur mit einem Status-Callback fuer
/// Statusmeldungen), damit <see cref="UpdaterViewModel"/> sie einfach im Hintergrund ausfuehren und
/// dabei den UI-Thread nicht blockieren kann.
/// </summary>
internal static class UpdateApplier
{
    /// <summary>Maximale Wartezeit, bis die Hauptanwendung sich beendet haben muss, bevor der
    /// Kopiervorgang trotzdem (mit dem Risiko gesperrter Dateien) versucht wird.</summary>
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Kurze zusaetzliche Verzoegerung nach dem Beenden der Hauptanwendung, damit das
    /// Betriebssystem Datei-Handles (insb. der WPF-nativen Interop-DLLs) zuverlaessig vollstaendig
    /// freigibt, bevor der erste Kopierversuch unternommen wird.</summary>
    private static readonly TimeSpan PostExitDelay = TimeSpan.FromMilliseconds(750);

    /// <summary>Anzahl der Kopierversuche, bevor endgueltig aufgegeben wird - siehe
    /// Klassendokumentation von <c>VirtualController.Core.Updates.UpdateInstaller</c>: die haeufigste
    /// Ursache eines einzelnen fehlgeschlagenen Kopierversuchs ist eine kurze Race Condition, bei der
    /// das Betriebssystem (oder ein Virenscanner) die Datei-Handles der soeben beendeten Anwendung noch
    /// nicht vollstaendig freigegeben hat.</summary>
    private const int MaxCopyAttempts = 5;

    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Wartet auf die Beendigung der Hauptanwendung (<paramref name="processId"/>) und kopiert
    /// anschliessend alle Dateien aus <paramref name="stagingDirectory"/> nach
    /// <paramref name="installDirectory"/> (mit bis zu <see cref="MaxCopyAttempts"/> Versuchen). Meldet
    /// den aktuellen Status ueber <paramref name="statusCallback"/> fuer die Anzeige im Updater-Fenster.
    /// Raeumt das Staging-Verzeichnis nach Abschluss (Erfolg oder endgueltiger Fehlschlag) auf.
    /// </summary>
    public static async Task<ApplyResult> ApplyAsync(
        int processId, string stagingDirectory, string installDirectory, Action<string> statusCallback, CancellationToken cancellationToken)
    {
        UpdaterLog.Write($"Updater-Prozess gestartet. Warte auf Beendigung von Prozess-ID {processId} ...");
        statusCallback("Warte auf Beenden der Anwendung ...");

        await WaitForProcessExitAsync(processId, cancellationToken).ConfigureAwait(false);
        await Task.Delay(PostExitDelay, cancellationToken).ConfigureAwait(false);

        statusCallback("Dateien werden aktualisiert ...");
        var copyResult = await CopyWithRetryAsync(stagingDirectory, installDirectory, cancellationToken).ConfigureAwait(false);

        TryDeleteDirectory(stagingDirectory);
        UpdaterLog.Write($"Staging-Verzeichnis '{stagingDirectory}' aufgeraeumt.");

        return copyResult;
    }

    /// <summary>Wartet, bis <paramref name="processId"/> beendet ist, hoechstens jedoch
    /// <see cref="ProcessExitTimeout"/> - laeuft die Hauptanwendung danach immer noch, wird trotzdem mit
    /// dem Kopieren fortgefahren (Dateien koennen dann ggf. gesperrt sein, siehe Aufrufer).</summary>
    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            UpdaterLog.Write($"Prozess-ID {processId} laeuft noch, warte auf Beendigung (Timeout: {ProcessExitTimeout.TotalSeconds}s) ...");

            using var timeoutCts = new CancellationTokenSource(ProcessExitTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                UpdaterLog.Write($"Prozess-ID {processId} ist beendet.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                UpdaterLog.Write(
                    $"WARNUNG: Prozess-ID {processId} laeuft nach {ProcessExitTimeout.TotalSeconds}s Timeout IMMER NOCH - " +
                    "Kopiervorgang startet trotzdem, Dateien koennen ggf. gesperrt sein.");
            }
        }
        catch (ArgumentException)
        {
            // Process.GetProcessById wirft ArgumentException, wenn die Prozess-ID bereits nicht mehr
            // existiert - das ist der erwartete Normalfall, wenn die Hauptanwendung sich bereits vor dem
            // Start dieses Updater-Prozesses vollstaendig beendet hat.
            UpdaterLog.Write($"Prozess-ID {processId} war bereits beim Start dieses Prozesses nicht mehr vorhanden.");
        }
        catch (Exception ex)
        {
            UpdaterLog.Write($"Fehler beim Warten auf Prozess-ID {processId} (wird ignoriert): {ex.Message}");
        }
    }

    /// <summary>Kopiert rekursiv alle Dateien/Unterordner aus <paramref name="sourceDirectory"/> nach
    /// <paramref name="destinationDirectory"/> (ueberschreibt dabei vorhandene Dateien), mit bis zu
    /// <see cref="MaxCopyAttempts"/> Versuchen bei fehlschlagender Datei-Sperre.</summary>
    private static async Task<ApplyResult> CopyWithRetryAsync(
        string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxCopyAttempts; attempt++)
        {
            try
            {
                CopyDirectoryRecursive(sourceDirectory, destinationDirectory);
                UpdaterLog.Write($"Kopieren erfolgreich abgeschlossen (Versuch {attempt} von {MaxCopyAttempts}).");
                return new ApplyResult(true, null);
            }
            catch (Exception ex) when (attempt < MaxCopyAttempts)
            {
                // Haeufigste Ursache: die soeben beendete Anwendung (bzw. ein Virenscanner, der die
                // frisch beendete exe kurz nachtraeglich scannt) hat die Datei-Handles noch nicht
                // vollstaendig freigegeben - reine Race Condition, die sich durch eine kurze Wartezeit
                // und einen erneuten Versuch in aller Regel selbst behebt.
                UpdaterLog.Write(
                    $"Kopieren fehlgeschlagen (Versuch {attempt} von {MaxCopyAttempts}), naechster Versuch in " +
                    $"{CopyRetryDelay.TotalMilliseconds}ms: {ex.Message}");
                await Task.Delay(CopyRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UpdaterLog.Write($"FEHLER beim Kopieren nach {MaxCopyAttempts} Versuchen, gebe endgueltig auf: {ex}");
                return new ApplyResult(
                    false,
                    "Die aktualisierten Dateien konnten nicht kopiert werden. Bitte die Anwendung manuell " +
                    "erneut aktualisieren oder die Log-Datei fuer Details pruefen.");
            }
        }

        return new ApplyResult(false, "Die aktualisierten Dateien konnten nicht kopiert werden.");
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectory))
        {
            string destinationFilePath = Path.Combine(destinationDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }

        foreach (var sourceSubDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectoryRecursive(sourceSubDirectory, destinationSubDirectory);
        }
    }

    /// <summary>Startet die neu installierte Anwendung - aufgerufen, nachdem der Nutzer im
    /// Updater-Fenster auf "VirtualController starten" geklickt hat.</summary>
    public static void LaunchApplication(string installDirectory, string executableFileName)
    {
        string exePath = Path.Combine(installDirectory, executableFileName);

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installDirectory,
                UseShellExecute = true,
            };
            var process = System.Diagnostics.Process.Start(startInfo);
            UpdaterLog.Write(process is null
                ? $"WARNUNG: Process.Start('{exePath}') hat null zurueckgegeben."
                : $"Anwendung neu gestartet, PID={process.Id}.");
        }
        catch (Exception ex)
        {
            UpdaterLog.Write($"FEHLER beim Neustart der Anwendung ('{exePath}'): {ex}");
            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Aufraeumen ist best-effort - ein fehlgeschlagenes Loeschen eines temporaeren Verzeichnisses
            // darf niemals einen sonst erfolgreichen/fehlgeschlagenen Update-Vorgang ueberdecken.
        }
    }
}

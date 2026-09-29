using System.IO.Compression;
using System.Text;

namespace VirtualController.Core.Updates;

/// <summary>
/// Fuehrt die eigentliche Update-Installation durch: laedt das ZIP-Archiv der neuen Version herunter,
/// entpackt es in ein temporaeres Staging-Verzeichnis und startet anschliessend einen separaten,
/// kurzlebigen PowerShell-Updater-Prozess, der - NACHDEM diese Anwendung sich beendet hat (die
/// laufende EXE/DLLs koennen waehrend der eigenen Laufzeit nicht überschrieben werden) - die neuen
/// Dateien an die Installation kopiert und die Anwendung anschliessend neu startet.
///
/// Bewusst zweigeteilt (<see cref="PrepareAsync"/> vor dem Beenden der Anwendung,
/// <see cref="LaunchUpdaterProcess"/> unmittelbar vor <c>Application.Shutdown()</c>): jeder Fehler
/// waehrend <see cref="PrepareAsync"/> (Download fehlgeschlagen, Archiv beschaedigt) wirft eine
/// <see cref="UpdateInstallException"/>, OHNE dass zu diesem Zeitpunkt bereits irgendeine Datei der
/// laufenden Installation angefasst wurde - die aktuell installierte Version bleibt unveraendert
/// lauffaehig, die Anwendung muss dafuer nicht beendet werden.
/// </summary>
public sealed class UpdateInstaller
{
    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// Laedt das Update-Archiv herunter und entpackt es in ein neues temporaeres Verzeichnis. Wirft
    /// <see cref="UpdateInstallException"/>, falls der Download oder das Entpacken fehlschlaegt (z.B.
    /// Verbindungsabbruch, beschaedigtes/unerwartetes Archiv) - in diesem Fall wurde die laufende
    /// Installation nicht veraendert.
    /// </summary>
    public async Task<UpdateInstallPreparation> PrepareAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "VirtualControllerUpdate_" + Guid.NewGuid().ToString("N"));
        string archivePath = stagingDirectory + ".zip";

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            using (var response = await HttpClientLazy.Value.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(archivePath);
                await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: true);

            string executableFileName = Path.GetFileName(Environment.ProcessPath)
                ?? throw new UpdateInstallException("Der Pfad der aktuell laufenden Anwendung konnte nicht ermittelt werden.");

            if (!File.Exists(Path.Combine(stagingDirectory, executableFileName)))
            {
                throw new UpdateInstallException(
                    $"Das heruntergeladene Update-Archiv enthaelt keine '{executableFileName}' und kann daher nicht installiert werden.");
            }

            string installDirectory = Path.GetDirectoryName(Environment.ProcessPath!)
                ?? throw new UpdateInstallException("Das Installationsverzeichnis der aktuell laufenden Anwendung konnte nicht ermittelt werden.");

            return new UpdateInstallPreparation(
                stagingDirectory,
                installDirectory,
                executableFileName,
                Environment.ProcessId);
        }
        catch (UpdateInstallException)
        {
            CleanupBestEffort(stagingDirectory, archivePath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupBestEffort(stagingDirectory, archivePath);
            throw new UpdateInstallException(
                "Das Update konnte nicht heruntergeladen oder entpackt werden. Bitte Internetverbindung prüfen und erneut versuchen.", ex);
        }
        finally
        {
            // Das Archiv selbst wird nach dem Entpacken nicht mehr benoetigt - nur der entpackte Inhalt
            // in stagingDirectory wird an den Updater-Prozess weitergegeben.
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>
    /// Erzeugt und startet den separaten Updater-Prozess (ein kleines, generiertes PowerShell-Skript):
    /// dieser wartet auf die Beendigung des aktuellen Prozesses (<see cref="UpdateInstallPreparation.ProcessId"/>),
    /// kopiert dann alle Dateien aus dem Staging-Verzeichnis in das Installationsverzeichnis (ueberschreibt
    /// dabei die alte Version), startet die neue EXE und raeumt anschliessend saemtliche temporaeren
    /// Dateien wieder auf. Muss unmittelbar VOR dem eigenen Beenden der Anwendung aufgerufen werden
    /// (z.B. direkt vor <c>Application.Current.Shutdown()</c>), da der laufende Prozess selbst seine
    /// eigene EXE/DLLs nicht ueberschreiben kann.
    /// </summary>
    public void LaunchUpdaterProcess(UpdateInstallPreparation preparation)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"VirtualControllerUpdater_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, BuildUpdaterScript(preparation, scriptPath), Encoding.UTF8);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            // -WindowStyle Hidden: der Updater-Vorgang (Warten + Kopieren) dauert i.d.R. nur wenige
            // Sekunden - ein sichtbares Konsolenfenster wuerde hier nur unnoetig verwirren, ohne dass der
            // Nutzer waehrenddessen sinnvoll eingreifen koennte.
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new UpdateInstallException("Der Update-Installationsprozess konnte nicht gestartet werden.", ex);
        }
    }

    private static string BuildUpdaterScript(UpdateInstallPreparation preparation, string ownScriptPath)
    {
        // Alle Pfade werden ueber PowerShell-Literale eingebettet - einfache Hochkommata escapen sich in
        // PowerShell durch Verdopplung ('' statt \'), was hier fuer beliebige (auch leerzeichenhaltige)
        // Windows-Pfade ausreicht, da keiner der eingebetteten Werte selbst vom Nutzer kontrolliert wird
        // (sie stammen ausschliesslich aus Environment.ProcessPath bzw. Path.GetTempPath()).
        string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        return $$"""
            $ErrorActionPreference = 'Stop'

            # Wartet, bis die aktuell laufende Anwendung (die dieses Skript unmittelbar vor ihrem eigenen
            # Beenden gestartet hat) tatsaechlich beendet ist - erst danach sind EXE/DLLs im
            # Installationsverzeichnis entsperrt und koennen ueberschrieben werden.
            $processId = {{preparation.ProcessId}}
            try {
                $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($proc) {
                    Wait-Process -Id $processId -Timeout 30 -ErrorAction SilentlyContinue
                }
            } catch {}
            # Kurze zusaetzliche Verzoegerung, damit das Betriebssystem Datei-Handles der beendeten
            # Anwendung (insb. der WPF-nativen Interop-DLLs) zuverlaessig vollstaendig freigibt.
            Start-Sleep -Milliseconds 750

            $stagingDir = {{Quote(preparation.StagingDirectory)}}
            $installDir = {{Quote(preparation.InstallDirectory)}}
            $exeName = {{Quote(preparation.ExecutableFileName)}}

            try {
                Copy-Item -Path (Join-Path $stagingDir '*') -Destination $installDir -Recurse -Force
            } finally {
                Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
            }

            Start-Process -FilePath (Join-Path $installDir $exeName) -WorkingDirectory $installDir

            # Raeumt sich selbst auf - das Skript wird nur einmalig fuer genau diese eine Installation benoetigt.
            Start-Sleep -Milliseconds 500
            Remove-Item -Path {{Quote(ownScriptPath)}} -Force -ErrorAction SilentlyContinue
            """;
    }

    private static void CleanupBestEffort(string stagingDirectory, string archivePath)
    {
        TryDeleteFile(archivePath);
        TryDeleteDirectory(stagingDirectory);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Aufraeumen ist best-effort - ein fehlgeschlagenes Loeschen einer temporaeren Datei darf
            // niemals einen sonst erfolgreichen/fehlgeschlagenen Update-Vorgang ueberdecken.
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
            // Siehe TryDeleteFile.
        }
    }
}

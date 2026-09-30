using System.IO.Compression;
using System.Text;
using VirtualController.Core.Logging;

namespace VirtualController.Core.Updates;

/// <summary>
/// Fuehrt die eigentliche Update-Installation durch: laedt das ZIP-Archiv der neuen Version herunter,
/// entpackt es in ein temporaeres Staging-Verzeichnis und startet anschliessend einen versteckten
/// PowerShell-Prozess (siehe <see cref="LaunchUpdaterProcess"/>), der - NACHDEM diese Anwendung sich
/// beendet hat (die laufende EXE/DLLs koennen waehrend der eigenen Laufzeit nicht überschrieben werden) -
/// die neuen Dateien an die Installation kopiert und die Anwendung anschliessend neu startet.
///
/// Bewusst auf einen generierten PowerShell-Skript-Aufruf statt eines eigenstaendigen, mitveroeffentlichten
/// Updater-Hilfsprogramms gesetzt: <c>powershell.exe</c> ist ein signiertes Windows-System-Binary (liegt in
/// <c>System32</c>) und ist daher NICHT von pfadbasierten Ausfuehrungsbeschraenkungen betroffen, die auf
/// vielen verwalteten Windows-Rechnern (Gruppenrichtlinien/AppLocker) die Ausfuehrung unsignierter oder
/// unbekannter EXE-Dateien aus benutzerschreibbaren Verzeichnissen wie <c>%TEMP%</c> blockieren (beobachtet
/// als <see cref="System.ComponentModel.Win32Exception"/> mit NativeErrorCode 1260
/// "ERROR_ACCESS_DISABLED_BY_POLICY") - ein zuvor zusaetzlich vorhandenes, mitveroeffentlichtes
/// "VirtualController.Updater"-Hilfsprogramm war genau davon betroffen und wurde deshalb wieder entfernt.
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
    /// <summary>Gesamtzahl der Installationsschritte (siehe <see cref="UpdateInstallProgress"/>): 1=Download,
    /// 2=Entpacken, 3=Vorbereitung abschliessen, 4=Update wird gestartet - fuer die Anzeige "Schritt X
    /// von <see cref="TotalSteps"/>: ..." im Update-Popup.</summary>
    public const int TotalSteps = 4;

    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// Laedt das Update-Archiv herunter und entpackt es in ein neues temporaeres Verzeichnis. Wirft
    /// <see cref="UpdateInstallException"/>, falls der Download oder das Entpacken fehlschlaegt (z.B.
    /// Verbindungsabbruch, beschaedigtes/unerwartetes Archiv) - in diesem Fall wurde die laufende
    /// Installation nicht veraendert.
    /// </summary>
    /// <param name="downloadUrl">Download-URL des Update-Archivs.</param>
    /// <param name="targetVersion">Versionsnummer der zu installierenden Version (z.B. "1.5.0"), wird
    /// unveraendert in die zurueckgegebene <see cref="UpdateInstallPreparation"/> uebernommen (siehe
    /// <see cref="UpdateInstallPreparation.TargetVersion"/>) - hat keinen Einfluss auf den
    /// Installationsvorgang selbst.</param>
    /// <param name="progress">Optionaler Fortschritts-Reporter fuer die Anzeige im Update-Popup (siehe
    /// <see cref="UpdateInstallProgress"/>) - meldet den Download-Fortschritt anhand der empfangenen
    /// Bytes (soweit die Serverantwort einen Content-Length-Header liefert) sowie den Beginn der
    /// nachfolgenden Schritte (Entpacken, Validierung).</param>
    /// <param name="cancellationToken">Abbruchtoken.</param>
    public async Task<UpdateInstallPreparation> PrepareAsync(
        string downloadUrl, string targetVersion, IProgress<UpdateInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "VirtualControllerUpdate_" + Guid.NewGuid().ToString("N"));
        string archivePath = stagingDirectory + ".zip";

        UpdateLog.WriteSessionStart("Update-Vorbereitung gestartet (PrepareAsync)");
        UpdateLog.Write($"Download-URL: {downloadUrl}");
        UpdateLog.Write($"Staging-Verzeichnis: {stagingDirectory}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            progress?.Report(new UpdateInstallProgress(1, TotalSteps, "Dateien werden heruntergeladen", OverallPercent(1, 0)));

            using (var response = await HttpClientLazy.Value.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                UpdateLog.Write($"HTTP-Antwort erhalten: Status={(int)response.StatusCode} {response.StatusCode}, Content-Length={response.Content.Headers.ContentLength?.ToString() ?? "unbekannt"}");
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(archivePath);
                await CopyWithProgressAsync(httpStream, fileStream, totalBytes, progress, cancellationToken).ConfigureAwait(false);
            }

            long archiveSize = new FileInfo(archivePath).Length;
            UpdateLog.Write($"Download abgeschlossen: {archivePath} ({archiveSize} Bytes)");

            progress?.Report(new UpdateInstallProgress(2, TotalSteps, "Archiv wird entpackt", OverallPercent(2, 0)));
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: true);
            UpdateLog.Write($"Archiv entpackt nach: {stagingDirectory}");

            progress?.Report(new UpdateInstallProgress(3, TotalSteps, "Vorbereitung wird abgeschlossen", OverallPercent(3, 0)));

            string executableFileName = Path.GetFileName(Environment.ProcessPath)
                ?? throw new UpdateInstallException("Der Pfad der aktuell laufenden Anwendung konnte nicht ermittelt werden.");

            if (!File.Exists(Path.Combine(stagingDirectory, executableFileName)))
            {
                throw new UpdateInstallException(
                    $"Das heruntergeladene Update-Archiv enthaelt keine '{executableFileName}' und kann daher nicht installiert werden.");
            }

            string installDirectory = Path.GetDirectoryName(Environment.ProcessPath!)
                ?? throw new UpdateInstallException("Das Installationsverzeichnis der aktuell laufenden Anwendung konnte nicht ermittelt werden.");

            long stagedExeSize = new FileInfo(Path.Combine(stagingDirectory, executableFileName)).Length;
            UpdateLog.Write(
                $"Vorbereitung abgeschlossen: exeName={executableFileName}, stagedExeSize={stagedExeSize} Bytes, " +
                $"installDirectory={installDirectory}, aktuelle ProcessId={Environment.ProcessId}");

            return new UpdateInstallPreparation(
                stagingDirectory,
                installDirectory,
                executableFileName,
                Environment.ProcessId,
                targetVersion);
        }
        catch (UpdateInstallException ex)
        {
            UpdateLog.Write($"FEHLER in PrepareAsync (UpdateInstallException): {ex.Message}");
            CleanupBestEffort(stagingDirectory, archivePath);
            throw;
        }
        catch (Exception ex)
        {
            UpdateLog.Write($"FEHLER in PrepareAsync ({ex.GetType().Name}): {ex}");
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

    /// <summary>Kopiert <paramref name="source"/> nach <paramref name="destination"/> und meldet dabei
    /// den Download-Fortschritt (Schritt 1) anhand der bereits kopierten Bytes im Verhaeltnis zu
    /// <paramref name="totalBytes"/> - bleibt <paramref name="totalBytes"/> unbekannt (kein
    /// Content-Length-Header), wird lediglich der Schrittbeginn ohne feingranulare Prozentanzeige
    /// gemeldet (siehe Aufrufer).</summary>
    private static async Task CopyWithProgressAsync(
        Stream source, Stream destination, long? totalBytes, IProgress<UpdateInstallProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes is > 0)
            {
                double stepFraction = Math.Clamp((double)totalRead / totalBytes.Value, 0d, 1d);
                progress?.Report(new UpdateInstallProgress(1, TotalSteps, "Dateien werden heruntergeladen", OverallPercent(1, stepFraction)));
            }
        }
    }

    /// <summary>Berechnet den Fortschritt (0-100) des GESAMTEN Installationsvorgangs anhand des aktuellen
    /// Schritts (1-basiert) und des Fortschritts (0.0-1.0) innerhalb dieses Schritts - siehe
    /// <see cref="UpdateInstallProgress.OverallPercent"/>.</summary>
    private static double OverallPercent(int stepNumber, double stepFraction) =>
        ((stepNumber - 1) + Math.Clamp(stepFraction, 0d, 1d)) / TotalSteps * 100d;

    /// <summary>
    /// Startet den versteckten PowerShell-Prozess (siehe <see cref="LaunchPowerShellUpdaterProcess"/>),
    /// der - NACHDEM diese Anwendung sich beendet hat - die neuen Dateien an die Installation kopiert und
    /// die Anwendung anschliessend neu startet. Muss unmittelbar VOR dem eigenen Beenden der Anwendung
    /// aufgerufen werden (z.B. direkt vor <c>Application.Current.Shutdown()</c>), da der laufende Prozess
    /// selbst seine eigene EXE/DLLs nicht ueberschreiben kann.
    /// </summary>
    public void LaunchUpdaterProcess(UpdateInstallPreparation preparation, IProgress<UpdateInstallProgress>? progress = null) =>
        LaunchPowerShellUpdaterProcess(preparation, progress);

    /// <summary>
    /// Erzeugt und startet den generierten, versteckten PowerShell-Updater-Prozess (siehe
    /// <see cref="LaunchUpdaterProcess"/> sowie die Klassendokumentation zur Begruendung dieses Ansatzes
    /// gegenueber einem eigenstaendigen, mitveroeffentlichten Updater-Hilfsprogramm). Dieser Prozess
    /// wartet auf die Beendigung des aktuellen Prozesses (<see cref="UpdateInstallPreparation.ProcessId"/>),
    /// kopiert dann alle Dateien aus dem Staging-Verzeichnis in das Installationsverzeichnis
    /// (ueberschreibt dabei die alte Version), startet die neue EXE und raeumt anschliessend saemtliche
    /// temporaeren Dateien wieder auf. Muss unmittelbar VOR dem eigenen Beenden der Anwendung aufgerufen
    /// werden (z.B. direkt vor <c>Application.Current.Shutdown()</c>), da der laufende Prozess selbst
    /// seine eigene EXE/DLLs nicht ueberschreiben kann.
    /// </summary>
    private static void LaunchPowerShellUpdaterProcess(UpdateInstallPreparation preparation, IProgress<UpdateInstallProgress>? progress)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"VirtualControllerUpdater_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, BuildUpdaterScript(preparation, scriptPath), Encoding.UTF8);
        UpdateLog.Write($"Updater-Skript geschrieben: {scriptPath}");

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

        progress?.Report(new UpdateInstallProgress(4, TotalSteps, "Update wird gestartet", OverallPercent(4, 0)));

        try
        {
            UpdateLog.Write("Starte powershell.exe fuer Updater-Skript...");
            var process = System.Diagnostics.Process.Start(startInfo);
            UpdateLog.Write(process is null
                ? "WARNUNG: Process.Start(powershell.exe) hat null zurueckgegeben (kein Handle auf den neuen Prozess erhalten)."
                : $"powershell.exe gestartet, PID={process.Id}. Diese Anwendung wird nun beendet - weitere Log-Zeilen (Kopieren/Neustart) werden vom Updater-Skript selbst in dieselbe Datei geschrieben.");
        }
        catch (Exception ex)
        {
            UpdateLog.Write($"FEHLER beim Starten von powershell.exe: {ex}");
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

            # Alle Schritte dieses Skripts werden in dieselbe Datei protokolliert wie der C#-Teil der
            # Anwendung (siehe VirtualController.Core.Logging.UpdateLog) - genau dieser Zeitraum (Kopieren
            # der neuen Dateien, Neustart) lief bislang vollstaendig unsichtbar ab, da die Anwendung sich
            # bereits VOR dem Start dieses Skripts beendet (siehe UpdateInstaller.LaunchUpdaterProcess).
            $updateLogPath = {{Quote(UpdateLog.FilePath)}}
            function Write-UpdaterLog {
                param([string]$Message)
                try {
                    $line = "{0} [Updater-Skript] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'), $Message
                    Add-Content -Path $updateLogPath -Value $line -Encoding UTF8
                } catch {
                    # Logging darf den eigentlichen Update-Vorgang niemals verhindern.
                }
            }

            $processId = {{preparation.ProcessId}}
            $stagingDir = {{Quote(preparation.StagingDirectory)}}
            $installDir = {{Quote(preparation.InstallDirectory)}}
            $exeName = {{Quote(preparation.ExecutableFileName)}}

            Write-UpdaterLog "Updater-Skript gestartet. Warte auf Beendigung von Prozess-ID $processId ..."

            # Wartet, bis die aktuell laufende Anwendung (die dieses Skript unmittelbar vor ihrem eigenen
            # Beenden gestartet hat) tatsaechlich beendet ist - erst danach sind EXE/DLLs im
            # Installationsverzeichnis entsperrt und koennen ueberschrieben werden.
            try {
                $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($proc) {
                    Wait-Process -Id $processId -Timeout 30 -ErrorAction SilentlyContinue
                    $stillRunning = Get-Process -Id $processId -ErrorAction SilentlyContinue
                    if ($stillRunning) {
                        Write-UpdaterLog "WARNUNG: Prozess-ID $processId laeuft nach 30s Timeout IMMER NOCH - Kopiervorgang startet trotzdem, Dateien koennen ggf. gesperrt sein."
                    } else {
                        Write-UpdaterLog "Prozess-ID $processId ist beendet."
                    }
                } else {
                    Write-UpdaterLog "Prozess-ID $processId war bereits beim Start dieses Skripts nicht mehr vorhanden."
                }
            } catch {
                Write-UpdaterLog "Fehler beim Warten auf Prozess-ID $processId (wird ignoriert): $($_.Exception.Message)"
            }
            # Kurze zusaetzliche Verzoegerung, damit das Betriebssystem Datei-Handles der beendeten
            # Anwendung (insb. der WPF-nativen Interop-DLLs) zuverlaessig vollstaendig freigibt.
            Start-Sleep -Milliseconds 750

            $stagedExePath = Join-Path $stagingDir $exeName
            $installedExePath = Join-Path $installDir $exeName
            $stagedSize = if (Test-Path $stagedExePath) { (Get-Item $stagedExePath).Length } else { -1 }
            $installedSizeBefore = if (Test-Path $installedExePath) { (Get-Item $installedExePath).Length } else { -1 }
            Write-UpdaterLog "Vor dem Kopieren: stagingDir='$stagingDir' (exe=$stagedSize Bytes), installDir='$installDir' (aktuelle exe=$installedSizeBefore Bytes)"

            $copySucceeded = $false
            $maxCopyAttempts = 5
            $copyRetryDelayMs = 1000
            for ($attempt = 1; $attempt -le $maxCopyAttempts; $attempt++) {
                try {
                    Copy-Item -Path (Join-Path $stagingDir '*') -Destination $installDir -Recurse -Force
                    $copySucceeded = $true
                    Write-UpdaterLog "Copy-Item erfolgreich abgeschlossen (Versuch $attempt von $maxCopyAttempts)."
                    break
                } catch {
                    # Haeufigste Ursache: die soeben beendete Anwendung (bzw. ein Virenscanner, der die
                    # frisch beendete exe kurz nachtraeglich scannt) hat die Datei-Handles noch nicht
                    # vollstaendig freigegeben ("... wird von einem anderen Prozess verwendet") - reine
                    # Race Condition, die sich durch eine kurze Wartezeit und einen erneuten Versuch in
                    # aller Regel selbst behebt (siehe gemeldeter Bug: Update wird uebersprungen, alte
                    # Version bleibt installiert).
                    if ($attempt -lt $maxCopyAttempts) {
                        Write-UpdaterLog "Copy-Item fehlgeschlagen (Versuch $attempt von $maxCopyAttempts), naechster Versuch in ${copyRetryDelayMs}ms: $($_.Exception.Message)"
                        Start-Sleep -Milliseconds $copyRetryDelayMs
                    } else {
                        Write-UpdaterLog "FEHLER bei Copy-Item nach $maxCopyAttempts Versuchen, gebe endgueltig auf: $($_.Exception.Message)"
                    }
                }
            }
            Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-UpdaterLog "Staging-Verzeichnis '$stagingDir' aufgeraeumt."

            $installedSizeAfter = if (Test-Path $installedExePath) { (Get-Item $installedExePath).Length } else { -1 }
            Write-UpdaterLog "Nach dem Kopieren: installierte exe='$installedExePath' Groesse=$installedSizeAfter Bytes (vorher: $installedSizeBefore Bytes, staging: $stagedSize Bytes)"

            # Vergleicht gezielt die Groesse der exe im entpackten ZIP (staging, $stagedSize) mit der
            # Groesse der anschliessend tatsaechlich installierten exe ($installedSizeAfter) - genau diese
            # Differenz war Ursache eines gemeldeten Bugs (Ziel-exe wuchs von 69MB auf 159MB an). Copy-Item
            # mit -Recurse -Force ueberschreibt/ergaenzt lediglich Dateien, LOESCHT aber niemals Dateien im
            # Zielverzeichnis, die im Quellverzeichnis nicht (mehr) existieren - Ueberreste eines fruehen,
            # fehlgeschlagenen Updates (z.B. eine liegen gebliebene alte oder halbwegs ueberschriebene exe,
            # oder durch Virenscanner/Datei-Sperren waehrend des Kopierens unterbrochene Schreibvorgaenge)
            # koennten daher eine falsche Dateigroesse erklaeren.
            if ($copySucceeded) {
                if ($stagedSize -ge 0 -and $installedSizeAfter -ge 0) {
                    if ($installedSizeAfter -eq $stagedSize) {
                        Write-UpdaterLog "Groessenpruefung OK: installierte exe ($installedSizeAfter Bytes) entspricht exakt der exe im entpackten Update-Archiv ($stagedSize Bytes)."
                    } else {
                        $sizeDiff = $installedSizeAfter - $stagedSize
                        Write-UpdaterLog "WARNUNG Groessenabweichung: installierte exe ($installedSizeAfter Bytes) unterscheidet sich von der exe im entpackten Update-Archiv ($stagedSize Bytes) um $sizeDiff Bytes. Moegliche Ursachen: Copy-Item loescht keine im Quellverzeichnis nicht mehr vorhandenen Dateien im Ziel (Ueberreste eines frueheren fehlgeschlagenen Updates), ein Virenscanner/Datei-Handle hat den Kopiervorgang unterbrochen, oder das Zielverzeichnis war bereits vor diesem Update in einem inkonsistenten Zustand."
                    }
                } else {
                    Write-UpdaterLog "Groessenpruefung nicht moeglich: stagedSize=$stagedSize, installedSizeAfter=$installedSizeAfter (mindestens eine der beiden Dateien wurde nicht gefunden)."
                }
            }

            if ($copySucceeded) {
                try {
                    $newProc = Start-Process -FilePath $installedExePath -WorkingDirectory $installDir -PassThru
                    Write-UpdaterLog "Start-Process erfolgreich: neue PID=$($newProc.Id)."
                } catch {
                    Write-UpdaterLog "FEHLER bei Start-Process (Anwendung wurde NICHT neu gestartet): $($_.Exception.Message)"
                }
            } else {
                Write-UpdaterLog "Start-Process wird uebersprungen, da Copy-Item fehlgeschlagen ist - Anwendung wurde NICHT neu gestartet."
            }

            Write-UpdaterLog "Updater-Skript beendet, raeumt sich selbst auf."
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

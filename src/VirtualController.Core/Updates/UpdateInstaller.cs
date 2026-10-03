using System.IO.Compression;
using System.Text;
using VirtualController.Core.Logging;

namespace VirtualController.Core.Updates;

/// <summary>
/// Performs the update installation: downloads the new version's ZIP archive, extracts it to a temporary
/// staging directory, then starts a hidden PowerShell process (see <see cref="LaunchUpdaterProcess"/>). After
/// this application exits (its executable and DLLs cannot be overwritten while it is running), the process
/// copies the new files into the installation and restarts the application.
///
/// Uses a generated PowerShell script instead of a separately shipped updater executable:
/// <c>powershell.exe</c> is a signed Windows system binary in <c>System32</c>, so it is not affected by
/// path-based execution restrictions used on many managed Windows machines (Group Policy/AppLocker) to block
/// unsigned or unknown executables from user-writable directories such as <c>%TEMP%</c>. A previously shipped
/// "VirtualController.Updater" helper was blocked by these restrictions and was removed.
///
/// The process is deliberately split into two phases (<see cref="PrepareAsync"/> before the application exits,
/// <see cref="LaunchUpdaterProcess"/> immediately before <c>Application.Shutdown()</c>). Any failure during
/// <see cref="PrepareAsync"/> (such as a failed download or corrupted archive) throws an
/// <see cref="UpdateInstallException"/> before touching installed files, leaving the current version usable.
/// </summary>
public sealed class UpdateInstaller
{
    /// <summary>Total number of installation steps (see <see cref="UpdateInstallProgress"/>): 1=download,
    /// 2=extract, 3=finish preparation, 4=start update. Used to display "Step X of <see cref="TotalSteps"/>: ..."
    /// in the update dialog.</summary>
    public const int TotalSteps = 4;

    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// Downloads the update archive and extracts it to a new temporary directory. Throws
    /// <see cref="UpdateInstallException"/> if the download or extraction fails (e.g. connection loss or a
    /// corrupted/unexpected archive); the current installation remains unchanged.
    /// </summary>
    /// <param name="downloadUrl">Download URL of the update archive.</param>
    /// <param name="targetVersion">Version to install (e.g. "1.5.0"), passed unchanged to the returned
    /// <see cref="UpdateInstallPreparation"/> (see <see cref="UpdateInstallPreparation.TargetVersion"/>); it
    /// does not affect the installation process.</param>
    /// <param name="progress">Optional progress reporter for the update dialog (see
    /// <see cref="UpdateInstallProgress"/>). Reports download progress based on received bytes (when the
    /// server provides a Content-Length header) and the start of subsequent steps (extraction, validation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpdateInstallPreparation> PrepareAsync(
        string downloadUrl, string targetVersion, IProgress<UpdateInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "VirtualControllerUpdate_" + Guid.NewGuid().ToString("N"));
        string archivePath = stagingDirectory + ".zip";

        UpdateLog.WriteSessionStart("Update preparation started (PrepareAsync)");
        UpdateLog.Write($"Download URL: {downloadUrl}");
        UpdateLog.Write($"Staging directory: {stagingDirectory}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            progress?.Report(new UpdateInstallProgress(1, TotalSteps, "Downloading files", OverallPercent(1, 0)));

            using (var response = await HttpClientLazy.Value.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                UpdateLog.Write($"HTTP response received: Status={(int)response.StatusCode} {response.StatusCode}, Content-Length={response.Content.Headers.ContentLength?.ToString() ?? "unknown"}");
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(archivePath);
                await CopyWithProgressAsync(httpStream, fileStream, totalBytes, progress, cancellationToken).ConfigureAwait(false);
            }

            long archiveSize = new FileInfo(archivePath).Length;
            UpdateLog.Write($"Download completed: {archivePath} ({archiveSize} bytes)");

            progress?.Report(new UpdateInstallProgress(2, TotalSteps, "Extracting archive", OverallPercent(2, 0)));
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: true);
            UpdateLog.Write($"Archive extracted to: {stagingDirectory}");

            progress?.Report(new UpdateInstallProgress(3, TotalSteps, "Finishing preparation", OverallPercent(3, 0)));

            string executableFileName = Path.GetFileName(Environment.ProcessPath)
                ?? throw new UpdateInstallException("Could not determine the path of the currently running application.");

            if (!File.Exists(Path.Combine(stagingDirectory, executableFileName)))
            {
                throw new UpdateInstallException(
                    $"The downloaded update archive does not contain '{executableFileName}' and cannot be installed.");
            }

            string installDirectory = Path.GetDirectoryName(Environment.ProcessPath!)
                ?? throw new UpdateInstallException("Could not determine the installation directory of the currently running application.");

            long stagedExeSize = new FileInfo(Path.Combine(stagingDirectory, executableFileName)).Length;
            UpdateLog.Write(
                $"Preparation completed: exeName={executableFileName}, stagedExeSize={stagedExeSize} bytes, " +
                $"installDirectory={installDirectory}, current processId={Environment.ProcessId}");

            return new UpdateInstallPreparation(
                stagingDirectory,
                installDirectory,
                executableFileName,
                Environment.ProcessId,
                targetVersion);
        }
        catch (UpdateInstallException ex)
        {
            UpdateLog.Write($"ERROR in PrepareAsync (UpdateInstallException): {ex.Message}");
            CleanupBestEffort(stagingDirectory, archivePath);
            throw;
        }
        catch (Exception ex)
        {
            UpdateLog.Write($"ERROR in PrepareAsync ({ex.GetType().Name}): {ex}");
            CleanupBestEffort(stagingDirectory, archivePath);
            throw new UpdateInstallException(
                "The update could not be downloaded or extracted. Check your internet connection and try again.", ex);
        }
        finally
        {
            // The archive is no longer needed after extraction; only the extracted contents of
            // stagingDirectory are passed to the updater process.
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/> and reports download
    /// progress (step 1) based on bytes copied relative to <paramref name="totalBytes"/>. If
    /// <paramref name="totalBytes"/> is unknown (no Content-Length header), only the step start is reported,
    /// without granular percentage updates (see caller).</summary>
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
                progress?.Report(new UpdateInstallProgress(1, TotalSteps, "Downloading files", OverallPercent(1, stepFraction)));
            }
        }
    }

    /// <summary>Calculates progress (0-100) across the entire installation based on the current step
    /// (1-based) and progress within that step (0.0-1.0); see <see cref="UpdateInstallProgress.OverallPercent"/>.</summary>
    private static double OverallPercent(int stepNumber, double stepFraction) =>
        ((stepNumber - 1) + Math.Clamp(stepFraction, 0d, 1d)) / TotalSteps * 100d;

    /// <summary>
    /// Starts the hidden PowerShell process (see <see cref="LaunchPowerShellUpdaterProcess"/>), which copies
    /// the new files into the installation and restarts the application after this application exits. Must be
    /// called immediately before shutting down the application (e.g. directly before
    /// <c>Application.Current.Shutdown()</c>), because a running process cannot overwrite its own executable/DLLs.
    /// </summary>
    public void LaunchUpdaterProcess(UpdateInstallPreparation preparation, IProgress<UpdateInstallProgress>? progress = null) =>
        LaunchPowerShellUpdaterProcess(preparation, progress);

    /// <summary>
    /// Creates and starts the generated, hidden PowerShell updater process (see <see cref="LaunchUpdaterProcess"/>
    /// and the class documentation for why this approach is used instead of a separately shipped updater).
    /// The process waits for the current process (<see cref="UpdateInstallPreparation.ProcessId"/>) to exit,
    /// copies all files from the staging directory into the installation directory (replacing the old version),
    /// starts the new executable, and removes temporary files. Must be called immediately before shutting down
    /// the application (e.g. directly before <c>Application.Current.Shutdown()</c>), because a running process
    /// cannot overwrite its own executable/DLLs.
    /// </summary>
    private static void LaunchPowerShellUpdaterProcess(UpdateInstallPreparation preparation, IProgress<UpdateInstallProgress>? progress)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"VirtualControllerUpdater_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, BuildUpdaterScript(preparation, scriptPath), Encoding.UTF8);
        UpdateLog.Write($"Updater script written: {scriptPath}");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            // -WindowStyle Hidden: the updater (waiting + copying) usually takes only a few seconds. A visible
            // console window would be confusing, and the user cannot meaningfully interact with it.
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        progress?.Report(new UpdateInstallProgress(4, TotalSteps, "Starting update", OverallPercent(4, 0)));

        try
        {
            UpdateLog.Write("Starting powershell.exe for updater script...");
            var process = System.Diagnostics.Process.Start(startInfo);
            UpdateLog.Write(process is null
                ? "WARNING: Process.Start(powershell.exe) returned null (no handle to the new process was received)."
                : $"powershell.exe started, PID={process.Id}. This application will now exit; subsequent log entries (copy/restart) are written to the same file by the updater script.");
        }
        catch (Exception ex)
        {
            UpdateLog.Write($"ERROR starting powershell.exe: {ex}");
            throw new UpdateInstallException("The update installation process could not be started.", ex);
        }
    }

    private static string BuildUpdaterScript(UpdateInstallPreparation preparation, string ownScriptPath)
    {
        // Paths are embedded as PowerShell literals. PowerShell escapes single quotes by doubling them (''),
        // which works for arbitrary Windows paths, including paths with spaces. None of these values is
        // controlled by the user; they come from Environment.ProcessPath or Path.GetTempPath().
        string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        return $$"""
            $ErrorActionPreference = 'Stop'

            # All steps in this script are logged to the same file as the application's C# code
            # (see VirtualController.Core.Logging.UpdateLog). Copying the new files and restarting were
            # previously invisible because the application exits before this script starts
            # (see UpdateInstaller.LaunchUpdaterProcess).
            $updateLogPath = {{Quote(UpdateLog.FilePath)}}
            function Write-UpdaterLog {
                param([string]$Message)
                try {
                    $line = "{0} [Updater-Skript] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'), $Message
                    Add-Content -Path $updateLogPath -Value $line -Encoding UTF8
                } catch {
                    # Logging must never prevent the update itself.
                }
            }

            $processId = {{preparation.ProcessId}}
            $stagingDir = {{Quote(preparation.StagingDirectory)}}
            $installDir = {{Quote(preparation.InstallDirectory)}}
            $exeName = {{Quote(preparation.ExecutableFileName)}}

            Write-UpdaterLog "Updater script started. Waiting for process ID $processId to exit..."

            # Wait until the currently running application (which started this script immediately before
            # exiting) has actually terminated. Only then are the executable/DLLs in the installation
            # directory unlocked and ready to be replaced.
            try {
                $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($proc) {
                    Wait-Process -Id $processId -Timeout 30 -ErrorAction SilentlyContinue
                    $stillRunning = Get-Process -Id $processId -ErrorAction SilentlyContinue
                    if ($stillRunning) {
                        Write-UpdaterLog "WARNING: Process ID $processId is STILL RUNNING after a 30s timeout; starting the copy anyway, but files may be locked."
                    } else {
                        Write-UpdaterLog "Process ID $processId has exited."
                    }
                } else {
                    Write-UpdaterLog "Process ID $processId was already absent when this script started."
                }
            } catch {
                Write-UpdaterLog "Error while waiting for process ID $processId (ignored): $($_.Exception.Message)"
            }
            # Wait briefly to let the operating system fully release file handles held by the terminated
            # application, especially those for native WPF interop DLLs.
            Start-Sleep -Milliseconds 750

            $stagedExePath = Join-Path $stagingDir $exeName
            $installedExePath = Join-Path $installDir $exeName
            $stagedSize = if (Test-Path $stagedExePath) { (Get-Item $stagedExePath).Length } else { -1 }
            $installedSizeBefore = if (Test-Path $installedExePath) { (Get-Item $installedExePath).Length } else { -1 }
            Write-UpdaterLog "Before copy: stagingDir='$stagingDir' (exe=$stagedSize bytes), installDir='$installDir' (current exe=$installedSizeBefore bytes)"

            $copySucceeded = $false
            $maxCopyAttempts = 5
            $copyRetryDelayMs = 1000
            for ($attempt = 1; $attempt -le $maxCopyAttempts; $attempt++) {
                try {
                    Copy-Item -Path (Join-Path $stagingDir '*') -Destination $installDir -Recurse -Force
                    $copySucceeded = $true
                    Write-UpdaterLog "Copy-Item completed successfully (attempt $attempt of $maxCopyAttempts)."
                    break
                } catch {
                    # Most common cause: the just-terminated application (or an antivirus scanning its
                    # executable) has not fully released its file handles ("... is being used by another
                    # process"). This race condition usually resolves after a short delay and retry (see
                    # reported bug: update was skipped and the old version remained installed).
                    if ($attempt -lt $maxCopyAttempts) {
                        Write-UpdaterLog "Copy-Item failed (attempt $attempt of $maxCopyAttempts); retrying in ${copyRetryDelayMs}ms: $($_.Exception.Message)"
                        Start-Sleep -Milliseconds $copyRetryDelayMs
                    } else {
                        Write-UpdaterLog "ERROR: Copy-Item failed after $maxCopyAttempts attempts; giving up: $($_.Exception.Message)"
                    }
                }
            }
            Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-UpdaterLog "Cleaned staging directory '$stagingDir'."

            $installedSizeAfter = if (Test-Path $installedExePath) { (Get-Item $installedExePath).Length } else { -1 }
            Write-UpdaterLog "After copy: installed exe='$installedExePath' size=$installedSizeAfter bytes (before: $installedSizeBefore bytes, staging: $stagedSize bytes)"

            # Compare the executable size in the extracted ZIP ($stagedSize) with the size actually installed
            # ($installedSizeAfter). This difference caused a reported bug (the target executable grew from
            # 69 MB to 159 MB). Copy-Item -Recurse -Force overwrites/adds files but does not delete files from
            # the destination that are absent from the source. Leftovers from an earlier failed update (such as
            # an old or partially overwritten executable, or writes interrupted by antivirus/file locks) could
            # therefore explain an incorrect file size.
            if ($copySucceeded) {
                if ($stagedSize -ge 0 -and $installedSizeAfter -ge 0) {
                    if ($installedSizeAfter -eq $stagedSize) {
                        Write-UpdaterLog "Size check OK: installed executable ($installedSizeAfter bytes) exactly matches the executable in the extracted update archive ($stagedSize bytes)."
                    } else {
                        $sizeDiff = $installedSizeAfter - $stagedSize
                        Write-UpdaterLog "WARNING: Size mismatch: installed executable ($installedSizeAfter bytes) differs from the executable in the extracted update archive ($stagedSize bytes) by $sizeDiff bytes. Possible causes: Copy-Item does not remove destination files absent from the source (leftovers from an earlier failed update), antivirus/file handles interrupted copying, or the destination was already inconsistent before this update."
                    }
                } else {
                    Write-UpdaterLog "Size check unavailable: stagedSize=$stagedSize, installedSizeAfter=$installedSizeAfter (at least one of the files was not found)."
                }
            }

            if ($copySucceeded) {
                try {
                    $newProc = Start-Process -FilePath $installedExePath -WorkingDirectory $installDir -PassThru
                    Write-UpdaterLog "Start-Process succeeded: new PID=$($newProc.Id)."
                } catch {
                    Write-UpdaterLog "ERROR: Start-Process failed (application was NOT restarted): $($_.Exception.Message)"
                }
            } else {
                Write-UpdaterLog "Skipping Start-Process because Copy-Item failed; application was NOT restarted."
            }

            Write-UpdaterLog "Updater script finished; cleaning itself up."
            # The script is only needed once for this installation, so remove it after completion.
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
            // Cleanup is best-effort; failure to delete a temporary file must never mask the outcome of
            // the update operation.
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
            // See TryDeleteFile.
        }
    }
}

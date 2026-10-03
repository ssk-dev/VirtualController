using System.Text;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Logging;

/// <summary>
/// Simple, robust file logging for the update installation process (see <see cref="Updates.UpdateInstaller"/>
/// and its generated PowerShell updater script). Writes timestamped lines immediately (no buffering or async)
/// to "%AppData%\VirtualController\update.log". Kept separate from the app project's <c>DebugLog</c> because
/// <see cref="Updates.UpdateInstaller"/> is in the WPF-independent Core project and must continue logging from
/// the separate generated PowerShell process after the app exits. This covers the period when files are copied
/// and the app restarts, which previously had no diagnostics (see <see cref="Updates.UpdateInstaller"/> docs).
///
/// Does not reset the file on every call/app startup like <c>DebugLog</c>; it appends so a failed update
/// attempt (where the app does not restart) is not overwritten by the next manual restart/update before the
/// user can inspect it. To prevent unbounded growth, the file is discarded and restarted when it exceeds
/// <see cref="MaxFileSizeBytes"/> (see <see cref="TrimIfTooLarge"/>). Write errors are swallowed; logging must
/// never crash either the app or updater script.
/// </summary>
public static class UpdateLog
{
    /// <summary>When the log exceeds this size, the next <see cref="WriteSessionStart"/> discards and recreates
    /// it to prevent unbounded growth across many update checks.</summary>
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly object Lock = new();

    /// <summary>Full path to the update log. Also written to the log itself (see
    /// <see cref="WriteSessionStart"/>) and can be shown on the Settings tab so users can find it after a failed
    /// update without searching.</summary>
    public static string FilePath { get; } = Path.Combine(ProfileStore.BaseDirectory, "update.log");

    /// <summary>Appends a timestamped line to the log. Called by both the running C# app and the separate
    /// updater process using the same line format reproduced in the generated PowerShell script (see
    /// <see cref="Updates.UpdateInstaller"/>), keeping both sides of the update chronological in one file.</summary>
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
            // Logging must never crash the application.
        }
    }

    /// <summary>Appends a clear separator for a new update attempt without deleting previous attempts (see
    /// class documentation), unless the file has exceeded <see cref="MaxFileSizeBytes"/>, in which case it is
    /// discarded here.</summary>
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
            // Logging must never crash the application.
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
                    $"===== Previous update log discarded because it exceeded the size limit @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Best effort; failure to trim must not prevent logging.
        }
    }
}

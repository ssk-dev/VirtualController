using System;
using System.IO;
using System.Text;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Minimal, robust file logging for diagnosing target type/value changes appearing on the wrong mapping row.
/// Writes timestamped lines immediately (no buffering or async) to %AppData%\VirtualController\debug.log.
/// After reproducing an issue in the UI, inspect the file to trace relevant view-model changes and determine
/// which mapping row set or received each value. Implemented as a simple static class without dependency
/// injection so any code can use it without constructor changes. Write failures are swallowed; logging must
/// never crash the application.
/// </summary>
public static class DebugLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualController", "debug.log");

    private static readonly object Lock = new();

    /// <summary>Appends a new line with a timestamp and thread ID to the log file.</summary>
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
            // Logging must never crash the application.
        }
    }

    /// <summary>Clears the log file and writes a separator, e.g. when starting a new diagnostic session.</summary>
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
            // Logging must never crash the application.
        }
    }
}

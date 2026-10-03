using Microsoft.Win32;

namespace VirtualController.App.Services;

internal static class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VirtualController";

    public static void SetEnabled(bool enabled, bool startMinimized)
    {
        if (!enabled)
        {
            using var existingRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingRunKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the path of the running application.");
        var command = $"\"{executablePath}\"{(startMinimized ? " --minimized" : string.Empty)}";

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");
        runKey.SetValue(ValueName, command, RegistryValueKind.String);
    }
}

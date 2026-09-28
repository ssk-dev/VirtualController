using System.Windows;
using System.Windows.Forms;
using VirtualController.App.Diagnostics;
using Application = System.Windows.Application;

namespace VirtualController.App;

/// <summary>
/// Anwendungs-Einstiegspunkt. Verwaltet zusaetzlich das Tray-Icon (ueber WinForms'
/// <see cref="NotifyIcon"/>, da WPF selbst keine native Tray-Icon-Unterstuetzung bietet) und
/// stellt sicher, dass alle laufenden virtuellen Controller beim Beenden sauber abgemeldet
/// werden (ViGEmBus entfernt die Geraete sonst erst beim Prozessende).
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DebugLog.Reset("App-Start");
        DebugLog.Write($"Debug-Log-Datei: {DebugLog.FilePath}");

        _trayIcon = new NotifyIcon
        {
            // Verwendet dasselbe Icon wie die EXE selbst (siehe ApplicationIcon in
            // VirtualController.App.csproj) statt des generischen Windows-Standardsymbols - so zeigen
            // Taskleiste, Alt+Tab UND das Tray-Icon einheitlich das App-Logo. ExtractAssociatedIcon liest
            // das bereits in die EXE eingebettete Icon direkt aus der laufenden Datei, ohne dass eine
            // separate .ico-Datei mit ausgeliefert werden muss.
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
                ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Virtual Controller"
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Oeffnen", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add("Beenden", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = contextMenu;
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}

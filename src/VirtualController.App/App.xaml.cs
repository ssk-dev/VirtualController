using System.Windows;
using System.Windows.Forms;
using VirtualController.App.Diagnostics;
using VirtualController.App.Services;
using VirtualController.Core.Profiles;
using Application = System.Windows.Application;

namespace VirtualController.App;

/// <summary>
/// Application entry point. Also manages the tray icon through WinForms' <see cref="NotifyIcon"/> because
/// WPF has no native tray icon support, and ensures running virtual controllers are cleanly removed on exit
/// (otherwise ViGEmBus removes the devices only when the process terminates).
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _trayIcon;

    public bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appProfile = ProfileStore.Load();
        TranslationService.Instance.ApplyLanguage(appProfile.UiLanguage);

        StartMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        DebugLog.Reset("App startup");
        DebugLog.Write($"Debug log file: {DebugLog.FilePath}");

        _trayIcon = new NotifyIcon
        {
            // Reuse the executable's icon (see ApplicationIcon in VirtualController.App.csproj) instead of
            // the generic Windows icon, keeping the taskbar, Alt+Tab, and tray icon consistent. ExtractAssociatedIcon
            // reads the icon embedded in the running executable, so no separate .ico file needs to ship.
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
                ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Virtual Controller"
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var contextMenu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem(TranslationService.Instance.GetText("tray.open"), null, (_, _) => ShowMainWindow());
        var exitItem = new ToolStripMenuItem(TranslationService.Instance.GetText("tray.exit"), null, (_, _) => Shutdown());
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = contextMenu;

        // The tray menu is created in code (WinForms), not through XAML bindings, so it does not pick up
        // language changes automatically. Update the item texts explicitly whenever the language changes.
        TranslationService.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(TranslationService.CurrentLanguage))
            {
                openItem.Text = TranslationService.Instance.GetText("tray.open");
                exitItem.Text = TranslationService.Instance.GetText("tray.exit");
            }
        };

        MainWindow = new Views.MainWindow
        {
            WindowState = StartMinimized ? WindowState.Minimized : WindowState.Normal
        };
        MainWindow.Show();
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

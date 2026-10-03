using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VirtualController.App.Views;

/// <summary>
/// Brief, display-only notification (not interactive) for an actual virtual controller mode change (see
/// <see cref="ViewModels.VirtualControllerViewModel.ModeActivated"/>). Appears at the bottom-left of the main
/// screen, remains visible for <see cref="DisplayDuration"/> with a short fade in/out, then closes itself.
/// Contains no business logic; <see cref="Show"/> is called directly from <see cref="MainWindow"/> when the
/// <see cref="ViewModels.VirtualControllerViewModel.ModeActivated"/> event fires.
/// </summary>
public partial class ModeChangeToast : Window
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>Most recently displayed instance. A new notification immediately replaces a visible one
    /// instead of stacking multiple popups, e.g. during rapid trigger switching.</summary>
    private static ModeChangeToast? _current;

    private readonly DispatcherTimer _closeTimer;

    private ModeChangeToast(string controllerName, string modeName)
    {
        InitializeComponent();
        ControllerNameText.Text = controllerName;
        ModeNameText.Text = modeName;

        _closeTimer = new DispatcherTimer { Interval = DisplayDuration };
        _closeTimer.Tick += (_, _) => BeginFadeOutAndClose();
    }

    /// <summary>Shows a notification for the specified virtual controller/mode at the bottom-left of the main
    /// screen, immediately replacing any visible notification.</summary>
    public static void Show(string controllerName, string modeName)
    {
        _current?.CloseImmediately();

        var toast = new ModeChangeToast(controllerName, modeName);
        _current = toast;

        // Position at the bottom-left of the main screen with some margin. WorkArea accounts for the taskbar
        // so the popup is not hidden behind it.
        const double margin = 24;
        toast.Left = SystemParameters.WorkArea.Left + margin;
        toast.Top = SystemParameters.WorkArea.Bottom - toast.Height - margin;

        toast.Opacity = 0;
        toast.Show();
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
        toast._closeTimer.Start();
    }

    private void BeginFadeOutAndClose()
    {
        _closeTimer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, FadeDuration);
        fadeOut.Completed += (_, _) => CloseImmediately();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CloseImmediately()
    {
        _closeTimer.Stop();

        if (_current == this)
        {
            _current = null;
        }

        Close();
    }
}

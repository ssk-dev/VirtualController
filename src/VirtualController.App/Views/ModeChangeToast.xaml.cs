using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VirtualController.App.Views;

/// <summary>
/// Kurze, reine Anzeige-Benachrichtigung (kein Nutzer-Interaktionselement) fuer einen tatsaechlichen
/// Moduswechsel eines virtuellen Controllers (siehe <see cref="ViewModels.VirtualControllerViewModel.ModeActivated"/>).
/// Erscheint automatisch unten links auf dem Hauptbildschirm, bleibt <see cref="DisplayDuration"/> lang
/// sichtbar (mit kurzem Ein-/Ausblenden) und schliesst sich danach selbststaendig. Enthaelt bewusst keine
/// Geschaeftslogik - <see cref="Show"/> wird direkt aus <see cref="MainWindow"/> aufgerufen, sobald das
/// <see cref="ViewModels.VirtualControllerViewModel.ModeActivated"/>-Ereignis eintritt.
/// </summary>
public partial class ModeChangeToast : Window
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>Zuletzt angezeigte Instanz - eine neue Benachrichtigung ersetzt eine noch sichtbare
    /// sofort, statt mehrere Popups uebereinander zu stapeln (z.B. bei schneller Trigger-Umschaltung).</summary>
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

    /// <summary>Zeigt die Benachrichtigung fuer den angegebenen virtuellen Controller/Modus unten links
    /// auf dem Hauptbildschirm an. Eine bereits sichtbare Benachrichtigung wird dabei sofort ersetzt.</summary>
    public static void Show(string controllerName, string modeName)
    {
        _current?.CloseImmediately();

        var toast = new ModeChangeToast(controllerName, modeName);
        _current = toast;

        // Unten links auf dem Hauptbildschirm (WorkArea beruecksichtigt die Taskleiste, damit das
        // Popup nicht dahinter verschwindet), mit etwas Abstand zum Bildschirmrand.
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

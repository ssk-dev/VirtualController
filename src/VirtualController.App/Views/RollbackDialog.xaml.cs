using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des "Version wechseln"-Dialogs (Rollback-Funktion). Enthaelt bewusst keine
/// Geschaeftslogik - diese lebt komplett im <see cref="RollbackDialogViewModel"/>. Hier wird lediglich
/// das ViewModel per <see cref="RollbackDialogViewModel.RequestClose"/>-Ereignis an <see cref="Window.Close"/>
/// gekoppelt, das Laden der Versionsliste beim Oeffnen angestossen und - falls die Installation
/// erfolgreich gestartet wurde - der Anwendung ueber <see cref="InstallationStarted"/> signalisiert, dass
/// sie sich beenden soll (damit der separate Updater-Prozess die aktuell gesperrten Installationsdateien
/// ueberschreiben kann). Analog zu <see cref="UpdateAvailableDialog"/>.
/// </summary>
public partial class RollbackDialog : Window
{
    private readonly RollbackDialogViewModel _viewModel;
    private bool _installationStarted;

    /// <summary>Wird ausgeloest, wenn der Nutzer erfolgreich eine Installation gestartet hat (nicht bei
    /// "Abbrechen" oder einfachem Schliessen) - der Aufrufer (<see cref="MainWindow"/>) beendet die
    /// Anwendung daraufhin, damit der separate Updater-Prozess die Dateien austauschen kann.</summary>
    public event Action? InstallationStarted;

    public RollbackDialog(RollbackDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Loaded += (_, _) => _ = _viewModel.LoadVersionsAsync();
    }

    private void OnRequestClose()
    {
        // IsInstalling bleibt ab dem erfolgreichen Start des Updater-Prozesses dauerhaft true (siehe
        // RollbackDialogViewModel.InstallAsync) - das unterscheidet diesen Fall zuverlaessig von
        // "Abbrechen" (dort wird RequestClose ausgeloest, OHNE dass zuvor IsInstalling gesetzt wurde).
        _installationStarted = _viewModel.IsInstalling;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_installationStarted)
        {
            InstallationStarted?.Invoke();
        }
    }
}

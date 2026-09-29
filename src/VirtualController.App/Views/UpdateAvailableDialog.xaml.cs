using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des modalen Update-Popups. Enthaelt bewusst keine Geschaeftslogik - diese lebt komplett
/// im <see cref="UpdateAvailableDialogViewModel"/>. Hier wird lediglich das ViewModel per
/// <see cref="UpdateAvailableDialogViewModel.RequestClose"/>-Ereignis an <see cref="Window.Close"/>
/// gekoppelt und - falls die Installation erfolgreich gestartet wurde - der Anwendung ueber
/// <see cref="InstallationStarted"/> signalisiert, dass sie sich beenden soll (damit der separate
/// Updater-Prozess die aktuell gesperrten Installationsdateien ueberschreiben kann).
/// </summary>
public partial class UpdateAvailableDialog : Window
{
    private readonly UpdateAvailableDialogViewModel _viewModel;
    private bool _installationStarted;

    /// <summary>Wird ausgeloest, wenn der Nutzer erfolgreich eine Installation gestartet hat (nicht bei
    /// "Update ueberspringen" oder einfachem Schliessen) - der Aufrufer (<see cref="MainWindow"/>) beendet
    /// die Anwendung daraufhin, damit der separate Updater-Prozess die Dateien austauschen kann.</summary>
    public event Action? InstallationStarted;

    public UpdateAvailableDialog(UpdateAvailableDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
    }

    private void OnRequestClose()
    {
        // IsInstalling bleibt ab dem erfolgreichen Start des Updater-Prozesses dauerhaft true (siehe
        // UpdateAvailableDialogViewModel.InstallAsync) - das unterscheidet diesen Fall zuverlaessig von
        // "Update ueberspringen" (dort wird RequestClose ausgeloest, OHNE dass zuvor IsInstalling gesetzt
        // wurde).
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

using System.Windows;

namespace VirtualController.Updater;

/// <summary>
/// Code-Behind des einzigen Fensters dieses eigenstaendigen Updater-Prozesses. Enthaelt bewusst keine
/// Geschaeftslogik - diese lebt komplett im <see cref="UpdaterViewModel"/>. Hier wird lediglich das
/// ViewModel per <see cref="UpdaterViewModel.RequestClose"/>-Ereignis an <see cref="Window.Close"/>
/// gekoppelt, was zugleich (siehe <see cref="App.OnStartup"/>, kein weiteres Fenster vorhanden) den
/// gesamten Prozess beendet.
/// </summary>
public partial class MainWindow : Window
{
    private readonly UpdaterViewModel _viewModel;

    public MainWindow(UpdaterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += Close;
        Closed += (_, _) => _viewModel.RequestClose -= Close;
    }
}

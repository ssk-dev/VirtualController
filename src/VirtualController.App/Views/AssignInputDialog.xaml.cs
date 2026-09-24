using System.Windows;
using System.Windows.Input;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des modalen "Zuweisen"-Dialogs. Enthaelt bewusst keine Geschaeftslogik - diese lebt
/// komplett im <see cref="AssignInputDialogViewModel"/>. Hier wird lediglich das ViewModel per
/// Confirmed/Cancelled-Events an <see cref="Window.DialogResult"/> gekoppelt, damit der Aufrufer
/// (<see cref="MainWindow"/>) per <c>ShowDialog()</c>-Rueckgabewert erkennen kann, ob der Nutzer eine
/// Eingabe bestaetigt hat.
/// </summary>
public partial class AssignInputDialog : Window
{
    private readonly AssignInputDialogViewModel _viewModel;

    public AssignInputDialog(AssignInputDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.Confirmed += OnConfirmed;
        _viewModel.Cancelled += OnCancelled;
        Closed += (_, _) =>
        {
            _viewModel.Confirmed -= OnConfirmed;
            _viewModel.Cancelled -= OnCancelled;
        };
    }

    private void OnConfirmed()
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelled()
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Doppelklick auf einen Listeneintrag bestaetigt die Auswahl direkt, ohne dass der
    /// Nutzer zusaetzlich den OK-Button anklicken muss (uebliches Verhalten in Auswahl-Dialogen).</summary>
    private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedInput is not null)
        {
            _viewModel.ConfirmCommand.Execute(null);
        }
    }
}

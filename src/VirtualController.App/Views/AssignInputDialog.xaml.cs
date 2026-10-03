using System.Windows;
using System.Windows.Input;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-behind for the modal Assign dialog. Business logic lives in <see cref="AssignInputDialogViewModel"/>;
/// this class connects its Confirmed/Cancelled events to <see cref="Window.DialogResult"/> so the caller
/// (<see cref="MainWindow"/>) can use the <c>ShowDialog()</c> return value to determine whether an input was confirmed.
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

    /// <summary>A double-click on a list entry confirms the selection without requiring the user to click OK,
    /// as is customary in selection dialogs.</summary>
    private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedInput is not null)
        {
            _viewModel.ConfirmCommand.Execute(null);
        }
    }
}

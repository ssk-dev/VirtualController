using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-behind for the modal update dialog. Business logic lives in
/// <see cref="UpdateAvailableDialogViewModel"/>. This class connects
/// <see cref="UpdateAvailableDialogViewModel.RequestClose"/> to <see cref="Window.Close"/> and signals through
/// <see cref="InstallationStarted"/> when the app should shut down after installation starts successfully,
/// allowing the separate updater to replace locked files.
/// </summary>
public partial class UpdateAvailableDialog : Window
{
    private readonly UpdateAvailableDialogViewModel _viewModel;
    private bool _installationStarted;

    /// <summary>Raised when the user successfully starts an installation, but not when skipping the update or
    /// closing normally. The caller (<see cref="MainWindow"/>) then shuts down the app so the separate updater
    /// can replace files.</summary>
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
        // IsInstalling remains true after the updater process starts successfully (see
        // UpdateAvailableDialogViewModel.InstallAsync), distinguishing installation from skipping, which
        // raises RequestClose without setting IsInstalling.
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

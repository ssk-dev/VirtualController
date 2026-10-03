using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-behind for the Change Version (rollback) dialog. Business logic lives in
/// <see cref="RollbackDialogViewModel"/>. This class connects
/// <see cref="RollbackDialogViewModel.RequestClose"/> to <see cref="Window.Close"/>, starts loading the version
/// list when the dialog opens, and signals through <see cref="InstallationStarted"/> when the app should shut
/// down after installation starts so the separate updater can replace locked files. Similar to
/// <see cref="UpdateAvailableDialog"/>.
/// </summary>
public partial class RollbackDialog : Window
{
    private readonly RollbackDialogViewModel _viewModel;
    private bool _installationStarted;

    /// <summary>Raised when the user successfully starts an installation, but not on cancellation or normal
    /// close. The caller (<see cref="MainWindow"/>) then shuts down the app so the separate updater can replace files.</summary>
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
        // IsInstalling remains true after the updater process starts successfully (see
        // RollbackDialogViewModel.InstallAsync), distinguishing this case from cancellation, which raises
        // RequestClose without setting IsInstalling.
        _installationStarted = _viewModel.IsInstalling;
        Close();
    }

    /// <summary>Closes the dialog through the custom title bar button (see the title bar Border in
    /// RollbackDialog.xaml), replacing the removed native title bar.</summary>
    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_installationStarted)
        {
            InstallationStarted?.Invoke();
        }
    }
}

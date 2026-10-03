using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel for the update dialog (<see cref="Views.UpdateAvailableDialog"/>), shown when
/// <see cref="UpdateViewModel"/> finds a newer version that has not been skipped. It offers two actions:
/// "Install update" starts <see cref="UpdateInstaller"/>, shows progress, and closes the app on success so
/// the separate updater process can replace the files and start the new version; "Skip update" permanently
/// skips the offered version through <see cref="UpdateCoordinator.SkipVersion"/> and closes the dialog.
/// </summary>
public sealed partial class UpdateAvailableDialogViewModel : ObservableObject
{
    private readonly UpdateCheckResult _details;
    private readonly UpdateCoordinator _coordinator;
    private readonly UpdateInstaller _installer = new();

    /// <summary>Whether a download or installation is currently running. The view shows a progress indicator
    /// and disables both buttons to prevent closing the dialog or starting another installation.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>Status text shown in the view during installation (see <see cref="InstallAsync"/>).</summary>
    [ObservableProperty]
    private string? _installStatusText;

    /// <summary>Whether the app is about to restart after a successful installation start. Shows the
    /// "Die app wird neugestartet..." message in the dialog before it closes.</summary>
    [ObservableProperty]
    private bool _isRestarting;

    /// <summary>Overall installation progress (0-100) across all steps (see
    /// <see cref="UpdateInstallProgress.OverallPercent"/>), displayed in the view's progress bar.</summary>
    [ObservableProperty]
    private double _installProgressPercent;

    /// <summary>Description of the current installation step, including its position, e.g. "Step 1 of 4:
    /// Downloading files". Displayed below the progress bar.</summary>
    [ObservableProperty]
    private string? _installStepText;

    /// <summary>Dialog title, e.g. "New version available: Version 1.5.0".</summary>
    public string TitleText => $"New version available: Version {_details.AvailableVersion}";

    public string InstalledVersionText => _details.InstalledVersion.ToString();

    public string AvailableVersionText => _details.AvailableVersion.ToString();

    /// <summary>Changelog or release notes for the available version (see
    /// <see cref="UpdateCheckResult.ReleaseNotes"/>), shown in the dialog's expandable section.</summary>
    public string? ReleaseNotesText => _details.ReleaseNotes;

    /// <summary>Whether <see cref="ReleaseNotesText"/> contains any content; controls whether the expandable
    /// changelog section is shown (some releases have no notes).</summary>
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotesText);

    /// <summary>Raised when the dialog should close, either after "Skip update" or after installation has
    /// started successfully, just before <see cref="Views.MainWindow"/> shuts down the app.</summary>
    public event Action? RequestClose;

    public UpdateAvailableDialogViewModel(UpdateCheckResult details, UpdateCoordinator coordinator)
    {
        _details = details;
        _coordinator = coordinator;
    }

    /// <summary>
    /// "Install update": downloads the update archive, extracts it to a temporary directory, and starts the
    /// separate updater process (see <see cref="UpdateInstaller"/>). If downloading or extraction fails, the
    /// installed version remains usable, an error is shown, and the dialog stays open so the user can retry
    /// or skip the update. On success, raises <see cref="RequestClose"/>; <see cref="Views.MainWindow"/> then
    /// shuts down the app so the updater can replace files locked by the running executable.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsInstalling = true;

        var progress = new Progress<UpdateInstallProgress>(p =>
        {
            InstallProgressPercent = p.OverallPercent;
            InstallStepText = $"Step {p.StepNumber} of {p.TotalSteps}: {p.StepDescription}";
            InstallStatusText = p.StepDescription;
        });

        try
        {
            var preparation = await _installer.PrepareAsync(_details.DownloadUrl, _details.AvailableVersion.ToString(), progress).ConfigureAwait(true);
            _installer.LaunchUpdaterProcess(preparation, progress);

            // The separate updater process is now running and waiting for this process to exit (see
            // UpdateInstaller.LaunchUpdaterProcess). The caller (Views.MainWindow), not this view model,
            // shuts down the WPF application so the view model does not need access to the Application instance.
            IsRestarting = true;
            RequestClose?.Invoke();
        }
        catch (UpdateInstallException ex)
        {
            IsInstalling = false;
            IsRestarting = false;
            InstallStatusText = null;
            InstallStepText = null;
            InstallProgressPercent = 0;
            System.Windows.MessageBox.Show(
                ex.Message, "Update installation failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanInstall() => !IsInstalling;

    /// <summary>
    /// "Skip update": permanently skips the currently offered version (see
    /// <see cref="UpdateCoordinator.SkipVersion"/>) and closes the dialog. A later, higher version will
    /// still be offered normally.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void SkipUpdate()
    {
        _coordinator.SkipVersion(_details.AvailableVersion);
        RequestClose?.Invoke();
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// A version available from the update source, prepared for display in the version list
/// of the "Change version" dialog (<see cref="Views.RollbackDialog"/>).
/// </summary>
/// <param name="Version">Version number.</param>
/// <param name="DownloadUrl">Download URL for the corresponding installation package.</param>
/// <param name="IsCurrentlyInstalled">Whether this is exactly the installed version (compared with
/// <see cref="AppVersionProvider.CurrentVersion"/>); marked in the list and selected by default when the dialog opens.</param>
public sealed record RollbackVersionOption(SemanticVersion Version, string DownloadUrl, bool IsCurrentlyInstalled)
{
    /// <summary>Display text for the version list, e.g. "1.4.2 (currently installed)" or "1.3.0".</summary>
    public string DisplayText => IsCurrentlyInstalled ? $"{Version} (currently installed)" : Version.ToString();
}

/// <summary>
/// ViewModel for the "Change version" dialog (<see cref="Views.RollbackDialog"/>). It loads every version
/// available from the update source (see <see cref="UpdateCoordinator.GetAllVersionsAsync"/>) and lets the
/// user switch to any of them, including an older version (rollback). This is an explicit user action, so it
/// is not subject to the regular update check's restriction (<see cref="UpdateChecker"/>) against downgrades;
/// automatic and manual checks (<see cref="UpdateViewModel"/>) remain unchanged. Each installation requires
/// confirmation to prevent accidental version changes. Installation uses the same <see cref="UpdateInstaller"/>
/// as the regular update dialog (see <see cref="UpdateAvailableDialogViewModel"/>), which accepts a download URL
/// regardless of whether the change is an upgrade or downgrade.
/// </summary>
public sealed partial class RollbackDialogViewModel : ObservableObject
{
    private readonly UpdateCoordinator _coordinator;
    private readonly UpdateInstaller _installer = new();

    /// <summary>Whether the list of available versions is currently loading; controls the view's progress indicator.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Error shown if loading the version list fails (see <see cref="LoadVersionsAsync"/>), otherwise <c>null</c>.</summary>
    [ObservableProperty]
    private string? _loadErrorText;

    /// <summary>Whether <see cref="LoadErrorText"/> contains an error; controls the error block's visibility
    /// because the project has no string-to-Visibility converter (see Converters.xaml).</summary>
    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorText);

    /// <summary>All versions available from the update source, sorted in descending order (see
    /// <see cref="LoadVersionsAsync"/>).</summary>
    public ObservableCollection<RollbackVersionOption> Versions { get; } = new();

    /// <summary>Version selected by the user in the list, targeted by <see cref="InstallAsync"/>.</summary>
    [ObservableProperty]
    private RollbackVersionOption? _selectedVersion;

    /// <summary>Whether a download or installation is running; controls the progress indicator and disables
    /// the list and buttons, as in <see cref="UpdateAvailableDialogViewModel.IsInstalling"/>.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>Status text shown in the view during installation.</summary>
    [ObservableProperty]
    private string? _installStatusText;

    /// <summary>Overall installation progress (0-100); see <see cref="UpdateInstallProgress.OverallPercent"/>.</summary>
    [ObservableProperty]
    private double _installProgressPercent;

    /// <summary>Description of the current installation step, including its position; see
    /// <see cref="UpdateAvailableDialogViewModel.InstallStepText"/>.</summary>
    [ObservableProperty]
    private string? _installStepText;

    /// <summary>Raised when the dialog should close, either after cancellation or after installation starts
    /// successfully, just before <see cref="Views.MainWindow"/> shuts down the app.</summary>
    public event Action? RequestClose;

    public RollbackDialogViewModel(UpdateCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    partial void OnLoadErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));

    partial void OnSelectedVersionChanged(RollbackVersionOption? value) => InstallCommand.NotifyCanExecuteChanged();

    partial void OnIsInstallingChanged(bool value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Loads all available versions (see <see cref="UpdateCoordinator.GetAllVersionsAsync"/>) and selects the
    /// installed version by default when it is present. The view calls this from its Loaded event rather than
    /// the constructor so the dialog appears immediately while loading continues in the background with a
    /// visible progress indicator.
    /// </summary>
    public async Task LoadVersionsAsync()
    {
        IsLoading = true;
        LoadErrorText = null;
        Versions.Clear();

        try
        {
            var installed = AppVersionProvider.CurrentVersion;
            var available = await _coordinator.GetAllVersionsAsync().ConfigureAwait(true);

            foreach (var info in available)
            {
                Versions.Add(new RollbackVersionOption(info.Version, info.DownloadUrl, info.Version == installed));
            }

            SelectedVersion = Versions.FirstOrDefault(v => v.IsCurrentlyInstalled) ?? Versions.FirstOrDefault();
        }
        catch (UpdateCheckException ex)
        {
            LoadErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Installs the version selected in <see cref="SelectedVersion"/>, whether it is an upgrade or downgrade
    /// from the currently installed version. Requests explicit confirmation through a
    /// <see cref="System.Windows.MessageBox"/> to prevent accidental changes, then uses the same
    /// <see cref="UpdateInstaller"/> flow as <see cref="UpdateAvailableDialogViewModel.InstallAsync"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var target = SelectedVersion;
        if (target is null)
        {
            return;
        }

        string action = target.Version.CompareTo(AppVersionProvider.CurrentVersion) switch
        {
            < 0 => "switch to an older version",
            > 0 => "switch to a newer version",
            _ => "reinstall this version",
        };

        var confirmation = System.Windows.MessageBox.Show(
            $"Are you sure you want to {action} (version {target.Version})? The app will close and restart automatically.",
            "Change version",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirmation != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        IsInstalling = true;

        var progress = new Progress<UpdateInstallProgress>(p =>
        {
            InstallProgressPercent = p.OverallPercent;
            InstallStepText = $"Step {p.StepNumber} of {p.TotalSteps}: {p.StepDescription}";
            InstallStatusText = p.StepDescription;
        });

        try
        {
            var preparation = await _installer.PrepareAsync(target.DownloadUrl, target.Version.ToString(), progress).ConfigureAwait(true);
            _installer.LaunchUpdaterProcess(preparation, progress);

            // The separate updater process has started. See UpdateAvailableDialogViewModel.InstallAsync
            // for why the caller (Views.MainWindow) is responsible for shutting down the app.
            RequestClose?.Invoke();
        }
        catch (UpdateInstallException ex)
        {
            IsInstalling = false;
            InstallStatusText = null;
            InstallStepText = null;
            InstallProgressPercent = 0;
            System.Windows.MessageBox.Show(
                ex.Message, "Installation failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanInstall() => SelectedVersion is not null && !IsInstalling && !IsLoading;

    /// <summary>"Cancel": closes the dialog without making any changes.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => RequestClose?.Invoke();

    private bool CanCancel() => !IsInstalling;
}

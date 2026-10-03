using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using VirtualController.Core.Logging;
using VirtualController.Core.Profiles;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel for the update feature (see the "Settings" tab in <see cref="Views.MainWindow"/>). It manages
/// the persisted "Check for updates automatically" setting and manual checks triggered by the
/// "Check for updates" button. <see cref="UpdateCoordinator"/> (VirtualController.Core) handles communication
/// with the update source and semantic version comparison; this view model only maps its results to UI state
/// (progress indicator and status text) and raises <see cref="UpdateAvailable"/>. <see cref="Views.MainWindow"/>
/// handles that event to show the update dialog, similar to <see cref="VirtualControllerViewModel.ModeActivated"/>
/// and <see cref="Views.ModeChangeToast"/>.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCoordinator _coordinator = new();

    /// <summary>Whether to automatically check for a newer version at each app startup. Changes are persisted
    /// immediately (see <see cref="UpdateCoordinator.AutoCheckEnabled"/>), independently of the explicit
    /// "Save profiles" button for the rest of the configuration.</summary>
    [ObservableProperty]
    private bool _autoCheckEnabled;

    /// <summary>Whether update checks should include versions marked as prereleases (e.g. tags with the
    /// "-alpha", "-beta", or "-nightly" suffix) instead of only stable releases. Changes are persisted
    /// immediately (see <see cref="UpdateCoordinator.IncludePreReleases"/>), like <see cref="AutoCheckEnabled"/>.</summary>
    [ObservableProperty]
    private bool _includePreReleases;

    /// <summary>Whether an update check is currently running, either manually or automatically at startup.
    /// The view shows a progress indicator and disables the "Check for updates" button to prevent
    /// accidentally starting multiple checks in parallel.</summary>
    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>Status text from the last check (success or failure), shown on the "Settings" tab.</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>Currently installed version, shown on the "Settings" tab.</summary>
    public string CurrentVersionText => AppVersionProvider.RawVersion;

    /// <summary>Raised when a manual or automatic check finds a newer version to display. The
    /// <see cref="Views.MainWindow"/> then shows the update dialog (<see cref="Views.UpdateAvailableDialog"/>).</summary>
    public event Action<UpdateCheckResult>? UpdateAvailable;

    public UpdateViewModel()
    {
        _autoCheckEnabled = _coordinator.AutoCheckEnabled;
        _includePreReleases = _coordinator.IncludePreReleases;
    }

    partial void OnAutoCheckEnabledChanged(bool value) => _coordinator.AutoCheckEnabled = value;

    partial void OnIncludePreReleasesChanged(bool value) => _coordinator.IncludePreReleases = value;

    partial void OnIsCheckingForUpdatesChanged(bool value) => CheckForUpdatesCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Manual check triggered by the "Check for updates" button. It runs regardless of
    /// <see cref="AutoCheckEnabled"/> and, unlike the startup check (<see cref="RunStartupCheckAsync"/>),
    /// shows a readable error instead of silently discarding failures. A version previously marked with
    /// "Skip update" is deliberately offered again here (<see cref="UpdateCheckResult.IsUpdateAvailable"/>)
    /// instead of using the version-specific skip outcome from <see cref="UpdateCoordinator.CheckAsync"/>.
    /// Clicking "Check for updates" is an explicit user action and should return a result regardless of
    /// an earlier decision to skip that version.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        StatusText = "Searching for updates...";

        try
        {
            var result = await _coordinator.CheckAsync().ConfigureAwait(true);

            if (result.Details.IsUpdateAvailable)
            {
                StatusText = $"New version available: {result.Details.AvailableVersion}";
                UpdateAvailable?.Invoke(result.Details);
            }
            else
            {
                StatusText = $"No update available. Version {result.Details.InstalledVersion} is up to date.";
            }
        }
        catch (UpdateCheckException ex)
        {
            StatusText = null;
            System.Windows.MessageBox.Show(
                ex.Message, "Update check failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanCheckForUpdates() => !IsCheckingForUpdates;

    /// <summary>
    /// Automatic startup check, run only when <see cref="AutoCheckEnabled"/> is enabled. Never throws or
    /// blocks the app (see the caller in <see cref="Views.MainWindow"/>): a missing internet connection or
    /// unreachable update server must not affect normal app behavior, so failures are deliberately ignored.
    /// Unlike <see cref="CheckForUpdatesAsync"/>, a version previously marked with "Skip update" does not
    /// trigger another dialog here.
    /// </summary>
    public async Task RunStartupCheckAsync()
    {
        if (!AutoCheckEnabled)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            var result = await _coordinator.CheckAsync().ConfigureAwait(true);

            if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
            {
                StatusText = $"New version available: {result.Details.AvailableVersion}";
                UpdateAvailable?.Invoke(result.Details);
            }
        }
        catch (UpdateCheckException)
        {
            // Startup checks must not affect normal app behavior. Ignore failures such as a missing
            // internet connection or an unreachable update server without showing a message to the user
            // (unlike the manual check in CheckForUpdatesAsync).
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Creates the update dialog view model for <paramref name="details"/>, sharing the same
    /// <see cref="UpdateCoordinator"/> so it can call <see cref="UpdateCoordinator.SkipVersion"/> when
    /// the user clicks "Skip update".</summary>
    public UpdateAvailableDialogViewModel CreateAvailableDialogViewModel(UpdateCheckResult details) =>
        new(details, _coordinator);

    /// <summary>Full path to the update log file (see <see cref="Logging.UpdateLog"/>), shown on the
    /// "Settings" tab so the user can locate it in File Explorer after a failed update without clicking
    /// <see cref="OpenUpdateLogCommand"/>.</summary>
    public string UpdateLogFilePath => UpdateLog.FilePath;

    /// <summary>Opens the update log file (see <see cref="Logging.UpdateLog"/>) in the system's default text
    /// editor so the user can determine which step failed (download, copy, or restart; see
    /// <see cref="Core.Updates.UpdateInstaller"/> and its generated PowerShell updater script) when an update
    /// behaves unexpectedly, without searching for the file under %AppData%\VirtualController manually.</summary>
    [RelayCommand]
    private void OpenUpdateLog()
    {
        try
        {
            if (!File.Exists(UpdateLog.FilePath))
            {
                StatusText = "No update log is available because no update check or installation has been performed yet.";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = UpdateLog.FilePath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open the update log file: {ex.Message}";
        }
    }

    /// <summary>Opens the application data folder ("%AppData%\VirtualController", see
    /// <see cref="ProfileStore.BaseDirectory"/>) in File Explorer so the user can access saved profiles,
    /// device settings, and logs (including <see cref="Logging.UpdateLog"/>) without entering the path
    /// manually.</summary>
    [RelayCommand]
    private void OpenAppDataFolder()
    {
        try
        {
            if (!Directory.Exists(ProfileStore.BaseDirectory))
            {
                Directory.CreateDirectory(ProfileStore.BaseDirectory);
            }

            Process.Start(new ProcessStartInfo { FileName = ProfileStore.BaseDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open the application data folder: {ex.Message}";
        }
    }

    /// <summary>Creates the "Change version" dialog view model (rollback feature), sharing the same
    /// <see cref="UpdateCoordinator"/> to call <see cref="UpdateCoordinator.GetAllVersionsAsync"/>,
    /// respecting the current <see cref="IncludePreReleases"/> setting.</summary>
    public RollbackDialogViewModel CreateRollbackDialogViewModel() => new(_coordinator);
}

namespace VirtualController.Core.Updates;

/// <summary>
/// Result of an update check (<see cref="UpdateChecker.CheckAsync"/>): includes the installed and available
/// versions, whether an update is available, and its download/installation source.
/// </summary>
/// <param name="InstalledVersion">Currently installed version (<see cref="AppVersionProvider.CurrentVersion"/>).</param>
/// <param name="AvailableVersion">Version currently available from the update source.</param>
/// <param name="IsUpdateAvailable">Whether <paramref name="AvailableVersion"/> is newer than
/// <paramref name="InstalledVersion"/>. Downgrades (available version less than or equal to installed) are
/// intentionally not offered as updates here.</param>
/// <param name="DownloadUrl">Download/installation source for the update.</param>
/// <param name="ReleaseNotes">Changelog/release notes for <paramref name="AvailableVersion"/> (see
/// <see cref="UpdateInfo.ReleaseNotes"/>), if provided by the source; otherwise <c>null</c> or empty. Displayed
/// in an expandable section of the update dialog.</param>
public sealed record UpdateCheckResult(
    SemanticVersion InstalledVersion,
    SemanticVersion AvailableVersion,
    bool IsUpdateAvailable,
    string DownloadUrl,
    string? ReleaseNotes = null);

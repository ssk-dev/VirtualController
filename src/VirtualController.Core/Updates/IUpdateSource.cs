namespace VirtualController.Core.Updates;

/// <summary>
/// Abstraction for an update source, providing information about currently available versions. Kept as an
/// interface instead of coupling communication directly to <see cref="UpdateChecker"/> so the source can be
/// replaced later (e.g. with a self-hosted endpoint instead of GitHub Releases; see
/// <see cref="GitHubReleaseUpdateSource"/>) without changing the checker.
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// Gets the latest version available from the update source. Returns <c>null</c> if the source was
    /// reachable but returned no valid version information (e.g. an unexpected response format). Connection
    /// failures (no internet, server unreachable) should be thrown so <see cref="UpdateChecker"/> can handle
    /// both invalid responses and connection errors as failed checks.
    /// </summary>
    /// <param name="includePreReleases">Whether to include prereleases (e.g. tags ending in -alpha/-beta/-nightly)
    /// instead of offering stable releases only. Matches the "Include prereleases" setting
    /// (see <see cref="UpdateSettings.IncludePreReleases"/>).</param>
    Task<UpdateInfo?> GetLatestAsync(bool includePreReleases, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all versions currently available from the update source, sorted descending (newest first). Unlike
    /// <see cref="GetLatestAsync"/>, returns the full list so users can choose an older version for rollback in
    /// the Change Version dialog (see <see cref="VirtualController.App.ViewModels.RollbackDialogViewModel"/>),
    /// which regular update checks (<see cref="UpdateChecker.CheckAsync"/>) intentionally do not offer.
    /// </summary>
    /// <param name="includePreReleases">Siehe <see cref="GetLatestAsync"/>.</param>
    Task<IReadOnlyList<UpdateInfo>> GetAllAsync(bool includePreReleases, CancellationToken cancellationToken = default);
}

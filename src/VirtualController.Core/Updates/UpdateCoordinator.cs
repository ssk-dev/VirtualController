namespace VirtualController.Core.Updates;

/// <summary>Outcome category of an <see cref="UpdateCoordinator.CheckAsync"/> operation.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>No newer version is available (the installed version is current or newer).</summary>
    UpToDate,

    /// <summary>A newer, not-yet-skipped version is available; the caller should show the update dialog.</summary>
    UpdateAvailable,

    /// <summary>A newer version is available but the user permanently skipped this exact version through
    /// <see cref="UpdateCoordinator.SkipVersion"/>; do not show another dialog for it.</summary>
    UpdateSkipped
}

/// <summary>Combines the outcome category with the complete version details.</summary>
public sealed record UpdateCoordinatorResult(UpdateCheckOutcome Outcome, UpdateCheckResult Details);

/// <summary>
/// App-layer entry point for updates. Combines <see cref="UpdateChecker"/> (source communication and semantic
/// version comparison) with the persisted "Check for updates automatically" setting and per-version skip marker
/// (<see cref="UpdateSettingsStore"/>). Both automatic startup and manual checks use this class so they share
/// identical skip logic. Also provides <see cref="GetAllVersionsAsync"/> for the Change Version/rollback dialog,
/// allowing users to switch to an older version.
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly UpdateChecker _checker;
    private readonly string? _baseDirectory;

    /// <param name="source">Update source to use. Optional so it can be replaced later or substituted by a test
    /// double; defaults to <see cref="GitHubReleaseUpdateSource"/>.</param>
    /// <param name="baseDirectory">Base directory for <see cref="UpdateSettingsStore"/>; defaults to
    /// <see cref="Profiles.ProfileStore.BaseDirectory"/>. Intended for tests only.</param>
    public UpdateCoordinator(IUpdateSource? source = null, string? baseDirectory = null)
    {
        _checker = new UpdateChecker(source ?? new GitHubReleaseUpdateSource());
        _baseDirectory = baseDirectory;
    }

    /// <summary>Whether to check for a newer version automatically at each app startup. Reads/writes
    /// "update-settings.json" immediately without batching (see <see cref="UpdateSettingsStore"/>), independent
    /// of the explicit Save profiles operation for the rest of the configuration.</summary>
    public bool AutoCheckEnabled
    {
        get => UpdateSettingsStore.Load(_baseDirectory).AutoCheckEnabled;
        set
        {
            var settings = UpdateSettingsStore.Load(_baseDirectory);
            if (settings.AutoCheckEnabled == value)
            {
                return;
            }

            settings.AutoCheckEnabled = value;
            UpdateSettingsStore.Save(settings, _baseDirectory);
        }
    }

    /// <summary>Whether update checks include prereleases (e.g. tags ending in -alpha/-beta/-nightly) instead
    /// of stable releases only. Reads/writes "update-settings.json" immediately without batching, like
    /// <see cref="AutoCheckEnabled"/> (see <see cref="UpdateSettingsStore"/>).</summary>
    public bool IncludePreReleases
    {
        get => UpdateSettingsStore.Load(_baseDirectory).IncludePreReleases;
        set
        {
            var settings = UpdateSettingsStore.Load(_baseDirectory);
            if (settings.IncludePreReleases == value)
            {
                return;
            }

            settings.IncludePreReleases = value;
            UpdateSettingsStore.Save(settings, _baseDirectory);
        }
    }

    /// <summary>
    /// Checks for updates and classifies the result using the most recently skipped version and
    /// <see cref="UpdateCheckOutcome"/>.
    /// </summary>
    /// <exception cref="UpdateCheckException">The check failed due to a connection error or invalid update
    /// source response; see <see cref="UpdateChecker.CheckAsync"/>.</exception>
    public async Task<UpdateCoordinatorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var details = await _checker.CheckAsync(IncludePreReleases, cancellationToken).ConfigureAwait(false);

        if (!details.IsUpdateAvailable)
        {
            return new UpdateCoordinatorResult(UpdateCheckOutcome.UpToDate, details);
        }

        var settings = UpdateSettingsStore.Load(_baseDirectory);
        bool alreadySkipped = SemanticVersion.TryParse(settings.SkippedVersion, out var skippedVersion)
            && skippedVersion == details.AvailableVersion;

        return new UpdateCoordinatorResult(
            alreadySkipped ? UpdateCheckOutcome.UpdateSkipped : UpdateCheckOutcome.UpdateAvailable,
            details);
    }

    /// <summary>Permanently marks <paramref name="version"/> as skipped. Subsequent <see cref="CheckAsync"/>
    /// calls return <see cref="UpdateCheckOutcome.UpdateSkipped"/> for that exact version instead of
    /// <see cref="UpdateCheckOutcome.UpdateAvailable"/>. A later, higher version that has not been skipped is
    /// still reported normally; skipping is version-specific, not global.</summary>
    public void SkipVersion(SemanticVersion version)
    {
        var settings = UpdateSettingsStore.Load(_baseDirectory);
        settings.SkippedVersion = version.ToString();
        UpdateSettingsStore.Save(settings, _baseDirectory);
    }

    /// <summary>
    /// Returns all versions available from the update source in descending order for the Change Version dialog.
    /// Unlike <see cref="CheckAsync"/>, this returns the full list of installable versions, including versions
    /// older than the installed one for rollback. Respects the persisted <see cref="IncludePreReleases"/> setting,
    /// like <see cref="CheckAsync"/>.
    /// </summary>
    /// <exception cref="UpdateCheckException">The request failed due to a connection error or invalid update
    /// source response; see <see cref="UpdateChecker.GetAllAvailableAsync"/>.</exception>
    public Task<IReadOnlyList<UpdateInfo>> GetAllVersionsAsync(CancellationToken cancellationToken = default) =>
        _checker.GetAllAvailableAsync(IncludePreReleases, cancellationToken);
}

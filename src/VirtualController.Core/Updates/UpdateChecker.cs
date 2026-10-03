using System.Net;
using System.Text.Json;

namespace VirtualController.Core.Updates;

/// <summary>
/// Checks for updates by requesting the currently available version from the configured
/// <see cref="IUpdateSource"/> and comparing it semantically (see <see cref="SemanticVersion"/>) with the
/// installed version (<see cref="AppVersionProvider.CurrentVersion"/>). Decoupled from the specific update
/// source through constructor injection so the source can be replaced without changing comparison logic.
/// </summary>
public sealed class UpdateChecker
{
    private readonly IUpdateSource _source;

    public UpdateChecker(IUpdateSource source)
    {
        _source = source;
    }

    /// <summary>
    /// Determines whether a newer version than the installed one is available.
    /// </summary>
    /// <param name="includePreReleases">Whether to include versions marked as prereleases (see
    /// <see cref="UpdateSettings.IncludePreReleases"/>) instead of offering stable releases only.</param>
    /// <exception cref="UpdateCheckException">
    /// The check failed because of a connection error or invalid update source response. Contains a user-facing
    /// <see cref="Exception.Message"/>.
    /// </exception>
    public async Task<UpdateCheckResult> CheckAsync(bool includePreReleases = false, CancellationToken cancellationToken = default)
    {
        var installed = AppVersionProvider.CurrentVersion;

        UpdateInfo? latest;
        try
        {
            latest = await _source.GetLatestAsync(includePreReleases, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Pass through only cancellations requested by the caller. HttpClient may also throw
            // TaskCanceledException for its own request timeout; that is not caused by the supplied token and
            // should instead be classified below as a timeout (see DescribeSourceFailure).
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(DescribeSourceFailure(ex), ex);
        }

        if (latest is null)
        {
            throw new UpdateCheckException(
                "The update source returned no valid version information. The GitHub repository may have no " +
                "eligible release, or none of its releases may contain the expected installation package.");
        }

        bool isUpdateAvailable = latest.Version > installed;
        return new UpdateCheckResult(installed, latest.Version, isUpdateAvailable, latest.DownloadUrl, latest.ReleaseNotes);
    }

    /// <summary>
    /// Returns all versions currently available from the update source, not only the latest. Used by the
    /// Change Version/rollback dialog (see <see cref="UpdateCoordinator.GetAllVersionsAsync"/>) so users can
    /// explicitly switch to a version older than the one installed.
    /// </summary>
    /// <param name="includePreReleases">Siehe <see cref="CheckAsync"/>.</param>
    /// <exception cref="UpdateCheckException">
    /// The request failed because of a connection error or invalid update source response. Contains a
    /// user-facing <see cref="Exception.Message"/>.
    /// </exception>
    public async Task<IReadOnlyList<UpdateInfo>> GetAllAvailableAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _source.GetAllAsync(includePreReleases, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // See CheckAsync for the reason for this condition.
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(DescribeSourceFailure(ex), ex);
        }
    }

    /// <summary>
    /// Creates a precise, user-facing error description based on the underlying exception. Distinguishes actual
    /// connection problems (e.g. no internet, DNS failure, timeout) from update-source problems (e.g. the GitHub
    /// repository/release no longer exists -> HTTP 404, API rate limit reached -> HTTP 403, or invalid JSON).
    /// Without this distinction, a renamed/deleted repository could be reported as "no internet connection"
    /// even when the user's connection is working.
    /// </summary>
    private static string DescribeSourceFailure(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "The update source was not found (HTTP 404). The GitHub repository may have been renamed, moved, " +
            "or made private. Check the update source configuration.",

        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "Access to the update source was denied (HTTP 403), usually because the GitHub API rate limit was " +
            "reached (e.g. from frequent unauthenticated checks). Try again later.",

        HttpRequestException { StatusCode: not null } httpEx =>
            $"The update source returned an error (HTTP {(int)httpEx.StatusCode!.Value} {httpEx.StatusCode}). Try again later.",

        HttpRequestException httpEx =>
            $"Could not reach the update source. Check your internet connection and try again later. (Cause: {httpEx.Message})",

        TaskCanceledException =>
            "The request to the update source timed out. Check your internet connection and try again later.",

        JsonException =>
            "The update source returned an invalid or unexpected response (not valid JSON). This indicates a " +
            "problem with the update source, not your internet connection.",

        _ =>
            $"Could not determine the available version(s). Check your internet connection and try again later. " +
            $"(Cause: {ex.Message})",
    };
}

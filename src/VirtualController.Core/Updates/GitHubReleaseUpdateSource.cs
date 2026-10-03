using System.Text.Json;

namespace VirtualController.Core.Updates;

/// <summary>
/// <see cref="IUpdateSource"/> implementation backed by the project's GitHub Releases API
/// (https://api.github.com/repos/{owner}/{repo}/releases), where the existing release workflow
/// (.github/workflows/release.yml) publishes a release with a ZIP asset for each pushed Git tag. Fetches the
/// full release list rather than only "/releases/latest", which returns only the release GitHub marks "Latest"
/// and ignores prereleases (e.g. tags ending in -alpha/-beta/-nightly). The caller controls whether prereleases
/// are included through <see cref="GetLatestAsync"/>'s includePreReleases parameter (see
/// <see cref="UpdateSettings.IncludePreReleases"/>). Of eligible, non-draft releases, selects the highest
/// semantic version and reads its tag name ("tag_name", e.g. "v1.5.0" -> "1.5.0") and matching ZIP asset URL.
/// </summary>
public sealed class GitHubReleaseUpdateSource : IUpdateSource
{
    /// <summary>GitHub account/repository owner providing releases as the update source.</summary>
    private const string RepositoryOwner = "ssk-dev";
    private const string RepositoryName = "VirtualController";

    /// <summary>Release asset whose download URL is returned for installation; must match the archive created
    /// by the release workflow (see "Compress-Archive" in release.yml).</summary>
    private const string AssetFileName = "VirtualController-win-x64.zip";

    private static readonly Uri ReleasesListUri =
        new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases");

    private static readonly Lazy<HttpClient> HttpClientLazy = new(CreateHttpClient);

    public async Task<UpdateInfo?> GetLatestAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        var eligibleReleases = await FetchEligibleReleasesAsync(includePreReleases, cancellationToken).ConfigureAwait(false);

        JsonElement? newestRelease = null;
        SemanticVersion newestVersion = default;

        foreach (var (version, release) in eligibleReleases)
        {
            // Select solely by highest semantic version among eligible releases, not GitHub's "Latest" label.
            if (newestRelease is null || version.CompareTo(newestVersion) > 0)
            {
                newestRelease = release;
                newestVersion = version;
            }
        }

        if (newestRelease is null)
        {
            return null;
        }

        string? downloadUrl = FindAssetDownloadUrl(newestRelease.Value);
        return downloadUrl is null ? null : new UpdateInfo(newestVersion, downloadUrl, FindReleaseNotes(newestRelease.Value));
    }

    public async Task<IReadOnlyList<UpdateInfo>> GetAllAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        var eligibleReleases = await FetchEligibleReleasesAsync(includePreReleases, cancellationToken).ConfigureAwait(false);

        var result = new List<UpdateInfo>(eligibleReleases.Count);
        foreach (var (version, release) in eligibleReleases.OrderByDescending(entry => entry.Version))
        {
            string? downloadUrl = FindAssetDownloadUrl(release);
            if (downloadUrl is not null)
            {
                result.Add(new UpdateInfo(version, downloadUrl, FindReleaseNotes(release)));
            }
        }

        return result;
    }

    /// <summary>Fetches the full release list and filters out entries ineligible for installation: drafts are
    /// always excluded, and prereleases are excluded unless <paramref name="includePreReleases"/> is true.
    /// Shared by <see cref="GetLatestAsync"/> (selects the highest version) and <see cref="GetAllAsync"/> (returns
    /// all eligible versions for the change-version/rollback dialog).</summary>
    private static async Task<List<(SemanticVersion Version, JsonElement Release)>> FetchEligibleReleasesAsync(
        bool includePreReleases, CancellationToken cancellationToken)
    {
        using var response = await HttpClientLazy.Value.GetAsync(ReleasesListUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var result = new List<(SemanticVersion Version, JsonElement Release)>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var release in document.RootElement.EnumerateArray())
        {
            // Drafts are not published and must not be offered as installable updates.
            if (release.TryGetProperty("draft", out var draftElement) && draftElement.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            // Include prereleases (see the "prerelease" flag in release.yml) only when explicitly requested;
            // by default, offer stable releases only.
            bool isPrerelease = release.TryGetProperty("prerelease", out var prereleaseElement)
                && prereleaseElement.ValueKind == JsonValueKind.True;
            if (isPrerelease && !includePreReleases)
            {
                continue;
            }

            if (!release.TryGetProperty("tag_name", out var tagElement)
                || tagElement.GetString() is not { Length: > 0 } tagName)
            {
                continue;
            }

            string versionText = tagName.StartsWith('v') ? tagName[1..] : tagName;
            if (!SemanticVersion.TryParse(versionText, out var version))
            {
                continue;
            }

            // The document is disposed when this method returns. Clone the JsonElement so it can be used
            // independently of its JsonDocument.
            result.Add((version, release.Clone()));
        }

        return result;
    }

    private static string? FindAssetDownloadUrl(JsonElement releaseElement)
    {
        if (!releaseElement.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var nameElement)
                && string.Equals(nameElement.GetString(), AssetFileName, StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                return urlElement.GetString();
            }
        }

        return null;
    }

    /// <summary>Reads a release's "body" field, containing the Markdown changelog/release notes written by the
    /// release workflow (see the "Create GitHub Release" step in release.yml, softprops/action-gh-release with
    /// body_path: release-notes.md). Returns <c>null</c> if the field is missing or empty.</summary>
    private static string? FindReleaseNotes(JsonElement releaseElement)
    {
        if (!releaseElement.TryGetProperty("body", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? body = bodyElement.GetString();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // The GitHub API requires a User-Agent header; otherwise it returns HTTP 403.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{RepositoryName}-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

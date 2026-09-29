using System.Text.Json;

namespace VirtualController.Core.Updates;

/// <summary>
/// <see cref="IUpdateSource"/>-Implementierung gegen die GitHub-Releases-API des Projekts
/// (https://api.github.com/repos/{owner}/{repo}/releases/latest) - dieselbe Quelle, in die der
/// bestehende Release-Workflow (.github/workflows/release.yml) bei jedem gepushten Git-Tag
/// automatisch ein neues Release samt ZIP-Anhang veroeffentlicht. Liest daraus den Tag-Namen
/// ("tag_name", z.B. "v1.5.0" -> Version "1.5.0") sowie die Download-URL des passenden ZIP-Assets aus.
/// </summary>
public sealed class GitHubReleaseUpdateSource : IUpdateSource
{
    /// <summary>Name/Repository-Eigner auf GitHub, dessen Releases als Update-Quelle dienen.</summary>
    private const string RepositoryOwner = "ssk-dev";
    private const string RepositoryName = "VirtualController";

    /// <summary>Name des Release-Assets, dessen Download-URL als Installationsquelle zurueckgegeben wird -
    /// entspricht exakt dem vom Release-Workflow erzeugten Archiv (siehe release.yml, "Compress-Archive").</summary>
    private const string AssetFileName = "VirtualController-win-x64.zip";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private static readonly Lazy<HttpClient> HttpClientLazy = new(CreateHttpClient);

    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClientLazy.Value.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("tag_name", out var tagElement)
            || tagElement.GetString() is not { Length: > 0 } tagName)
        {
            return null;
        }

        string versionText = tagName.StartsWith('v') ? tagName[1..] : tagName;
        if (!SemanticVersion.TryParse(versionText, out var version))
        {
            return null;
        }

        string? downloadUrl = FindAssetDownloadUrl(document.RootElement);
        return downloadUrl is null ? null : new UpdateInfo(version, downloadUrl);
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Die GitHub-API verlangt zwingend einen User-Agent-Header, sonst wird die Anfrage mit HTTP 403
        // abgelehnt.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{RepositoryName}-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

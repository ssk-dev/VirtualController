using System.Text.Json;

namespace VirtualController.Core.Updates;

/// <summary>
/// <see cref="IUpdateSource"/>-Implementierung gegen die GitHub-Releases-API des Projekts
/// (https://api.github.com/repos/{owner}/{repo}/releases) - dieselbe Quelle, in die der
/// bestehende Release-Workflow (.github/workflows/release.yml) bei jedem gepushten Git-Tag
/// automatisch ein neues Release samt ZIP-Anhang veroeffentlicht. Bewusst wird die komplette
/// Release-Liste abgefragt statt nur "/releases/latest" - letzteres liefert ausschliesslich das von
/// GitHub mit dem Label "Latest" markierte Release und ignoriert alle als "Pre-release" markierten
/// Eintraege (z.B. Tags mit Suffix "-alpha"/"-beta"/"-nightly", siehe release.yml). Ob solche
/// Vorabversionen bei der Auswahl beruecksichtigt werden, steuert der Aufrufer explizit ueber den
/// Parameter <see cref="GetLatestAsync"/>.includePreReleases (siehe <see cref="UpdateSettings.IncludePreReleases"/>) -
/// aus allen dafuer in Frage kommenden (nicht als Entwurf/"draft" markierten) Releases wird dasjenige
/// mit der hoechsten semantischen Versionsnummer ausgewaehlt. Liest daraus den Tag-Namen ("tag_name",
/// z.B. "v1.5.0" -> Version "1.5.0") sowie die Download-URL des passenden ZIP-Assets aus.
/// </summary>
public sealed class GitHubReleaseUpdateSource : IUpdateSource
{
    /// <summary>Name/Repository-Eigner auf GitHub, dessen Releases als Update-Quelle dienen.</summary>
    private const string RepositoryOwner = "ssk-dev";
    private const string RepositoryName = "VirtualController";

    /// <summary>Name des Release-Assets, dessen Download-URL als Installationsquelle zurueckgegeben wird -
    /// entspricht exakt dem vom Release-Workflow erzeugten Archiv (siehe release.yml, "Compress-Archive").</summary>
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
            // Entscheidend ist ausschliesslich die hoechste semantische Versionsnummer unter den
            // dafuer in Frage kommenden Releases, nicht das von GitHub vergebene "Latest"-Label.
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

    /// <summary>Fragt die komplette Release-Liste ab und filtert daraus alle fuer eine Installation in
    /// Frage kommenden Eintraege heraus: Entwuerfe ("draft": true) werden immer ausgeschlossen, als
    /// "Pre-release" markierte Eintraege nur, falls <paramref name="includePreReleases"/> false ist -
    /// gemeinsam genutzt von <see cref="GetLatestAsync"/> (waehlt daraus die hoechste Version) und
    /// <see cref="GetAllAsync"/> (gibt alle davon zurueck, fuer den Versionswechsel-/Rollback-Dialog).</summary>
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
            // Entwuerfe ("draft": true) sind noch nicht veroeffentlicht und duerfen nicht als
            // installierbares Update angeboten werden.
            if (release.TryGetProperty("draft", out var draftElement) && draftElement.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            // Als "Pre-release" markierte Eintraege (siehe release.yml, "prerelease"-Flag) werden nur
            // beruecksichtigt, wenn der Aufrufer dies ueber includePreReleases explizit angefordert hat -
            // Standardverhalten ist, Nutzern ausschliesslich vollwertige, stabile Versionen anzubieten.
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

            // ACHTUNG: document wird am Ende dieser Methode disposed - JsonElement.Clone() erzeugt eine
            // eigenstaendige Kopie, die unabhaengig vom JsonDocument weiterverwendet werden kann.
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

    /// <summary>Liest das "body"-Feld eines GitHub-Releases aus - enthaelt den vom Release-Workflow
    /// (siehe release.yml, Schritt "GitHub Release erstellen und Dateien anhaengen",
    /// "softprops/action-gh-release" mit "body_path: release-notes.md") hinterlegten, Markdown-
    /// formatierten Changelog-/Release-Notes-Text der Version (gruppiert in "## Features"/"## Fixes").
    /// Gibt <c>null</c> zurueck, falls das Feld fehlt oder leer ist (z.B. bei manuell ohne Notizen
    /// erstellten Releases).</summary>
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
        // Die GitHub-API verlangt zwingend einen User-Agent-Header, sonst wird die Anfrage mit HTTP 403
        // abgelehnt.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{RepositoryName}-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

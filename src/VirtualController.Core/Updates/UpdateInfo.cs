namespace VirtualController.Core.Updates;

/// <summary>
/// Information about a version currently available from the update source, as returned by
/// <see cref="IUpdateSource"/>.
/// </summary>
/// <param name="Version">Available version.</param>
/// <param name="DownloadUrl">URL of the installation/download package for this version.</param>
/// <param name="ReleaseNotes">Changelog/release notes for this version, e.g. the GitHub release's Markdown
/// "body" field, if provided by the update source; otherwise <c>null</c> or empty.</param>
public sealed record UpdateInfo(SemanticVersion Version, string DownloadUrl, string? ReleaseNotes = null);

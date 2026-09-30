namespace VirtualController.Core.Updates;

/// <summary>
/// Informationen ueber die aktuell an der Update-Quelle verfuegbare Version, wie von
/// <see cref="IUpdateSource"/> geliefert.
/// </summary>
/// <param name="Version">Verfuegbare Version.</param>
/// <param name="DownloadUrl">URL des Installations-/Download-Pakets fuer diese Version.</param>
/// <param name="ReleaseNotes">Changelog/Release-Notes-Text dieser Version (z.B. das "body"-Feld des
/// GitHub-Releases, Markdown-formatiert), sofern die Update-Quelle einen solchen liefert - sonst
/// <c>null</c> oder leer.</param>
public sealed record UpdateInfo(SemanticVersion Version, string DownloadUrl, string? ReleaseNotes = null);

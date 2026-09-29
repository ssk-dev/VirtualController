namespace VirtualController.Core.Updates;

/// <summary>
/// Informationen ueber die aktuell an der Update-Quelle verfuegbare Version, wie von
/// <see cref="IUpdateSource"/> geliefert.
/// </summary>
/// <param name="Version">Verfuegbare Version.</param>
/// <param name="DownloadUrl">URL des Installations-/Download-Pakets fuer diese Version.</param>
public sealed record UpdateInfo(SemanticVersion Version, string DownloadUrl);

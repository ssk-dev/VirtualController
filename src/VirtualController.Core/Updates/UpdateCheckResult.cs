namespace VirtualController.Core.Updates;

/// <summary>
/// Ergebnis einer Update-Pruefung (<see cref="UpdateChecker.CheckAsync"/>): enthaelt sowohl die aktuell
/// installierte als auch die an der Update-Quelle verfuegbare Version, ob daraus ein Update resultiert,
/// sowie dessen Download-/Installationsquelle.
/// </summary>
/// <param name="InstalledVersion">Aktuell installierte Version (<see cref="AppVersionProvider.CurrentVersion"/>).</param>
/// <param name="AvailableVersion">Aktuell an der Update-Quelle verfuegbare Version.</param>
/// <param name="IsUpdateAvailable">Ob <paramref name="AvailableVersion"/> echt hoeher als
/// <paramref name="InstalledVersion"/> ist - ein Downgrade (verfuegbare Version kleiner oder gleich der
/// installierten) wird hier bewusst NICHT als Update angeboten.</param>
/// <param name="DownloadUrl">Download-/Installationsquelle des Updates.</param>
public sealed record UpdateCheckResult(
    SemanticVersion InstalledVersion,
    SemanticVersion AvailableVersion,
    bool IsUpdateAvailable,
    string DownloadUrl);

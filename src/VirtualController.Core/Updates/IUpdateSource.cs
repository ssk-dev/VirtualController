namespace VirtualController.Core.Updates;

/// <summary>
/// Abstraktion der Update-Quelle: liefert Informationen ueber die aktuell dort verfuegbare Version.
/// Bewusst als Interface ausgelagert (statt die Kommunikation direkt in <see cref="UpdateChecker"/> zu
/// verdrahten), damit die Update-Quelle spaeter einfach ausgetauscht werden kann (z.B. ein
/// selbst-gehosteter Update-Endpunkt statt GitHub Releases, siehe <see cref="GitHubReleaseUpdateSource"/>
/// fuer die aktuelle Implementierung) - <see cref="UpdateChecker"/> selbst muesste dafuer nicht
/// angepasst werden.
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// Ermittelt die aktuell an der Update-Quelle verfuegbare Version. Gibt <c>null</c> zurueck, falls
    /// die Quelle erreichbar war, aber keine gueltige Versionsinformation lieferte (z.B. unerwartetes
    /// Antwortformat) - ein Verbindungsfehler (keine Internetverbindung, Server nicht erreichbar) soll
    /// stattdessen als Exception durchgereicht werden, damit <see cref="UpdateChecker"/> beide Faelle
    /// (ungueltige Antwort vs. Verbindungsfehler) einheitlich als fehlgeschlagene Pruefung behandeln kann.
    /// </summary>
    /// <param name="includePreReleases">Ob auch als "Pre-release" markierte Versionen (z.B. Tags mit
    /// Suffix "-alpha"/"-beta"/"-nightly") beruecksichtigt werden sollen, statt ausschliesslich
    /// vollwertige, stabile Releases. Entspricht der Einstellung "Auch Vorabversionen beruecksichtigen"
    /// (siehe <see cref="UpdateSettings.IncludePreReleases"/>).</param>
    Task<UpdateInfo?> GetLatestAsync(bool includePreReleases, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ermittelt ALLE aktuell an der Update-Quelle verfuegbaren Versionen, absteigend sortiert (hoechste
    /// Version zuerst). Im Gegensatz zu <see cref="GetLatestAsync"/> nicht auf die jeweils neueste Version
    /// beschraenkt - wird benoetigt, damit der Nutzer ueber den "Zu einer anderen Version wechseln"-Dialog
    /// (siehe <see cref="VirtualController.App.ViewModels.RollbackDialogViewModel"/>) explizit auch eine
    /// aeltere Version als die aktuell installierte auswaehlen kann (Rollback) - ein Vorgang, den die
    /// automatische Update-Pruefung (<see cref="UpdateChecker.CheckAsync"/>) bewusst nicht anbietet.
    /// </summary>
    /// <param name="includePreReleases">Siehe <see cref="GetLatestAsync"/>.</param>
    Task<IReadOnlyList<UpdateInfo>> GetAllAsync(bool includePreReleases, CancellationToken cancellationToken = default);
}

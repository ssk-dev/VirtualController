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
    Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default);
}

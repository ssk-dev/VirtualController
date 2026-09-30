using System.Net;
using System.Text.Json;

namespace VirtualController.Core.Updates;

/// <summary>
/// Fuehrt eine Update-Pruefung durch: fragt die konfigurierte <see cref="IUpdateSource"/> nach der
/// aktuell verfuegbaren Version und vergleicht sie semantisch (siehe <see cref="SemanticVersion"/>) mit
/// der aktuell installierten Version (<see cref="AppVersionProvider.CurrentVersion"/>). Bewusst von der
/// konkreten Update-Quelle entkoppelt (Konstruktor-Injection von <see cref="IUpdateSource"/>), damit
/// diese spaeter ausgetauscht werden kann, ohne die Vergleichslogik hier anzufassen.
/// </summary>
public sealed class UpdateChecker
{
    private readonly IUpdateSource _source;

    public UpdateChecker(IUpdateSource source)
    {
        _source = source;
    }

    /// <summary>
    /// Ermittelt, ob eine neuere Version als die installierte verfuegbar ist.
    /// </summary>
    /// <param name="includePreReleases">Ob auch als "Pre-release" markierte Versionen beruecksichtigt
    /// werden sollen (siehe <see cref="UpdateSettings.IncludePreReleases"/>), statt ausschliesslich
    /// vollwertige, stabile Releases.</param>
    /// <exception cref="UpdateCheckException">
    /// Die Pruefung ist fehlgeschlagen (Verbindungsfehler oder ungueltige Antwort der Update-Quelle).
    /// Enthaelt eine fuer die Anzeige an den Nutzer geeignete <see cref="Exception.Message"/>.
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
            // Nur echte, vom Aufrufer ausgeloeste Abbrueche werden unveraendert weitergereicht - eine
            // TaskCanceledException, die HttpClient intern durch sein eigenes Anfrage-Timeout auswirft,
            // ist ebenfalls eine OperationCanceledException, wurde aber NICHT vom uebergebenen
            // cancellationToken verursacht und soll stattdessen unten als praezise Zeitueberschreitung
            // klassifiziert werden (siehe DescribeSourceFailure).
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(DescribeSourceFailure(ex), ex);
        }

        if (latest is null)
        {
            throw new UpdateCheckException(
                "Die Update-Quelle hat keine gültige Versionsinformation zurückgegeben. Möglicherweise " +
                "enthält das GitHub-Repository kein passendes Release oder keines der Releases besitzt das " +
                "erwartete Installationspaket als Anhang.");
        }

        bool isUpdateAvailable = latest.Version > installed;
        return new UpdateCheckResult(installed, latest.Version, isUpdateAvailable, latest.DownloadUrl, latest.ReleaseNotes);
    }

    /// <summary>
    /// Ermittelt ALLE aktuell an der Update-Quelle verfuegbaren Versionen (nicht nur die neueste) - wird
    /// vom "Version wechseln"-Dialog (Rollback-Funktion, siehe <see cref="UpdateCoordinator.GetAllVersionsAsync"/>)
    /// benoetigt, damit der Nutzer explizit auch zu einer aelteren als der aktuell installierten Version
    /// zurueckwechseln kann.
    /// </summary>
    /// <param name="includePreReleases">Siehe <see cref="CheckAsync"/>.</param>
    /// <exception cref="UpdateCheckException">
    /// Die Abfrage ist fehlgeschlagen (Verbindungsfehler oder ungueltige Antwort der Update-Quelle).
    /// Enthaelt eine fuer die Anzeige an den Nutzer geeignete <see cref="Exception.Message"/>.
    /// </exception>
    public async Task<IReadOnlyList<UpdateInfo>> GetAllAvailableAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _source.GetAllAsync(includePreReleases, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Siehe CheckAsync fuer die Begruendung dieser Bedingung.
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(DescribeSourceFailure(ex), ex);
        }
    }

    /// <summary>
    /// Erstellt eine praezise, fuer die Anzeige an den Nutzer geeignete Fehlerbeschreibung anhand der Art
    /// der zugrundeliegenden Ausnahme - unterscheidet dabei bewusst zwischen einem tatsaechlichen
    /// Verbindungsproblem (z.B. keine Internetverbindung, DNS-Fehler, Timeout) und einem Problem, das von
    /// der Update-Quelle selbst verursacht wurde (z.B. das GitHub-Repository oder das angefragte Release
    /// existiert nicht (mehr) -&gt; HTTP 404, das Anfragelimit der GitHub-API wurde erreicht -&gt; HTTP 403,
    /// oder die Antwort war kein gueltiges JSON). Ohne diese Unterscheidung wuerde z.B. ein umbenanntes
    /// oder geloeschtes GitHub-Repository dem Nutzer faelschlich als "keine Internetverbindung"
    /// angezeigt, obwohl die eigene Internetverbindung einwandfrei funktioniert.
    /// </summary>
    private static string DescribeSourceFailure(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "Die Update-Quelle wurde nicht gefunden (HTTP 404). Möglicherweise wurde das GitHub-Repository " +
            "umbenannt, verschoben oder ist nicht (mehr) öffentlich zugänglich. Bitte die Konfiguration der " +
            "Update-Quelle prüfen.",

        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "Der Zugriff auf die Update-Quelle wurde verweigert (HTTP 403). Dies ist meist auf das " +
            "Anfragelimit der GitHub-API zurückzuführen (z.B. bei häufigen Prüfungen ohne Authentifizierung) " +
            "- bitte später erneut versuchen.",

        HttpRequestException { StatusCode: not null } httpEx =>
            $"Die Update-Quelle hat einen Fehler zurückgegeben (HTTP {(int)httpEx.StatusCode!.Value} {httpEx.StatusCode}). " +
            "Bitte später erneut versuchen.",

        HttpRequestException httpEx =>
            $"Die Update-Quelle konnte nicht erreicht werden. Bitte Internetverbindung prüfen und später " +
            $"erneut versuchen. (Ursache: {httpEx.Message})",

        TaskCanceledException =>
            "Die Anfrage an die Update-Quelle hat zu lange gedauert (Zeitüberschreitung). Bitte " +
            "Internetverbindung prüfen und später erneut versuchen.",

        JsonException =>
            "Die Update-Quelle hat eine ungültige oder unerwartete Antwort geliefert (kein gültiges JSON). " +
            "Dies deutet auf ein Problem der Update-Quelle selbst hin, nicht auf die eigene Internetverbindung.",

        _ =>
            $"Die verfügbare(n) Version(en) konnten nicht ermittelt werden. Bitte Internetverbindung prüfen " +
            $"und später erneut versuchen. (Ursache: {ex.Message})",
    };
}

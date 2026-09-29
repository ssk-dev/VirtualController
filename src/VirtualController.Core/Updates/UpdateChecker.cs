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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(
                "Die verfügbare Version konnte nicht ermittelt werden. Bitte Internetverbindung prüfen und später erneut versuchen.",
                ex);
        }

        if (latest is null)
        {
            throw new UpdateCheckException("Die Update-Quelle hat keine gültige Versionsinformation zurückgegeben.");
        }

        bool isUpdateAvailable = latest.Version > installed;
        return new UpdateCheckResult(installed, latest.Version, isUpdateAvailable, latest.DownloadUrl);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateCheckException(
                "Die verfügbaren Versionen konnten nicht ermittelt werden. Bitte Internetverbindung prüfen und später erneut versuchen.",
                ex);
        }
    }
}

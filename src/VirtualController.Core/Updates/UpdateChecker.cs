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
    /// <exception cref="UpdateCheckException">
    /// Die Pruefung ist fehlgeschlagen (Verbindungsfehler oder ungueltige Antwort der Update-Quelle).
    /// Enthaelt eine fuer die Anzeige an den Nutzer geeignete <see cref="Exception.Message"/>.
    /// </exception>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installed = AppVersionProvider.CurrentVersion;

        UpdateInfo? latest;
        try
        {
            latest = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
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
}

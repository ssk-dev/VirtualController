namespace VirtualController.Core.Updates;

/// <summary>Ergebnis-Kategorie einer <see cref="UpdateCoordinator.CheckAsync"/>-Pruefung.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>Keine neuere Version verfuegbar (installierte Version ist aktuell oder neuer).</summary>
    UpToDate,

    /// <summary>Eine neuere, noch nicht uebersprungene Version ist verfuegbar - der Aufrufer soll das
    /// Update-Popup anzeigen.</summary>
    UpdateAvailable,

    /// <summary>Eine neuere Version ist verfuegbar, wurde vom Nutzer aber bereits per "Update
    /// ueberspringen" fuer genau diese Versionsnummer dauerhaft uebersprungen (siehe
    /// <see cref="UpdateCoordinator.SkipVersion"/>) - kein erneutes Popup fuer dieselbe Version.</summary>
    UpdateSkipped
}

/// <summary>Kombiniert die Ergebnis-Kategorie mit den vollstaendigen Versionsdetails.</summary>
public sealed record UpdateCoordinatorResult(UpdateCheckOutcome Outcome, UpdateCheckResult Details);

/// <summary>
/// Zentrale Schnittstelle der App-Schicht zur Update-Funktion: kombiniert <see cref="UpdateChecker"/>
/// (Kommunikation mit der Update-Quelle + semantischer Versionsvergleich) mit der Verwaltung der
/// persistierten Einstellung "Automatisch auf Updates pruefen" sowie der versionsbezogenen
/// "Update ueberspringen"-Markierung (<see cref="UpdateSettingsStore"/>). Sowohl die automatische
/// Pruefung beim Programmstart als auch die manuelle Pruefung ueber den "Auf Updates pruefen"-Button
/// nutzen ausschliesslich diese eine Klasse, damit beide Pfade exakt dieselbe Skip-Logik anwenden.
/// Bietet zusaetzlich <see cref="GetAllVersionsAsync"/> fuer den "Version wechseln"-Dialog (Rollback-
/// Funktion), mit dem der Nutzer explizit auch zu einer aelteren Version zurueckwechseln kann.
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly UpdateChecker _checker;
    private readonly string? _baseDirectory;

    /// <param name="source">Zu verwendende Update-Quelle. Optional, damit die Quelle spaeter leicht
    /// ausgetauscht werden kann bzw. in Tests durch ein Test-Double ersetzbar ist - Standard ist
    /// <see cref="GitHubReleaseUpdateSource"/>.</param>
    /// <param name="baseDirectory">Basisverzeichnis fuer <see cref="UpdateSettingsStore"/>, Standard ist
    /// <see cref="Profiles.ProfileStore.BaseDirectory"/>. Nur fuer Tests relevant.</param>
    public UpdateCoordinator(IUpdateSource? source = null, string? baseDirectory = null)
    {
        _checker = new UpdateChecker(source ?? new GitHubReleaseUpdateSource());
        _baseDirectory = baseDirectory;
    }

    /// <summary>Ob bei jedem App-Start automatisch geprueft werden soll, ob eine neuere Version
    /// verfuegbar ist. Liest/schreibt sofort (kein Batching) von/nach "update-settings.json"
    /// (siehe <see cref="UpdateSettingsStore"/>), unabhaengig vom expliziten "Profile speichern"-Vorgang
    /// der restlichen Konfiguration.</summary>
    public bool AutoCheckEnabled
    {
        get => UpdateSettingsStore.Load(_baseDirectory).AutoCheckEnabled;
        set
        {
            var settings = UpdateSettingsStore.Load(_baseDirectory);
            if (settings.AutoCheckEnabled == value)
            {
                return;
            }

            settings.AutoCheckEnabled = value;
            UpdateSettingsStore.Save(settings, _baseDirectory);
        }
    }

    /// <summary>Ob bei der Update-Pruefung auch als "Pre-release" markierte Versionen (z.B. Tags mit
    /// Suffix "-alpha"/"-beta"/"-nightly") beruecksichtigt werden sollen, statt ausschliesslich
    /// vollwertige, stabile Releases. Liest/schreibt sofort (kein Batching) von/nach
    /// "update-settings.json" (siehe <see cref="UpdateSettingsStore"/>), analog zu
    /// <see cref="AutoCheckEnabled"/>.</summary>
    public bool IncludePreReleases
    {
        get => UpdateSettingsStore.Load(_baseDirectory).IncludePreReleases;
        set
        {
            var settings = UpdateSettingsStore.Load(_baseDirectory);
            if (settings.IncludePreReleases == value)
            {
                return;
            }

            settings.IncludePreReleases = value;
            UpdateSettingsStore.Save(settings, _baseDirectory);
        }
    }

    /// <summary>
    /// Fuehrt eine Update-Pruefung durch und ordnet das Ergebnis anhand der zuletzt uebersprungenen
    /// Version einer der <see cref="UpdateCheckOutcome"/>-Kategorien zu.
    /// </summary>
    /// <exception cref="UpdateCheckException">Die Pruefung ist fehlgeschlagen (Verbindungsfehler oder
    /// ungueltige Antwort der Update-Quelle) - siehe <see cref="UpdateChecker.CheckAsync"/>.</exception>
    public async Task<UpdateCoordinatorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var details = await _checker.CheckAsync(IncludePreReleases, cancellationToken).ConfigureAwait(false);

        if (!details.IsUpdateAvailable)
        {
            return new UpdateCoordinatorResult(UpdateCheckOutcome.UpToDate, details);
        }

        var settings = UpdateSettingsStore.Load(_baseDirectory);
        bool alreadySkipped = SemanticVersion.TryParse(settings.SkippedVersion, out var skippedVersion)
            && skippedVersion == details.AvailableVersion;

        return new UpdateCoordinatorResult(
            alreadySkipped ? UpdateCheckOutcome.UpdateSkipped : UpdateCheckOutcome.UpdateAvailable,
            details);
    }

    /// <summary>Markiert <paramref name="version"/> dauerhaft als uebersprungen: eine erneute
    /// <see cref="CheckAsync"/>-Pruefung liefert fuer exakt diese Versionsnummer danach
    /// <see cref="UpdateCheckOutcome.UpdateSkipped"/> statt <see cref="UpdateCheckOutcome.UpdateAvailable"/>.
    /// Eine spaeter erscheinende, noch nicht uebersprungene hoehere Version wird davon nicht betroffen
    /// weiterhin regulaer als <see cref="UpdateCheckOutcome.UpdateAvailable"/> gemeldet (versionsbezogenes,
    /// kein generelles Uebersprringen).</summary>
    public void SkipVersion(SemanticVersion version)
    {
        var settings = UpdateSettingsStore.Load(_baseDirectory);
        settings.SkippedVersion = version.ToString();
        UpdateSettingsStore.Save(settings, _baseDirectory);
    }

    /// <summary>
    /// Ermittelt ALLE an der Update-Quelle verfuegbaren Versionen (absteigend sortiert), fuer den
    /// "Version wechseln"-Dialog: im Gegensatz zu <see cref="CheckAsync"/> nicht auf ein einzelnes,
    /// bewertetes Ergebnis (neuer/uebersprungen/aktuell) beschraenkt, sondern die vollstaendige Liste
    /// installierbarer Versionen - einschliesslich solcher, die AELTER als die aktuell installierte
    /// Version sind, um einen gezielten Rollback zu ermoeglichen. Beruecksichtigt dabei die persistierte
    /// <see cref="IncludePreReleases"/>-Einstellung genau wie <see cref="CheckAsync"/>.
    /// </summary>
    /// <exception cref="UpdateCheckException">Die Abfrage ist fehlgeschlagen (Verbindungsfehler oder
    /// ungueltige Antwort der Update-Quelle) - siehe <see cref="UpdateChecker.GetAllAvailableAsync"/>.</exception>
    public Task<IReadOnlyList<UpdateInfo>> GetAllVersionsAsync(CancellationToken cancellationToken = default) =>
        _checker.GetAllAvailableAsync(IncludePreReleases, cancellationToken);
}

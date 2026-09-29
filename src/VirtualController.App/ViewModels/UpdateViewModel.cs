using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel der Update-Funktion (siehe "Einstellungen"-Tab in <see cref="Views.MainWindow"/>): verwaltet
/// die persistierte Einstellung "Automatisch auf Updates pruefen" sowie die manuelle Pruefung ueber den
/// "Auf Updates pruefen"-Button. Delegiert die eigentliche Kommunikation mit der Update-Quelle und den
/// semantischen Versionsvergleich vollstaendig an <see cref="UpdateCoordinator"/> (VirtualController.Core) -
/// dieses ViewModel uebersetzt dessen Ergebnisse lediglich in UI-Zustand (Ladeindikator, Statustext) und
/// in das <see cref="UpdateAvailable"/>-Ereignis, auf das <see cref="Views.MainWindow"/> reagiert, um das
/// Update-Popup anzuzeigen (analog zu <see cref="VirtualControllerViewModel.ModeActivated"/> ->
/// <see cref="Views.ModeChangeToast"/>).
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCoordinator _coordinator = new();

    /// <summary>Ob bei jedem App-Start automatisch geprueft werden soll, ob eine neuere Version verfuegbar
    /// ist. Aenderungen werden sofort persistiert (siehe <see cref="UpdateCoordinator.AutoCheckEnabled"/>),
    /// unabhaengig vom expliziten "Profile speichern"-Button der restlichen Konfiguration.</summary>
    [ObservableProperty]
    private bool _autoCheckEnabled;

    /// <summary>Ob bei der Update-Pruefung auch als "Pre-release" markierte Versionen (z.B. Tags mit
    /// Suffix "-alpha"/"-beta"/"-nightly") beruecksichtigt werden sollen, statt ausschliesslich
    /// vollwertige, stabile Releases. Aenderungen werden sofort persistiert (siehe
    /// <see cref="UpdateCoordinator.IncludePreReleases"/>), analog zu <see cref="AutoCheckEnabled"/>.</summary>
    [ObservableProperty]
    private bool _includePreReleases;

    /// <summary>Ob aktuell eine Update-Pruefung laeuft (manuell oder automatisch beim Start) - blendet in
    /// der View einen Ladeindikator ein und deaktiviert den "Auf Updates pruefen"-Button, damit der Nutzer
    /// erkennen kann, dass eine Pruefung bereits laeuft, statt sie versehentlich mehrfach parallel
    /// auszuloesen.</summary>
    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>Ergebnistext der letzten Pruefung (Erfolg oder Fehler) fuer die Anzeige im
    /// "Einstellungen"-Tab.</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>Aktuell installierte Version, fuer die Anzeige im "Einstellungen"-Tab.</summary>
    public string CurrentVersionText => AppVersionProvider.RawVersion;

    /// <summary>Wird ausgeloest, sobald eine Pruefung (manuell oder automatisch) eine tatsaechlich neuere,
    /// anzuzeigende Version ermittelt hat - <see cref="Views.MainWindow"/> zeigt daraufhin das
    /// Update-Popup (<see cref="Views.UpdateAvailableDialog"/>) an.</summary>
    public event Action<UpdateCheckResult>? UpdateAvailable;

    public UpdateViewModel()
    {
        _autoCheckEnabled = _coordinator.AutoCheckEnabled;
        _includePreReleases = _coordinator.IncludePreReleases;
    }

    partial void OnAutoCheckEnabledChanged(bool value) => _coordinator.AutoCheckEnabled = value;

    partial void OnIncludePreReleasesChanged(bool value) => _coordinator.IncludePreReleases = value;

    partial void OnIsCheckingForUpdatesChanged(bool value) => CheckForUpdatesCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Manuelle Pruefung ueber den "Auf Updates pruefen"-Button: laeuft unabhaengig von
    /// <see cref="AutoCheckEnabled"/> immer, und zeigt - im Gegensatz zur automatischen Pruefung beim
    /// Programmstart (<see cref="RunStartupCheckAsync"/>) - dem Nutzer bei einem Fehler eine verstaendliche
    /// Meldung an, statt ihn stillschweigend zu verwerfen. Eine bereits per "Update ueberspringen"
    /// markierte Version wird hier bewusst trotzdem erneut angeboten (<see cref="UpdateCheckResult.IsUpdateAvailable"/>
    /// statt der versionsbezogenen Skip-Kategorie aus <see cref="UpdateCoordinator.CheckAsync"/>) - ein
    /// expliziter Klick auf "Auf Updates pruefen" ist eine bewusste Nutzeraktion, die unabhaengig von
    /// einer frueher getroffenen Uebersprringen-Entscheidung ein Ergebnis liefern soll.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        StatusText = "Suche nach Updates...";

        try
        {
            var result = await _coordinator.CheckAsync().ConfigureAwait(true);

            if (result.Details.IsUpdateAvailable)
            {
                StatusText = $"Neue Version verfügbar: {result.Details.AvailableVersion}";
                UpdateAvailable?.Invoke(result.Details);
            }
            else
            {
                StatusText = $"Kein Update verfügbar. Version {result.Details.InstalledVersion} ist aktuell.";
            }
        }
        catch (UpdateCheckException ex)
        {
            StatusText = null;
            System.Windows.MessageBox.Show(
                ex.Message, "Update-Prüfung fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanCheckForUpdates() => !IsCheckingForUpdates;

    /// <summary>
    /// Automatische Pruefung beim App-Start, nur ausgefuehrt, wenn <see cref="AutoCheckEnabled"/> aktiv
    /// ist. Wirft NIEMALS eine Ausnahme und blockiert nicht (siehe Aufrufer in <see cref="Views.MainWindow"/>):
    /// eine fehlende Internetverbindung oder ein nicht erreichbarer Update-Server duerfen die normale
    /// Anwendungsfunktion in keinem Fall beeintraechtigen - Fehler werden hier bewusst stillschweigend
    /// verworfen. Eine bereits per "Update ueberspringen" markierte Version loest hier (im Gegensatz zu
    /// <see cref="CheckForUpdatesAsync"/>) bewusst KEIN erneutes Popup aus.
    /// </summary>
    public async Task RunStartupCheckAsync()
    {
        if (!AutoCheckEnabled)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            var result = await _coordinator.CheckAsync().ConfigureAwait(true);

            if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
            {
                StatusText = $"Neue Version verfügbar: {result.Details.AvailableVersion}";
                UpdateAvailable?.Invoke(result.Details);
            }
        }
        catch (UpdateCheckException)
        {
            // Automatische Pruefung beim Start darf die Anwendung nicht beeintraechtigen - z.B. fehlende
            // Internetverbindung oder nicht erreichbarer Update-Server werden hier bewusst verworfen,
            // ohne den Nutzer damit zu stoeren (im Gegensatz zur manuellen Pruefung, siehe
            // CheckForUpdatesAsync).
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Erzeugt das ViewModel des Update-Popups fuer <paramref name="details"/>, mit Zugriff auf
    /// denselben <see cref="UpdateCoordinator"/> (fuer <see cref="UpdateCoordinator.SkipVersion"/> bei
    /// Klick auf "Update ueberspringen").</summary>
    public UpdateAvailableDialogViewModel CreateAvailableDialogViewModel(UpdateCheckResult details) =>
        new(details, _coordinator);

    /// <summary>Erzeugt das ViewModel des "Version wechseln"-Dialogs (Rollback-Funktion), mit Zugriff auf
    /// denselben <see cref="UpdateCoordinator"/> (fuer <see cref="UpdateCoordinator.GetAllVersionsAsync"/>,
    /// unter Beruecksichtigung der aktuellen <see cref="IncludePreReleases"/>-Einstellung).</summary>
    public RollbackDialogViewModel CreateRollbackDialogViewModel() => new(_coordinator);
}

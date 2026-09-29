using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel des Update-Popups (<see cref="Views.UpdateAvailableDialog"/>), das erscheint, sobald
/// <see cref="UpdateViewModel"/> eine neuere, noch nicht uebersprungene Version ermittelt hat. Bietet
/// dem Nutzer zwei Aktionen: "Update installieren" (startet <see cref="UpdateInstaller"/>, zeigt den
/// Installationsfortschritt an und beendet bei Erfolg die Anwendung, damit der separate Updater-Prozess
/// die Dateien austauschen und die neue Version starten kann) und "Update ueberspringen" (markiert die
/// angebotene Version dauerhaft als uebersprungen ueber <see cref="UpdateCoordinator.SkipVersion"/> und
/// schliesst das Popup ohne weitere Aktion).
/// </summary>
public sealed partial class UpdateAvailableDialogViewModel : ObservableObject
{
    private readonly UpdateCheckResult _details;
    private readonly UpdateCoordinator _coordinator;
    private readonly UpdateInstaller _installer = new();

    /// <summary>Ob aktuell ein Download/eine Installation laeuft - blendet in der View einen
    /// Ladeindikator ein und deaktiviert beide Buttons, damit der Nutzer waehrend der laufenden
    /// Installation nicht versehentlich das Popup schliessen oder eine zweite Installation anstossen
    /// kann.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>Statustext waehrend der Installation (siehe <see cref="InstallAsync"/>), fuer die Anzeige
    /// in der View.</summary>
    [ObservableProperty]
    private string? _installStatusText;

    /// <summary>Titeltext des Popups, z.B. "Neue Version verfügbar: Version 1.5.0".</summary>
    public string TitleText => $"Neue Version verfügbar: Version {_details.AvailableVersion}";

    public string InstalledVersionText => _details.InstalledVersion.ToString();

    public string AvailableVersionText => _details.AvailableVersion.ToString();

    /// <summary>Wird ausgeloest, sobald der Nutzer das Popup schliessen soll - entweder nach "Update
    /// ueberspringen" oder nachdem die Installation erfolgreich gestartet wurde (unmittelbar vor dem
    /// bevorstehenden Beenden der Anwendung durch <see cref="Views.MainWindow"/>).</summary>
    public event Action? RequestClose;

    public UpdateAvailableDialogViewModel(UpdateCheckResult details, UpdateCoordinator coordinator)
    {
        _details = details;
        _coordinator = coordinator;
    }

    /// <summary>
    /// "Update installieren": laedt das Update-Archiv herunter, entpackt es in ein temporaeres
    /// Verzeichnis und startet danach den separaten Updater-Prozess (siehe <see cref="UpdateInstaller"/>).
    /// Schlaegt der Download/das Entpacken fehl, bleibt die aktuell installierte Version unveraendert
    /// lauffaehig und eine verstaendliche Fehlermeldung wird angezeigt - das Popup bleibt in diesem Fall
    /// geoeffnet, damit der Nutzer es erneut versuchen oder stattdessen ueberspringen kann. Bei Erfolg
    /// wird <see cref="RequestClose"/> ausgeloest; <see cref="Views.MainWindow"/> beendet daraufhin die
    /// Anwendung, damit der Updater-Prozess die aktuell durch die laufende EXE gesperrten Dateien
    /// ueberschreiben kann.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        InstallStatusText = "Update wird heruntergeladen...";

        try
        {
            var preparation = await _installer.PrepareAsync(_details.DownloadUrl).ConfigureAwait(true);
            InstallStatusText = "Update wird installiert...";
            _installer.LaunchUpdaterProcess(preparation);

            // Ab hier ist der separate Updater-Prozess gestartet und wartet auf die Beendigung dieses
            // Prozesses (siehe UpdateInstaller.LaunchUpdaterProcess) - das eigentliche Beenden der
            // Anwendung (Application.Shutdown) obliegt bewusst dem Aufrufer (Views.MainWindow), nicht
            // diesem ViewModel, das keine Kenntnis von der WPF-Application-Instanz haben soll.
            RequestClose?.Invoke();
        }
        catch (UpdateInstallException ex)
        {
            IsInstalling = false;
            InstallStatusText = null;
            System.Windows.MessageBox.Show(
                ex.Message, "Update-Installation fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanInstall() => !IsInstalling;

    /// <summary>
    /// "Update ueberspringen": markiert die aktuell angebotene Version dauerhaft als uebersprungen
    /// (siehe <see cref="UpdateCoordinator.SkipVersion"/>) und schliesst das Popup. Eine spaeter
    /// erscheinende, hoehere Version wird davon unbeeintraechtigt wieder regulaer angeboten.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void SkipUpdate()
    {
        _coordinator.SkipVersion(_details.AvailableVersion);
        RequestClose?.Invoke();
    }
}

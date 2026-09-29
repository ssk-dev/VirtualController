using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Updates;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Eine an der Update-Quelle verfuegbare Version, aufbereitet fuer die Anzeige in der Versionsliste
/// des "Version wechseln"-Dialogs (<see cref="Views.RollbackDialog"/>).
/// </summary>
/// <param name="Version">Versionsnummer.</param>
/// <param name="DownloadUrl">Download-URL des zugehoerigen Installationspakets.</param>
/// <param name="IsCurrentlyInstalled">Ob dies exakt die aktuell installierte Version ist (Vergleich
/// gegen <see cref="AppVersionProvider.CurrentVersion"/>) - wird in der Liste entsprechend markiert und
/// ist beim Oeffnen des Dialogs standardmaessig vorausgewaehlt.</param>
public sealed record RollbackVersionOption(SemanticVersion Version, string DownloadUrl, bool IsCurrentlyInstalled)
{
    /// <summary>Anzeigetext fuer die Versionsliste, z.B. "1.4.2 (aktuell installiert)" oder "1.3.0".</summary>
    public string DisplayText => IsCurrentlyInstalled ? $"{Version} (aktuell installiert)" : Version.ToString();
}

/// <summary>
/// ViewModel des "Version wechseln"-Dialogs (<see cref="Views.RollbackDialog"/>): laedt ALLE an der
/// Update-Quelle verfuegbaren Versionen (siehe <see cref="UpdateCoordinator.GetAllVersionsAsync"/>) und
/// erlaubt dem Nutzer, gezielt zu JEDER dieser Versionen zu wechseln - insbesondere auch zu einer
/// AELTEREN als der aktuell installierten (Rollback). Dies ist eine bewusste, explizite Nutzeraktion und
/// unterliegt daher NICHT der Einschraenkung der regulaeren Update-Pruefung (<see cref="UpdateChecker"/>),
/// die niemals ein Downgrade anbietet - die automatische bzw. manuelle Update-Pruefung
/// (<see cref="UpdateViewModel"/>) bleibt davon unberuehrt und unveraendert. Vor jeder Installation wird
/// zur Vermeidung versehentlicher Versionswechsel eine Bestaetigung eingeholt. Die eigentliche
/// Installation nutzt denselben <see cref="UpdateInstaller"/> wie das reguläre Update-Popup (siehe
/// <see cref="UpdateAvailableDialogViewModel"/>), da dieser lediglich eine Download-URL entgegennimmt und
/// somit unabhaengig davon funktioniert, ob es sich um ein Up- oder ein Downgrade handelt.
/// </summary>
public sealed partial class RollbackDialogViewModel : ObservableObject
{
    private readonly UpdateCoordinator _coordinator;
    private readonly UpdateInstaller _installer = new();

    /// <summary>Ob aktuell die Liste der verfuegbaren Versionen geladen wird - blendet in der View einen
    /// Ladeindikator ein.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Fehlertext, falls das Laden der Versionsliste fehlgeschlagen ist (siehe
    /// <see cref="LoadVersionsAsync"/>), sonst <c>null</c>.</summary>
    [ObservableProperty]
    private string? _loadErrorText;

    /// <summary>Ob <see cref="LoadErrorText"/> aktuell einen Fehlertext enthaelt - fuer die
    /// Sichtbarkeit des Fehlertext-Blocks in der View (kein String-zu-Visibility-Konverter im
    /// Projekt vorhanden, siehe Converters.xaml).</summary>
    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorText);

    /// <summary>Alle an der Update-Quelle verfuegbaren Versionen, absteigend sortiert (siehe
    /// <see cref="LoadVersionsAsync"/>).</summary>
    public ObservableCollection<RollbackVersionOption> Versions { get; } = new();

    /// <summary>Die vom Nutzer in der Liste ausgewaehlte Version, Ziel von <see cref="InstallAsync"/>.</summary>
    [ObservableProperty]
    private RollbackVersionOption? _selectedVersion;

    /// <summary>Ob aktuell ein Download/eine Installation laeuft - blendet in der View einen
    /// Ladeindikator ein und deaktiviert Liste und Buttons, analog zu
    /// <see cref="UpdateAvailableDialogViewModel.IsInstalling"/>.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>Statustext waehrend der Installation, fuer die Anzeige in der View.</summary>
    [ObservableProperty]
    private string? _installStatusText;

    /// <summary>Fortschritt (0-100) des GESAMTEN Installationsvorgangs, siehe
    /// <see cref="UpdateInstallProgress.OverallPercent"/>.</summary>
    [ObservableProperty]
    private double _installProgressPercent;

    /// <summary>Beschreibung des aktuellen Installationsschritts inkl. Schrittzaehler, siehe
    /// <see cref="UpdateAvailableDialogViewModel.InstallStepText"/>.</summary>
    [ObservableProperty]
    private string? _installStepText;

    /// <summary>Wird ausgeloest, sobald der Dialog geschlossen werden soll - entweder nach "Abbrechen"
    /// oder nachdem die Installation erfolgreich gestartet wurde (unmittelbar vor dem bevorstehenden
    /// Beenden der Anwendung durch <see cref="Views.MainWindow"/>).</summary>
    public event Action? RequestClose;

    public RollbackDialogViewModel(UpdateCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    partial void OnLoadErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));

    partial void OnSelectedVersionChanged(RollbackVersionOption? value) => InstallCommand.NotifyCanExecuteChanged();

    partial void OnIsInstallingChanged(bool value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Laedt die Liste aller verfuegbaren Versionen (siehe <see cref="UpdateCoordinator.GetAllVersionsAsync"/>)
    /// und waehlt standardmaessig die aktuell installierte Version vor, falls sie in der Liste enthalten
    /// ist. Wird von der View beim Oeffnen des Dialogs (Loaded-Ereignis) aufgerufen, NICHT bereits im
    /// Konstruktor, damit der Dialog sofort sichtbar wird und der Ladevorgang mit sichtbarem
    /// Ladeindikator im Hintergrund erfolgen kann.
    /// </summary>
    public async Task LoadVersionsAsync()
    {
        IsLoading = true;
        LoadErrorText = null;
        Versions.Clear();

        try
        {
            var installed = AppVersionProvider.CurrentVersion;
            var available = await _coordinator.GetAllVersionsAsync().ConfigureAwait(true);

            foreach (var info in available)
            {
                Versions.Add(new RollbackVersionOption(info.Version, info.DownloadUrl, info.Version == installed));
            }

            SelectedVersion = Versions.FirstOrDefault(v => v.IsCurrentlyInstalled) ?? Versions.FirstOrDefault();
        }
        catch (UpdateCheckException ex)
        {
            LoadErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Installiert die in <see cref="SelectedVersion"/> ausgewaehlte Version - unabhaengig davon, ob es
    /// sich dabei um ein Up- oder ein Downgrade gegenueber der aktuell installierten Version handelt.
    /// Holt vorab ueber einen <see cref="System.Windows.MessageBox"/>-Dialog eine ausdrueckliche
    /// Bestaetigung ein, um versehentliche Versionswechsel durch einen Fehlklick zu vermeiden. Nutzt
    /// anschliessend denselben <see cref="UpdateInstaller"/>-Ablauf wie
    /// <see cref="UpdateAvailableDialogViewModel.InstallAsync"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var target = SelectedVersion;
        if (target is null)
        {
            return;
        }

        string action = target.Version.CompareTo(AppVersionProvider.CurrentVersion) switch
        {
            < 0 => "zu einer älteren Version zurückwechseln",
            > 0 => "zu einer neueren Version wechseln",
            _ => "diese Version erneut installieren",
        };

        var confirmation = System.Windows.MessageBox.Show(
            $"Möchten Sie wirklich {action} (Version {target.Version})? Die Anwendung wird dazu beendet und automatisch neu gestartet.",
            "Version wechseln",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirmation != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        IsInstalling = true;

        var progress = new Progress<UpdateInstallProgress>(p =>
        {
            InstallProgressPercent = p.OverallPercent;
            InstallStepText = $"Schritt {p.StepNumber} von {p.TotalSteps}: {p.StepDescription}";
            InstallStatusText = p.StepDescription;
        });

        try
        {
            var preparation = await _installer.PrepareAsync(target.DownloadUrl, progress).ConfigureAwait(true);
            _installer.LaunchUpdaterProcess(preparation, progress);

            // Ab hier ist der separate Updater-Prozess gestartet - siehe
            // UpdateAvailableDialogViewModel.InstallAsync fuer die Begruendung, warum das eigentliche
            // Beenden der Anwendung bewusst dem Aufrufer (Views.MainWindow) obliegt.
            RequestClose?.Invoke();
        }
        catch (UpdateInstallException ex)
        {
            IsInstalling = false;
            InstallStatusText = null;
            InstallStepText = null;
            InstallProgressPercent = 0;
            System.Windows.MessageBox.Show(
                ex.Message, "Installation fehlgeschlagen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanInstall() => SelectedVersion is not null && !IsInstalling && !IsLoading;

    /// <summary>"Abbrechen": schliesst den Dialog ohne eine Aenderung vorzunehmen.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => RequestClose?.Invoke();

    private bool CanCancel() => !IsInstalling;
}

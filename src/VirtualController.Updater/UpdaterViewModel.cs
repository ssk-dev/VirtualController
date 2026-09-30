using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VirtualController.Updater;

/// <summary>
/// ViewModel des einzigen Fensters dieses eigenstaendigen Updater-Prozesses (<see cref="MainWindow"/>).
/// Durchlaeuft nach dem Start automatisch drei Phasen: waehrend <see cref="IsWorking"/> zeigt die View
/// einen Statustext samt unbestimmtem Fortschrittsbalken (Warten auf Prozess-Ende, anschliessendes
/// Kopieren der Dateien); bei Erfolg wird <see cref="IsSucceeded"/> gesetzt und ein Button
/// "VirtualController starten" angeboten (siehe <see cref="StartApplicationAsync"/>); bei einem Fehler
/// wird <see cref="IsFailed"/> gesetzt und <see cref="ErrorMessage"/> mit einer verstaendlichen
/// Fehlermeldung angezeigt.
/// </summary>
public sealed partial class UpdaterViewModel : ObservableObject
{
    private readonly UpdaterArguments _arguments;

    /// <summary>Ob aktuell auf das Beenden der Hauptanwendung gewartet bzw. der Kopiervorgang
    /// durchgefuehrt wird - blendet in der View den Statustext samt Fortschrittsbalken ein.</summary>
    [ObservableProperty]
    private bool _isWorking = true;

    /// <summary>Ob der Update-Vorgang erfolgreich abgeschlossen wurde - blendet in der View die
    /// Erfolgsmeldung samt "VirtualController starten"-Button ein.</summary>
    [ObservableProperty]
    private bool _isSucceeded;

    /// <summary>Ob der Update-Vorgang fehlgeschlagen ist - blendet in der View die Fehlermeldung ein.</summary>
    [ObservableProperty]
    private bool _isFailed;

    /// <summary>Statustext waehrend <see cref="IsWorking"/>, z.B. "Warte auf Beenden der Anwendung ..."
    /// oder "Dateien werden aktualisiert ...".</summary>
    [ObservableProperty]
    private string _statusText = "Update wird vorbereitet ...";

    /// <summary>Fehlermeldung, sobald <see cref="IsFailed"/> gesetzt ist, sonst <c>null</c>.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Anzeigetext bei Erfolg, z.B. "Version 1.5.0 erfolgreich installiert."</summary>
    public string SuccessText => $"Version {_arguments.TargetVersion} erfolgreich installiert.";

    /// <summary>Wird ausgeloest, sobald sich dieses Fenster schliessen soll (nach erfolgreichem
    /// Neustart der Anwendung, siehe <see cref="StartApplicationAsync"/>).</summary>
    public event Action? RequestClose;

    internal UpdaterViewModel(UpdaterArguments arguments)
    {
        _arguments = arguments;
    }

    /// <summary>
    /// Fuehrt den eigentlichen Update-Vorgang durch (siehe <see cref="UpdateApplier.ApplyAsync"/>) und
    /// aktualisiert dabei laufend <see cref="StatusText"/>. Wird unmittelbar nach dem Anzeigen des
    /// Fensters aufgerufen (siehe <see cref="MainWindow"/>), damit der Nutzer sofort sichtbaren
    /// Fortschritt sieht, statt vor einem scheinbar untaetigen Fenster zu warten.
    /// </summary>
    public async Task RunAsync()
    {
        var result = await UpdateApplier.ApplyAsync(
            _arguments.ProcessId,
            _arguments.StagingDirectory,
            _arguments.InstallDirectory,
            status => StatusText = status,
            CancellationToken.None).ConfigureAwait(true);

        IsWorking = false;

        if (result.Succeeded)
        {
            IsSucceeded = true;
        }
        else
        {
            IsFailed = true;
            ErrorMessage = result.ErrorMessage;
        }
    }

    /// <summary>"VirtualController starten": startet die neu installierte Anwendung und schliesst
    /// anschliessend dieses Updater-Fenster (siehe <see cref="RequestClose"/>).</summary>
    [RelayCommand]
    private void StartApplication()
    {
        try
        {
            UpdateApplier.LaunchApplication(_arguments.InstallDirectory, _arguments.ExecutableFileName);
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            IsSucceeded = false;
            IsFailed = true;
            ErrorMessage = $"Die Anwendung konnte nicht gestartet werden: {ex.Message}";
        }
    }

    /// <summary>"Schließen": schliesst das Updater-Fenster ohne die Anwendung zu starten (nur im
    /// Fehlerfall verfuegbar - siehe MainWindow.xaml).</summary>
    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    /// <summary>Oeffnet die Update-Log-Datei im Standard-Texteditor, damit der Nutzer im Fehlerfall
    /// Details einsehen kann, ohne selbst nach der Datei suchen zu muessen.</summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = UpdaterLog.FilePath,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch
        {
            // Kann z.B. fehlschlagen, falls kein Standard-Texteditor fuer .log-Dateien registriert ist -
            // in diesem Fall bleibt dem Nutzer nur der in UpdaterLog.FilePath angezeigte Pfad zum
            // manuellen Oeffnen, was hier nicht weiter kritisch behandelt werden muss.
        }
    }
}

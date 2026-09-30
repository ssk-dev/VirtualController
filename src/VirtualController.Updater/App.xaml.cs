using System.Windows;

namespace VirtualController.Updater;

/// <summary>
/// Anwendungs-Einstiegspunkt dieses eigenstaendigen Updater-Prozesses. Bewusst OHNE
/// <c>StartupUri</c> in App.xaml, da die Kommandozeilenargumente (siehe <see cref="UpdaterArguments"/>)
/// zunaechst geparst werden muessen, bevor das Hauptfenster (mit dem daraus erzeugten
/// <see cref="UpdaterViewModel"/>) ueberhaupt erzeugt werden kann - schlaegt das Parsen fehl (z.B. bei
/// einem versehentlichen manuellen Start ohne Argumente), wird stattdessen eine verstaendliche
/// Fehlermeldung angezeigt und der Prozess beendet, statt mit einer unbehandelten Ausnahme abzustuerzen.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UpdaterLog.WriteSessionStart();

        if (!UpdaterArguments.TryParse(e.Args, out var arguments) || arguments is null)
        {
            UpdaterLog.Write(
                $"FEHLER: Ungueltige Kommandozeilenargumente (Anzahl={e.Args.Length}) - dieser Prozess ist nicht " +
                "zum manuellen Start vorgesehen, sondern wird ausschliesslich von VirtualController.exe waehrend eines " +
                "Update-Vorgangs gestartet.");
            MessageBox.Show(
                "Dieses Programm wird ausschließlich automatisch von VirtualController während eines Update-Vorgangs " +
                "gestartet und ist nicht zum manuellen Start vorgesehen.",
                "VirtualController Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var viewModel = new UpdaterViewModel(arguments);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        _ = viewModel.RunAsync();
    }
}

namespace VirtualController.Updater;

/// <summary>
/// Ergebnis des Parsens der Kommandozeilenargumente, mit denen dieser Prozess von
/// <c>VirtualController.Core.Updates.UpdateInstaller.LaunchUpdaterProcess</c> gestartet wird. Erwartet
/// GENAU 5 positionelle Argumente in fester Reihenfolge (kein benanntes Argument-Parsing, da diese
/// Aufrufkette vollstaendig intern ist und niemals von einem Nutzer manuell zusammengestellt wird):
/// <list type="number">
/// <item><description>Prozess-ID der Hauptanwendung, auf deren Beendigung gewartet werden muss.</description></item>
/// <item><description>Staging-Verzeichnis mit den bereits entpackten neuen Dateien.</description></item>
/// <item><description>Installationsverzeichnis, in das kopiert werden soll.</description></item>
/// <item><description>Dateiname der Haupt-EXE, mit dem nach dem Kopieren neu gestartet wird.</description></item>
/// <item><description>Versionsnummer der Zielversion, rein informativ fuer die Erfolgsanzeige.</description></item>
/// </list>
/// </summary>
/// <param name="ProcessId">Prozess-ID der zu erwartenden, sich beendenden Hauptanwendung.</param>
/// <param name="StagingDirectory">Quellverzeichnis der bereits entpackten neuen Dateien.</param>
/// <param name="InstallDirectory">Zielverzeichnis der Installation.</param>
/// <param name="ExecutableFileName">Dateiname der Haupt-EXE.</param>
/// <param name="TargetVersion">Versionsnummer der Zielversion, fuer die Anzeige "Version x.x.x
/// erfolgreich installiert".</param>
internal sealed record UpdaterArguments(
    int ProcessId,
    string StagingDirectory,
    string InstallDirectory,
    string ExecutableFileName,
    string TargetVersion)
{
    /// <summary>
    /// Versucht, <paramref name="args"/> (siehe <c>Main</c>/<c>App.OnStartup</c>) in eine
    /// <see cref="UpdaterArguments"/>-Instanz zu parsen. Liefert <c>false</c>, falls die Anzahl oder das
    /// Format der Argumente nicht den Erwartungen entspricht (z.B. beim versehentlichen manuellen Start
    /// dieser EXE ohne Argumente) - der Aufrufer zeigt in diesem Fall eine verstaendliche Fehlermeldung
    /// an, statt mit einer unbehandelten Ausnahme abzustuerzen.
    /// </summary>
    public static bool TryParse(string[] args, out UpdaterArguments? parsed)
    {
        parsed = null;

        if (args.Length != 5)
        {
            return false;
        }

        if (!int.TryParse(args[0], out int processId))
        {
            return false;
        }

        parsed = new UpdaterArguments(processId, args[1], args[2], args[3], args[4]);
        return true;
    }
}

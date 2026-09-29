namespace VirtualController.Core.Updates;

/// <summary>
/// Signalisiert, dass die Vorbereitung oder der Start der eigentlichen Update-Installation
/// fehlgeschlagen ist (Download fehlgeschlagen, Archiv beschaedigt/nicht extrahierbar, Updater-Prozess
/// konnte nicht gestartet werden). Wird geworfen, BEVOR irgendeine bereits installierte Datei angefasst
/// wurde (siehe <see cref="UpdateInstaller"/>-Klassendokumentation) - die aktuell installierte Version
/// bleibt in jedem hierdurch abgedeckten Fehlerfall unveraendert lauffaehig.
/// </summary>
public sealed class UpdateInstallException : Exception
{
    public UpdateInstallException(string message) : base(message)
    {
    }

    public UpdateInstallException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

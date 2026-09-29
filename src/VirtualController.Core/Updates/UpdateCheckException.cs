namespace VirtualController.Core.Updates;

/// <summary>
/// Signalisiert, dass eine Update-Pruefung fehlgeschlagen ist - entweder weil die Update-Quelle nicht
/// erreichbar war (z.B. keine Internetverbindung, Server down; siehe <see cref="Exception.InnerException"/>
/// fuer die genaue Ursache) oder weil sie eine ungueltige/unerwartete Antwort geliefert hat. Wird von
/// <see cref="UpdateChecker.CheckAsync"/> geworfen, damit Aufrufer beide Faelle einheitlich behandeln
/// koennen: bei der automatischen Pruefung beim Programmstart wird die Ausnahme verworfen (die
/// Anwendung darf dadurch nicht beeintraechtigt werden), bei einer manuellen Pruefung wird
/// <see cref="Exception.Message"/> dem Nutzer als verstaendliche Fehlermeldung angezeigt.
/// </summary>
public sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message) : base(message)
    {
    }

    public UpdateCheckException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

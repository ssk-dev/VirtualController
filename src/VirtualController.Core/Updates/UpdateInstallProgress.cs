namespace VirtualController.Core.Updates;

/// <summary>
/// Fortschrittsinformation eines einzelnen Installationsschritts (siehe <see cref="UpdateInstaller"/>),
/// gemeldet ueber <see cref="IProgress{T}"/> waehrend <see cref="UpdateInstaller.PrepareAsync"/> - fuer
/// die Anzeige eines Fortschrittsbalkens samt Schritt-Beschreibung im Update-Popup, z.B.
/// "Schritt 1 von 3: Dateien werden heruntergeladen".
/// </summary>
/// <param name="StepNumber">Nummer des aktuellen Schritts (1-basiert).</param>
/// <param name="TotalSteps">Gesamtzahl der Schritte (siehe <see cref="UpdateInstaller.TotalSteps"/>).</param>
/// <param name="StepDescription">Fuer die Anzeige geeignete Beschreibung des aktuellen Schritts, z.B.
/// "Dateien werden heruntergeladen".</param>
/// <param name="OverallPercent">Fortschritt des GESAMTEN Installationsvorgangs (0-100), nicht nur des
/// aktuellen Schritts - damit ein Fortschrittsbalken ueber alle Schritte hinweg gleichmaessig von 0 auf
/// 100 laeuft, statt bei jedem Schrittwechsel wieder bei 0 zu beginnen.</param>
public sealed record UpdateInstallProgress(int StepNumber, int TotalSteps, string StepDescription, double OverallPercent);

using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Gemeinsame Hilfslogik fuer alle "Erfassen"-Funktionen (Mapping-Zeile, Modus-Switch-Trigger,
/// controller-weiter Toggle-Trigger): wartet wie <see cref="InputCaptureService.WaitForNextInputAsync"/>
/// auf die naechste physische Eingabe, meldet dabei aber zusaetzlich per Sekunden-Countdown (heruntergezaehlt
/// bis 0) an die aufrufende View, wie lange noch auf eine Eingabe gewartet wird - damit der Nutzer sieht,
/// dass der Erfassen-Modus automatisch endet, falls innerhalb der Zeit keine neue Eingabe erkannt wird.
/// </summary>
public static class CaptureCountdownHelper
{
    /// <summary>Wartet auf die naechste physische Eingabe (siehe <see cref="InputCaptureService.WaitForNextInputAsync"/>)
    /// und ruft parallel dazu <paramref name="setSecondsRemaining"/> einmal pro Sekunde mit der verbleibenden
    /// Wartezeit auf (beginnend bei der vollen <paramref name="timeout"/>, heruntergezaehlt bis 0). Der Countdown
    /// wird sofort beendet, sobald eine Eingabe erkannt wird oder die Zeit abgelaufen ist - in beiden Faellen
    /// wird <paramref name="setSecondsRemaining"/> abschliessend mit 0 aufgerufen.</summary>
    public static async Task<PhysicalInputRef?> CaptureWithCountdownAsync(
        IReadOnlyList<PhysicalDeviceInfo> devices,
        TimeSpan timeout,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings,
        Action<int> setSecondsRemaining)
    {
        using var countdownCts = new CancellationTokenSource();
        var countdownTask = RunCountdownAsync(timeout, setSecondsRemaining, countdownCts.Token);

        try
        {
            return await InputCaptureService.WaitForNextInputAsync(devices, timeout, deviceSettings).ConfigureAwait(true);
        }
        finally
        {
            // Eingabe wurde bereits erkannt (oder Timeout ist ohnehin abgelaufen) -> Countdown-Schleife
            // nicht bis zum naechsten Sekundentick weiterlaufen lassen, sondern sofort beenden.
            countdownCts.Cancel();
            setSecondsRemaining(0);

            try
            {
                await countdownTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Erwartet, wenn die Eingabe vor Ablauf des Countdowns erkannt wurde.
            }
        }
    }

    private static async Task RunCountdownAsync(TimeSpan timeout, Action<int> setSecondsRemaining, CancellationToken cancellationToken)
    {
        int secondsRemaining = (int)Math.Ceiling(timeout.TotalSeconds);
        setSecondsRemaining(secondsRemaining);

        while (secondsRemaining > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
            secondsRemaining--;
            setSecondsRemaining(secondsRemaining);
        }
    }
}

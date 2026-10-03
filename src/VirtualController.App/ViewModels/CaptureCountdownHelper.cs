using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Shared helper for all Capture actions (mapping row, mode switch trigger, and controller-wide toggle trigger).
/// Like <see cref="InputCaptureService.WaitForNextInputAsync"/>, it waits for the next physical input and also
/// reports a per-second countdown to the calling view so users know Capture ends automatically if no input
/// is detected before the timeout.
/// </summary>
public static class CaptureCountdownHelper
{
    /// <summary>Waits for the next physical input (see <see cref="InputCaptureService.WaitForNextInputAsync"/>)
    /// and calls <paramref name="setSecondsRemaining"/> once per second with the remaining time, counting down
    /// from <paramref name="timeout"/> to zero. Stops as soon as an input is detected or the timeout expires;
    /// in either case, makes a final call to <paramref name="setSecondsRemaining"/> with zero.</summary>
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
            // An input was detected or the timeout expired; stop the countdown immediately instead of waiting
            // for the next one-second tick.
            countdownCts.Cancel();
            setSecondsRemaining(0);

            try
            {
                await countdownTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Expected when an input is detected before the countdown expires.
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

using VirtualController.Core.Virtual;

namespace VirtualController.Core.Devices;

/// <summary>
/// Captures the next physical input (button press, axis deflection, or D-pad movement) across all supplied
/// devices. Used by the UI's per-row Capture action: the user clicks Capture, then moves or presses the
/// desired physical input, similar to the feature in x360ce.
/// </summary>
public static class InputCaptureService
{
    private const float AxisThreshold = 0.6f;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);

    /// <summary>Number of warm-up polls discarded after opening/acquiring a device before accepting the baseline
    /// state for change detection. Prevents an already-active signal (e.g. a selector switch held before Capture
    /// starts) from being detected as a new change. The first poll after Acquire() may be stale or unsettled,
    /// especially for DirectInput; using it as the baseline can make the next correct poll appear to be a
    /// change even when the user did nothing.</summary>
    private const int BaselineWarmupPollCount = 5;

    public static async Task<PhysicalInputRef?> WaitForNextInputAsync(
        IReadOnlyList<PhysicalDeviceInfo> devices,
        TimeSpan timeout,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null,
        CancellationToken cancellationToken = default)
    {
        if (devices.Count == 0)
        {
            return null;
        }

        var readers = new List<IDeviceReader>();
        var baselines = new List<DeviceState>();
        var owners = new List<PhysicalDeviceInfo>();

        try
        {
            foreach (var device in devices)
            {
                try
                {
                    var reader = DeviceEnumerator.OpenReader(device);

                    // Discard several warm-up polls (see BaselineWarmupPollCount) and use the latest settled
                    // state as the baseline.
                    DeviceState? initial = null;
                    for (int warmup = 0; warmup < BaselineWarmupPollCount; warmup++)
                    {
                        if (reader.Poll(out var warmupState))
                        {
                            initial = warmupState;
                        }

                        await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    }

                    if (initial is null)
                    {
                        reader.Dispose();
                        continue;
                    }

                    readers.Add(reader);
                    baselines.Add(initial);
                    owners.Add(device);
                }
                catch
                {
                    // The device cannot currently be opened (e.g. it was just disconnected); skip it for capture.
                }
            }

            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i < readers.Count; i++)
                {
                    if (!readers[i].Poll(out var current))
                    {
                        continue;
                    }

                    var detected = DetectChange(owners[i], baselines[i], current, deviceSettings);
                    if (detected is not null)
                    {
                        return detected;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private static PhysicalInputRef? DetectChange(
        PhysicalDeviceInfo device,
        DeviceState baseline,
        DeviceState current,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings)
    {
        for (int i = 0; i < current.Buttons.Length && i < baseline.Buttons.Length; i++)
        {
            if (current.Buttons[i] && !baseline.Buttons[i])
            {
                if (!deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.Button, i))
                {
                    continue; // Disabled in device configuration; skip it as a capture target.
                }

                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.Button, i, $"Button {i + 1}");
            }
        }

        var axisSlots = device.Api == InputApi.XInput
            ? Enumerable.Range(0, 6)
            : device.AvailableAxes.Select(a => (int)a);

        foreach (int slot in axisSlots)
        {
            float currentValue = current.GetAxisRaw(slot);
            float baselineValue = baseline.GetAxisRaw(slot);
            float delta = currentValue - baselineValue;
            if (MathF.Abs(delta) >= AxisThreshold)
            {
                var kind = delta > 0 ? PhysicalInputKind.AxisPositive : PhysicalInputKind.AxisNegative;
                if (!deviceSettings.IsInputEnabled(device.DeviceId, kind, slot))
                {
                    continue; // Disabled in device configuration; skip it as a capture target.
                }

                return new PhysicalInputRef(device.DeviceId, kind, slot, $"Axis {slot} {(delta > 0 ? "+" : "-")}");
            }
        }

        if (device.HasPov && current.PovDirectionDegrees >= 0)
        {
            var baselineDirection = DPadDirectionExtensions.FromPovDegrees(baseline.PovDirectionDegrees);
            var currentDirection = DPadDirectionExtensions.FromPovDegrees(current.PovDirectionDegrees);

            if (currentDirection.HasUp() && !baselineDirection.HasUp()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadUp, 0))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadUp, 0, "D-pad Up");
            }
            if (currentDirection.HasDown() && !baselineDirection.HasDown()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadDown, 1))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadDown, 1, "D-pad Down");
            }
            if (currentDirection.HasLeft() && !baselineDirection.HasLeft()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadLeft, 2))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadLeft, 2, "D-pad Left");
            }
            if (currentDirection.HasRight() && !baselineDirection.HasRight()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadRight, 3))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadRight, 3, "D-pad Right");
            }
        }

        return null;
    }
}

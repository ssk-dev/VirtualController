using VirtualController.Core.Virtual;

namespace VirtualController.Core.Devices;

/// <summary>
/// Erfasst die naechste physische Eingabe (Knopfdruck, Achsenausschlag oder D-Pad-Bewegung)
/// ueber alle uebergebenen Geraete hinweg. Wird von der UI fuer die "Erfassen"-Funktion pro
/// Mapping-Zeile genutzt (Nutzer klickt "Erfassen" und bewegt/drueckt dann die gewuenschte
/// physische Eingabe - analog zur bekannten Funktion in x360ce).
/// </summary>
public static class InputCaptureService
{
    private const float AxisThreshold = 0.6f;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);

    /// <summary>Anzahl verworfener "Aufwaerm"-Polls direkt nach dem Oeffnen/Acquire eines Geraets, bevor
    /// der tatsaechliche Baseline-Zustand fuer die Aenderungserkennung uebernommen wird. Behebt den Bug,
    /// dass ein bereits dauerhaft aktives Signal (z.B. ein Wahlschalter, der schon vor dem Klick auf
    /// "Erfassen" eine Position haelt) sofort als "Aenderung" erkannt wurde: der allererste Poll-Aufruf
    /// nach Acquire() kann bei manchen Geraeten (insbesondere DirectInput) noch einen veralteten/nicht
    /// eingeschwungenen Zwischenzustand liefern, bevor sich der echte Live-Zustand stabilisiert hat. Wird
    /// ausgerechnet dieser fehlerhafte erste Wert als Baseline verwendet, erkennt der naechste (korrekte)
    /// Poll wenige Millisekunden spaeter faelschlich eine Aenderung, obwohl der Nutzer nichts angefasst hat.</summary>
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

                    // Mehrere Aufwaerm-Polls verwerfen (siehe BaselineWarmupPollCount) und erst den
                    // zuletzt gelesenen, eingeschwungenen Zustand als tatsaechliche Baseline uebernehmen.
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
                    // Geraet aktuell nicht oeffenbar (z.B. gerade getrennt) -> fuer die Erfassung ignorieren.
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
                    continue; // Im Konfigurationsdialog deaktiviert -> als Erfassungsziel ignorieren.
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
                    continue; // Im Konfigurationsdialog deaktiviert -> als Erfassungsziel ignorieren.
                }

                return new PhysicalInputRef(device.DeviceId, kind, slot, $"Achse {slot} {(delta > 0 ? "+" : "-")}");
            }
        }

        if (device.HasPov && current.PovDirectionDegrees >= 0)
        {
            var baselineDirection = DPadDirectionExtensions.FromPovDegrees(baseline.PovDirectionDegrees);
            var currentDirection = DPadDirectionExtensions.FromPovDegrees(current.PovDirectionDegrees);

            if (currentDirection.HasUp() && !baselineDirection.HasUp()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadUp, 0))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadUp, 0, "D-Pad Hoch");
            }
            if (currentDirection.HasDown() && !baselineDirection.HasDown()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadDown, 1))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadDown, 1, "D-Pad Runter");
            }
            if (currentDirection.HasLeft() && !baselineDirection.HasLeft()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadLeft, 2))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadLeft, 2, "D-Pad Links");
            }
            if (currentDirection.HasRight() && !baselineDirection.HasRight()
                && deviceSettings.IsInputEnabled(device.DeviceId, PhysicalInputKind.DPadRight, 3))
            {
                return new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadRight, 3, "D-Pad Rechts");
            }
        }

        return null;
    }
}

namespace VirtualController.Core.Devices;

/// <summary>Ergebnis einer Bereichskalibrierung (kleinster/groesster beobachteter Rohwert) einer einzelnen physischen Achse.</summary>
public readonly record struct AxisRangeSample(float Min, float Max);

/// <summary>
/// Fuehrt zeitlich begrenzte Messungen an einer einzelnen physischen Achse durch, um daraus
/// Kalibrierungswerte (Min/Max/Mitte) sowie eine Deadzone abzuleiten. Wird vom Konfigurationsdialog
/// genutzt, wenn der Nutzer auf "Bereich kalibrieren", "Zentrum setzen" oder "Deadzone kalibrieren"
/// klickt. Arbeitet direkt auf einem bereits geoeffneten <see cref="IDeviceReader"/> (unabhaengig
/// davon, ob das Geraet gerade auch von einer laufenden <see cref="Mapping.MappingEngine"/>-Session
/// gelesen wird - DirectInput-Geraete werden non-exklusiv geoeffnet, siehe <see cref="DirectInputDeviceReader"/>).
/// </summary>
public static class AxisCalibrationService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Misst ueber <paramref name="duration"/> hinweg den kleinsten und groessten Rohwert einer Achse.
    /// Der Nutzer soll die Achse waehrend dieser Zeit mehrmals bis zu allen physischen Anschlaegen bewegen.
    /// </summary>
    public static async Task<AxisRangeSample> SampleRangeAsync(
        IDeviceReader reader,
        int axisSlot,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var samples = await SampleRangeAsync(reader, new[] { axisSlot }, duration, cancellationToken).ConfigureAwait(false);
        return samples[0];
    }

    /// <summary>
    /// Wie <see cref="SampleRangeAsync(IDeviceReader,int,TimeSpan,CancellationToken)"/>, misst aber mehrere
    /// Achsen gleichzeitig innerhalb desselben Poll-Durchlaufs (z.B. X/Y eines kombinierten Sticks), damit
    /// der Nutzer den Stick nur einmal in alle Richtungen bewegen muss statt die Kalibrierung pro Achse
    /// getrennt zu wiederholen.
    /// </summary>
    public static async Task<AxisRangeSample[]> SampleRangeAsync(
        IDeviceReader reader,
        int[] axisSlots,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var min = new float[axisSlots.Length];
        var max = new float[axisSlots.Length];
        Array.Fill(min, float.PositiveInfinity);
        Array.Fill(max, float.NegativeInfinity);
        var deadline = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Poll(out var state))
            {
                for (int i = 0; i < axisSlots.Length; i++)
                {
                    float value = state.GetAxisRaw(axisSlots[i]);
                    if (value < min[i]) min[i] = value;
                    if (value > max[i]) max[i] = value;
                }
            }

            await Task.Delay(SamplingInterval, cancellationToken).ConfigureAwait(false);
        }

        var results = new AxisRangeSample[axisSlots.Length];
        for (int i = 0; i < axisSlots.Length; i++)
        {
            results[i] = float.IsInfinity(min[i]) || float.IsInfinity(max[i])
                // Reader lieferte waehrend der gesamten Messung keinen einzigen gueltigen Wert
                // (z.B. Geraet zwischenzeitlich getrennt) -> unveraendert lassen statt Unsinn zu speichern.
                ? new AxisRangeSample(-1f, 1f)
                : new AxisRangeSample(min[i], max[i]);
        }

        return results;
    }

    /// <summary>Misst den Mittelwert einer Achse ueber eine kurze Dauer (Ruheposition). Wird sowohl
    /// direkt fuer "Zentrum setzen" als auch intern fuer die Deadzone-Kalibrierung verwendet.</summary>
    public static async Task<float> SampleCenterAsync(
        IDeviceReader reader,
        int axisSlot,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var samples = await SampleCenterAsync(reader, new[] { axisSlot }, duration, cancellationToken).ConfigureAwait(false);
        return samples[0];
    }

    /// <summary>Wie <see cref="SampleCenterAsync(IDeviceReader,int,TimeSpan,CancellationToken)"/>, misst aber
    /// mehrere Achsen gleichzeitig innerhalb desselben Poll-Durchlaufs (z.B. X/Y eines kombinierten Sticks).</summary>
    public static async Task<float[]> SampleCenterAsync(
        IDeviceReader reader,
        int[] axisSlots,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var sums = new float[axisSlots.Length];
        int count = 0;
        var deadline = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Poll(out var state))
            {
                for (int i = 0; i < axisSlots.Length; i++)
                {
                    sums[i] += state.GetAxisRaw(axisSlots[i]);
                }
                count++;
            }

            await Task.Delay(SamplingInterval, cancellationToken).ConfigureAwait(false);
        }

        var results = new float[axisSlots.Length];
        for (int i = 0; i < axisSlots.Length; i++)
        {
            results[i] = count > 0 ? sums[i] / count : 0f;
        }

        return results;
    }

    /// <summary>
    /// Ermittelt automatisch eine sinnvolle Deadzone anhand des tatsaechlichen Stickdrifts/Rauschens:
    /// wartet zunaechst <paramref name="graceDuration"/> (damit der Nutzer nach dem Klick noch Zeit hat,
    /// die Achse loszulassen), misst dann die Ruheposition und beobachtet anschliessend ueber
    /// <paramref name="sampleDuration"/> die maximale Abweichung von dieser Ruheposition. Die Deadzone
    /// wird als diese maximale Abweichung zuzueglich Sicherheitsaufschlag (<paramref name="safetyFactor"/>)
    /// festgelegt, damit normales Rauschen sicher innerhalb der Deadzone bleibt.
    /// </summary>
    public static async Task<float> SampleDeadzoneAsync(
        IDeviceReader reader,
        int axisSlot,
        TimeSpan graceDuration,
        TimeSpan sampleDuration,
        float safetyFactor = 1.15f,
        CancellationToken cancellationToken = default)
    {
        var samples = await SampleDeadzoneAsync(
            reader, new[] { axisSlot }, graceDuration, sampleDuration, safetyFactor, cancellationToken).ConfigureAwait(false);
        return samples[0];
    }

    /// <summary>Wie <see cref="SampleDeadzoneAsync(IDeviceReader,int,TimeSpan,TimeSpan,float,CancellationToken)"/>,
    /// misst aber mehrere Achsen gleichzeitig innerhalb desselben Poll-Durchlaufs (z.B. X/Y eines kombinierten
    /// Sticks), damit der Nutzer den Stick nur einmal loslassen muss statt die Messung pro Achse zu wiederholen.</summary>
    public static async Task<float[]> SampleDeadzoneAsync(
        IDeviceReader reader,
        int[] axisSlots,
        TimeSpan graceDuration,
        TimeSpan sampleDuration,
        float safetyFactor = 1.15f,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(graceDuration, cancellationToken).ConfigureAwait(false);

        var centers = await SampleCenterAsync(reader, axisSlots, TimeSpan.FromMilliseconds(200), cancellationToken)
            .ConfigureAwait(false);

        var maxDeviation = new float[axisSlots.Length];
        var deadline = DateTime.UtcNow + sampleDuration;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Poll(out var state))
            {
                for (int i = 0; i < axisSlots.Length; i++)
                {
                    float deviation = MathF.Abs(state.GetAxisRaw(axisSlots[i]) - centers[i]);
                    if (deviation > maxDeviation[i]) maxDeviation[i] = deviation;
                }
            }

            await Task.Delay(SamplingInterval, cancellationToken).ConfigureAwait(false);
        }

        var results = new float[axisSlots.Length];
        for (int i = 0; i < axisSlots.Length; i++)
        {
            // Obergrenze 0.9 verhindert, dass eine waehrend der Messung versehentlich bewegte Achse
            // die Deadzone auf einen praktisch nutzlosen Wert nahe 1.0 hochtreibt.
            results[i] = Math.Clamp(maxDeviation[i] * safetyFactor, 0f, 0.9f);
        }

        return results;
    }
}

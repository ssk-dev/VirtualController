namespace VirtualController.Core.Devices;

/// <summary>Result of range calibration (smallest/largest observed raw value) for one physical axis.</summary>
public readonly record struct AxisRangeSample(float Min, float Max);

/// <summary>
/// Performs timed measurements on one physical axis to derive calibration values (min/max/center) and a
/// deadzone. Used by the configuration dialog for range calibration, setting the center, or deadzone
/// calibration. Operates directly on an already-open <see cref="IDeviceReader"/>, independently of whether
/// a running <see cref="Mapping.MappingEngine"/> session also reads the device; DirectInput devices are
/// opened non-exclusively (see <see cref="DirectInputDeviceReader"/>).
/// </summary>
public static class AxisCalibrationService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Measures the minimum and maximum raw values of an axis over <paramref name="duration"/>. The user should
    /// move the axis to each physical limit several times during this period.
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
    /// Like <see cref="SampleRangeAsync(IDeviceReader,int,TimeSpan,CancellationToken)"/>, but measures several
    /// axes in the same polling pass (e.g. X/Y of a combined stick) so the user moves the stick in all
    /// directions once instead of calibrating each axis separately.
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
                // The reader returned no valid values during the measurement (e.g. the device disconnected);
                // use the default range rather than storing invalid values.
                ? new AxisRangeSample(-1f, 1f)
                : new AxisRangeSample(min[i], max[i]);
        }

        return results;
    }

    /// <summary>Measures an axis's average over a short period (resting position). Used by Set center and
    /// internally by deadzone calibration.</summary>
    public static async Task<float> SampleCenterAsync(
        IDeviceReader reader,
        int axisSlot,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var samples = await SampleCenterAsync(reader, new[] { axisSlot }, duration, cancellationToken).ConfigureAwait(false);
        return samples[0];
    }

    /// <summary>Like <see cref="SampleCenterAsync(IDeviceReader,int,TimeSpan,CancellationToken)"/>, but measures
    /// several axes in the same polling pass (e.g. X/Y of a combined stick).</summary>
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
    /// Automatically estimates a suitable deadzone from actual stick drift/noise. Waits for
    /// <paramref name="graceDuration"/> so the user can release the axis, measures its resting position, then
    /// observes the maximum deviation over <paramref name="sampleDuration"/>. Sets the deadzone to that
    /// maximum deviation plus a safety margin (<paramref name="safetyFactor"/>) so normal noise remains inside it.
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

    /// <summary>Like <see cref="SampleDeadzoneAsync(IDeviceReader,int,TimeSpan,TimeSpan,float,CancellationToken)"/>,
    /// but measures several axes in the same polling pass (e.g. X/Y of a combined stick) so the user only
    /// releases the stick once.</summary>
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
            // Cap at 0.9 so accidental axis movement during measurement cannot increase the deadzone to a
            // practically unusable value near 1.0.
            results[i] = Math.Clamp(maxDeviation[i] * safetyFactor, 0f, 0.9f);
        }

        return results;
    }
}

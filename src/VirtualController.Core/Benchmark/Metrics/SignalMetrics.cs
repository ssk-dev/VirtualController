using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="Axis">Axis being measured (see <see cref="HidAxisUsage"/>).</param>
/// <param name="SampleCount">Number of raw values collected for this axis.</param>
/// <param name="DeclaredElementBits">Bit depth declared for the field in the device report descriptor
/// (<see cref="HidAxisFieldInfo.ElementBits"/>), representing theoretical resolution.</param>
/// <param name="DeclaredLogicalRange">Raw value range declared by the device (logical maximum - logical minimum + 1).</param>
/// <param name="ObservedDistinctValues">Number of distinct raw values actually observed during measurement;
/// see <see cref="SignalMetrics"/> docs for how this differs from declared resolution.</param>
/// <param name="EffectiveBits">log2(<see cref="ObservedDistinctValues"/>), the resolution in bits actually
/// observed, usually lower than <see cref="DeclaredElementBits"/> (see class documentation).</param>
/// <param name="ResolutionSupported">True once at least one raw value has been observed.</param>
/// <param name="NoiseStdDevRaw">Standard deviation of raw values during detected idle periods (see the Noise
/// section in the class documentation); meaningful only when <see cref="NoiseSupported"/> is true.</param>
/// <param name="NoiseSupported">False when no idle period was detected during the measurement because the axis
/// was continuously moving. In that case, <see cref="NoiseStdDevRaw"/> is unavailable, not "no noise".</param>
/// <param name="DeadzoneRawWidth">Estimated width, in raw units, of a contiguous deadzone around the most
/// frequently observed raw value (see class documentation); valid only when <see cref="DeadzoneSupported"/> is true.</param>
/// <param name="DeadzonePercentOfRange"><see cref="DeadzoneRawWidth"/> relative to <see cref="DeclaredLogicalRange"/>.</param>
/// <param name="DeadzoneSupported">False when there are too few samples for a reliable estimate (see the
/// threshold constant in <see cref="SignalMetrics"/>).</param>
/// <param name="LinearitySupported">Always false. A true linearity test requires calibration at known reference
/// positions, which a passive benchmark cannot perform.</param>
/// <param name="HysteresisSupported">Always false; see the <see cref="LinearitySupported"/> limitation.</param>
public sealed record SignalAxisResult(
    HidAxisUsage Axis,
    long SampleCount,
    int DeclaredElementBits,
    int DeclaredLogicalRange,
    int ObservedDistinctValues,
    double EffectiveBits,
    bool ResolutionSupported,
    double NoiseStdDevRaw,
    bool NoiseSupported,
    int DeadzoneRawWidth,
    double DeadzonePercentOfRange,
    bool DeadzoneSupported,
    bool LinearitySupported,
    bool HysteresisSupported);

/// <summary>
/// Computes per-axis signal metrics (resolution, noise, deadzone, linearity, hysteresis) directly from raw
/// HID logical values (see <see cref="HidAxisReportParser"/>), not values normalized by DirectInput to [-1,1]
/// (<see cref="Devices.DeviceState.Axes"/>), because normalization would hide the device's actual bit resolution.
///
/// Limitations are reported transparently rather than presenting unsupported values as measurements:
///
/// - <b>Resolution</b>: the bit depth declared by the device (<see cref="HidAxisFieldInfo.ElementBits"/>) does
///   not show whether the device actually uses that resolution. A device may declare 16 bits but provide only
///   10 bits of real resolution, with remaining bits constant or noisy. This class counts distinct raw values
///   actually observed (<see cref="SignalAxisResult.ObservedDistinctValues"/>) as a more honest estimate of
///   usable resolution. A short measurement or limited axis movement can make the observed resolution appear
///   artificially low because not all possible values were visited.
///
/// - <b>Noise</b>: ideally requires a controlled, motionless reference position, which a passive benchmark
///   cannot enforce. This class detects idle periods heuristically using a sliding window of
///   <see cref="QuasiStaticWindowSize"/> consecutive raw values whose range stays within a device-proportional
///   tolerance, then calculates noise only from those periods. If an axis moves continuously, no such period
///   exists; see <see cref="SignalAxisResult.NoiseSupported"/>.
///
/// - <b>Deadzone</b>: accurately measuring a deadzone requires a known, calibrated reference position (center).
///   This class instead estimates the resting position as the most frequently observed raw value, assuming an
///   analog stick/pedal spends most of its time at rest, then measures the contiguous region around that mode
///   where values occur disproportionately often (see <see cref="MinDeadzoneIncrementFraction"/>). Results
///   are plausible but not guaranteed when the resting position differs from the mode, such as a trigger that
///   usually rests at zero rather than center; in that case, "no deadzone around the trigger's resting point"
///   is correct but may be less informative for the intended use.
///
/// - <b>Linearity</b>/<b>Hysteresis</b>: not implemented (<c>Supported</c> = false). Both require a defined
///   calibration run with known reference positions (e.g. hold the stick at exactly 0%/50%/100%) or a
///   controlled forward/backward movement with position references to compare physical position with the
///   reported raw value. A passive benchmark has no independent position reference and cannot calculate these
///   values reliably.
/// </summary>
public sealed class SignalMetrics
{
    /// <summary>Window size (number of consecutive raw values) for idle-period detection; see the Noise section
    /// above. Kept small so brief idle periods during an otherwise active session can be detected.</summary>
    private const int QuasiStaticWindowSize = 30;

    /// <summary>A raw value adjacent to the mode is included in the deadzone estimate only if it accounts for
    /// at least this fraction of all axis samples (see the Deadzone section above). 0.5% is deliberately small
    /// but clearly above the expected fraction for a uniformly traversed axis.</summary>
    private const double MinDeadzoneIncrementFraction = 0.005;

    /// <summary>Minimum number of samples required for the deadzone heuristic to be considered reliable
    /// (see <see cref="SignalAxisResult.DeadzoneSupported"/>).</summary>
    private const int MinSamplesForDeadzoneHeuristic = 50;

    private readonly Dictionary<HidAxisUsage, AxisAccumulator> _axes;

    /// <param name="availableAxes">Axis fields found in the report descriptor (see
    /// <see cref="HidAxisReportParser.AvailableAxes"/>), defining which axes this instance accepts. If multiple
    /// fields describe the same axis (not expected in practice), the first is used.</param>
    public SignalMetrics(IReadOnlyList<HidAxisFieldInfo> availableAxes)
    {
        _axes = availableAxes
            .GroupBy(field => field.Axis)
            .ToDictionary(group => group.Key, group => new AxisAccumulator(group.First()));
    }

    /// <summary>Accepts raw values parsed from one report (see <see cref="HidAxisReportParser.TryParse"/>);
    /// unknown or unmonitored axes are ignored.</summary>
    public void Add(IReadOnlyDictionary<HidAxisUsage, int> rawAxisValues)
    {
        foreach (var (axis, rawValue) in rawAxisValues)
        {
            if (_axes.TryGetValue(axis, out var accumulator))
            {
                accumulator.Add(rawValue);
            }
        }
    }

    public IReadOnlyDictionary<HidAxisUsage, SignalAxisResult> ComputeResult()
        => _axes.ToDictionary(entry => entry.Key, entry => entry.Value.ComputeResult());

    private sealed class AxisAccumulator
    {
        private readonly HidAxisFieldInfo _fieldInfo;
        private readonly Dictionary<int, long> _histogram = new();
        private readonly Queue<int> _quasiStaticWindow = new(QuasiStaticWindowSize);
        private readonly StreamingStatisticsAccumulator _noiseAccumulator = new();
        private readonly double _quasiStaticToleranceRawUnits;
        private long _sampleCount;

        public AxisAccumulator(HidAxisFieldInfo fieldInfo)
        {
            _fieldInfo = fieldInfo;
            int declaredRange = Math.Max(1, fieldInfo.LogicalMaximum - fieldInfo.LogicalMinimum + 1);

            // Scale tolerance to the declared range (0.5%) rather than using a fixed raw value; otherwise
            // idle detection would be miscalibrated for both low- and high-resolution axes.
            _quasiStaticToleranceRawUnits = Math.Max(1, declaredRange * 0.005);
        }

        public void Add(int rawValue)
        {
            _sampleCount++;

            // Histogram for resolution and deadzone estimates (see SignalMetrics docs). Cap its size to avoid
            // unbounded growth for a device with an extremely large declared range. Existing values continue
            // to be counted after the cap; new keys are ignored.
            if (_histogram.Count < 200_000 || _histogram.ContainsKey(rawValue))
            {
                _histogram[rawValue] = _histogram.GetValueOrDefault(rawValue) + 1;
            }

            UpdateQuasiStaticNoiseWindow(rawValue);
        }

        private void UpdateQuasiStaticNoiseWindow(int rawValue)
        {
            _quasiStaticWindow.Enqueue(rawValue);
            if (_quasiStaticWindow.Count > QuasiStaticWindowSize)
            {
                _quasiStaticWindow.Dequeue();
            }

            if (_quasiStaticWindow.Count < QuasiStaticWindowSize)
            {
                return;
            }

            int windowMin = int.MaxValue;
            int windowMax = int.MinValue;
            foreach (var value in _quasiStaticWindow)
            {
                if (value < windowMin) windowMin = value;
                if (value > windowMax) windowMax = value;
            }

            // A window with negligible movement is considered an idle period; add its newest raw value as a
            // noise sample (see the Noise section above).
            if (windowMax - windowMin <= _quasiStaticToleranceRawUnits)
            {
                _noiseAccumulator.Add(rawValue);
            }
        }

        public SignalAxisResult ComputeResult()
        {
            int declaredRange = Math.Max(1, _fieldInfo.LogicalMaximum - _fieldInfo.LogicalMinimum + 1);
            int distinctValues = _histogram.Count;
            double effectiveBits = distinctValues > 0 ? Math.Log2(distinctValues) : 0;
            var (deadzoneWidth, deadzonePercent, deadzoneSupported) = ComputeDeadzone(declaredRange);

            return new SignalAxisResult(
                Axis: _fieldInfo.Axis,
                SampleCount: _sampleCount,
                DeclaredElementBits: _fieldInfo.ElementBits,
                DeclaredLogicalRange: declaredRange,
                ObservedDistinctValues: distinctValues,
                EffectiveBits: effectiveBits,
                ResolutionSupported: _sampleCount > 0,
                NoiseStdDevRaw: _noiseAccumulator.ComputeResult().StdDev,
                NoiseSupported: _noiseAccumulator.Count > 0,
                DeadzoneRawWidth: deadzoneWidth,
                DeadzonePercentOfRange: deadzonePercent,
                DeadzoneSupported: deadzoneSupported,
                LinearitySupported: false,
                HysteresisSupported: false);
        }

        /// <summary>See the Deadzone section in <see cref="SignalMetrics"/> documentation for the method and
        /// its limitations.</summary>
        private (int Width, double PercentOfRange, bool Supported) ComputeDeadzone(int declaredRange)
        {
            if (_sampleCount < MinSamplesForDeadzoneHeuristic || _histogram.Count == 0)
            {
                return (0, 0, false);
            }

            int modeValue = _histogram.OrderByDescending(entry => entry.Value).First().Key;
            double minCountToExpand = _sampleCount * MinDeadzoneIncrementFraction;

            int lower = modeValue;
            int upper = modeValue;
            while (true)
            {
                bool expandLower = _histogram.TryGetValue(lower - 1, out long lowerCount) && lowerCount >= minCountToExpand;
                bool expandUpper = _histogram.TryGetValue(upper + 1, out long upperCount) && upperCount >= minCountToExpand;

                if (!expandLower && !expandUpper)
                {
                    break;
                }

                if (expandLower)
                {
                    lower--;
                }

                if (expandUpper)
                {
                    upper++;
                }
            }

            int width = upper - lower + 1;
            return (width, (double)width / declaredRange, true);
        }
    }
}

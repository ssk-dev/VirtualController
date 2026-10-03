namespace VirtualController.Core.Devices;

/// <summary>
/// Applies calibration (min/max/center) and a response curve (<see cref="AxisCurveType"/>) from
/// <see cref="InputSettings"/> to an axis raw value already normalized by its <see cref="IDeviceReader"/>,
/// before passing it to mapping evaluation. This is a stateless conversion for both bidirectional axes
/// (sticks, -1.0 .. 1.0) and unidirectional axes (triggers/sliders, 0.0 .. 1.0).
/// </summary>
public static class AxisSignalProcessor
{
    /// <summary>Minimum curve exponent/strength to avoid division by zero or undefined powers in
    /// <see cref="AxisCurveType.Exponential"/>/<see cref="AxisCurveType.SCurve"/>.</summary>
    private const float MinCurveStrength = 0.01f;

    /// <summary>
    /// Applies calibration, device-wide deadzone, and response curve, in that order, to an axis raw value.
    /// If <paramref name="settings"/> is null, passes the raw value through unchanged (the default for
    /// unconfigured axes).
    /// </summary>
    /// <param name="raw">Raw value already normalized by the reader (-1.0 .. 1.0 or 0.0 .. 1.0).</param>
    /// <param name="settings">Calibration/curve settings for this physical axis, or null.</param>
    /// <param name="bidirectional">True for stick-like axes (-1.0 .. 1.0); false for triggers/sliders (0.0 .. 1.0).</param>
    public static float Process(float raw, InputSettings? settings, bool bidirectional)
    {
        if (settings is null)
        {
            return raw;
        }

        float value = ApplyCalibration(raw, settings, bidirectional);
        value = ApplyDeadzone(value, settings.Deadzone);
        value = ApplyCurve(value, settings.CurveType, settings.CurveStrength);
        return value;
    }

    /// <summary>
    /// Applies only min/max/center calibration to an axis raw value, without deadzone or response curve. Used
    /// by the live axis visualization so the marker moves smoothly through the deadzone, which is shown as a
    /// separate highlighted region instead of clamping the value to zero as mapping evaluation does.
    /// </summary>
    public static float Calibrate(float raw, InputSettings? settings, bool bidirectional)
        => settings is null ? raw : ApplyCalibration(raw, settings, bidirectional);

    /// <summary>
    /// Linearly scales the observed calibrated range (<see cref="InputSettings.CalibratedMin"/>/
    /// <see cref="InputSettings.CalibratedMax"/>, optionally centered around
    /// <see cref="InputSettings.CalibratedCenter"/>) to the full target range (-1.0 .. 1.0 or 0.0 .. 1.0). This
    /// lets a stick reach full virtual deflection even if component tolerances or wear prevent it from reaching
    /// physical limits. Without min/max calibration, leaves the value unchanged.
    /// </summary>
    private static float ApplyCalibration(float raw, InputSettings settings, bool bidirectional)
    {
        if (settings.CalibratedMin is not { } min || settings.CalibratedMax is not { } max
            || MathF.Abs(max - min) < 1e-6f)
        {
            return raw;
        }

        if (!bidirectional)
        {
            return Math.Clamp((raw - min) / (max - min), 0f, 1f);
        }

        float center = settings.CalibratedCenter ?? 0f;
        float shifted = raw - center;
        float scale = shifted >= 0f
            ? MathF.Max(max - center, 1e-6f)
            : MathF.Max(center - min, 1e-6f);
        return Math.Clamp(shifted / scale, -1f, 1f);
    }

    /// <summary>Values inside the deadzone radius around zero become zero. Values outside are linearly rescaled
    /// from the deadzone boundary to the respective extreme to avoid a jump at the edge. Works for both
    /// bidirectional (-1..1) and unidirectional (0..1) values because the latter also rest at zero.</summary>
    private static float ApplyDeadzone(float value, float deadzone)
    {
        if (deadzone <= 0f)
        {
            return value;
        }

        float abs = MathF.Abs(value);
        if (abs <= deadzone)
        {
            return 0f;
        }

        float sign = MathF.Sign(value);
        float scaled = (abs - deadzone) / (1f - deadzone);
        return sign * Math.Clamp(scaled, 0f, 1f);
    }

    /// <summary>Applies the selected response curve to the calibrated, deadzone-adjusted value.</summary>
    private static float ApplyCurve(float value, AxisCurveType curveType, float curveStrength)
    {
        if (curveType == AxisCurveType.Linear || value == 0f)
        {
            return value;
        }

        float strength = MathF.Max(curveStrength, MinCurveStrength);
        float sign = MathF.Sign(value);
        float magnitude = MathF.Abs(value);

        return curveType switch
        {
            // Pure power function: strength > 1 makes values near zero less sensitive and the curve steeper
            // near the extremes (the derivative of t^n at t=1 is n).
            AxisCurveType.Exponential => sign * MathF.Pow(magnitude, strength),

            // Generalized logistic S-curve: exactly linear at strength=1 (t/(t+(1-t)) = t); strength > 1
            // reduces sensitivity near zero and steepens near the extreme (t=1), while remaining strictly
            // monotonic and bounded to [0,1].
            AxisCurveType.SCurve => sign * SCurve(magnitude, strength),

            _ => value
        };
    }

    private static float SCurve(float t, float strength)
    {
        float tp = MathF.Pow(t, strength);
        float otherP = MathF.Pow(1f - t, strength);
        return tp / (tp + otherP);
    }
}

namespace VirtualController.Core.Devices;

/// <summary>
/// Wendet die in <see cref="InputSettings"/> hinterlegte Kalibrierung (Min/Max/Center) und
/// Antwortkurve (<see cref="AxisCurveType"/>) auf einen bereits vom jeweiligen <see cref="IDeviceReader"/>
/// grundnormalisierten Achsen-Rohwert an, bevor dieser an die Mapping-Auswertung weitergegeben wird.
/// Reine, zustandslose Umrechnung - unabhaengig davon, ob die Achse bidirektional (Stick, -1.0 .. 1.0)
/// oder unidirektional (Trigger/Schieberegler, 0.0 .. 1.0) ist.
/// </summary>
public static class AxisSignalProcessor
{
    /// <summary>Mindest-Exponent/-Staerke, um Division-durch-0 bzw. undefinierte Potenzen bei
    /// <see cref="AxisCurveType.Exponential"/>/<see cref="AxisCurveType.SCurve"/> zu vermeiden.</summary>
    private const float MinCurveStrength = 0.01f;

    /// <summary>
    /// Wendet Kalibrierung, geraeteweite Deadzone und Antwortkurve (in dieser Reihenfolge) auf einen
    /// Achsen-Rohwert an. <paramref name="settings"/> == null bedeutet "keine Einstellungen vorhanden"
    /// -> der Rohwert wird unveraendert durchgereicht (Standardverhalten fuer nicht konfigurierte Achsen).
    /// </summary>
    /// <param name="raw">Bereits vom Reader normalisierter Rohwert (-1.0 .. 1.0 bzw. 0.0 .. 1.0).</param>
    /// <param name="settings">Kalibrierungs-/Kurveneinstellungen dieser physischen Achse, oder null.</param>
    /// <param name="bidirectional">true fuer Stick-artige Achsen (-1.0 .. 1.0), false fuer Trigger/Slider (0.0 .. 1.0).</param>
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
    /// Wendet ausschliesslich die Kalibrierung (Min/Max/Center) auf einen Achsen-Rohwert an, ohne
    /// Deadzone oder Antwortkurve. Wird von der Live-Achsenvisualisierung im Konfigurationsdialog
    /// genutzt, damit sich der angezeigte Zeiger/Marker fluessig durch die Deadzone hindurch bewegt
    /// (die Deadzone wird dort separat als eigener, hervorgehobener Bereich dargestellt, statt den
    /// Wert wie bei der eigentlichen Mapping-Auswertung auf 0 zu klemmen).
    /// </summary>
    public static float Calibrate(float raw, InputSettings? settings, bool bidirectional)
        => settings is null ? raw : ApplyCalibration(raw, settings, bidirectional);

    /// <summary>
    /// Skaliert den beobachteten, kalibrierten Wertebereich (<see cref="InputSettings.CalibratedMin"/>/
    /// <see cref="InputSettings.CalibratedMax"/>, optional um <see cref="InputSettings.CalibratedCenter"/>
    /// verschoben) linear auf den vollen Zielbereich (-1.0 .. 1.0 bzw. 0.0 .. 1.0), damit ein Stick, der
    /// wegen Bauteiltoleranz/Verschleiss seine physischen Enden nicht exakt erreicht, trotzdem den vollen
    /// virtuellen Ausschlag liefert. Ohne Kalibrierung (Min/Max nicht gesetzt) bleibt der Wert unveraendert.
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

    /// <summary>Werte innerhalb des Deadzone-Radius um 0 werden zu 0; ausserhalb wird linear von der
    /// Deadzone-Grenze bis zum jeweiligen Extremwert neu skaliert, damit kein Sprung am Deadzone-Rand entsteht.
    /// Funktioniert unveraendert fuer bidirektionale (-1..1) und unidirektionale (0..1) Werte, da bei
    /// letzteren der Ruhepunkt ebenfalls bei 0 liegt.</summary>
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

    /// <summary>Wendet die gewaehlte Antwortkurve auf den bereits kalibrierten/deadzone-bereinigten Wert an.</summary>
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
            // Reine Potenzfunktion: bei Staerke > 1 nahe 0 unempfindlicher (feinfuehliger), an den
            // Extremen zunehmend steiler (Ableitung von t^n bei t=1 ist n).
            AxisCurveType.Exponential => sign * MathF.Pow(magnitude, strength),

            // Generalisierte logistische S-Kurve: bei Staerke=1 exakt linear (t/(t+(1-t)) = t), bei
            // Staerke > 1 nahe 0 unempfindlicher und kurz vor dem Extremwert (t=1) zunehmend steiler,
            // dabei stets streng monoton und exakt auf [0,1] begrenzt.
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

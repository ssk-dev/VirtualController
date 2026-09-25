using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Benchmark.Metrics;

/// <param name="Axis">Die betroffene Achse (siehe <see cref="HidAxisUsage"/>).</param>
/// <param name="SampleCount">Anzahl der fuer diese Achse eingeflossenen Rohwerte.</param>
/// <param name="DeclaredElementBits">Vom Geraet im Report-Deskriptor deklarierte Bit-Tiefe des Feldes
/// (<see cref="HidAxisFieldInfo.ElementBits"/>) - die THEORETISCH moegliche Aufloesung.</param>
/// <param name="DeclaredLogicalRange">Vom Geraet deklarierter Rohwertbereich (Logical Maximum - Logical
/// Minimum + 1).</param>
/// <param name="ObservedDistinctValues">Anzahl TATSAECHLICH waehrend der Messung beobachteter
/// unterschiedlicher Rohwerte - siehe Klassendokumentation von <see cref="SignalMetrics"/> fuer den
/// Unterschied zur deklarierten Aufloesung.</param>
/// <param name="EffectiveBits">log2(<see cref="ObservedDistinctValues"/>) - die TATSAECHLICH beobachtete
/// Aufloesung in Bit, meist kleiner als <see cref="DeclaredElementBits"/> (siehe Klassendokumentation).</param>
/// <param name="ResolutionSupported">Immer true, sobald mindestens ein Rohwert beobachtet wurde.</param>
/// <param name="NoiseStdDevRaw">Standardabweichung der Rohwerte innerhalb erkannter Ruhephasen (siehe
/// Klassendokumentation, "Noise") - nur sinnvoll interpretierbar, wenn <see cref="NoiseSupported"/> true ist.</param>
/// <param name="NoiseSupported">False, wenn waehrend der gesamten Messung keine erkennbare Ruhephase
/// aufgetreten ist (Achse war durchgehend in Bewegung) - dann ist <see cref="NoiseStdDevRaw"/> 0 und
/// NICHT als "kein Rauschen" zu interpretieren, sondern als "nicht ermittelbar".</param>
/// <param name="DeadzoneRawWidth">Geschaetzte Breite (in Rohwert-Einheiten) einer zusammenhaengenden
/// "Totzone" um den haeufigsten beobachteten Rohwert (siehe Klassendokumentation) - nur gueltig, wenn
/// <see cref="DeadzoneSupported"/> true ist.</param>
/// <param name="DeadzonePercentOfRange"><see cref="DeadzoneRawWidth"/> relativ zu <see cref="DeclaredLogicalRange"/>.</param>
/// <param name="DeadzoneSupported">False bei zu wenigen Messwerten fuer eine belastbare Schaetzung
/// (siehe <see cref="SignalMetrics"/>-Konstante fuer den Schwellwert).</param>
/// <param name="LinearitySupported">Immer false - siehe Klassendokumentation: eine echte
/// Linearitaets-Pruefung erfordert einen Kalibrier-Durchlauf mit bekannten Referenzpositionen, den ein
/// passiver Benchmark nicht durchfuehren kann.</param>
/// <param name="HysteresisSupported">Immer false - siehe Klassendokumentation: aus demselben Grund wie
/// <see cref="LinearitySupported"/> nicht ermittelbar.</param>
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
/// Berechnet Signal-Kennzahlen (Resolution, Noise, Deadzone, Linearity, Hysteresis) je Achse, direkt auf
/// Basis der ROHEN HID-Logical-Werte (siehe <see cref="HidAxisReportParser"/>) - bewusst NICHT auf den
/// bereits durch DirectInput auf [-1,1] normalisierten Werten (<see cref="Devices.DeviceState.Axes"/>),
/// da die Normalisierung die tatsaechliche Bit-Aufloesung des Geraets verschleiern wuerde.
///
/// EHRLICHE EINSCHRAENKUNGEN (siehe Session-weite Vorgabe, reale statt vorgetaeuschte Werte zu liefern):
///
/// - <b>Resolution</b>: die vom Geraet DEKLARIERTE Bit-Tiefe (<see cref="HidAxisFieldInfo.ElementBits"/>)
///   sagt nichts darueber aus, ob das Geraet diese Aufloesung tatsaechlich ausnutzt - manche Geraete
///   deklarieren z.B. 16 Bit, liefern intern aber nur 10 Bit echte Aufloesung (die restlichen Bits sind
///   konstant 0 oder Rauschen). Diese Klasse zaehlt daher die Anzahl TATSAECHLICH waehrend der Messung
///   beobachteter unterschiedlicher Rohwerte (<see cref="SignalAxisResult.ObservedDistinctValues"/>) als
///   ehrlichere Naeherung der real nutzbaren Aufloesung - mit der Einschraenkung, dass eine zu kurze
///   Messung oder eine zu wenig bewegte Achse die beobachtete Aufloesung kuenstlich niedrig erscheinen
///   laesst (es wurden schlicht nicht alle moeglichen Werte durchlaufen).
///
/// - <b>Noise</b>: erfordert eigentlich eine kontrollierte, vollstaendig unbewegte Referenzposition -
///   ein passiver Benchmark kann eine solche nicht erzwingen. Diese Klasse erkennt daher heuristisch
///   "Ruhephasen" (ein gleitendes Fenster von <see cref="QuasiStaticWindowSize"/> aufeinanderfolgenden
///   Rohwerten, deren Spannweite eine geraeteproportionale Toleranz nicht ueberschreitet) und berechnet
///   das Rauschen NUR aus diesen Phasen. Bewegt sich eine Achse waehrend der gesamten Messung
///   durchgehend, gibt es keine solche Phase - siehe <see cref="SignalAxisResult.NoiseSupported"/>.
///
/// - <b>Deadzone</b>: eine echte Deadzone-Vermessung erfordert eine bekannte, kalibrierte
///   Referenzposition ("Mitte"). Diese Klasse SCHAETZT die Ruheposition stattdessen als den im gesamten
///   Messverlauf haeufigsten beobachteten Rohwert (unter der Annahme, dass ein Analogstick/Pedal die
///   meiste Zeit in Ruhe verbringt) und ermittelt die Breite einer zusammenhaengenden Werteregion um
///   diesen Modalwert, in der jeder einzelne Rohwert ueberproportional haeufig vorkommt (siehe
///   <see cref="MinDeadzoneIncrementFraction"/>). Diese Heuristik liefert bei einer Achse, deren
///   Ruheposition NICHT dem Modalwert entspricht (z.B. ein Trigger, der meist auf 0 statt in der Mitte
///   ruht - dort ist das Ergebnis dann korrekt als "keine Deadzone um den Trigger-Ruhepunkt" zu lesen,
///   nicht falsch, aber ggf. weniger aussagekraeftig fuer den erwarteten Anwendungsfall) plausible, aber
///   nicht garantiert korrekte Ergebnisse.
///
/// - <b>Linearity</b>/<b>Hysteresis</b>: nicht implementiert (jeweils <c>Supported</c> = false) - beide
///   erfordern einen definierten Kalibrier-Durchlauf mit BEKANNTEN Referenzpositionen (z.B. "Stick jetzt
///   exakt bei 0%/50%/100% halten") bzw. eine kontrollierte Hin- und Ruecklauf-Bewegung mit
///   Positionsreferenz, um die tatsaechliche physische Position mit dem gemeldeten Rohwert zu
///   vergleichen. Ein rein passiv beobachtender Benchmark (siehe Session-Vorgabe: laeuft bis der Nutzer
///   ihn stoppt, ohne gefuehrten Kalibrier-Ablauf) hat keine solche unabhaengige Positionsreferenz und
///   kann diese Werte daher nicht ehrlich berechnen.
/// </summary>
public sealed class SignalMetrics
{
    /// <summary>Fenstergroesse (Anzahl aufeinanderfolgender Rohwerte) fuer die Ruhephasen-Erkennung, siehe
    /// Klassendokumentation "Noise". Bewusst klein gehalten, damit auch kurze Ruhephasen innerhalb einer
    /// ansonsten bewegten Session erkannt werden.</summary>
    private const int QuasiStaticWindowSize = 30;

    /// <summary>Ein an den Modalwert angrenzender Rohwert gilt fuer die Deadzone-Heuristik nur dann als
    /// Teil der "Totzone", wenn er mindestens diesen Anteil aller Messwerte der Achse ausmacht (siehe
    /// Klassendokumentation "Deadzone"). 0,5% - bewusst klein, aber deutlich ueber dem, was bei einer
    /// gleichmaessig druchlaufenen Achse pro Einzelwert zu erwarten waere.</summary>
    private const double MinDeadzoneIncrementFraction = 0.005;

    /// <summary>Mindestanzahl an Messwerten, unterhalb derer die Deadzone-Heuristik als nicht belastbar
    /// gilt (siehe <see cref="SignalAxisResult.DeadzoneSupported"/>).</summary>
    private const int MinSamplesForDeadzoneHeuristic = 50;

    private readonly Dictionary<HidAxisUsage, AxisAccumulator> _axes;

    /// <param name="availableAxes">Die im Report-Deskriptor gefundenen Achsen-Felder (siehe
    /// <see cref="HidAxisReportParser.AvailableAxes"/>) - legt fest, welche Achsen diese Instanz
    /// entgegennimmt; bei mehreren Feldern fuer dieselbe Achse (sollte praktisch nicht vorkommen) wird
    /// das erste verwendet.</param>
    public SignalMetrics(IReadOnlyList<HidAxisFieldInfo> availableAxes)
    {
        _axes = availableAxes
            .GroupBy(field => field.Axis)
            .ToDictionary(group => group.Key, group => new AxisAccumulator(group.First()));
    }

    /// <summary>Nimmt die aus einem einzelnen Report geparsten Rohwerte entgegen (siehe
    /// <see cref="HidAxisReportParser.TryParse"/>) - unbekannte/nicht ueberwachte Achsen werden ignoriert.</summary>
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

            // Toleranz bewusst proportional zum deklarierten Wertebereich (0,5%), nicht als fester
            // Rohwert - sonst waere die Ruhephasen-Erkennung fuer sehr niedrig- bzw. sehr hochaufloesende
            // Achsen gleichermassen falsch kalibriert.
            _quasiStaticToleranceRawUnits = Math.Max(1, declaredRange * 0.005);
        }

        public void Add(int rawValue)
        {
            _sampleCount++;

            // Histogramm fuer Resolution- und Deadzone-Heuristik (siehe Klassendokumentation von
            // SignalMetrics). Sicherheitslimit gegen unbegrenztes Wachstum bei einem (praktisch nicht
            // beobachteten, aber theoretisch moeglichen) Geraet mit extrem grossem deklariertem
            // Wertebereich - bereits bekannte Werte werden weiterhin gezaehlt, nur keine neuen Schluessel
            // mehr aufgenommen.
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

            // Ein Fenster ohne nennenswerte Bewegung gilt als Ruhephase - dessen neuester Rohwert fliesst
            // als ein Rausch-Messpunkt ein (siehe Klassendokumentation, "Noise").
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

        /// <summary>Siehe Klassendokumentation von <see cref="SignalMetrics"/>, Abschnitt "Deadzone", fuer
        /// die Methodik und ihre Grenzen.</summary>
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

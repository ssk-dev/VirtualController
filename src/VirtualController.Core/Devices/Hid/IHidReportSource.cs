namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Ein einzelner, zeitgestempelter roher HID-Input-Report. Der Zeitstempel wird bewusst als
/// <see cref="System.Diagnostics.Stopwatch"/>-Tick (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>)
/// erfasst statt als <see cref="DateTime"/> - fuer die geplanten Timing-/Latenz-Kennzahlen (Jitter,
/// Intervall-Statistik) ist ausschliesslich die relative, monotone und hochaufloesende Differenz
/// zwischen zwei Reports relevant, keine Wanduhrzeit.
/// </summary>
/// <param name="Data">Die rohen Report-Bytes, exakt so lang wie tatsaechlich gelesen (siehe
/// <see cref="IHidReportSource.InputReportLength"/> fuer die vom Geraet gemeldete Nominal-Laenge).</param>
/// <param name="TimestampTicks">Erfassungszeitpunkt in Stopwatch-Ticks, unmittelbar nach Rueckkehr
/// des zugrunde liegenden blockierenden Lesevorgangs erfasst (siehe <see cref="CaptureTimestampTicks"/>).</param>
public sealed record HidReport(byte[] Data, long TimestampTicks)
{
    /// <summary>Erfasst den aktuellen Zeitstempel fuer einen soeben gelesenen Report - zentrale Stelle,
    /// falls sich die verwendete Zeitquelle spaeter einmal aendern sollte.</summary>
    public static long CaptureTimestampTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
}

/// <summary>
/// Liefert zeitgestempelte, rohe HID-Input-Reports eines konkreten physischen Eingabegeraets -
/// bewusst als schmale Abstraktion ueber der tatsaechlich verwendeten Zugriffsbibliothek (aktuell
/// <c>HidSharp</c>, siehe <see cref="HidDeviceInfoReader"/>/<see cref="HidSharpReportSource"/>).
///
/// Diese Trennung ist bewusst so gewaehlt, dass die geplante Benchmark-/Metrics-Schicht (Timing,
/// Latenz, Reliability - siehe zukuenftiges <c>VirtualController.Core.Benchmark</c>) niemals direkt
/// gegen HidSharp-Typen programmiert. Ein spaeterer Wechsel der zugrunde liegenden Implementierung
/// (z.B. auf eigenes natives P/Invoke) würde dadurch keine Aenderungen oberhalb dieser Schnittstelle
/// erfordern.
///
/// Implementierungen muessen fuer wiederholte <see cref="ReadReport"/>-Aufrufe aus einem einzigen
/// dedizierten Hintergrund-Thread sicher sein (analog zu <see cref="IDeviceReader.Poll"/>), aber NICHT
/// notwendigerweise von mehreren Threads gleichzeitig aufrufbar sein - ein Benchmark liest stets
/// sequenziell von genau einem Thread.
/// </summary>
public interface IHidReportSource : IDisposable
{
    /// <summary>Vom Geraet gemeldete Nominal-Laenge eines Input-Reports in Byte (HidD_GetCaps/
    /// <c>MaxInputReportLength</c>) - bereits vor dem ersten <see cref="ReadReport"/>-Aufruf bekannt,
    /// fuer die Transport-Kennzahl "Report Size".</summary>
    int InputReportLength { get; }

    /// <summary>
    /// Blockiert, bis der naechste Input-Report eintrifft (oder <paramref name="cancellationToken"/>
    /// abgebrochen wird), und liefert ihn zeitgestempelt zurueck.
    /// </summary>
    /// <exception cref="OperationCanceledException">Bei Abbruch ueber <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="IOException">Bei Verbindungsverlust waehrend des Lesens (Geraet getrennt).</exception>
    HidReport ReadReport(CancellationToken cancellationToken);
}

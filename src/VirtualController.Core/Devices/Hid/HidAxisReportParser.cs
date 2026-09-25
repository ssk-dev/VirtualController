using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Statische Kenndaten eines einzelnen, im HID-Report-Deskriptor gefundenen Achsen-Feldes - die
/// Grundlage fuer die geplante "Resolution"-Kennzahl (<c>SignalMetrics</c>): <see cref="ElementBits"/>
/// gibt die vom Geraet deklarierte Bit-Tiefe an, <see cref="LogicalMinimum"/>/<see cref="LogicalMaximum"/>
/// den vom Geraet deklarierten Rohwertbereich. Ob tatsaechlich alle dadurch theoretisch moeglichen
/// Werte auch wirklich vom Geraet geliefert werden (echte Aufloesung vs. deklarierte Aufloesung), muss
/// separat durch Beobachtung der tatsaechlich beobachteten unterschiedlichen Rohwerte ermittelt werden.
/// </summary>
public sealed record HidAxisFieldInfo(HidAxisUsage Axis, int LogicalMinimum, int LogicalMaximum, int ElementBits, byte ReportId);

/// <summary>
/// Loest die im HID-Report-Deskriptor deklarierten "Generic Desktop"-Achsen-Usages (siehe
/// <see cref="HidAxisUsage"/>) auf und liefert daraus fuer eintreffende rohe <see cref="HidReport"/>s die
/// jeweiligen rohen logischen Achsenwerte - bewusst auf Basis der HidSharp-eigenen Report-Parsing-Klassen
/// (<see cref="HidSharp.Reports.Input.DeviceItemInputParser"/>) statt eigener Bit-Arithmetik, da HidSharp
/// bereits die (nicht-triviale) Zuordnung von Bit-Offsets innerhalb eines Reports zu einzelnen Datenfeldern
/// korrekt beherrscht.
///
/// Diese Klasse ist - wie <see cref="HidDeviceInfoReader"/> - eine der wenigen Stellen, die HidSharp-Typen
/// direkt verwendet; nach aussen (insbesondere Richtung <c>Benchmark/Metrics</c>) werden ausschliesslich
/// <see cref="HidAxisUsage"/>, <see cref="HidAxisFieldInfo"/> und rohe <see langword="int"/>-Werte
/// sichtbar (siehe Kapselungs-Hinweis in <see cref="IHidReportSource"/>).
///
/// WICHTIGE EINSCHRAENKUNG: Enthaelt ein Report-Deskriptor mehr als eine Slider-Usage (0x36), wird - analog
/// zur bestehenden Behandlung in <c>DeviceEnumerator.DetectAvailableAxes</c> - die erste gefundene auf
/// <see cref="HidAxisUsage.Slider0"/>, jede weitere auf <see cref="HidAxisUsage.Slider1"/> abgebildet
/// (dritte und weitere Slider-Usages werden mangels weiterer Slot-Namen ignoriert).
/// </summary>
public sealed class HidAxisReportParser
{
    private const int GenericDesktopUsagePage = 0x01;
    private const int SliderUsageId = 0x36;

    private static readonly IReadOnlyDictionary<uint, HidAxisUsage> FixedUsageMap = new Dictionary<uint, HidAxisUsage>
    {
        [Pack(0x30)] = HidAxisUsage.X,
        [Pack(0x31)] = HidAxisUsage.Y,
        [Pack(0x32)] = HidAxisUsage.Z,
        [Pack(0x33)] = HidAxisUsage.RotationX,
        [Pack(0x34)] = HidAxisUsage.RotationY,
        [Pack(0x35)] = HidAxisUsage.RotationZ,
        [Pack(0x37)] = HidAxisUsage.Dial,
        [Pack(0x38)] = HidAxisUsage.Wheel,
    };

    private readonly ReportDescriptor _descriptor;
    private readonly List<(DeviceItem DeviceItem, DeviceItemInputParser Parser)> _parsers;
    private readonly Dictionary<DataItem, HidAxisUsage> _dataItemToAxis;
    private readonly List<HidAxisFieldInfo> _availableAxes;

    private HidAxisReportParser(ReportDescriptor descriptor, List<(DeviceItem, DeviceItemInputParser)> parsers,
        Dictionary<DataItem, HidAxisUsage> dataItemToAxis, List<HidAxisFieldInfo> availableAxes)
    {
        _descriptor = descriptor;
        _parsers = parsers;
        _dataItemToAxis = dataItemToAxis;
        _availableAxes = availableAxes;
    }

    /// <summary>Alle im Report-Deskriptor gefundenen Achsen-Felder (Kenndaten, siehe <see cref="HidAxisFieldInfo"/>).
    /// Kann leer sein, wenn das Geraet keine Generic-Desktop-Achsen-Usages deklariert (z.B. ein reines
    /// Tastatur-/Button-Geraet).</summary>
    public IReadOnlyList<HidAxisFieldInfo> AvailableAxes => _availableAxes;

    /// <summary>Oeffnet den Report-Deskriptor des Geraets am angegebenen Betriebssystem-Geraetepfad (siehe
    /// <see cref="HidDeviceInfo.DevicePath"/>) und ermittelt daraus die verfuegbaren Achsen-Felder. Gibt
    /// null zurueck, falls das Geraet nicht gefunden wird oder der Deskriptor nicht gelesen werden kann -
    /// beides erwartbare, nicht-fatale Faelle (analog zu <see cref="HidDeviceInfoReader.TryOpenReportSource"/>).</summary>
    public static HidAxisReportParser? TryCreate(string devicePath)
    {
        try
        {
            var device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == devicePath);
            if (device is null)
            {
                return null;
            }

            var descriptor = device.GetReportDescriptor();

            var parsers = new List<(DeviceItem, DeviceItemInputParser)>();
            var dataItemToAxis = new Dictionary<DataItem, HidAxisUsage>();
            var availableAxes = new List<HidAxisFieldInfo>();
            bool slider0Assigned = false;

            foreach (var deviceItem in descriptor.DeviceItems)
            {
                parsers.Add((deviceItem, deviceItem.CreateDeviceItemInputParser()));

                foreach (var report in deviceItem.InputReports)
                {
                    foreach (var dataItem in report.DataItems)
                    {
                        var usages = dataItem.Usages.GetAllValues();
                        HidAxisUsage? matched = null;

                        foreach (var usage in usages)
                        {
                            if (FixedUsageMap.TryGetValue(usage, out var fixedAxis))
                            {
                                matched = fixedAxis;
                                break;
                            }

                            if (usage == Pack(SliderUsageId))
                            {
                                matched = slider0Assigned ? HidAxisUsage.Slider1 : HidAxisUsage.Slider0;
                                slider0Assigned = true;
                                break;
                            }
                        }

                        if (matched is not { } axis)
                        {
                            continue;
                        }

                        dataItemToAxis[dataItem] = axis;
                        availableAxes.Add(new HidAxisFieldInfo(axis, dataItem.LogicalMinimum, dataItem.LogicalMaximum,
                            dataItem.ElementBits, report.ReportID));
                    }
                }
            }

            return new HidAxisReportParser(descriptor, parsers, dataItemToAxis, availableAxes);
        }
        catch
        {
            // Geraet ggf. waehrend des Zugriffs getrennt worden, oder Deskriptor aus sonstigen Gruenden
            // nicht lesbar - beides darf nicht zum Absturz der Anwendung fuehren (analog zu allen anderen
            // Hid-Zugriffsstellen in diesem Projekt).
            return null;
        }
    }

    /// <summary>
    /// Parst einen zuvor per <see cref="IHidReportSource.ReadReport"/> gelesenen rohen Report und liefert
    /// die darin enthaltenen rohen logischen Achsenwerte (siehe <see cref="HidSharp.Reports.DataItem.LogicalMinimum"/>/
    /// <see cref="HidSharp.Reports.DataItem.LogicalMaximum"/> fuer den jeweils gueltigen Wertebereich).
    /// Liefert <see langword="false"/>, wenn der Report keinem bekannten Input-Report dieses Geraets
    /// entspricht (z.B. veraltete Report-ID nach Geraete-Wechsel) - in diesem Fall ist
    /// <paramref name="rawAxisValues"/> leer, aber nicht null.
    /// </summary>
    public bool TryParse(HidReport report, out IReadOnlyDictionary<HidAxisUsage, int> rawAxisValues)
    {
        var result = new Dictionary<HidAxisUsage, int>();
        rawAxisValues = result;

        try
        {
            var buffer = report.Data;
            byte reportId = _descriptor.ReportsUseID && buffer.Length > 0 ? buffer[0] : (byte)0;

            if (!_descriptor.TryGetReport(ReportType.Input, reportId, out var matchedReport))
            {
                return false;
            }

            var entry = _parsers.FirstOrDefault(p => p.DeviceItem == matchedReport.DeviceItem);
            if (entry.Parser is null)
            {
                return false;
            }

            if (!entry.Parser.TryParseReport(buffer, 0, matchedReport))
            {
                return false;
            }

            for (int i = 0; i < entry.Parser.ValueCount; i++)
            {
                var value = entry.Parser.GetValue(i);
                if (_dataItemToAxis.TryGetValue(value.DataItem, out var axis))
                {
                    result[axis] = value.GetLogicalValue();
                }
            }

            return true;
        }
        catch
        {
            // Ein einzelner, unerwartet fehlerhafter Report (z.B. Laenge passt nicht mehr zum zuvor
            // gelesenen Deskriptor nach einer Geraete-Neuverbindung) darf einen laufenden Benchmark/Log
            // nicht abbrechen - siehe analoge Fehlerbehandlung in HidSharpReportSource/RawHidReportReader.
            return false;
        }
    }

    private static uint Pack(int usageId) => (uint)((GenericDesktopUsagePage << 16) | usageId);
}

using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Static metadata for one axis field in the HID report descriptor, used by the SignalMetrics resolution
/// metric. <see cref="ElementBits"/> is the bit depth declared by the device; <see cref="LogicalMinimum"/>/
/// <see cref="LogicalMaximum"/> define its declared raw value range. Whether the device actually reports all
/// theoretically possible values (effective vs. declared resolution) must be measured separately by observing
/// distinct raw values.
/// </summary>
public sealed record HidAxisFieldInfo(HidAxisUsage Axis, int LogicalMinimum, int LogicalMaximum, int ElementBits, byte ReportId);

/// <summary>
/// Resolves the Generic Desktop axis usages declared by the HID report descriptor (see <see cref="HidAxisUsage"/>),
/// then extracts their raw logical values from incoming <see cref="HidReport"/>s. Uses HidSharp's report parsing
/// classes (<see cref="HidSharp.Reports.Input.DeviceItemInputParser"/>) instead of custom bit arithmetic because
/// HidSharp correctly maps bit offsets within reports to individual data fields.
///
/// Like <see cref="HidDeviceInfoReader"/>, this is one of the few places that directly uses HidSharp types.
/// Only <see cref="HidAxisUsage"/>, <see cref="HidAxisFieldInfo"/>, and raw <see langword="int"/> values are
/// exposed to callers, especially <c>Benchmark/Metrics</c> (see the encapsulation note in <see cref="IHidReportSource"/>).
///
/// Important limitation: if a report descriptor contains multiple Slider usages (0x36), the first maps to
/// <see cref="HidAxisUsage.Slider0"/> and each subsequent one maps to <see cref="HidAxisUsage.Slider1"/>,
/// matching <c>DeviceEnumerator.DetectAvailableAxes</c>. Third and later Slider usages are ignored because no
/// additional slots are defined.
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

    /// <summary>Axis fields found in the report descriptor (metadata in <see cref="HidAxisFieldInfo"/>). May be
    /// empty if the device declares no Generic Desktop axis usages, e.g. a keyboard/button-only device.</summary>
    public IReadOnlyList<HidAxisFieldInfo> AvailableAxes => _availableAxes;

    /// <summary>Opens the report descriptor for the device at the specified OS device path (see
    /// <see cref="HidDeviceInfo.DevicePath"/>) and discovers its axis fields. Returns null if the device cannot
    /// be found or its descriptor cannot be read; both are expected, non-fatal cases (like
    /// <see cref="HidDeviceInfoReader.TryOpenReportSource"/>).</summary>
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
            // The device may have disconnected during access, or the descriptor may be unreadable for another
            // reason. Neither case should crash the app, matching other HID access points in this project.
            return null;
        }
    }

    /// <summary>
    /// Parses a raw report read by <see cref="IHidReportSource.ReadReport"/> and returns its raw logical axis
    /// values (see <see cref="HidSharp.Reports.DataItem.LogicalMinimum"/>/<see cref="HidSharp.Reports.DataItem.LogicalMaximum"/>
    /// for the valid range). Returns false if the report does not match a known input report for this device,
    /// e.g. a stale report ID after a device change; in that case, <paramref name="rawAxisValues"/> is empty but non-null.
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
            // One unexpectedly malformed report (e.g. its length no longer matches the descriptor after a
            // reconnect) must not abort a running benchmark/log; see similar handling in
            // HidSharpReportSource/RawHidReportReader.
            return false;
        }
    }

    private static uint Pack(int usageId) => (uint)((GenericDesktopUsagePage << 16) | usageId);
}

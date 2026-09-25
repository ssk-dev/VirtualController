using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt <see cref="ViewModels.DeviceConfigDeviceViewModel.IsBenchmarking"/> in die Beschriftung
/// des Benchmark-Buttons um: "Benchmark stoppen" waehrend eine Sitzung laeuft, sonst "Benchmark starten".</summary>
public sealed class BoolToBenchmarkButtonTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Benchmark stoppen" : "Benchmark starten";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

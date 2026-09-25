using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt <see cref="ViewModels.DeviceConfigDeviceViewModel.IsLogging"/> in die Beschriftung
/// des Protokollierungs-Buttons um: "Log stoppen" waehrend eine Sitzung laeuft, sonst "Log starten".</summary>
public sealed class BoolToLogButtonTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Log stoppen" : "Log starten";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

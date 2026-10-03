using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace VirtualController.App.Converters;

/// <summary>Converts a boolean state (running/connected vs. not) into a green or gray status color for the small UI indicators.</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

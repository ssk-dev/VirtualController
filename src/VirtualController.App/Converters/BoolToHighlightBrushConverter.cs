using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace VirtualController.App.Converters;

/// <summary>Converts whether a physical input is currently active into a highlight color
/// for the live indicator in the expandable input list.</summary>
public sealed class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A));
    private static readonly Brush InactiveBrush = System.Windows.Media.Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

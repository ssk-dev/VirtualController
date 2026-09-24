using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace VirtualController.App.Converters;

/// <summary>Wandelt den "Ist diese physische Eingabe aktuell aktiv?"-Zustand einer Eingabezeile in eine
/// auffaellige Hervorhebungsfarbe fuer die Live-Anzeige in der aufklappbaren Eingabeliste.</summary>
public sealed class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A));
    private static readonly Brush InactiveBrush = System.Windows.Media.Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

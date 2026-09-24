using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace VirtualController.App.Converters;

/// <summary>Wandelt den "Gibt es ungespeicherte Aenderungen?"-Zustand in eine Textfarbe fuer den
/// Speichern-Button: rot bei ungespeicherten Aenderungen, sonst die normale Textfarbe. Bewusst
/// ueber <c>Foreground</c> statt <c>Background</c> umgesetzt, damit dies nicht mit dem globalen
/// Klick-Feedback-Style (der die Hintergrundfarbe beim Draufklicken kurz aendert) kollidiert.</summary>
public sealed class BoolToUnsavedForegroundConverter : IValueConverter
{
    private static readonly Brush UnsavedBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
    private static readonly Brush DefaultBrush = System.Windows.SystemColors.ControlTextBrush;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? UnsavedBrush : DefaultBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

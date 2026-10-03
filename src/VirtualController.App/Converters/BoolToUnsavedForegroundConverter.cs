using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace VirtualController.App.Converters;

/// <summary>Converts whether there are unsaved changes into a text color for the save button:
/// red when changes are unsaved, otherwise the default text color. Uses <c>Foreground</c> instead
/// of <c>Background</c> to avoid conflicting with the global click-feedback style, which briefly
/// changes the background color when clicked.</summary>
public sealed class BoolToUnsavedForegroundConverter : IValueConverter
{
    private static readonly Brush UnsavedBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
    private static readonly Brush DefaultBrush = System.Windows.SystemColors.ControlTextBrush;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? UnsavedBrush : DefaultBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

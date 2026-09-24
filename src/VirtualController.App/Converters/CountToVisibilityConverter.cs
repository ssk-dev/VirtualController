using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt eine Anzahl (int) in Visibility (Anzahl &gt; 0 = Visible, sonst Collapsed). Dient z.B.
/// dazu, den "Ausgeblendete Geräte"-Bereich im "Gerätekonfiguration"-Tab nur anzuzeigen, wenn tatsaechlich
/// mindestens ein Geraet ausgeblendet wurde (Bindung an HiddenDevices.Count).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int count || count <= 0)
        {
            return Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

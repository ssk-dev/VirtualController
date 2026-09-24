using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt bool in Visibility (true = Visible, false = Collapsed). Optionaler ConverterParameter
/// "Invert" dreht die Zuordnung um (true = Collapsed, false = Visible), z.B. um einen Hinweis nur
/// anzuzeigen, wenn eine bool-Property false ist, ohne dafuer einen eigenen Converter zu brauchen.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isTrue = value is true;
        bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        bool showVisible = invert ? !isTrue : isTrue;
        return showVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Converts a boolean to Visibility (true = Visible, false = Collapsed). The optional ConverterParameter
/// "Invert" reverses the mapping (true = Collapsed, false = Visible), for example to show a hint only
/// when a boolean property is false without needing a dedicated converter.</summary>
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

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>
/// Compares the enum value of a binding source (e.g. <see cref="Mapping.MappingTargetKind"/>)
/// with the name passed through <see cref="ConverterParameter"/> and returns Visible or Collapsed.
/// Used to show the matching ComboBox (button/axis/trigger/D-pad) in the mapping table
/// based on the selected target type.
/// </summary>
public sealed class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

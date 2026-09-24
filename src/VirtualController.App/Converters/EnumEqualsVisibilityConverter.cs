using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>
/// Vergleicht den Enum-Wert einer Binding-Quelle (z.B. <see cref="Mapping.MappingTargetKind"/>)
/// gegen den als <see cref="ConverterParameter"/> uebergebenen Namen und liefert Visible/Collapsed.
/// Wird genutzt, um in der Mapping-Tabelle je nach gewaehltem Ziel-Typ die passende ComboBox
/// (Button/Achse/Trigger/DPad) einzublenden.
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

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Converts a boolean state to a <see cref="FontWeight"/>: bold for true, normal for false.
/// Used to emphasize the save button for unsaved changes in addition to its red text color
/// (see <see cref="BoolToUnsavedForegroundConverter"/>).</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

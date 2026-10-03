using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Converts a boolean to an opacity value (true = 1.0 fully visible, false = 0.4 dimmed).
/// Used to visually distinguish disabled physical inputs in the main window (see
/// <see cref="ViewModels.PhysicalInputRowViewModel.IsEnabled"/>) visuell hervorzuheben.</summary>
public sealed class BoolToDimOpacityConverter : IValueConverter
{
    private const double DimmedOpacity = 0.4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : DimmedOpacity;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

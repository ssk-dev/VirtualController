using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>
/// Converts a fraction (0.0 .. 1.0, e.g. marker/deadzone fractions from
/// <see cref="ViewModels.AxisVisualizationViewModel"/>) into a proportionally weighted
/// <see cref="GridLength"/> (Star). This keeps the three grid columns/rows
/// ("before" / marker or deadzone band / "after") proportional to the actual width
/// or height of the parent control, regardless of its pixel size. The generic axis visualization
/// therefore relies exclusively on WPF Grid layout for marker and deadzone positioning instead of custom pixel calculations.
/// </summary>
public sealed class FractionToStarGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double fraction = value is double d ? d : 0.0;
        return new GridLength(Math.Max(0.0, fraction), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

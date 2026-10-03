using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Converts a count (int) to Visibility (count &gt; 0 = Visible, otherwise Collapsed). Used, for example,
/// to show the "Hidden Devices" section on the "Device Configuration" tab only when at least one device
/// has actually been hidden (bound to HiddenDevices.Count).</summary>
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

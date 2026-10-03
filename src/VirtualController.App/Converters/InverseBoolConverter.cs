using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Inverts a boolean value (true -&gt; false, false -&gt; true). Used, for example, to
/// disable the virtual controller's "Layout" selection while it is running
/// (IsEnabled should then be false while IsRunning is true).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.Updater.Converters;

/// <summary>Wandelt bool in Visibility (true = Visible, false = Collapsed) - minimale, eigenstaendige
/// Kopie von VirtualController.App.Converters.BoolToVisibilityConverter (dieses Projekt referenziert
/// bewusst keine anderen Projekte, siehe VirtualController.Updater.csproj).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

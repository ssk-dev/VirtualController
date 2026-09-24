using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt bool in eine Opacity (true = 1.0 voll sichtbar, false = 0.4 ausgegraut).
/// Wird genutzt, um im Hauptfenster deaktivierte physische Eingaben (siehe
/// <see cref="ViewModels.PhysicalInputRowViewModel.IsEnabled"/>) visuell hervorzuheben.</summary>
public sealed class BoolToDimOpacityConverter : IValueConverter
{
    private const double DimmedOpacity = 0.4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : DimmedOpacity;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt <see cref="ViewModels.MainViewModel.DriverReady"/> in den Untertitel der ViGEmBus-
/// Status-Anzeige im Header um ("Treiber aktiv" vs. "Treiber nicht verfuegbar"). Ergaenzt
/// <see cref="ViewModels.MainViewModel.DriverStatusText"/> (Haupttext, z.B. "ViGEmBus verbunden") um eine
/// zweite, kleinere Detailzeile in der Status-"Pille" (siehe MainWindow.xaml).</summary>
public sealed class BoolToDriverSubtextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Treiber aktiv" : "Treiber nicht verfuegbar";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

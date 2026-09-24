using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>
/// Wandelt einen Bruchwert (0.0 .. 1.0, z.B. die Marker-/Deadzone-Anteile aus
/// <see cref="ViewModels.AxisVisualizationViewModel"/>) in eine mit diesem Anteil gewichtete
/// <see cref="GridLength"/> (Star) um. Damit lassen sich die drei Spalten/Zeilen eines Grids
/// ("davor" / Marker bzw. Deadzone-Band / "danach") stets proportional zur tatsaechlichen Breite
/// bzw. Hoehe des umgebenden Steuerelements aufteilen, unabhaengig von dessen konkreter Pixelgroesse -
/// die eigentliche Positionierung von Marker und Deadzone-Rahmen in der generischen Achsenvisualisierung
/// verlaesst sich also ausschliesslich auf das WPF-Grid-Layoutsystem statt auf eigene Pixel-Berechnungen.
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

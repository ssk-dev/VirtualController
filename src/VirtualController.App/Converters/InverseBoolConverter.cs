using System.Globalization;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Kehrt einen bool-Wert um (true -&gt; false, false -&gt; true). Wird u.a. genutzt, um
/// die "Layout"-Auswahl eines virtuellen Controllers zu sperren, waehrend dieser laeuft
/// (IsEnabled soll dann false sein, waehrend IsRunning true ist).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualController.App.Converters;

/// <summary>Wandelt einen bool-Zustand in ein <see cref="FontWeight"/> um: fett bei true, normal bei false.
/// Wird u.a. genutzt, um den Speichern-Button bei ungespeicherten Aenderungen zusaetzlich zur roten
/// Textfarbe (siehe <see cref="BoolToUnsavedForegroundConverter"/>) hervorzuheben.</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

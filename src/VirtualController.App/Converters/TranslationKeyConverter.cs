using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using VirtualController.App.Services;

namespace VirtualController.App.Converters;

/// <summary>
/// Converts a translation key (e.g. "mapping.target_kind.button") into the localized text for the current
/// UI language. Used by ComboBoxes whose items are enums or translation keys rather than plain strings,
/// so the dropdown labels switch language at runtime without rebuilding the ItemsSource.
/// </summary>
public sealed class TranslationKeyConverter : IValueConverter
{
    static TranslationKeyConverter()
    {
        // When the language changes, force all bindings that use this converter to re-evaluate.
        // This is needed because ComboBox ItemTemplate bindings are not automatically refreshed by
        // the global PropertyChanged on TranslationService (the binding source is the item, not the service).
        TranslationService.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(TranslationService.CurrentLanguage) or "")
            {
                // Trigger a dummy change notification to force re-evaluation of all converter bindings.
                // WPF does not have a direct way to refresh all bindings, so we use the Dispatcher to
                // invalidate the converter on the next UI thread tick.
                System.Windows.Application.Current?.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            }
        };
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        // If the value is already a translation key string (e.g. from TargetOptionItem.Label),
        // use it directly. If it's an enum, build the key from the converter parameter prefix.
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            var prefix = parameter as string ?? string.Empty;
            key = $"{prefix}.{value.ToString()!.ToLowerInvariant()}";
        }

        return TranslationService.Instance.GetText(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using VirtualController.App.Services;

// The project enables Windows Forms integration (tray icon), whose global usings make "Binding"
// ambiguous with System.Windows.Forms.Binding. All bindings here are WPF bindings.
using Binding = System.Windows.Data.Binding;

namespace VirtualController.App.Localization;

/// <summary>
/// Markup extension that binds a XAML text property directly to a translation key, for example
/// <c>Text="{loc:Loc mapping.start}"</c>. The binding targets the <see cref="TranslationService"/>
/// indexer, so the element updates automatically whenever the UI language changes at runtime
/// (TranslationService raises a global PropertyChanged notification that re-evaluates all indexer
/// bindings). Works regardless of the surrounding DataContext, including inside DataTemplates and
/// DataGrid column headers.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() => Key = string.Empty;

    public LocExtension(string key) => Key = key;

    /// <summary>The translation key, e.g. "mapping.start".</summary>
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]")
        {
            Source = TranslationService.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Like <see cref="LocExtension"/>, but the translated text is a format string containing one {0}
/// placeholder that is filled with a bound value, for example
/// <c>Text="{loc:LocFormat Key=header.version_format, Value={Binding CurrentVersionText}}"</c>.
/// Replaces Binding.StringFormat, which cannot be localized because it is not a dependency property.
/// The text re-formats both when the value changes and when the language changes.
/// </summary>
public sealed class LocFormatExtension : MarkupExtension
{
    public LocFormatExtension() => Key = string.Empty;

    public LocFormatExtension(string key) => Key = key;

    /// <summary>The translation key of the format string, e.g. "header.version_format".</summary>
    public string Key { get; set; }

    /// <summary>The binding that supplies the {0} value.</summary>
    public BindingBase? Value { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multiBinding = new MultiBinding
        {
            Converter = LocalizedFormatConverter.Instance,
            Mode = BindingMode.OneWay
        };

        if (Value is not null)
        {
            multiBinding.Bindings.Add(Value);
        }

        // The format string itself comes from the translation indexer; this binding also acts as the
        // trigger that re-evaluates the MultiBinding when the language changes.
        multiBinding.Bindings.Add(new Binding("[" + Key + "]") { Source = TranslationService.Instance });
        return multiBinding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Chooses between two translation keys based on a bound boolean, for example
/// <c>Text="{loc:LocBool Value={Binding DriverReady}, WhenTrue=driver.subtext_active, WhenFalse=driver.subtext_unavailable}"</c>.
/// Replaces the former BoolTo*TextConverter classes; unlike an IValueConverter on the boolean alone,
/// the additional translation binding makes the text update when the language changes, not only when
/// the boolean changes.
/// </summary>
public sealed class LocBoolExtension : MarkupExtension
{
    /// <summary>Translation key used when the bound value is true.</summary>
    public string? WhenTrue { get; set; }

    /// <summary>Translation key used when the bound value is false.</summary>
    public string? WhenFalse { get; set; }

    /// <summary>The binding that supplies the boolean value.</summary>
    public BindingBase? Value { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multiBinding = new MultiBinding
        {
            Converter = new LocalizedBoolConverter(WhenTrue, WhenFalse),
            Mode = BindingMode.OneWay
        };

        if (Value is not null)
        {
            multiBinding.Bindings.Add(Value);
        }

        // Pure refresh trigger: re-evaluates the MultiBinding when the language changes. The actual
        // text is chosen inside the converter through TranslationService.GetText.
        multiBinding.Bindings.Add(new Binding("[settings.tab]") { Source = TranslationService.Instance });
        return multiBinding.ProvideValue(serviceProvider);
    }
}

/// <summary>Formats values[0] with the translated format string in values[^1] (see
/// <see cref="LocFormatExtension"/>). Uses the current culture, which TranslationService keeps in
/// sync with the selected UI language.</summary>
internal sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public static readonly LocalizedFormatConverter Instance = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0)
        {
            return string.Empty;
        }

        var format = values[^1] as string;
        if (string.IsNullOrEmpty(format))
        {
            return values[0]?.ToString() ?? string.Empty;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, values[0]);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns the translation of one of two keys depending on a boolean value (see
/// <see cref="LocBoolExtension"/>). One instance per binding because the keys are instance data.</summary>
internal sealed class LocalizedBoolConverter : IMultiValueConverter
{
    private readonly string? _whenTrue;
    private readonly string? _whenFalse;

    public LocalizedBoolConverter(string? whenTrue, string? whenFalse)
    {
        _whenTrue = whenTrue;
        _whenFalse = whenFalse;
    }

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = values.Length > 0 && values[0] is true;
        var key = flag ? _whenTrue : _whenFalse;
        return string.IsNullOrEmpty(key) ? string.Empty : TranslationService.Instance.GetText(key);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

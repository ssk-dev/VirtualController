using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace VirtualController.App.Services;

public sealed class TranslationService : INotifyPropertyChanged
{
    private const string DefaultLanguage = "en";

    private static readonly Lazy<TranslationService> InstanceLazy = new(() => new TranslationService());

    private readonly Dictionary<string, Dictionary<string, string>> _languageCache = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = DefaultLanguage;

    public static TranslationService Instance => InstanceLazy.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage => _currentLanguage;

    /// <summary>Indexer that allows XAML to bind directly to translation keys (e.g. [mapping.start]).
    /// Combined with the empty <see cref="PropertyChanged"/> notification raised in <see cref="ApplyLanguage"/>,
    /// every such binding re-queries its text automatically when the UI language changes.</summary>
    public string this[string key] => GetText(key);

    public void ApplyLanguage(string? languageCode)
    {
        var normalizedCode = NormalizeLanguage(languageCode);
        var effectiveLanguage = normalizedCode == "system" ? DetectSystemLanguage() : normalizedCode;

        _currentLanguage = effectiveLanguage;
        LoadLanguage(effectiveLanguage);
        SetCurrentCulture(effectiveLanguage);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        // An empty property name tells WPF to re-evaluate ALL bindings to this source, including every
        // indexer binding created through the Loc/LocFormat/LocBool markup extensions, so the whole UI
        // switches language live without restarting or rebuilding any view.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string GetText(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_languageCache.TryGetValue(_currentLanguage, out var lookup) && lookup.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_languageCache.TryGetValue(DefaultLanguage, out var fallbackLookup) && fallbackLookup.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    private static readonly IReadOnlyList<UiLanguageOption> SupportedLanguages = new[]
    {
        new UiLanguageOption("system", "settings.language.system"),
        new UiLanguageOption("en", "settings.language.english"),
        new UiLanguageOption("de", "settings.language.german")
    };

    public IReadOnlyList<UiLanguageOption> GetAvailableLanguages() => SupportedLanguages;

    public static string DetectSystemLanguage()
    {
        var language = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        return language is "de" or "en" ? language : DefaultLanguage;
    }

    private static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "system";
        }

        var trimmed = languageCode.Trim();
        if (trimmed.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        if (trimmed.Length >= 2)
        {
            return trimmed.Substring(0, 2).ToLowerInvariant();
        }

        return trimmed.ToLowerInvariant();
    }

    private void LoadLanguage(string languageCode)
    {
        var normalized = NormalizeLanguage(languageCode);

        if (_languageCache.ContainsKey(normalized))
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "translations", normalized + ".json");
        if (!File.Exists(path))
        {
            if (normalized != DefaultLanguage)
            {
                LoadLanguage(DefaultLanguage);
            }

            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (values is not null)
            {
                _languageCache[normalized] = values;
            }
        }
        catch
        {
            // Ignore unreadable translation files and fall back to the defaults.
        }
    }

    private static void SetCurrentCulture(string languageCode)
    {
        var normalized = NormalizeLanguage(languageCode);
        var culture = normalized switch
        {
            "de" => new CultureInfo("de-DE"),
            "en" => new CultureInfo("en-US"),
            _ => CultureInfo.CurrentCulture
        };

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

public sealed record UiLanguageOption(string Code, string DisplayName);

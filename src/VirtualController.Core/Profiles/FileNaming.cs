using System.Text;
using System.Text.RegularExpressions;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Hilfsfunktionen zum Ableiten von unter Windows gueltigen, lesbaren Dateinamen aus Anzeigenamen
/// (Geraete, virtuelle Controller) fuer die pro Geraet/Controller aufgeteilte Persistenz (siehe
/// <see cref="DeviceSettingsStore"/>, <see cref="ControllerStore"/>). Oeffentlich, da dieselbe
/// Marke/Name-Aufteilung auch von den unabhaengigen Logging- und Benchmark-Exportdateien
/// (log-device-{marke}-{name}.txt bzw. benchmark-device-{marke}-{name}.json) verwendet wird, um
/// unterschiedliche Dateinamenskonventionen fuer denselben Geraetenamen zu vermeiden.
/// </summary>
public static class FileNaming
{
    /// <summary>Wandelt einen beliebigen Anzeigenamen in ein Dateinamen-taugliches Segment um:
    /// klein geschrieben, ungueltige/problematische Zeichen entfernt, Leerraum durch '-' ersetzt,
    /// mehrfache '-' zusammengefasst. Liefert "unbenannt", falls am Ende nichts uebrig bleibt.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unbenannt";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (Array.IndexOf(invalidChars, ch) >= 0 || ch is '|' or ':')
            {
                continue; // Fuer Dateinamen ungeeignetes Zeichen einfach weglassen statt zu ersetzen.
            }

            builder.Append(char.IsWhiteSpace(ch) ? '-' : ch);
        }

        var collapsed = Regex.Replace(builder.ToString(), "-{2,}", "-").Trim('-');
        return collapsed.Length == 0 ? "unbenannt" : collapsed;
    }

    /// <summary>Teilt einen Geraete-Anzeigenamen in Marke (erstes Wort) und restlichen Namen auf, z.B.
    /// "Logitech Extreme 3D Pro" -&gt; ("logitech", "extreme-3d-pro") - ergibt den Dateinamen
    /// "device-logitech-extreme-3d-pro.json". Ohne erkennbares zweites Wort wird als Marke "geraet"
    /// verwendet und der komplette (sanitisierte) Name als Namensteil beibehalten.</summary>
    public static (string Brand, string Name) SplitBrandAndName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ("geraet", "unbenannt");
        }

        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex <= 0)
        {
            return ("geraet", Sanitize(trimmed));
        }

        var brand = Sanitize(trimmed[..spaceIndex]);
        var name = Sanitize(trimmed[(spaceIndex + 1)..]);
        return (brand.Length == 0 ? "geraet" : brand, name.Length == 0 ? "unbenannt" : name);
    }
}

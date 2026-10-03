using System.Text;
using System.Text.RegularExpressions;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Helpers for deriving readable Windows-compatible filenames from device and virtual controller display
/// names for per-device/controller persistence (see <see cref="DeviceSettingsStore"/> and
/// <see cref="ControllerStore"/>). Public because the same brand/name split is also used by independent log
/// and benchmark export files (log-device-{brand}-{name}.txt and benchmark-device-{brand}-{name}.json) for
/// consistent naming.
/// </summary>
public static class FileNaming
{
    /// <summary>Converts a display name to a filename-safe segment: lowercase, remove invalid/problematic
    /// characters, replace whitespace with hyphens, and collapse repeated hyphens. Returns "unnamed" if the
    /// result is empty.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (Array.IndexOf(invalidChars, ch) >= 0 || ch is '|' or ':')
            {
                continue; // Omit characters unsuitable for filenames rather than replacing them.
            }

            builder.Append(char.IsWhiteSpace(ch) ? '-' : ch);
        }

        var collapsed = Regex.Replace(builder.ToString(), "-{2,}", "-").Trim('-');
        return collapsed.Length == 0 ? "unnamed" : collapsed;
    }

    /// <summary>Splits a device display name into brand (first word) and remainder, e.g.
    /// "Logitech Extreme 3D Pro" -> ("logitech", "extreme-3d-pro"), producing
    /// "device-logitech-extreme-3d-pro.json". If no second word exists, uses "device" as the brand and keeps
    /// the sanitized full name as the name segment.</summary>
    public static (string Brand, string Name) SplitBrandAndName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ("device", "unnamed");
        }

        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex <= 0)
        {
            return ("device", Sanitize(trimmed));
        }

        var brand = Sanitize(trimmed[..spaceIndex]);
        var name = Sanitize(trimmed[(spaceIndex + 1)..]);
        return (brand.Length == 0 ? "device" : brand, name.Length == 0 ? "unnamed" : name);
    }
}

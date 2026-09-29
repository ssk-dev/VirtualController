using System.Text.RegularExpressions;

namespace VirtualController.Core.Updates;

/// <summary>
/// Einfache SemVer-Repraesentation ("Major.Minor.Patch[-PreRelease]", z.B. "1.4.2" oder "0.0.0-dev") mit
/// numerischem Vergleich, wie ihn <see cref="UpdateChecker"/> benoetigt, um eine installierte gegen eine
/// verfuegbare Version zu vergleichen (siehe Akzeptanzkriterium "Versionsnummern sollen semantisch
/// verglichen werden"). Bewusst keine vollstaendige SemVer-2.0.0-Implementierung (z.B. keine Build-
/// Metadaten, keine mehrteiligen PreRelease-Bezeichner mit eigener Prioritaet) - fuer die hier
/// benoetigten Versionsstrings (Git-Tags im Format "vX.Y.Z", Fallback "0.0.0-dev") reicht dieser
/// Ausschnitt vollstaendig aus.
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.\-]+))?$",
        RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Optionaler PreRelease-Bezeichner (z.B. "dev" bei "0.0.0-dev"), oder null bei einer
    /// regulaeren Release-Version. Eine PreRelease-Version gilt bei sonst gleichen Major/Minor/Patch als
    /// "kleiner" als die entsprechende Release-Version (analog zur SemVer-Spezifikation), damit ein
    /// Entwicklungsbuild ("0.0.0-dev") niemals versehentlich als neuer als eine echte Release-Version
    /// gilt.</summary>
    public string? PreRelease { get; }

    public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        version = new SemanticVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    /// <summary>Wirft eine <see cref="FormatException"/>, falls <paramref name="value"/> keinem gueltigen
    /// "Major.Minor.Patch[-PreRelease]"-Format entspricht - genutzt fuer <see cref="AppVersionProvider"/>,
    /// wo ein ungueltiger Wert einen Programmierfehler anzeigen wuerde (die dortige Fallback-Zeichenkette
    /// "0.0.0-dev" ist selbst immer gueltig).</summary>
    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"'{value}' ist keine gueltige Versionsnummer.");

    public int CompareTo(SemanticVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // Gleiche Major.Minor.Patch: eine Version OHNE PreRelease-Suffix gilt als neuer/hoeher als
        // dieselben Zahlen MIT Suffix (z.B. "1.4.2" > "1.4.2-dev"). Sind beide PreRelease-Versionen,
        // entscheidet ein einfacher, kulturinvarianter String-Vergleich der Suffixe.
        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
}

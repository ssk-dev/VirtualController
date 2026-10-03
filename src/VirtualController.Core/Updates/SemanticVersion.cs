using System.Text.RegularExpressions;

namespace VirtualController.Core.Updates;

/// <summary>
/// Simple SemVer representation ("Major.Minor.Patch[-PreRelease]", e.g. "1.4.2" or "0.0.0-dev") with numeric
/// comparison, used by <see cref="UpdateChecker"/> to compare installed and available versions. Not a complete
/// SemVer 2.0.0 implementation (e.g. no build metadata or precedence rules for multi-part prerelease labels),
/// but sufficient for the version strings used here (Git tags in "vX.Y.Z" format and the "0.0.0-dev" fallback).
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.\-]+))?$",
        RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Optional prerelease label (e.g. "dev" in "0.0.0-dev"), or null for a stable release. With the
    /// same major/minor/patch, a prerelease ranks lower than the corresponding release per SemVer, so a
    /// development build is never considered newer than a real release.</summary>
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

    /// <summary>Throws <see cref="FormatException"/> if <paramref name="value"/> does not match
    /// "Major.Minor.Patch[-PreRelease]". Used by <see cref="AppVersionProvider"/>, where an invalid value
    /// indicates a programming error; its "0.0.0-dev" fallback is always valid.</summary>
    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a valid version number.");

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

        // Same major/minor/patch: a release without a prerelease suffix ranks higher than the same version
        // with a suffix (e.g. "1.4.2" > "1.4.2-dev"). If both are prereleases, compare suffixes ordinally.
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

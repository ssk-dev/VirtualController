using System.Reflection;

namespace VirtualController.Core.Updates;

/// <summary>
/// Reads the app version derived from the Git tag at build time (see the ComputeVersionFromGitTag target in
/// Directory.Build.targets) from the running assembly's <see cref="AssemblyInformationalVersionAttribute"/>.
/// The .NET SDK generates this attribute from the MSBuild InformationalVersion property, so the version does
/// not need to be maintained manually in source code; it is determined by the Git tag used for the build.
/// </summary>
public static class AppVersionProvider
{
    /// <summary>Fallback if the attribute is missing or invalid. This should not normally happen because
    /// Directory.Build.targets uses the same value when no Git tag is available. Ensures
    /// <see cref="CurrentVersion"/> never throws and update checks/display treat the build as a development build.</summary>
    public const string FallbackVersion = "0.0.0-dev";

    private static readonly Lazy<string> RawVersionLazy = new(ReadRawVersion);
    private static readonly Lazy<SemanticVersion> CurrentVersionLazy = new(() =>
        SemanticVersion.TryParse(RawVersionLazy.Value, out var version) ? version : SemanticVersion.Parse(FallbackVersion));

    /// <summary>Raw version string from the assembly attribute, e.g. "1.4.2" or "0.0.0-dev", for display in
    /// the title bar/update dialog without further formatting.</summary>
    public static string RawVersion => RawVersionLazy.Value;

    /// <summary>Currently installed version as <see cref="SemanticVersion"/>, for semantic comparison with an
    /// available version (see <see cref="UpdateChecker"/>).</summary>
    public static SemanticVersion CurrentVersion => CurrentVersionLazy.Value;

    private static string ReadRawVersion()
    {
        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var value = attribute?.InformationalVersion;
        return string.IsNullOrWhiteSpace(value) ? FallbackVersion : value;
    }
}

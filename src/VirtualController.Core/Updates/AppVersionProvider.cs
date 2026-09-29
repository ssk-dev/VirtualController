using System.Reflection;

namespace VirtualController.Core.Updates;

/// <summary>
/// Liest die zur Build-Zeit aus dem Git-Tag ermittelte App-Version (siehe
/// Directory.Build.targets, Target "ComputeVersionFromGitTag") zur Laufzeit aus dem
/// <see cref="AssemblyInformationalVersionAttribute"/> der aktuell laufenden Assembly aus. Dieses
/// Attribut wird vom .NET SDK automatisch aus der MSBuild-Property "InformationalVersion" generiert -
/// dadurch muss die Versionsnummer nirgends manuell im Quellcode gepflegt werden, sie ist vielmehr eine
/// direkte Funktion des Git-Tags, mit dem gebaut wurde.
/// </summary>
public static class AppVersionProvider
{
    /// <summary>Fallback, falls das Attribut aus irgendeinem Grund fehlt oder keinen gueltigen Wert
    /// enthaelt (sollte im Normalfall nie eintreten, da Directory.Build.targets bereits bei einem
    /// fehlenden Git-Tag auf genau diesen Wert zurueckfaellt) - stellt sicher, dass <see cref="CurrentVersion"/>
    /// niemals eine Ausnahme wirft, sondern die Update-Pruefung/Anzeige lediglich als Entwicklungsbuild
    /// erkennt.</summary>
    public const string FallbackVersion = "0.0.0-dev";

    private static readonly Lazy<string> RawVersionLazy = new(ReadRawVersion);
    private static readonly Lazy<SemanticVersion> CurrentVersionLazy = new(() =>
        SemanticVersion.TryParse(RawVersionLazy.Value, out var version) ? version : SemanticVersion.Parse(FallbackVersion));

    /// <summary>Roher Versions-String wie im Assembly-Attribut hinterlegt, z.B. "1.4.2" oder "0.0.0-dev" -
    /// fuer Anzeige-Zwecke (Titelzeile, Update-Popup), bei denen der Text 1:1 ohne weitere Formatierung
    /// benoetigt wird.</summary>
    public static string RawVersion => RawVersionLazy.Value;

    /// <summary>Aktuell installierte Version als <see cref="SemanticVersion"/>, fuer den semantischen
    /// Vergleich mit einer verfuegbaren Version (siehe <see cref="UpdateChecker"/>).</summary>
    public static SemanticVersion CurrentVersion => CurrentVersionLazy.Value;

    private static string ReadRawVersion()
    {
        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var value = attribute?.InformationalVersion;
        return string.IsNullOrWhiteSpace(value) ? FallbackVersion : value;
    }
}

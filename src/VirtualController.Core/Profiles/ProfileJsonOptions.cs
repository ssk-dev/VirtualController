using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Zentrale <see cref="JsonSerializerOptions"/>-Konfiguration fuer alle Profil-Teilspeicher
/// (<see cref="DeviceSettingsStore"/>, <see cref="ControllerStore"/>, <see cref="SettingsStore"/>),
/// damit z.B. Enum-Namen ueberall gleich (als lesbarer String statt Zahl) serialisiert werden.
/// </summary>
internal static class ProfileJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

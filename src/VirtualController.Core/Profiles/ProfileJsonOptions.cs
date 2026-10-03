using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualController.Core.Profiles;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for all profile stores (<see cref="DeviceSettingsStore"/>,
/// <see cref="ControllerStore"/>, <see cref="SettingsStore"/>), ensuring enums are consistently serialized
/// as readable strings rather than numbers.
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

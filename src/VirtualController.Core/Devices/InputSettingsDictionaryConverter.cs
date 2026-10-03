using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualController.Core.Devices;

/// <summary>
/// Custom serialization for <see cref="DeviceSettings.Inputs"/>. Omits fields that do not apply to the input
/// type, determined from the dictionary key (see <see cref="PhysicalInputCatalog.TryParseStorageKey"/>).
/// Digital buttons and D-pad directions have no calibration, deadzone, or response curve; those fields apply
/// only to axes/sliders (<see cref="PhysicalInputKind.AxisPositive"/>/<see cref="PhysicalInputKind.AxisNegative"/>).
/// This keeps saved button profiles smaller and avoids meaningless values.
///
/// Standard deserialization is sufficient when reading: omitted JSON fields receive their normal defaults
/// (see <see cref="InputSettings"/>), like any other optional field.
/// </summary>
public sealed class InputSettingsDictionaryConverter : JsonConverter<Dictionary<string, InputSettings>>
{
    public override Dictionary<string, InputSettings>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Null)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected an object for the Inputs dictionary.");
            }

            var result = new Dictionary<string, InputSettings>();
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                var value = JsonSerializer.Deserialize<InputSettings>(ref reader, options) ?? new InputSettings();
                result[key] = value;
                reader.Read();
            }

            return result;
        }

        return null;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, InputSettings> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, settings) in value)
        {
            writer.WritePropertyName(key);
            WriteEntry(writer, key, settings);
        }

        writer.WriteEndObject();
    }

    private static void WriteEntry(Utf8JsonWriter writer, string key, InputSettings settings)
    {
        writer.WriteStartObject();

        if (settings.CustomName is null)
        {
            writer.WriteNull(nameof(InputSettings.CustomName));
        }
        else
        {
            writer.WriteString(nameof(InputSettings.CustomName), settings.CustomName);
        }

        writer.WriteBoolean(nameof(InputSettings.Enabled), settings.Enabled);

        if (SupportsAnalogSettings(key))
        {
            WriteNullableFloat(writer, nameof(InputSettings.CalibratedMin), settings.CalibratedMin);
            WriteNullableFloat(writer, nameof(InputSettings.CalibratedMax), settings.CalibratedMax);
            WriteNullableFloat(writer, nameof(InputSettings.CalibratedCenter), settings.CalibratedCenter);
            writer.WriteNumber(nameof(InputSettings.Deadzone), settings.Deadzone);
            writer.WriteString(nameof(InputSettings.CurveType), settings.CurveType.ToString());
            writer.WriteNumber(nameof(InputSettings.CurveStrength), settings.CurveStrength);
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableFloat(Utf8JsonWriter writer, string propertyName, float? value)
    {
        if (value is { } v)
        {
            writer.WriteNumber(propertyName, v);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    /// <summary>Only axes/sliders support calibration, deadzone, and response curves; buttons and D-pad
    /// directions are digital and have no calibratable range. If the key cannot be parsed (e.g. an unexpected
    /// future format), serialize all fields to avoid silently discarding potentially relevant data.</summary>
    private static bool SupportsAnalogSettings(string key)
    {
        if (!PhysicalInputCatalog.TryParseStorageKey(key, out _, out var kind, out _))
        {
            return true;
        }

        return kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;
    }
}

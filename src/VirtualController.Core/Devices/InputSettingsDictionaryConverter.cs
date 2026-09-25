using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualController.Core.Devices;

/// <summary>
/// Custom-Serialisierung fuer <see cref="DeviceSettings.Inputs"/>: blendet beim Schreiben Felder aus,
/// die fuer die jeweilige Eingabeart (aus dem Dictionary-Key ermittelt, siehe
/// <see cref="PhysicalInputCatalog.TryParseStorageKey"/>) fachlich keine Bedeutung haben. Ein digitaler
/// Button oder eine D-Pad-Richtung besitzt z.B. keine Kalibrierung, Deadzone oder Antwortkurve - diese
/// Felder sind ausschliesslich fuer Achsen/Slider (<see cref="PhysicalInputKind.AxisPositive"/>/
/// <see cref="PhysicalInputKind.AxisNegative"/>) relevant. So bleiben gespeicherte Profile fuer Buttons
/// deutlich kompakter und enthalten keine irrefuehrenden, fachlich bedeutungslosen Werte.
///
/// Beim Lesen genuegt die Standard-Deserialisierung: fehlt eines der ausgeblendeten Felder im JSON,
/// erhaelt die Eigenschaft ohnehin ihren regulaeren Default-Wert (siehe <see cref="InputSettings"/>),
/// exakt wie bei jedem anderen optionalen Feld.
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
                throw new JsonException("Erwartetes Objekt fuer Inputs-Dictionary nicht gefunden.");
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

    /// <summary>Nur Achsen/Slider unterstuetzen Kalibrierung, Deadzone und Antwortkurve - Buttons und
    /// D-Pad-Richtungen sind rein digital und besitzen keinen kalibrierbaren Wertebereich. Kann der Key
    /// nicht geparst werden (z.B. unerwartetes/zukuenftiges Format), wird sicherheitshalber weiterhin
    /// alles geschrieben, statt moeglicherweise relevante Daten stillschweigend zu verwerfen.</summary>
    private static bool SupportsAnalogSettings(string key)
    {
        if (!PhysicalInputCatalog.TryParseStorageKey(key, out _, out var kind, out _))
        {
            return true;
        }

        return kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;
    }
}

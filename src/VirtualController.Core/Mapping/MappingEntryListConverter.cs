using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtualController.Core.Devices;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Custom serialization for <see cref="ControllerMode.Mappings"/>. Omits fields that do not apply to the
/// entry's source type (<see cref="MappingEntry.SourceKind"/>). Digital buttons and D-pad directions have
/// neither inversion nor axis-half selection (<see cref="MappingEntry.DirectionalOnly"/>); those fields only
/// apply to axes (<see cref="PhysicalInputKind.AxisPositive"/>/<see cref="PhysicalInputKind.AxisNegative"/>).
/// This keeps button/D-pad mappings compact and avoids misleading, meaningless values.
///
/// Reading can use standard deserialization per entry: omitted JSON fields receive their normal defaults
/// (see <see cref="MappingEntry"/>), like any other optional field.
/// </summary>
public sealed class MappingEntryListConverter : JsonConverter<List<MappingEntry>>
{
    public override List<MappingEntry>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected an array for the mappings list.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.EnumerateArray()
            .Select(element => element.Deserialize<MappingEntry>(options))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToList();
    }

    public override void Write(
        Utf8JsonWriter writer, List<MappingEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value)
        {
            BuildEntryNode(entry).WriteTo(writer, options);
        }

        writer.WriteEndArray();
    }

    /// <summary>Builds one mapping entry as a <see cref="JsonObject"/> rather than writing fields directly
    /// through <see cref="Utf8JsonWriter"/>. Axis-specific fields (<see cref="IsAxisSource"/>) are included
    /// only for axis sources.</summary>
    private static JsonObject BuildEntryNode(MappingEntry entry)
    {
        var node = new JsonObject
        {
            [nameof(MappingEntry.SourceDeviceId)] = entry.SourceDeviceId,
            [nameof(MappingEntry.SourceKind)] = entry.SourceKind.ToString(),
            [nameof(MappingEntry.SourceIndex)] = entry.SourceIndex,
            [nameof(MappingEntry.TargetKind)] = entry.TargetKind.ToString(),
            [nameof(MappingEntry.TargetButton)] = entry.TargetButton?.ToString(),
            [nameof(MappingEntry.TargetAxis)] = entry.TargetAxis?.ToString(),
            [nameof(MappingEntry.TargetTrigger)] = entry.TargetTrigger?.ToString(),
            [nameof(MappingEntry.TargetDPadDirection)] = entry.TargetDPadDirection?.ToString()
        };

        if (IsAxisSource(entry.SourceKind))
        {
            node[nameof(MappingEntry.Invert)] = entry.Invert;
            node[nameof(MappingEntry.DirectionalOnly)] = entry.DirectionalOnly;
        }

        node[nameof(MappingEntry.Description)] = entry.Description;

        return node;
    }

    /// <summary>Only axes support inversion and direction isolation; buttons and D-pad directions are digital
    /// and have no analog range.</summary>
    private static bool IsAxisSource(PhysicalInputKind kind)
        => kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;
}

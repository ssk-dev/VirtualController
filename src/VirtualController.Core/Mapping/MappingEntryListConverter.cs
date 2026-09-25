using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtualController.Core.Devices;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Custom-Serialisierung fuer <see cref="ControllerMode.Mappings"/>: blendet beim Schreiben Felder aus,
/// die fuer die jeweilige Quellart (<see cref="MappingEntry.SourceKind"/>) fachlich keine Bedeutung haben.
/// Ein digitaler Button oder eine D-Pad-Richtung kennt weder eine Invertierung noch eine Deadzone oder
/// die Aufteilung einer Achse in zwei Haelften (<see cref="MappingEntry.DirectionalOnly"/>) - diese Felder
/// sind ausschliesslich fuer Achsen (<see cref="PhysicalInputKind.AxisPositive"/>/
/// <see cref="PhysicalInputKind.AxisNegative"/>) relevant. So bleiben gespeicherte Mapping-Eintraege fuer
/// Buttons/D-Pad deutlich kompakter und enthalten keine irrefuehrenden, fachlich bedeutungslosen Werte.
///
/// Beim Lesen genuegt die Standard-Deserialisierung pro Eintrag: fehlt eines der ausgeblendeten Felder
/// im JSON, erhaelt die Eigenschaft ohnehin ihren regulaeren Default-Wert (siehe <see cref="MappingEntry"/>),
/// exakt wie bei jedem anderen optionalen Feld.
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
            throw new JsonException("Erwartetes Array fuer Mappings-Liste nicht gefunden.");
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

    /// <summary>Baut die JSON-Darstellung eines einzelnen Mapping-Eintrags als <see cref="JsonObject"/> auf,
    /// statt die Felder direkt ueber den <see cref="Utf8JsonWriter"/> zu schreiben. Achsenspezifische Felder
    /// (<see cref="IsAxisSource"/>) werden dem Objekt nur bei einer analogen Quelle ueberhaupt hinzugefuegt.</summary>
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
            node[nameof(MappingEntry.Deadzone)] = entry.Deadzone;
        }

        node[nameof(MappingEntry.Description)] = entry.Description;

        return node;
    }

    /// <summary>Nur Achsen kennen Invertierung, Richtungsisolation und Deadzone - Buttons und D-Pad-Richtungen
    /// sind rein digital und besitzen keinen analogen Wertebereich.</summary>
    private static bool IsAxisSource(PhysicalInputKind kind)
        => kind is PhysicalInputKind.AxisPositive or PhysicalInputKind.AxisNegative;
}

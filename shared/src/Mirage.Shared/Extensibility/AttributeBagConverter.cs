using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Reads and writes an <see cref="AttributeBag"/> as a plain JSON object whose values are plain JSON
/// values — <c>{ "baseHp": 45, "shiny": false, "species": "ember" }</c>.
///
/// <para>A JSON number becomes <see cref="AttributeKind.Integer"/> when it fits a <c>long</c> exactly
/// and <see cref="AttributeKind.Real"/> otherwise, so whole numbers survive a round trip as whole
/// numbers.</para>
///
/// <para><b>An entry Core cannot represent is skipped, not thrown on.</b> A null, an array, or a nested
/// object in a hand-edited world file drops that one key and leaves the rest of the record loadable.
/// Throwing here would take down a server's boot over a stray comma in a value the engine never reads
/// — and the loaders on the startup path have no guard to catch it.</para>
/// </summary>
public sealed class AttributeBagConverter : JsonConverter<AttributeBag>
{
    public override AttributeBag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var bag = new AttributeBag();
        if (reader.TokenType == JsonTokenType.Null) return bag;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"An attribute bag must be a JSON object; found {reader.TokenType}.");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return bag;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string key = reader.GetString() ?? string.Empty;
            if (!reader.Read()) break;

            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    bag[key] = reader.TryGetInt64(out long whole)
                        ? AttributeValue.From(whole)
                        : AttributeValue.From(reader.GetDouble());
                    break;
                case JsonTokenType.True:
                    bag[key] = AttributeValue.From(true);
                    break;
                case JsonTokenType.False:
                    bag[key] = AttributeValue.From(false);
                    break;
                case JsonTokenType.String:
                    bag[key] = AttributeValue.From(reader.GetString());
                    break;
                default:
                    // Null, an array, or a nested object: not a value this bag can hold. Step over
                    // whatever it is — Skip() walks a container to its end — and keep the rest.
                    reader.Skip();
                    break;
            }
        }

        return bag;
    }

    public override void Write(Utf8JsonWriter writer, AttributeBag value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Sorted, so a bag written twice produces identical bytes regardless of the order its keys were
        // set in. Both a file diff and the editor's save comparison read those bytes.
        foreach (var (key, entry) in value.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            switch (entry.Kind)
            {
                case AttributeKind.Integer: writer.WriteNumberValue(entry.AsLong()); break;
                case AttributeKind.Real: writer.WriteNumberValue(entry.AsDouble()); break;
                case AttributeKind.Flag: writer.WriteBooleanValue(entry.AsBool()); break;
                default: writer.WriteStringValue(entry.AsText()); break;
            }
        }

        writer.WriteEndObject();
    }
}

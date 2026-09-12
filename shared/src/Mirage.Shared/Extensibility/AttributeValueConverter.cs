using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Reads and writes one <see cref="AttributeValue"/> as a plain JSON value — <c>45</c>, <c>false</c>,
/// <c>"ember"</c> — with no kind tag beside it.
///
/// <para>The same shape <see cref="AttributeBagConverter"/> writes inside a bag, so a value looks the
/// same in an authored world file and on the wire. That is worth having: the person reading a captured
/// line and the person editing the file are usually the same person.</para>
///
/// <para>A number that fits a <c>long</c> exactly reads back as <see cref="AttributeKind.Integer"/> and
/// any other as <see cref="AttributeKind.Real"/>, so whole numbers survive a round trip whole. Anything
/// this cannot represent — a null, an array, an object — reads as the zero Integer rather than
/// throwing, matching the bag's rule that a value the engine never interprets must not be able to fail
/// a load.</para>
/// </summary>
public sealed class AttributeValueConverter : JsonConverter<AttributeValue>
{
    public override AttributeValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out long whole)
                    ? AttributeValue.From(whole)
                    : AttributeValue.From(reader.GetDouble());
            case JsonTokenType.True: return AttributeValue.From(true);
            case JsonTokenType.False: return AttributeValue.From(false);
            case JsonTokenType.String: return AttributeValue.From(reader.GetString());
            default:
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, AttributeValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case AttributeKind.Integer: writer.WriteNumberValue(value.AsLong()); break;
            case AttributeKind.Real: writer.WriteNumberValue(value.AsDouble()); break;
            case AttributeKind.Flag: writer.WriteBooleanValue(value.AsBool()); break;
            default: writer.WriteStringValue(value.AsText()); break;
        }
    }
}

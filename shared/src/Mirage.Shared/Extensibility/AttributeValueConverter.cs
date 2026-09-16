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
/// this cannot represent — a null, an object — reads as the zero Integer rather than throwing, matching
/// the bag's rule that a value the engine never interprets must not be able to fail a load.</para>
///
/// <para><b>An array is a set</b>, and its kind is the kind of its FIRST member: <c>[1, 2]</c> is a set
/// of whole numbers and <c>["a", "b"]</c> a set of words. ⚠ A member of any other kind is coerced to
/// that one rather than refused, because a set may hold only one kind and a hand-edited file is exactly
/// where a stray <c>"3"</c> among numbers comes from. An empty array is an empty set of whole numbers,
/// there being nothing in it to read a kind off.</para>
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
            case JsonTokenType.StartArray: return ReadSet(ref reader, typeToConvert, options);
            default:
                reader.Skip();
                return default;
        }
    }

    /// <summary>An array, as a set of whatever its first member is.</summary>
    private AttributeValue ReadSet(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var members = new List<AttributeValue>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            members.Add(Read(ref reader, typeToConvert, options));
        }

        if (members.Count == 0) return AttributeValue.EmptySet(AttributeKind.Integer);

        // The first member settles it, and the rest are read through that kind's own accessor - which
        // is the same coercion every other reader of a bag already relies on.
        return members[0].Kind switch
        {
            AttributeKind.Text => AttributeValue.From(members.Select(m => m.AsText())),
            AttributeKind.Flag => AttributeValue.From(members.Select(m => m.AsBool())),
            AttributeKind.Real => AttributeValue.From(members.Select(m => m.AsDouble())),
            _ => AttributeValue.From(members.Select(m => m.AsLong())),
        };
    }

    public override void Write(Utf8JsonWriter writer, AttributeValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case AttributeKind.Integer: writer.WriteNumberValue(value.AsLong()); break;
            case AttributeKind.Real: writer.WriteNumberValue(value.AsDouble()); break;
            case AttributeKind.Flag: writer.WriteBooleanValue(value.AsBool()); break;
            case AttributeKind.Set: WriteSet(writer, value); break;
            default: writer.WriteStringValue(value.AsText()); break;
        }
    }

    /// <summary>A set, as a JSON array of its members. Shared with the bag's own writer so a value
    /// written inside a bag and one written on its own cannot drift apart.</summary>
    internal static void WriteSet(Utf8JsonWriter writer, AttributeValue value)
    {
        writer.WriteStartArray();

        foreach (AttributeValue member in value.Members)
        {
            switch (value.Of)
            {
                case AttributeKind.Real: writer.WriteNumberValue(member.AsDouble()); break;
                case AttributeKind.Flag: writer.WriteBooleanValue(member.AsBool()); break;
                case AttributeKind.Text: writer.WriteStringValue(member.AsText()); break;
                default: writer.WriteNumberValue(member.AsLong()); break;
            }
        }

        writer.WriteEndArray();
    }
}

using System.Text.Json;
using Mirage.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Host.Scripting;

/// <summary>
/// A message a world's own rules declared, as it arrives off the wire.
///
/// <para>🔴 <b>A packet does not need a compiled type.</b> The registry takes a PARSE DELEGATE rather
/// than a type — <c>Register&lt;T&gt;</c> is only the convenient overload for a shape somebody compiled
/// — so a command nobody built an assembly for reads perfectly well into the bag below. That is what
/// lets a script own a message of its own without a rebuild.</para>
///
/// <para>The values arrive in the shapes the MODEL declared, because a line is text either way and
/// something has to say which field is a number. A field the line leaves out is absent rather than
/// zero, which <c>Has</c> is what tells apart.</para>
/// </summary>
public sealed record ScriptedPacket(string Cmd, AttributeBag Values) : IPacket
{
    /// <summary>Reads one line as the fields a model declared, or null for one that does not fit.
    ///
    /// <para>Anything the model did not name is DROPPED rather than carried. A script reads what it
    /// declared, and a sender that adds fields cannot reach past what the rules said they may send.</para></summary>
    public static ScriptedPacket? Read(string command, string json, IReadOnlyList<ScriptModelField> fields)
    {
        try
        {
            using JsonDocument line = JsonDocument.Parse(json);
            if (line.RootElement.ValueKind is not JsonValueKind.Object) return null;

            var values = new AttributeBag();

            foreach (ScriptModelField field in fields)
            {
                if (!line.RootElement.TryGetProperty(field.Name, out JsonElement value)) continue;
                if (Value(field, value) is { } read) values.Set(field.Name, read);
            }

            return new ScriptedPacket(command, values);
        }
        catch (JsonException)
        {
            // A malformed line is a line, not a crash: the reader answers null and the caller drops
            // it, which is what every other parse in the registry does.
            return null;
        }
    }

    /// <summary>One field, in the shape the model asked for, or null where the line disagrees.</summary>
    private static AttributeValue? Value(ScriptModelField field, JsonElement value) => field.Shape switch
    {
        ScriptFieldShape.Whole when value.TryGetInt64(out long whole) => AttributeValue.From(whole),
        ScriptFieldShape.Fraction when value.TryGetDouble(out double part) => AttributeValue.From(part),
        ScriptFieldShape.Text when value.ValueKind is JsonValueKind.String
            => AttributeValue.From(value.GetString()),
        ScriptFieldShape.Truth when value.ValueKind is JsonValueKind.True or JsonValueKind.False
            => AttributeValue.From(value.GetBoolean()),

        // An enumeration travels as the member's own name, the same text the editor writes into a
        // record, so one vocabulary covers both.
        ScriptFieldShape.Choice when value.ValueKind is JsonValueKind.String
                                    && field.Choices.Contains(value.GetString(), StringComparer.Ordinal)
            => AttributeValue.From(value.GetString()),

        _ => null,
    };
}

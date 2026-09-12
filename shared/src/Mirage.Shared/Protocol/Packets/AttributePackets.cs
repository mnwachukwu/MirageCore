using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C, once per session before anything else is synced: what the loaded game's attribute keys are
/// called, and which small number stands for each on the wire.
///
/// <para><b>The client learns the numbering rather than agreeing on it.</b> Ordinals are handed out by
/// the server's schema in declaration order, so two servers running different module sets number the
/// same key differently and neither is wrong. A client that assumed a constant would read one game's
/// values as another's.</para>
///
/// <para>A world whose game declares nothing sends an empty list, which is the ordinary case for Core
/// on its own.</para>
/// </summary>
public sealed record AttributeSchemaPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AttributeSchema;

    /// <summary>One row per declared key. Visibility is included because the client uses it to know
    /// which values it may be told about at all — a key it never receives is not a bug to report.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<Row> Keys { get; init; } = [];

    public readonly record struct Row(
        [property: JsonPropertyName("ord")] int Ordinal,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("vis")] AttributeVisibility Visibility,
        [property: JsonPropertyName("label")] string? LabelKey);
}

/// <summary>
/// S→C: some of one body's attributes changed, here are their new values.
///
/// <para><b>Only what the receiver may see, and only what moved.</b> The server projects a bag against
/// the schema and the viewer's closeness before building this, so an onlooker is never sent an owner's
/// values and no filtering is left to the client. Keys ride as ordinals because this is the packet that
/// repeats — once per body per change — and a name per entry would be most of the payload.</para>
///
/// <para>A value is the natural JSON one, exactly as it is in an authored file: a number, a bool, or a
/// string. There is no kind tag, so a line stays readable to somebody watching the wire.</para>
/// </summary>
public sealed record AttributeSyncPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AttributeSync;

    /// <summary>Whose attributes these are.</summary>
    [JsonPropertyName("who")] public EntityHandle Who { get; init; }

    /// <summary>The changed entries. Never empty: a sync with nothing in it is not sent.</summary>
    [JsonPropertyName("set")] public IReadOnlyList<Entry> Set { get; init; } = [];

    public readonly record struct Entry(
        [property: JsonPropertyName("ord")] int Ordinal,
        [property: JsonPropertyName("v")] AttributeValue Value);
}

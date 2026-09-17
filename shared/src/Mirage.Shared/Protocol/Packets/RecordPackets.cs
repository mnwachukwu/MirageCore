using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// Authoring a record of a family neither the editor nor the engine was compiled against.
///
/// <para><b>One trio for every family a game declares.</b> Core's own families each have their own
/// packet with a property per field, because Core knows what those fields are. A module's family is
/// described only by the <see cref="RecordFamily"/> the server sent on login, so its records travel as
/// what they are: a family id, a slot number, and an <see cref="AttributeBag"/>. Adding a family to a
/// game adds no packet.</para>
///
/// <para><b>The bag is passed through, never interpreted.</b> The server stores the keys it is sent and
/// sends back the keys it holds. A key no field describes survives a round trip untouched, which is
/// what lets a world authored against a newer build of a game open in an older editor without being
/// quietly stripped on the next save.</para>
/// </summary>
public sealed record EditorRequestRecordPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestRecord;

    /// <summary>The <see cref="RecordFamily.Id"/> being authored.</summary>
    [JsonPropertyName("family")] public string Family { get; init; } = "";

    /// <summary>1-based slot number within that family.</summary>
    [JsonPropertyName("num")] public int Num { get; init; }
}

/// <summary>C→S: every record of one family, for the editor's list. The whole-family fetch Core's own
/// families each have their own packet for.</summary>
public sealed record EditorRequestAllRecordsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllRecords;
    [JsonPropertyName("family")] public string Family { get; init; } = "";
}

/// <summary>C→S: store this record.
///
/// <para>The bag REPLACES what the slot held rather than merging into it, so clearing a field in the
/// editor actually clears it. An editor that means to keep a key it does not show must send it back,
/// which the request/apply pair exists for.</para></summary>
public sealed record EditorSaveRecordPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveRecord;
    [JsonPropertyName("family")] public string Family { get; init; } = "";
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("fields")] public AttributeBag Fields { get; init; } = new();
}

/// <summary>S→C: one record, as the answer to a request and as the broadcast after a save.</summary>
public sealed record UpdateRecordPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateRecord;
    [JsonPropertyName("family")] public string Family { get; init; } = "";
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("fields")] public AttributeBag Fields { get; init; } = new();
}

/// <summary>S→C: every slot of one family, in slot order, blanks included — the editor lists a fixed
/// range of slots and an author fills the ones they want.</summary>
public sealed record EditorAllRecordsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllRecords;
    [JsonPropertyName("family")] public string Family { get; init; } = "";
    [JsonPropertyName("records")] public Entry[] Records { get; init; } = [];

    public sealed record Entry(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("fields")] AttributeBag Fields);
}

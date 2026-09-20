using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>S→C: the full item definition table, sent once at join and cached by the client.</summary>
public sealed record SendItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendItems;
    [JsonPropertyName("items")] public ItemData[] Items { get; init; } = [];

    /// <summary>One item's definition as sent to the client.</summary>
    public sealed record ItemData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("pic")] short Pic,
        [property: JsonPropertyName("type")] ItemType Type,
        // Type-specific fields; see ItemRecord for which apply to which ItemType.
        [property: JsonPropertyName("durability")] short Durability,
        // Item restriction flags — drive the client's list/mail/drop-warning gates.
        [property: JsonPropertyName("nonTradeable")] bool NonTradeable,
        [property: JsonPropertyName("nonListable")] bool NonListable,
        [property: JsonPropertyName("nonMailable")] bool NonMailable,
        [property: JsonPropertyName("destroyOnDrop")] bool DestroyOnDrop,
        [property: JsonPropertyName("nonJunkable")] bool NonJunkable = false,
        // Gold worth. The client needs it to quote a shop's sales list and to preview what a sell pays,
        // both of which happen before any server round-trip.
        [property: JsonPropertyName("price")] int Price = 0,
        // Which item sheet Pic is a row of. Appended with a default: a positional record re-reads every
        // argument after an inserted field, at every construction site, without a compile error.
        [property: JsonPropertyName("itemSheet")] short ItemSheet = 0,
        // Which equipment slot a piece is worn in; blank on anything not worn.
        [property: JsonPropertyName("equipSlot")] string EquipSlot = ""
    );
}

/// <summary>S→C: one item's definition — the editor's request response, and the live broadcast on an editor save so clients refresh without reconnecting.</summary>
public sealed record UpdateItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("pic")] public short Pic { get; init; }
    [JsonPropertyName("itemSheet")] public short ItemSheet { get; init; }
    [JsonPropertyName("type")] public ItemType Type { get; init; }
    // Type-specific fields; see ItemRecord for which apply to which ItemType.
    [JsonPropertyName("durability")] public short Durability { get; init; }
    /// <summary>Which equipment slot this is worn in; blank on anything not worn. A key, not a label:
    /// what it is called is the game's, and the editor is told the list separately.</summary>
    [JsonPropertyName("equipSlot")] public string EquipSlot { get; init; } = "";
    // Item restriction flags. See ItemRecord for behavior.
    [JsonPropertyName("nonTradeable")] public bool NonTradeable { get; init; }
    [JsonPropertyName("nonListable")] public bool NonListable { get; init; }
    [JsonPropertyName("nonMailable")] public bool NonMailable { get; init; }
    [JsonPropertyName("destroyOnDrop")] public bool DestroyOnDrop { get; init; }
    [JsonPropertyName("nonJunkable")] public bool NonJunkable { get; init; }
    [JsonPropertyName("price")] public int Price { get; init; }
}

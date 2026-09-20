using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── Shop ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: raise the (client-local) Inn panel — the response when a player interacts with an NPC that
/// keeps an Inn. Carries the keeper's shop number so the inn's actions (set spawn / bank / market) resolve the
/// right inn from anywhere the keeper stands (shops are not map-bound).</summary>
public sealed record OpenInnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.OpenInn;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
}

/// <summary>C→S: trade against one row of the open shop's BARTER table.</summary>
public sealed record ShopBarterPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ShopBarter;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("barterSlot")] public int BarterSlot { get; init; }
    /// <summary>How many times to apply the row, which is a RATE rather than a single swap: five teeth
    /// against a two-teeth row buys two helpings and leaves one behind. 0 and 1 both mean once. The server
    /// refuses outright — never trims — when the bag cannot take the whole payout.</summary>
    [JsonPropertyName("multiples")] public int Multiples { get; init; } = 1;
}

/// <summary>C→S: buy from the open shop's SALES list. <see cref="SalesSlot"/> is 1-based to match
/// <see cref="ShopBarterPacket.BarterSlot"/> — the client sends its display index + 1. <see cref="Quantity"/>
/// applies to stackables (currency) and is clamped by the server to what the purse covers; anything else is
/// one piece however many are asked for. The server prices it — the client never proposes a value.</summary>
public sealed record ShopBuyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ShopBuy;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("salesSlot")] public int SalesSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: sell one inventory slot to the open shop. <see cref="Quantity"/> applies to stackables
/// (currency); 0 or more than the stack holds means "all of it", mirroring the drop/deposit convention.
/// The server prices it — the client never proposes a value.</summary>
public sealed record ShopSellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ShopSell;
    [JsonPropertyName("invSlot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

public sealed record FixItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.FixItem;
    [JsonPropertyName("invSlot")] public int InvSlot { get; init; }
}

public sealed record SendShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendShops;
    [JsonPropertyName("shops")] public ShopData[] Shops { get; init; } = [];

    public sealed record ShopData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("fixes")] bool FixesItems,
        [property: JsonPropertyName("shopType")] ShopType ShopType,
        [property: JsonPropertyName("allowBanking")] bool AllowBanking
    );
}

/// <summary>S→C: everything needed to draw an open shop — its barter rows AND its sales list.
///
/// <para>The two are separate because they are different transactions, not two spellings of one. A barter row
/// names both sides explicitly and can ask for anything; a sales entry is just an item number, priced from
/// <see cref="Records.ItemRecord.Price"/>, which the client already holds from the item definitions. So the
/// sales list costs one int per entry on the wire no matter how large the shopfront gets.</para></summary>
public sealed record ShopContentsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ShopContents;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("barters")] public BarterRow[] Barters { get; init; } = [];
    /// <summary>Item numbers the shop sells for gold, in authored display order.</summary>
    [JsonPropertyName("sales")] public int[] Sales { get; init; } = [];

    public sealed record BarterRow(
        [property: JsonPropertyName("giveItem")] int GiveItem,
        [property: JsonPropertyName("giveQuantity")] int GiveQuantity,
        [property: JsonPropertyName("getItem")] int GetItem,
        [property: JsonPropertyName("getQuantity")] int GetQuantity
    );
}

// ── The action bar ───────────────────────────────────────────────────────────

/// <summary>
/// S→C: how wide this game's action bar is, and what the character has on it. Sent at join and echoed
/// after every change, so the client never has to guess whether its own edit was accepted.
///
/// <para>🔴 <b>Each bound slot arrives DESCRIBED.</b> A slot may hold a record of a family the client has
/// never heard of — a game's spellbook, its recipes — and the client holds no record schema and no copy
/// of a game's records. So the server says what to draw and what to call it, and the client draws boxes.
/// The alternative is shipping a game's whole catalogue to every client to render four icons.</para>
///
/// <para><see cref="Slots"/> travels with the bindings rather than on a declaration packet of its own:
/// the width is wanted at exactly the moment the contents are, and one packet cannot arrive out of order
/// with itself.</para>
/// </summary>
public sealed record PlayerHotkeysPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerHotkeys;

    /// <summary>How many slots this game declared. Zero draws no bar at all.</summary>
    [JsonPropertyName("slots")] public int Slots { get; init; }

    /// <summary>The bar, 1-based slots flattened to a 0-based wire array. An empty slot is
    /// <see cref="HotkeyKind.None"/>.</summary>
    [JsonPropertyName("bound")] public IReadOnlyList<Slot> Bound { get; init; } = [];

    /// <summary>One slot as the client needs it: what it points at, and everything needed to draw it.</summary>
    /// <param name="Kind">The <see cref="HotkeyKind"/> as a byte.</param>
    /// <param name="Id">The action id, or the record family id.</param>
    /// <param name="Num">The record number, or a verb's subject number, or zero.</param>
    /// <param name="Caption">What to call it, already in the words the player reads.</param>
    /// <param name="Icon">The glyph to draw, from <see cref="Extensibility.GameIcon.Offered"/>.</param>
    /// <param name="Sprite">A picture from the item sheet instead of the glyph, or zero. Core fills this
    /// for its own item family; a game's records have no art the client holds.</param>
    public readonly record struct Slot(
        [property: JsonPropertyName("k")] byte Kind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("n")] short Num,
        [property: JsonPropertyName("c")] string Caption,
        [property: JsonPropertyName("i")] string Icon,
        [property: JsonPropertyName("s")] int Sprite);
}

/// <summary>C→S: bind or clear one action-bar slot. Kind <see cref="HotkeyKind.None"/> clears it.
/// The server validates and echoes <see cref="PlayerHotkeysPacket"/>; it never trusts this to be
/// in-range or to name something the game declared as bindable.</summary>
public sealed record SetHotkeyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetHotkey;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("kind")] public byte Kind { get; init; }
    /// <summary>The action id for a verb, or the family id for a record.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("num")] public short Num { get; init; }
}

/// <summary>
/// C→S: fire one action-bar slot.
///
/// <para>🔴 <b>The slot, and not what it holds.</b> The server reads its own copy of the bar, so a client
/// cannot fire something it never bound — and everything about what a slot MEANS stays on the server,
/// where a game's rules are. The square is carried because a verb may act on a place: it is the one the
/// player is facing, exactly as a verb bound to a key acts on it.</para>
/// </summary>
public sealed record UseHotkeyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UseHotkey;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
}

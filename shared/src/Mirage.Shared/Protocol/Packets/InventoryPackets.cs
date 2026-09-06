using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record UseItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UseItem;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>C→S: tidy the sender's own inventory into the canonical sort order (see
/// <c>ItemSystem.SortInventory</c>). No payload — the server reorders the caller's bag, remaps the
/// equipped-slot indices, and resyncs.</summary>
public sealed record SortInventoryPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SortInventory;
}

public sealed record MapGetItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapGetItem;
}

/// <summary>C→S: pick up ONE named map item from the tile menu, possibly from a few tiles away.
///
/// <para>Identified by its stable per-map <see cref="Slot"/> rather than by position, so what gets
/// taken is what was clicked even if the pile shifted while the menu was open.</para>
///
/// <para>The server re-validates reach (r=5, and the two planes must connect) — the menu decides what
/// to OFFER, never what is allowed.</para></summary>
public sealed record MapPickUpPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapPickUp;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>C→S: pick up everything on one tile the sender can claim — the tile menu's "Pick Up All".
///
/// <para>A tile rather than a list of slots, because the set is decided server-side at the moment of
/// the request: anything that dropped, expired or was taken since the menu opened is accounted for
/// without the client having to be right about it.</para></summary>
public sealed record MapPickUpAllPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapPickUpAll;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("layer")] public WorldLayer Layer { get; init; }
}

public sealed record MapDropItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapDropItem;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: bulk drop, keyed by item id rather than a specific inventory slot. Server scans
/// inventory for matching non-currency slots (skipping equipped), clamps to the map's
/// player-dropped clutter cap, and drops up to <c>Quantity</c> items. Quantity = 0 means "as many as
/// fit". Currency uses the per-slot <see cref="MapDropItemPacket"/>.</summary>
public sealed record MapDropBulkPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapDropBulk;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record SendInventoryPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendInventory;
    [JsonPropertyName("slots")] public InvSlotData[] Slots { get; init; } = [];

    public sealed record InvSlotData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("dur")] int Dur
    );
}

public sealed record InventoryUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.InventoryUpdate;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("dur")] public int Dur { get; init; }
}

public sealed record EquippedGearPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EquippedGear;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("armor")] public int Armor { get; init; }
    [JsonPropertyName("weapon")] public int Weapon { get; init; }
    [JsonPropertyName("helmet")] public int Helmet { get; init; }
    [JsonPropertyName("shield")] public int Shield { get; init; }
}

public sealed record MapItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapItems;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("items")] public MapItemData[] Items { get; init; } = [];

    public sealed record MapItemData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("dur")] int Dur,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        // Lets the client count PlayerDropped items so the inventory Drop button can disable when
        // the voluntary clutter cap is reached without also locking out drops just because NPC loot
        // happens to be on the ground. Default 0 = TileDefined for backward-compatible removals
        // (Num=0 sentinel packets don't carry meaningful Source).
        [property: JsonPropertyName("src")] ItemSource Source = ItemSource.TileDefined,
        // Two-layer world: the logical layer the drop sits on (Ground omitted on the wire). Removal sentinels
        // (Num=0) leave it default — the client removes by slot, so the layer is irrelevant there.
        [property: JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground,
        // Who holds the loot claim (1-based player index; 0 = nobody), and how long it has left in
        // MILLISECONDS FROM RECEIPT rather than as a server timestamp — TickCount64 is meaningless on
        // another machine's clock, and the client only needs to know when to stop calling it somebody's.
        //
        // Sent so the tile menu can group a pile into "your loot" and grey out what belongs to
        // somebody else. It is not authoritative for anything: the server re-checks the claim on every
        // pick-up, so a client that ignores this gains nothing but a refusal.
        [property: JsonPropertyName("tag"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TaggedTo = 0,
        [property: JsonPropertyName("tagMs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TagMsLeft = 0
    )
    {
        /// <summary>The wire form of a live map item, tag included.
        ///
        /// <para>One constructor for every send site — the spawn broadcast, the re-broadcast when a
        /// loot tag is stamped, and the full list a joining player receives — because the tag was
        /// added to the packet once and promptly left off two of the three, which is precisely the
        /// class of bug that makes an item look unclaimed to whoever walks in late.</para></summary>
        public static MapItemData From(MapItemRecord mi, long nowMs)
        {
            // Milliseconds remaining rather than the server's TickCount64: the client cannot read
            // another machine's clock, and an expired tag is simply absent.
            int msLeft = mi.TaggedToPlayer > 0 && mi.TagExpiresAt > nowMs
                ? (int)Math.Min(int.MaxValue, mi.TagExpiresAt - nowMs)
                : 0;
            return new MapItemData(mi.Slot, mi.Num, mi.Quantity, mi.Dur, mi.X, mi.Y, mi.Source, mi.Layer,
                                   msLeft > 0 ? mi.TaggedToPlayer : 0, msLeft);
        }
    }
}

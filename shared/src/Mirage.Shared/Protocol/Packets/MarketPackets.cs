using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the current marketplace listings, sent when a player opens the market from an inn and
/// after any change. <see cref="Open"/> doubles as the "open the market panel" signal. <see cref="MeLogin"/>
/// is the viewer's own account login, so the client can pick out its own listings without a separate feed.</summary>
public sealed record MarketListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketList;
    [JsonPropertyName("listings")] public List<Entry> Listings { get; init; } = new();
    [JsonPropertyName("sales")] public List<MarketSale> MySales { get; init; } = new();
    [JsonPropertyName("me")] public string MeLogin { get; init; } = "";
    [JsonPropertyName("open")] public bool Open { get; init; }
    [JsonPropertyName("now")] public long NowUtc { get; init; }   // server UTC-seconds, so the client can render each listing's time-left

    /// <summary>One listing as a browser sees it.
    ///
    /// <para>🔴 Its own shape rather than <see cref="MarketListing"/> because the two want opposite things
    /// from the id. On disk the FILENAME owns it and a copy inside the file would be a second source of
    /// truth able to disagree with the name it is stored under, so the record marks it
    /// <c>[JsonIgnore]</c>. Here it is the only handle there is: a buyer names a listing back to the
    /// server by this number, and one that arrives without it can be neither bought nor canceled — the
    /// server is handed 0, finds no listing 0, and answers that the listing is gone.</para>
    ///
    /// <para>Everything else is a straight copy. <see cref="From"/> is the one place that mapping is
    /// made.</para></summary>
    public sealed record Entry
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("seller")] public string Seller { get; init; } = "";
        [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
        [JsonPropertyName("quantity")] public int Quantity { get; init; }
        [JsonPropertyName("dur")] public int Dur { get; init; }
        [JsonPropertyName("price")] public int Price { get; init; }
        [JsonPropertyName("listedUtc")] public long ListedUtc { get; init; }

        public static Entry From(MarketListing listing) => new()
        {
            Id = listing.Id,
            Seller = listing.Seller,
            ItemNum = listing.ItemNum,
            Quantity = listing.Quantity,
            Dur = listing.Dur,
            Price = listing.Price,
            ListedUtc = listing.ListedUtc,
        };
    }
}

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: open + browse the marketplace (server-validated to be at an inn); the server replies with
/// a <see cref="MarketListPacket"/> carrying the open signal.</summary>
public sealed record MarketOpenPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketOpen;
}

/// <summary>C→S: list an inventory stack for sale at a fixed gold price. <see cref="Quantity"/> applies only
/// to a currency slot (a partial take); a non-currency slot is listed whole.</summary>
public sealed record MarketCreatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketCreate;
    [JsonPropertyName("slot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("price")] public int Price { get; init; }
}

/// <summary>C→S: buy a listing by id. The buyer is charged the price in gold; the goods and the (post-tax)
/// payout are delivered as delayed marketplace mail. <see cref="Quantity"/> buys only that many units of a
/// CURRENCY listing (a partial buy); 0 (or a non-currency listing) buys the whole stack.</summary>
public sealed record MarketBuyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketBuy;
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: cancel your own listing by id; the escrowed item is returned to you.</summary>
public sealed record MarketCancelPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketCancel;
    [JsonPropertyName("id")] public int Id { get; init; }
}

/// <summary>C→S: re-fetch the current listings on demand (the "Refresh" button) without a close-reopen. The
/// server replies with a <see cref="MarketListPacket"/> (no open toggle).</summary>
public sealed record MarketRefreshPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketRefresh;
}

/// <summary>C→S: the market panel closed — stop sending this player live listing broadcasts.</summary>
public sealed record MarketClosePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarketClose;
}

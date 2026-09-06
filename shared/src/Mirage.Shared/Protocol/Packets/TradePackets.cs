using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: invite a player to a direct trade by character name (must be online + within range).</summary>
public sealed record TradeInvitePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeInvite;
    [JsonPropertyName("to")] public string Target { get; init; } = "";
}

/// <summary>C→S: accept or decline a pending trade request.</summary>
public sealed record TradeRespondPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeRespond;
    [JsonPropertyName("accept")] public bool Accept { get; init; }
}

/// <summary>C→S: stage an inventory item into my trade offer (escrowed off me). Quantity applies to a currency
/// slot (a partial take); a non-currency slot is staged whole.</summary>
public sealed record TradeOfferAddPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeOfferAdd;
    [JsonPropertyName("slot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: pull a staged item back out of my trade offer (returned to my inventory), by offer index.</summary>
public sealed record TradeOfferRemovePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeOfferRemove;
    [JsonPropertyName("index")] public int Index { get; init; }
}

/// <summary>C→S: set my confirm flag. When BOTH parties are confirmed the swap executes atomically.</summary>
public sealed record TradeConfirmPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeConfirm;
    [JsonPropertyName("confirmed")] public bool Confirmed { get; init; }
}

/// <summary>C→S: cancel the trade (returns both sides' staged items).</summary>
public sealed record TradeCancelPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeCancel;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: someone invited me to a trade — show the accept/decline prompt.</summary>
public sealed record TradeInviteNotifyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeInviteNotify;
    [JsonPropertyName("from")] public string FromName { get; init; } = "";
}

/// <summary>S→C: the live trade window state — my staged offer, my partner's, and both confirm flags.
/// <see cref="Open"/> true keeps the window shown; false closes it (trade completed or canceled). Any change
/// to either offer clears both confirm flags.</summary>
public sealed record TradeWindowPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeWindow;
    [JsonPropertyName("partner")] public string PartnerName { get; init; } = "";
    [JsonPropertyName("mine")] public List<PlayerInvSlot> MyOffer { get; init; } = new();
    [JsonPropertyName("theirs")] public List<PlayerInvSlot> TheirOffer { get; init; } = new();
    [JsonPropertyName("myok")] public bool MyConfirmed { get; init; }
    [JsonPropertyName("theirok")] public bool TheirConfirmed { get; init; }
    [JsonPropertyName("open")] public bool Open { get; init; }
}

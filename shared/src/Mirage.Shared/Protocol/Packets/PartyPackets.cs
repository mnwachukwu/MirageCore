using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record PartyRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Party;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record JoinPartyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.JoinParty;
    [JsonPropertyName("target")] public int Target { get; init; }
}

public sealed record LeavePartyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LeaveParty;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record PartyRequestNotifyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PartyRequest;
    [JsonPropertyName("from")] public string FromName { get; init; } = "";
    [JsonPropertyName("fromIdx")] public int FromIndex { get; init; }
}

/// <summary>
/// Snapshot of the local player's partner — pushed every tick while partnered. Empty
/// <see cref="Name"/> means "you have no partner; tear down the overlay" and is sent on
/// /join → /leave/disband.
///
/// <para><b>It carries the partner's BARS as fractions rather than their attributes.</b> A partner is
/// usually somebody you cannot see, and a client holds attributes only for bodies in its own
/// neighbourhood — so asking it to compute the rows would be asking it about a body it has never been
/// told about. The server reads them off the declared <c>OverheadBarSet</c> and sends what the row
/// actually draws.</para>
///
/// <para>One entry per declared bar, in declaration order, so the two ends line up by position without
/// naming a key on the wire. A game that declares none sends none.</para>
/// </summary>
public sealed record PartyPartnerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PartyPartner;

    /// <summary>How full each declared bar is for the partner, 0 to 1 — or negative for a bar this
    /// body has nothing to say about, which is how <c>OverheadBar.FractionIn</c> already says it.</summary>
    [JsonPropertyName("bars")] public IReadOnlyList<float> Bars { get; init; } = [];
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("marked")] public bool ShowAsMarked { get; init; }
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    // int.MaxValue = not in combat.  Otherwise milliseconds elapsed since the partner's
    // LastCombatMs at send time — converted to the receiver's clock in the handler.
    [JsonPropertyName("combatMs")] public int MsSinceCombat { get; init; }
}

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
/// Snapshot of the local player's partner — pushed to the partner whenever vitals, level, map
/// position, or combat state changes.  Empty <see cref="Name"/> means "you have no partner; tear
/// down the overlay" and is sent on /join → /leave/disband.
/// </summary>
public sealed record PartyVitalsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PartyVitals;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("hp")] public int Hp { get; init; }
    [JsonPropertyName("maxHp")] public int MaxHp { get; init; }
    [JsonPropertyName("mp")] public int Mp { get; init; }
    [JsonPropertyName("maxMp")] public int MaxMp { get; init; }
    [JsonPropertyName("sp")] public int Sp { get; init; }
    [JsonPropertyName("maxSp")] public int MaxSp { get; init; }
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("pk")] public bool ShowAsPk { get; init; }
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    // int.MaxValue = not in combat.  Otherwise milliseconds elapsed since the partner's
    // LastCombatMs at send time — converted to the receiver's clock in the handler.
    [JsonPropertyName("combatMs")] public int MsSinceCombat { get; init; }
}

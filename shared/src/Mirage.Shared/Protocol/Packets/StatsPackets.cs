using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>Stage-and-confirm training: the client stages point buys locally and, on Confirm,
/// sends the whole allocation as counts per stat.  Named fields (no stat-index contract), applied
/// atomically server-side.</summary>
public sealed record TrainStatsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TrainStats;
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
}

public sealed record GetStatsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GetStats;
}

public sealed record RequestLocationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestLocation;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record SendHpPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendHp;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("hp")] public int Hp { get; init; }
    [JsonPropertyName("maxHp")] public int MaxHp { get; init; }
    [JsonPropertyName("showFloat")] public bool ShowFloat { get; init; }
    [JsonPropertyName("isCrit")] public bool IsCrit { get; init; }
    /// <summary>Actual damage dealt (may exceed remaining HP on kill). 0 = derive from HP diff.</summary>
    [JsonPropertyName("dmg")] public int Damage { get; init; }
    // int.MaxValue = not in combat.  Otherwise ms elapsed since the player's CombatExpiresAt window
    // opened — the client converts to its own clock so a re-syncing observer (re-entering observable
    // range, joining the map, etc.) sees combat expire at the true server time, not 10s after re-entry.
    [JsonPropertyName("combatMs")] public int MsSinceCombat { get; init; } = int.MaxValue;
}

public sealed record SendMpPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMp;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("mp")] public int Mp { get; init; }
    [JsonPropertyName("maxMp")] public int MaxMp { get; init; }
    [JsonPropertyName("showFloat")] public bool ShowFloat { get; init; }
}

public sealed record SendSpPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendSp;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("sp")] public int Sp { get; init; }
    [JsonPropertyName("maxSp")] public int MaxSp { get; init; }
    [JsonPropertyName("showFloat")] public bool ShowFloat { get; init; }
}

public sealed record SendStatsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendStats;
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("points")] public int Points { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("exp")] public long Exp { get; init; }
}

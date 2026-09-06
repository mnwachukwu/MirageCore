using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: the local player asks to step one tile in <c>Dir</c>.</summary>
public sealed record PlayerMovePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerMove;
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("movement")] public MovementType Movement { get; init; }
}

/// <summary>C→S: turn to face <c>Dir</c> without moving.</summary>
public sealed record PlayerDirPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerDir;
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: a player's authoritative step, broadcast to everyone observing them.</summary>
public sealed record SendPlayerMovePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerMove;
    [JsonPropertyName("index")] public int Index { get; init; }
    // 0 for a same-map step; the destination map number when this move is a seamless SEAM CROSS, so an
    // observer animates the one-tile slide across the border (and re-homes the player) instead of snapping.
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("movement")] public MovementType Movement { get; init; }
    // Two-layer world: the mover's logical layer after this step (persists across a seam cross so the bridge
    // continues). Omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
}

/// <summary>S→C: an NPC's authoritative step.</summary>
public sealed record NpcMovePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcMove;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("movement")] public MovementType Movement { get; init; }
    // Two-layer world: the NPC's logical layer after this step (sticky / ramp-gated). Omitted when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
}

/// <summary>S→C: an NPC turned in place, with no movement to animate.</summary>
public sealed record NpcDirPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcDir;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
}

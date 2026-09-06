using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>S→C packets for map/world events not covered by the core set.</summary>

/// <summary>Server tells client which map it has been warped to; client checks revision and requests data if needed.</summary>
public sealed record CheckForMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.CheckForMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("revision")] public int Revision { get; init; }
    // Seamless-scroll grid cell (center = 1,1). Neighbor pre-load checks carry their
    // own cell; the center map change keeps the default and blocks input as before.
    [JsonPropertyName("col")] public int Col { get; init; } = 1;
    [JsonPropertyName("row")] public int Row { get; init; } = 1;
}

/// <summary>
/// Seamless border crossing: the player walked off a map edge into an already-loaded neighbor.
/// The client shifts its 3×3 grid one cell opposite <see cref="Dir"/> (preserving loaded maps and
/// entities — no flicker, no input block, no reload), re-centers on <see cref="MapNum"/>, and places
/// the player at (X,Y). Only the new edge row/column is fetched (cache-aware). Unlike a true warp,
/// this never sets GettingMap.
/// </summary>
public sealed record SeamlessCrossPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SeamlessCross;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    // Two-layer world: the logical layer the player is on after the cross (a bridge continues across the seam).
    // Omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
    // For the rare fallback where the neighbor wasn't preloaded: lets the client do a normal reload.
    [JsonPropertyName("revision")] public int Revision { get; init; }
}

/// <summary>Server broadcasts that a key-tile door has been opened or closed.</summary>
public sealed record MapKeyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapKey;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("open")] public bool Open { get; init; }
    // Two-layer world: which logical layer's door at (x,y) changed — a fringe-layer door on a bridge is
    // independent of the ground door beneath it. Omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
}

/// <summary>Server broadcasts that a map NPC slot has died (clients remove the sprite).</summary>
public sealed record NpcDeadPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcDead;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("dmg")] public int Damage { get; init; }
    [JsonPropertyName("crit")] public bool IsCrit { get; init; }
}

/// <summary>Server broadcasts that an AttackOnSight/Guard NPC acquired or dropped its target.</summary>
public sealed record NpcTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcTarget;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("hasTarget")] public bool HasTarget { get; init; }
}

/// <summary>Server broadcasts a player's new facing direction (no position change).</summary>
public sealed record SendPlayerDirPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendPlayerDir;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
}

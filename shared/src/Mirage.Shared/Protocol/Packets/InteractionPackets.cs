using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: the player clicked a tile.
///
/// <para>The click is the whole message; what it MEANS is the game's to decide. The engine's part is
/// saying which tile was clicked and which body the client believes was under the cursor, so a game
/// can act on a body rather than on a coordinate.</para></summary>
public sealed record SearchPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Search;

    // Map of the clicked tile (a center or neighbor map).  0 = the player's own map; the server
    // validates it's one the player can currently observe.
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }

    // Client's opportunistic selection proposal — what the player visually clicked, picked
    // from the rendered viewport via sprite-pixel hit test.  The server validates by
    // identity (not by tile) and sends ClearTargetPacket if the proposal is stale.
    // ProposedType mirrors ServerPlayer.TargetType: 0=player, 1=npc, 2=self, 3=traversal,
    // 255=none (empty tile click, or no entity under the click pixel).
    [JsonPropertyName("pType")] public byte ProposedType { get; init; } = 255;
    [JsonPropertyName("pId")] public int ProposedId { get; init; }
    [JsonPropertyName("pMap")] public int ProposedMap { get; init; }
}

/// <summary>C→S: the client's current selection left the viewport; clear it server-side.</summary>
public sealed record DropTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DropTarget;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: server-driven selection.
/// <c>TargetType</c> mirrors <c>ServerPlayer.TargetType</c>: 0=player, 1=npc, 2=self, 3=traversal.</summary>
public sealed record SetTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetTarget;
    [JsonPropertyName("targetType")] public byte TargetType { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("targetMap")] public int TargetMap { get; init; }
    [JsonPropertyName("spawnMap")] public int SpawnMap { get; init; }
    [JsonPropertyName("spawnSlot")] public int SpawnSlot { get; init; }
}

/// <summary>S→C: drop the client's locally proposed selection — sent when a <see cref="SearchPacket"/>
/// proposal fails server-side validation (entity gone, slot mismatch, not observable).
/// The client clears its visual selection marker and any local selection state.</summary>
public sealed record ClearTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ClearTarget;
}

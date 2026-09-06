using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record AttackPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Attack;
}

public sealed record CastPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Cast;
    [JsonPropertyName("spell")] public int Spell { get; init; }
    // Ctrl+Cast: a one-shot override that lands the spell on the caster without touching the
    // player's selected target.  Only Add (heal/buff) spells self-cast; Sub spells still reject.
    [JsonPropertyName("self")] public bool Self { get; init; }
}

public sealed record SearchPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Search;
    // Map of the clicked tile (a center or neighbor map).  0 = the player's own map
    // (back-compat); the server validates it's one the player can currently observe.
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }

    // Client's opportunistic target proposal — what the player visually clicked, picked
    // from the rendered viewport via sprite-pixel hit test.  The server validates by
    // identity (not by tile) and sends ClearTargetPacket if the proposal is stale.
    // ProposedType mirrors ServerPlayer.TargetType: 0=player, 1=npc, 2=self, 3=traversal,
    // 255=none (empty tile click, or no entity under the click pixel).
    [JsonPropertyName("pType")] public byte ProposedType { get; init; } = 255;
    [JsonPropertyName("pId")] public int ProposedId { get; init; }
    [JsonPropertyName("pMap")] public int ProposedMap { get; init; }
}

/// <summary>C→S: the client's current target left the viewport; clear it server-side.</summary>
public sealed record DropTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DropTarget;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record PlayerAttackPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerAttack;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("inCombat")] public bool InCombat { get; init; }
}

/// <summary>S→C: a player cast its spell. Beyond the animation flags, carries <see cref="SpellNum"/> and
/// the target identity (same shape as <see cref="SetTargetPacket"/>) so observers can spawn the typed
/// projectile FX and home it to the live target. TargetType: 0=player, 1=npc, 2=self, 3=traversal.</summary>
public sealed record PlayerCastPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerCast;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("inCombat")] public bool InCombat { get; init; }
    [JsonPropertyName("spell")] public int SpellNum { get; init; }
    [JsonPropertyName("targetType")] public byte TargetType { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("targetMap")] public int TargetMap { get; init; }
    [JsonPropertyName("spawnMap")] public int SpawnMap { get; init; }
    [JsonPropertyName("spawnSlot")] public int SpawnSlot { get; init; }
}

public sealed record NpcAttackPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcAttack;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
}

/// <summary>S→C: a player was killed at (MapNum, X, Y) facing Dir — sent to observers just BEFORE the victim
/// respawns/warps, so they can hold a delayed-death sprite in sync with a killing spell bolt.</summary>
public sealed record PlayerDeathPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerDeath;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
}

/// <summary>S→C: an NPC cast. NPC magic is always an implicit HP-drain bolt (no spell record), so there's no
/// spell number — the client renders it as the damage (red) projectile. Carries the target identity so
/// observers can home the bolt to the live target. TargetType: 0=player, 1=native npc, 3=traversal guest
/// (addressed by <see cref="SpawnMap"/>/<see cref="SpawnSlot"/> since a guest has no resolvable native slot).</summary>
public sealed record NpcCastPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcCast;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("targetType")] public byte TargetType { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("targetMap")] public int TargetMap { get; init; }
    [JsonPropertyName("spawnMap")] public int SpawnMap { get; init; }   // traversal-guest target identity (TargetType 3)
    [JsonPropertyName("spawnSlot")] public int SpawnSlot { get; init; }
}

public sealed record NpcDamagePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcDamage;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("damage")] public int Damage { get; init; }
    [JsonPropertyName("isCrit")] public bool IsCrit { get; init; }
}

/// <summary>S→C: a map's blood as a full list of pools (5 bytes each: x, y, size, amount, freshness).  Sent
/// whenever a deposit changed the map (broadcast, Reset=false) or to a client that just began observing the map
/// (snapshot, Reset=true).  It is a FULL-LIST REPLACE — the client swaps its pool list for this map, so a
/// merged-away pool simply drops out and there is no per-pool removal.  Pure decay is never sent; the client
/// replays it locally.</summary>
public sealed record BloodUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BloodUpdate;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("reset")] public bool Reset { get; init; }
    [JsonPropertyName("pools")] public byte[] Pools { get; init; } = [];

    /// <summary>Bytes per pool in <see cref="Pools"/>: x and y as little-endian 16-bit, then size, amount,
    /// freshness, layer. Both ends read this rather than a literal, so the stride cannot drift on one side
    /// and turn the whole list into noise.</summary>
    public const int BytesPerPool = 8;

    /// <summary>Reads the tile coordinate of the pool starting at <paramref name="offset"/>.</summary>
    public static (int X, int Y) PoolTileAt(byte[] pools, int offset) =>
        (pools[offset] | (pools[offset + 1] << 8),
         pools[offset + 2] | (pools[offset + 3] << 8));
}

/// <summary>Kind of no-damage combat outcome floated by <see cref="CombatTextPacket"/>. 0 = unset sentinel.</summary>
public enum CombatTextKind { None = 0, Block = 1, Dodge = 2, ZeroHit = 3, Miss = 4 }

/// <summary>Which vital a <see cref="CombatTextKind.ZeroHit"/> float labels (melee = Hp; Sub spells pick
/// by type). Values match the client VitalType ordering (Hp=0, Mp=1, Sp=2).</summary>
public enum CombatVital : byte { Hp = 0, Mp = 1, Sp = 2 }

/// <summary>S→C: a no-damage combat outcome (block, dodge, or a hit that mitigated to 0) — the client
/// floats localized text over the entity. Players and native slot NPCs are positioned by
/// <see cref="Index"/> (player index / NPC slot) via the client's own entity record; traversal-guest
/// NPCs have no slot (<see cref="Index"/> = 0) and are positioned by the (<see cref="X"/>,<see cref="Y"/>)
/// tile, mirroring <c>TraversalNpcPacket</c>. <see cref="Vital"/> is meaningful only for
/// <see cref="CombatTextKind.ZeroHit"/> (which vital the "0" labels).</summary>
public sealed record CombatTextPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.CombatText;
    [JsonPropertyName("isNpc")] public bool IsNpc { get; init; }
    [JsonPropertyName("index")] public int Index { get; init; }   // player index, or NPC slot (0 = traversal guest → use X/Y)
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }  // entity's map (may be a neighbor)
    [JsonPropertyName("x")] public int X { get; init; }       // traversal guests only (Index == 0)
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("kind")] public CombatTextKind Kind { get; init; }
    [JsonPropertyName("vital")] public CombatVital Vital { get; init; }  // ZeroHit only: which vital the "0" labels
}

/// <summary>S→C: server-driven target assignment (e.g. auto-target on melee hit).
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

/// <summary>S→C: drop the client's locally proposed target — sent when a SearchPacket
/// proposal fails server-side validation (entity gone, slot mismatch, not observable).
/// The client clears its visual target arrow and any local target state.</summary>
public sealed record ClearTargetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ClearTarget;
}

using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class SpawnSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private const int SpawnSearchAttempts = 100;

    public SpawnSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       IRandomSource? rng = null)
        : base(dispatcher, rng: rng)
    {
        _world = world;
        _pm = pm;
    }

    public void SpawnNpc(int mapNpcSlot, int mapNum)
    {
        if (mapNpcSlot <= 0 || mapNpcSlot > Constants.MaxMapNpcs) return;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;

        // Runtime post mapNpcSlot (1-based) reads dense entry [mapNpcSlot - 1]; posts past the authored list
        // are empty and spawn nothing.
        var entries = _world.Maps[mapNum].Npcs;
        if (mapNpcSlot > entries.Count) return;
        var entry = entries[mapNpcSlot - 1];
        int npcNum = entry.Npc;
        if (npcNum <= 0) return;

        // Territory war: a map whose territory has a live contest spawns no NPCs for the whole war
        // state (setup + contest + cooldown), guards excepted. The single spawn chokepoint, so respawn /
        // guest-return / bulk spawn are all covered; the contest-end resume clears the suppression before
        // re-spawning. Asked AFTER the slot resolves, because the answer depends on which NPC this is.
        if (_world.IsContestSuppressedNpc(mapNum, npcNum)) return;

        var mn = _world.MapNpcs[mapNum, mapNpcSlot];
        // A copy of this NPC is away chasing as a traversal guest — its slot is held.  Spawning now would
        // create a DUPLICATE native (a phantom blocker that lingers).  The chase-return path clears the
        // flag before respawning; every other caller must leave a reserved slot alone.
        if (mn.IsReservedSlot) return;
        var npcRec = _world.Npcs[npcNum];

        mn.Num = npcNum;
        mn.Target = 0;
        mn.JanitorTarget = 0;
        mn.NpcTargetSpawnMap = 0;
        mn.NpcTargetSpawnSlot = 0;
        mn.WasInCombat = false;
        mn.LastAttackSayTarget = 0;
        mn.LastAttackSayNpcTarget = 0;
        mn.LastReachedTargetMs = 0;
        mn.ChaseTargetKey = 0;       // fresh slot — drop any stale chase-stall tracking from the prior occupant
        mn.ResetChaseStall();
        mn.ClearDamageCredit();
        mn.Hp = _world.EffectiveNpcMaxHp(npcRec);
        mn.Mp = _world.EffectiveNpcMaxMp(npcRec);
        mn.Sp = _world.EffectiveNpcMaxSp(npcRec);
        mn.Dir = (Direction)Rng.Next(Constants.NumDirections);
        // Two-layer world: a PINNED entry spawns on its own authored plane (entry.PinLayer) — see the pin
        // branch below. A random one starts on the ground and may be moved up by the search. A guest
        // returning home reseeds through here.
        mn.Layer = WorldLayer.Ground;

        bool spawned = false;
        // Footprint size: a size-S NPC needs an SxS block of clear, walkable, on-map tiles at its anchor.
        int size = npcRec.EffectiveSize;
        var map = _world.Maps[mapNum];

        // Fixed placement: a slot pinned to a tile always spawns there, as long as the
        // authored tile is on-map + walkable. Occupancy is deliberately ignored so a passerby standing on the
        // post can't block it; an invalid (off-map / on-wall) authored tile falls through to the random search
        // below so the NPC still spawns somewhere rather than not at all.
        if (entry.HasPin
            && IsFootprintOnWalkableGround(mapNum, entry.PinX!.Value, entry.PinY!.Value, size, entry.PinLayer))
        {
            mn.X = entry.PinX.Value;
            mn.Y = entry.PinY.Value;
            mn.Layer = entry.PinLayer;   // spawn on the pinned plane (Ground, or up on the bridge Fringe)
            spawned = true;
        }

        // Has this map any deck a body could be put on — one joined to a ramp, wherever in the world that
        // ramp stands? Asked once, not per attempt.
        bool upstairs = _world.HasSpawnableFringe(mapNum);

        // Try random walkable tiles before falling back to a full scan
        for (int i = 0; !spawned && i < SpawnSearchAttempts; i++)
        {
            // A coin flip per attempt rather than a fixed share of spawns: a deck is a small part of a map
            // and most fringe anchors fail the surface test, so what actually reaches the upper plane comes
            // out proportional to how much of the map IS deck, with no ratio to pick.
            var layer = upstairs && Rng.Next(2) == 0 ? WorldLayer.Fringe : WorldLayer.Ground;
            // Clamp the random anchor so the whole footprint fits on the map (no edge-straddle at spawn).
            int x = Rng.Next(map.Width + 1 - size);
            int y = Rng.Next(map.Height + 1 - size);
            if (IsFootprintSpawnClear(mapNum, x, y, size, mapNpcSlot, layer))
            {
                mn.X = x;
                mn.Y = y;
                mn.Layer = layer;
                spawned = true;
                break;
            }
        }

        // Fallback: scan all tiles
        if (!spawned)
        {
            for (int y = 0; y <= map.Height - size && !spawned; y++)
            {
                for (int x = 0; x <= map.Width - size && !spawned; x++)
                {
                    if (IsFootprintSpawnClear(mapNum, x, y, size, mapNpcSlot))
                    {
                        mn.X = x;
                        mn.Y = y;
                        spawned = true;
                    }
                }
            }
        }

        if (spawned)
        {
            SendToMap(_world, mapNum, new NpcSpawnPacket
            {
                MapNum = mapNum,
                NpcSlot = mapNpcSlot,
                Num = mn.Num,
                X = mn.X,
                Y = mn.Y,
                Dir = mn.Dir,
                MaxHp = _world.EffectiveNpcMaxHp(npcRec),
                MaxMp = _world.EffectiveNpcMaxMp(npcRec),
                MaxSp = _world.EffectiveNpcMaxSp(npcRec),
                Layer = mn.Layer,
            });
        }
    }

    private bool IsTileOccupied(int mapNum, int x, int y, int excludeNpcSlot, WorldLayer layer)
    {
        // Footprint-aware via GameWorld.IsTileOccupiedByNpc (a big NPC's whole body counts), excluding the
        // spawning slot's own record by reference so a respawn never blocks itself. Layer-scoped: someone
        // standing under a bridge is not standing on it.
        if (_world.IsTileOccupiedByNpc(mapNum, x, y, _world.MapNpcs[mapNum, excludeNpcSlot], layer)) return true;
        // Players: iterate the pre-maintained observable-area set for this map (the players who can
        // see it, which includes everyone standing ON it) instead of the whole 1,000-slot roster.
        foreach (int i in _world.MapObservers[mapNum])
        {
            var p = _pm[i];
            if (p.IsPlaying && p.Char.Map == mapNum && p.Char.X == x && p.Char.Y == y && p.Char.Layer == layer) return true;
        }
        return false;
    }

    // True if the whole SxS footprint anchored (top-left) at (aX,aY) is on-map and every tile is Walkable —
    // the authoring-validity half of IsFootprintSpawnClear, WITHOUT the occupancy check. A fixed-placement
    // spawn uses this so a passerby standing on the post can't block it, while an off-map / on-wall authored
    // tile (a mistake) still fails and falls back to the random search.
    private bool IsFootprintOnWalkableGround(int mapNum, int aX, int aY, int size, WorldLayer layer = WorldLayer.Ground)
    {
        var map = _world.Maps[mapNum];
        if (aX < 0 || aY < 0 || aX + size > map.Width || aY + size > map.Height) return false;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                if (LayerLogic.AttrFor(_world.Maps[mapNum].Tile[aX + i, aY + j], layer).Type != TileType.Walkable) return false;
        }

        return true;
    }

    /// <summary>Every tile of the footprint is a deck joined to a ramp — see
    /// <see cref="GameWorld.IsFringeSpawnable"/>.
    ///
    /// <para>🔴 This is what keeps a random spawn INSIDE the railings. A deck is bounded by fringe Blocked
    /// tiles, but only along its own edge; past them the plane reads Walkable again, so a search that asked
    /// only "is this walkable up top" would drop bodies anywhere on the map, outside the barriers entirely
    /// and with no way down. The deck is the surface, so the deck is the rule.</para>
    ///
    /// <para>A PINNED entry is exempt: an author naming a tile and a plane has said what they meant.</para></summary>
    private bool IsFootprintOnDeck(int mapNum, int aX, int aY, int size)
    {
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                if (!_world.IsFringeSpawnable(mapNum, aX + i, aY + j)) return false;
        }

        return true;
    }

    // True if the whole SxS footprint anchored (top-left) at (aX,aY) is on-map, all Walkable on the given
    // plane, and free of players and other NPCs on it.  Used so a big NPC spawns fully on clear ground rather
    // than half-in-a-wall, straddling the map edge, or overlapping another body.
    private bool IsFootprintSpawnClear(int mapNum, int aX, int aY, int size, int excludeNpcSlot,
                                       WorldLayer layer = WorldLayer.Ground)
    {
        if (!IsFootprintOnWalkableGround(mapNum, aX, aY, size, layer)) return false;
        if (layer == WorldLayer.Fringe && !IsFootprintOnDeck(mapNum, aX, aY, size)) return false;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                if (IsTileOccupied(mapNum, aX + i, aY + j, excludeNpcSlot, layer)) return false;
        }

        return true;
    }
    public void SpawnMapNpcs(int mapNum)
    {
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            SpawnNpc(i, mapNum);
    }

    public void SpawnAllMapNpcs()
    {
        for (int i = 1; i <= _world.Limits.Maps; i++)
            SpawnMapNpcs(i);
    }

    /// <summary>Clear every live native NPC on a map and tell observers to remove them — the territory-war
    /// despawn. Mirrors the death-side slot cleanup (Num/Hp zeroed, SpawnWait stamped) but with no
    /// damage or FX; respawns then stay suppressed by <see cref="GameWorld.IsContestSuppressedNpc"/> for the
    /// war state, and the contest-end resume calls <see cref="SpawnMapNpcs"/> once suppression lifts. Reserved
    /// slots (a native away chasing as a guest) already read Num = 0, so they are left untouched.
    ///
    /// <para><paramref name="keepGuards"/> leaves <see cref="NpcBehavior.Guard"/> NPCs standing, and pairs
    /// with the same exemption in <see cref="GameWorld.IsContestSuppressedNpc"/>.</para></summary>
    public void DespawnMapNpcs(int mapNum, bool keepGuards)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var mn = _world.MapNpcs[mapNum, i];
            if (mn.Num <= 0) continue;   // already dead/empty (or a reserved guest home)
            if (keepGuards && _world.Npcs[mn.Num].Behavior == NpcBehavior.Guard) continue;
            mn.Num = 0;
            mn.Hp = 0;
            mn.SpawnWait = Environment.TickCount64;
            SendToMap(_world, mapNum,
                new NpcDeadPacket { MapNum = mapNum, NpcSlot = i, Damage = 0, IsCrit = false });
        }
    }

    /// <summary>Check each dead NPC slot; respawn once SpawnSecs has elapsed.  The caller only invokes
    /// this for OBSERVED maps, so neighbor-map NPCs respawn while you watch from across a seam — not just
    /// maps you physically stand on (which would leave a neighbor you cleared looking permanently empty
    /// until you stepped onto it).</summary>
    public void CheckNpcRespawn(int mapNum, long now)
    {
        var entries = _world.Maps[mapNum].Npcs;
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var mn = _world.MapNpcs[mapNum, i];
            if (mn.Num > 0) continue;  // still alive
            if (mn.IsReservedSlot) continue;  // NPC is away chasing across a border — slot is held

            if (i > entries.Count) continue;   // post past the authored list — nothing to respawn
            int npcNum = entries[i - 1].Npc;
            if (npcNum <= 0) continue;  // no NPC defined in this slot

            long spawnMs = _world.Npcs[npcNum].SpawnSecs * 1000L;
            if (now - mn.SpawnWait >= spawnMs)
                SpawnNpc(i, mapNum);
        }
    }
}

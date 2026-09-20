using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class SpawnSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly WorldQueries _queries;
    private readonly SelectionTracking _selection;
    private readonly IReadOnlyList<ILootPolicy> _loot;
    private readonly WorldEvents _events;
    private const int SpawnSearchAttempts = 100;

    public SpawnSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       ItemSystem items, IRandomSource? rng = null,
                       IReadOnlyList<ILootPolicy>? loot = null, WorldEvents? events = null)
        : base(dispatcher, rng: rng)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _loot = loot ?? [];
        _events = events ?? WorldEvents.None;
        _queries = new WorldQueries(world, pm);
        _selection = new SelectionTracking(pm);
    }

    public void SpawnNpc(int mapNpcSlot, int mapNum)
    {
        if (mapNpcSlot <= 0 || mapNpcSlot > Constants.MaxMapNpcs) return;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;

        // ⚠ The one chokepoint, so every route back onto a map answers to it: the respawn clock, a guest
        // coming home from a chase, and a game asking for a map's own bodies. An emptied map takes none of
        // them until it is refilled.
        if (_emptied.Contains(mapNum)) return;

        // Runtime post mapNpcSlot (1-based) reads dense entry [mapNpcSlot - 1]; posts past the authored list
        // are empty and spawn nothing.
        var entries = _world.Maps[mapNum].Npcs;
        if (mapNpcSlot > entries.Count) return;
        var entry = entries[mapNpcSlot - 1];
        int npcNum = entry.Npc;
        if (npcNum <= 0) return;

        var mn = _world.MapNpcs[mapNum, mapNpcSlot];
        // A copy of this NPC is away chasing as a traversal guest — its slot is held.  Spawning now would
        // create a DUPLICATE native (a phantom blocker that lingers).  The chase-return path clears the
        // flag before respawning; every other caller must leave a reserved slot alone.
        if (mn.IsReservedSlot) return;
        var npcRec = _world.Npcs[npcNum];

        mn.Num = npcNum;
        // A fresh copy of the template's game values, not a reference to them: one wolf taking damage
        // must not wound the species, and a respawn must not inherit the last copy's bookkeeping.
        mn.Attributes = npcRec.Attributes.Clone();
        mn.Target = 0;
        mn.JanitorTarget = 0;
        mn.NpcTargetSpawnMap = 0;
        mn.NpcTargetSpawnSlot = 0;
        mn.Roused = false;           // a fresh body was sent after nobody, whatever the last occupant was doing
        mn.LastSpokeTo = 0;
        mn.LastSpokeToNpc = 0;
        mn.LastReachedTargetMs = 0;
        mn.ChaseTargetKey = 0;       // fresh slot — drop any stale chase-stall tracking from the prior occupant
        mn.ResetChaseStall();
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
            // An authored facing, for anything that stays where it is put: a shopkeeper behind a counter
            // faces the counter. Only applied on the pin, because an NPC that reached its tile by the
            // random search is not standing anywhere its author chose a direction for.
            if (entry.PinDir is { } facing) mn.Dir = facing;
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
                Layer = mn.Layer,
            });

            // After the packet, so a game writing the body's numbers from here sends them to people who
            // already have a body to hang them on. The identity is the spawn post rather than the slot,
            // the identity every other seam names a creature by.
            var (spawnMap, spawnSlot) = mn.GetSpawnIdentity(mapNum, mapNpcSlot);
            _events.NpcSpawned(EntityHandle.ForNpc(spawnMap, spawnSlot));
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
    /// <para>🔴 Without it a random spawn escapes the railings. A deck is bounded by fringe Blocked
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

    // ── A creature stops ──────────────────────────────────────────────────────

    /// <summary>
    /// Takes a creature out of the world, puts what it was carrying on the tile it fell on, and starts
    /// its respawn clock.
    ///
    /// <para><b>The inverse of <see cref="SpawnNpc"/>, and here for the same reason.</b> A creature's
    /// life is a slot's life: it is the slot that is cleared, the slot that is broadcast, and the slot
    /// that comes back when its interval has run. A player's death is a warp and belongs to
    /// <c>DeathSystem</c>; this one is not.</para>
    ///
    /// <para><b>Core has no rule that ends a creature either.</b> Nothing here calls this — an engine
    /// with no game loaded has no hit points and nothing that could reach nothing. It is reached through
    /// <c>IWorld.Kill</c>, which is a game asking.</para>
    ///
    /// <para>Returns whether a body was actually there to kill, so a rule that pays out for a kill can
    /// tell one from a verb aimed at an empty square.</para>
    /// </summary>
    public bool KillNpc(EntityHandle npc, EntityHandle killer = default)
    {
        if (_queries.ResolveNpc(npc) is not { Record.Num: > 0 } found) return false;

        int mapNum = found.CurrentMap;
        var body = found.Record;
        var template = _world.Npcs[body.Num];

        // ⚠ Read BEFORE anything is cleared. Every one of these is about to stop being answerable, and
        // the drops land where the body was rather than where its slot is.
        int kind = body.Num;
        int x = body.X, y = body.Y;
        var layer = body.Layer;

        ShedLoot(npc, killer, kind, template, mapNum, x, y, layer);

        if (found.CurrentSlot > 0) ClearNative(mapNum, found.CurrentSlot, body);
        else ClearVisitor((TraversalNpcRecord)body);

        return true;
    }

    /// <summary>A creature on its home map: the slot is emptied, the clock stamped, and everybody
    /// watching told to take the sprite away.</summary>
    private void ClearNative(int mapNum, int slot, MapNpcRecord body)
    {
        body.Num = 0;
        body.Target = 0;
        body.NpcTargetSpawnMap = 0;
        body.NpcTargetSpawnSlot = 0;
        body.Roused = false;
        body.SpawnWait = Environment.TickCount64;

        SendToMap(_world, mapNum, new NpcDeadPacket { MapNum = mapNum, NpcSlot = slot });

        // Anybody locked onto it is locked onto a slot that is about to hold a different creature.
        _selection.ClearSelectionsOfNpcSlot(mapNum, slot);
    }

    /// <summary>And one killed away from home. The husk is left in the visiting list for the AI pass to
    /// drop — it looks for <c>Num</c> at nothing and does exactly this — while the home slot stops being
    /// reserved and starts counting, so the creature comes back where it belongs rather than where
    /// it died.</summary>
    private void ClearVisitor(TraversalNpcRecord guest)
    {
        SendToMap(_world, guest.CurrentMapNum,
            new NpcDespawnPacket { SpawnMapNum = guest.SpawnMapNum, SpawnSlot = guest.SpawnSlot });

        guest.Num = 0;
        guest.Target = 0;

        _selection.ClearSelectionsOfVisitor(guest.SpawnMapNum, guest.SpawnSlot);

        var home = _world.MapNpcs[guest.SpawnMapNum, guest.SpawnSlot];
        home.IsReservedSlot = false;
        home.Num = 0;
        home.SpawnWait = Environment.TickCount64;
    }

    /// <summary>
    /// What it was carrying, onto the tile it fell on.
    ///
    /// <para>🔴 <b>Every line rolls on its own</b>, so one death yields nothing, one thing, or several —
    /// so a table can say "almost always a little gold, sometimes a potion, very rarely the
    /// sword" in three lines. <see cref="NpcDrop"/> carries the reasoning.</para>
    ///
    /// <para>Each line is shown to every loot policy first, so a game can lift the rate, change the
    /// amount, or say whose it is before the roll happens. With no policy loaded the table lands exactly
    /// as it was authored, free to whoever reaches it.</para>
    ///
    /// <para>⚠ Everything lands on ONE tile rather than scattering. Several claimed stacks sharing a
    /// square are told apart by their claim rather than by where they sit, and a pile nobody can stand
    /// on top of to deny is already what pickup at range buys.</para>
    /// </summary>
    private void ShedLoot(EntityHandle npc, EntityHandle killer, int kind, NpcRecord template,
                          int mapNum, int x, int y, WorldLayer layer)
    {
        var table = template.Drops;
        if (table is null) return;

        for (int i = 0; i < table.Count; i++)
        {
            var line = table[i];
            if (!line.IsLive || line.ItemNum > _world.Limits.Items) continue;

            var spoil = new Spoil
            {
                Body = npc,
                Killer = killer,
                Kind = kind,
                ItemNum = line.ItemNum,
                Quantity = line.Quantity,
                ChancePercent = line.Chance,
            };

            foreach (var policy in _loot) policy.Weigh(spoil);

            if (spoil.ChancePercent <= 0 || Rng.Percent() >= spoil.ChancePercent) continue;

            // A stack of nothing is not a drop. Only a stacking item reads the count at all; everything
            // else lands as one however large this is, so the floor costs nothing there.
            int value = Math.Max(spoil.Quantity, 1);

            int slot = _items.SpawnItem(spoil.ItemNum, value, mapNum, x, y, ItemSource.NpcDropped, layer: layer);
            if (slot <= 0) continue;

            if (spoil.ClaimedBy.IsPlayer && spoil.ClaimSeconds > 0)
                _items.TagMapItem(mapNum, slot, spoil.ClaimedBy.PlayerIndex, spoil.ClaimSeconds * 1000L);
        }
    }

    /// <summary>Clear every live native NPC on a map and tell observers to remove them — the wholesale
    /// despawn a game reaches for when a map is being taken out of play. The same slot cleanup
    /// <see cref="KillNpc"/> does, with nothing dropped and nothing credited: each slot's clock starts,
    /// so they come back on their own intervals once whatever suppressed them lifts. Reserved slots (a
    /// native away chasing as a guest) already read Num = 0, so they are left untouched.</summary>
    public void DespawnMapNpcs(int mapNum)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var mn = _world.MapNpcs[mapNum, i];
            if (mn.Num <= 0) continue;   // already dead/empty (or a reserved guest home)
            mn.Num = 0;
            mn.SpawnWait = Environment.TickCount64;
            SendToMap(_world, mapNum, new NpcDeadPacket { MapNum = mapNum, NpcSlot = i });
        }
    }

    // ── A map kept clear of creatures ────────────────────────────────────────

    /// <summary>Maps a game is keeping clear. Runtime only: a server that stopped is not still holding
    /// anything, and a world comes back with its creatures where they belong.</summary>
    private readonly HashSet<int> _emptied = [];

    /// <summary>Whether a map is being kept clear of creatures.</summary>
    public bool IsEmptied(int mapNum) => _emptied.Contains(mapNum);

    /// <summary>
    /// Takes every creature off a map and keeps it that way.
    ///
    /// <para>What a game reaches for when a place has to stop being ordinary ground for a while: a war
    /// fought over it, a ritual nobody should be interrupted during, an arena cleared for a duel. The
    /// bodies go now and the slots stay empty — their clocks keep running, but nothing comes back until
    /// <see cref="Refill"/>, which separates this from clearing a map and watching it refill a
    /// minute later.</para>
    ///
    /// <para>False for a map that is not there, or one already emptied.</para>
    /// </summary>
    public bool Empty(int mapNum)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps || !_emptied.Add(mapNum)) return false;

        DespawnMapNpcs(mapNum);
        return true;
    }

    /// <summary>Lets a map hold creatures again, and spawns its own back onto it at once rather than
    /// leaving it bare until each slot's clock comes round. False for a map that was not emptied.</summary>
    public bool Refill(int mapNum)
    {
        if (!_emptied.Remove(mapNum)) return false;

        SpawnMapNpcs(mapNum);
        return true;
    }

    /// <summary>Check each dead NPC slot; respawn once SpawnSecs has elapsed.  The caller only invokes
    /// this for OBSERVED maps, so neighbor-map NPCs respawn while you watch from across a seam — not just
    /// maps you physically stand on (which would leave a neighbor you cleared looking permanently empty
    /// until you stepped onto it).
    ///
    /// <para>⚠ An emptied map respawns nothing. Without this the despawn is undone a minute later, one
    /// slot at a time, and the map a game emptied fills back up while it is still being fought over.</para></summary>
    public void CheckNpcRespawn(int mapNum, long now)
    {
        if (_emptied.Contains(mapNum)) return;

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

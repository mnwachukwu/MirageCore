using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Per-map NPC AI loop: notices bodies, advances pursuits and retreats, ticks regen, and
/// drives idle wander for every native NPC and visiting guest on the map. Also sweeps open doors
/// shut, which rides the same per-map tick.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly WorldEvents _events;
    private readonly PlayerManager _pm;
    private readonly MovementSystem _movement;
    private readonly SpawnSystem _spawn;
    private readonly ItemSystem _items;

    public NpcAiSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       MovementSystem movement, SpawnSystem spawn, ItemSystem items, IClock? clock = null,
                       IRandomSource? rng = null, WorldEvents? events = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _world = world;
        _events = events ?? WorldEvents.None;
        _pm = pm;
        _movement = movement;
        _spawn = spawn;
        _items = items;
        _occupancyCache = new byte[_world.Limits.Maps + 1][];
        _occupancyCacheTicks = new long[_world.Limits.Maps + 1];
        _queries = new WorldQueries(world, pm);
        _selection = new SelectionTracking(pm);
    }

    /// <summary>Reach, viewport scans and identity resolution — the geometry the brain reasons over.</summary>
    private readonly WorldQueries _queries;

    /// <summary>Keeps players' selections pointing at the right body as NPCs cross seams and
    /// despawn.</summary>
    private readonly SelectionTracking _selection;

    private const long DoorAutoCloseMs = 5_000;  // a door swings shut this long after it opens
    // NPC regen tick, matched to the player cadence so NPC recovery stays close to the player's
    // per-second rate.

    // How long an NPC holds a lock it cannot make progress on before letting go, and how long a
    // traversal guest with nobody at all wanders abroad before walking home.  One window for both:
    // each is "this NPC has had nothing to do for long enough", measured off the same
    // MapNpcRecord.LastReachedTargetMs stamp.
    private const long NpcUnreachedGiveUpMs = 10_000;

    // Timestamp of the current RunForAllMaps pass, so a guest CREATED mid-pass (a native crossing a
    // border) can stamp its LastAiTick and the destination map's RunTraversalAi won't act on it a
    // second time the same pass.  See the LastAiTick guard in RunTraversalAi.
    private long _aiNow;

    // Timestamp of the current pathing pass — set by BOTH the 500ms brain (RunForAllMaps) and the fast
    // legs (RunMovement), unlike _aiNow which is brain-only.  Keys the attack-slot occupancy memo below
    // so it refreshes every pass (~100ms) instead of going stale for a whole brain tick.
    private long _pathNow;

    // Per-pass memo of live-actor tile occupancy for the chase BFS attack-slot mask.  Every chaser
    // hunting one target queries the same ≤4 ring tiles; without the memo a 5-mob gang recomputes that
    // ring (an observer scan each) five times a pass.  Computed once per absolute tile per pass and
    // shared; a snapshot at pass start, same mild-staleness tradeoff as the occupancy bitmap below (the
    // live per-step CanNpcMove still refuses any real overlap).  Single game thread, so no lock.
    private readonly Dictionary<long, bool> _attackSlotMemo = new();
    private long _attackSlotMemoStamp = -1;

    // Whether the chase BFS PLANS around the live positions of other actors (players + NPCs), treating each as
    // a wall so a chaser pre-routes around them.  Default OFF: an NPC chases "blind" — it heads for the target's
    // tile using only STATIC geometry (walls / doors / npc-avoid) and resolves actor collisions
    // reactively (the per-step CanNpcMove still refuses to overlap an occupied tile, so it just bumps and
    // re-plans next tick).  That reads as an organic pursuit with no god's-eye foreknowledge of everyone's
    // authoritative position, so a mob doesn't "wait" for another NPC that has somewhere to be.  Set true to
    // plan around live actors instead: fewer bumps in a crowd, at the cost of that waiting.  Runtime-readonly,
    // not const, so toggling it never leaves unreachable-code warnings behind.
    private static readonly bool NpcChasePlansAroundLiveActors = false;

    // Per-(centered map, AI tick) occupancy bitmap cache for the pathing BFS.  Every chaser on the
    // same map shares the same 3×3 observable area, so we build the bitmap once per (map, tick) and
    // reuse it for every other BFS on that map for the rest of the tick.  Cuts the per-BFS bitmap
    // cost from a full player-roster scan to one array reference once warm — the biggest win at
    // high player counts, where the roster scan dominates everything else in the BFS.  Lazy-
    // allocated per map (3,456 bytes each — 2 layers x 1,728) so an unused map costs nothing.  Tick parity is
    // tracked by stamping the cached AI-now value; any value != _aiNow means rebuild on next access.
    // Sized in the constructor, not here: a field initializer cannot see _world, and the length has to
    // follow the operator's map count.
    private readonly byte[]?[] _occupancyCache;
    private readonly long[] _occupancyCacheTicks;

    // Per-pass shared BFS direction-field cache for the chase legs pass.  One target-rooted expansion
    // (FillPathField) solves the next-step for EVERY source tile toward that target, so a gang of N chasers
    // hunting one target shares ONE flood per pass instead of running N of them: the first chaser for a key
    // builds the 1,728-byte field, every other chaser reads its own from-tile in O(1).  Same per-_pathNow
    // lifetime as _attackSlotMemo (which the field bakes in) — cleared lazily at first use of each pass — so
    // it needs no map/door/tile-edit invalidation.  A null value caches "target not in the observable area".
    // Buffers are pooled + Array.Clear'd on rent (allocation-neutral over time, like _occupancyCache above).
    // Single game thread, so no lock.  See CachedStepTowardObservableArea / FillPathField.
    private readonly Dictionary<PathFieldKey, byte[]?> _pathFieldCache = new();
    private long _pathFieldStamp = -1;
    private readonly List<byte[]> _pathFieldBuffers = new();
    private int _pathFieldBuffersUsed;

    // Key for _pathFieldCache: everything that determines the BLIND (non-stalled, no live-occupancy) direction
    // field.  CenterMap fixes the BuildMapGrid frame (the same target local-coords differ per center map);
    // (TargetMap,ToX,ToY,TargetLayer) is the expansion root — the layer is part of the root because the field
    // spans BOTH source layers (2*N states) and a target on the ground vs the fringe surface roots a different
    // flood; Footprint (npc.EffectiveSize) drives walkability.  That is the complete set of inputs
    // FillPathField reads, so the key is exhaustive by construction: the chaser's own spawn map is not an
    // input to the flood at all, which is what lets a gang converging from different home maps share one
    // field (locked by NpcPathCacheTests).
    // The attack-slot ring is deliberately NOT keyed: it is frozen per _pathNow via _attackSlotMemo
    // and is chaser-independent, so it bakes into the field consistently.  selfSpawnMap/selfSpawnSlot are
    // omitted because they are read only in the stalled planAroundActors branch, which never uses this cache.
    // If a future per-chaser or per-destination rule is ever added inside the flood, it must be added here too.
    private readonly record struct PathFieldKey(
        int CenterMap, int TargetMap, int ToX, int ToY, int TargetLayer, int Footprint, int TargetFootprint);

    /// <summary>The 500ms NPC "brain" pass over every map: noticing, give-up, warp-follow, wander, and (on its
    /// own 5s cadence) regen. An observed map gets the full player-scanning AI; an unobserved one gets only
    /// pursuit upkeep and scavenging, since nothing there can notice anybody. Visiting guests tick on every map
    /// either way, so a pursuer can't be stranded by luring it somewhere nobody is watching.</summary>
    public void RunForAllMaps(long now)
    {
        _aiNow = now;
        _pathNow = now;

        for (int mapNum = 1; mapNum <= _world.Limits.Maps; mapNum++)
        {
            // Seamless world: run the full, player-scanning AI only on maps someone can SEE (it or a
            // neighbor of their map) — an unobserved map can't have a player in notice range, so its
            // native NPCs have nobody to notice and nothing to broadcast.
            if (_world.MapObservers[mapNum].Count > 0)
            {
                RunAiForMap(mapNum, now);
                _spawn.CheckNpcRespawn(mapNum, now);
                CheckDoorAutoClose(mapNum, now);
            }
            else
            {
                RunUnobservedPursuit(mapNum, now);
            }

            // Visiting guests are ticked on EVERY map, observed or not, so a player can't "stick" a
            // pursuer by luring it into space nobody is currently watching — it keeps pursuing (incl.
            // through warps) or returns home.  Free where there are no guests (empty list, no scan).
            RunTraversalAi(mapNum, now);
        }

    }

    /// <summary>Fast per-NPC MOVEMENT pass (GameLoop.NpcMoveTick, Constants.NpcMoveIntervalMs — finer than the 500ms brain).
    /// Executes the STEPS for NPCs holding a lock (native + guest, player + NPC), each on its own SPD step-clock, so
    /// one runs (SPD-scaled, capped just under player max) while it has stamina and walks once SP runs out.  A
    /// <see cref="NpcBehavior.Pursue"/> NPC steps toward, a <see cref="NpcBehavior.Flee"/> one steps away, and a
    /// <see cref="NpcBehavior.Shadow"/> one does whichever keeps its standoff.  Closing
    /// includes crossing a map seam toward a body on an adjacent map (the BFS routes to the border and converts a
    /// native into a guest), so an NPC keeps its run pace through a boundary.  Only the step lives here — the brain
    /// (<see cref="RunForAllMaps"/> @ 500ms) still does noticing, give-up AND warp-follow.  Cheap: observed maps
    /// only, movers only, and the BFS reuses the brain tick's occupancy snapshot.</summary>
    public void RunMovement(long now)
    {
        _pathNow = now;
        for (int mapNum = 1; mapNum <= _world.Limits.Maps; mapNum++)
        {
            if (_world.MapObservers[mapNum].Count == 0) continue;   // chasing only happens where someone watches
            for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
            {
                var mn = _world.MapNpcs[mapNum, slot];
                if (mn.Num <= 0) continue;
                if (mn.Target > 0) AdvanceNativeChaseStep(mapNum, slot, mn, now);
                else if (mn.NpcTargetSpawnSlot > 0) AdvanceNativeNpcChaseStep(mapNum, slot, mn, now);
            }
            // Guests iterate BACKWARD so a (rare, obstacle-detour) cross-border RemoveAt during a step can't
            // shift an unprocessed entry; the step-clock guards a crossed guest against a second step.
            var guests = _world.MapTraversalNpcs[mapNum];
            for (int i = guests.Count - 1; i >= 0; i--)
            {
                var t = guests[i];
                if (t.Num > 0 && (t.Target > 0 || t.NpcTargetSpawnSlot > 0))
                    AdvanceGuestChaseStep(mapNum, i, t, now);
            }
        }
    }

    /// <summary>True when this NPC's own tile is a <see cref="TileType.LayerRamp"/> (it is on a ramp, hence on the
    /// Fringe).  Read at its map-local (X,Y) — size-1 movers only stand on one tile; big NPCs never fit a ramp.</summary>
    private bool NpcStandsOnRamp(int mapNum, MapNpcRecord mn)
        => _world.Maps[mapNum].Contains(mn.X, mn.Y)
           && _world.Maps[mapNum]?.Tile[mn.X, mn.Y].FringeAttr is { Type: TileType.LayerRamp };

    /// <summary>A chasing NPC standing ON a ramp with its target on the SAME layer (up on the deck the ramp leads
    /// to) must NOT camp on the ramp to attack: a ramp is a 1-wide transit chokepoint, so holding there walls off
    /// any other chaser trying to climb behind it — they mask the occupied mount tile and freeze at the foot.
    /// Returning true makes the legs fall through to a STEP, so it moves off the ramp onto the deck to a proper
    /// attack slot, vacating the mount.  A CROSS-layer target (a ground entity at the ramp's foot) still holds —
    /// that is the intended "layer 1.5" foot reach, and stepping off would only descend away from it.</summary>
    private bool ChaserVacatesRampFor(int mapNum, MapNpcRecord mn, WorldLayer targetLayer)
        => targetLayer == mn.Layer && NpcStandsOnRamp(mapNum, mn);

    /// <summary>A pursuer is in reach of what it was chasing: hold position, stop sprinting, and tell the
    /// game — once per engagement, not once per tick it stays in reach.
    ///
    /// <para><b>Core has nothing to do next.</b> Arriving is the whole of what it knows how to do; an
    /// attack, a conversation, a battle screen and a mugging are all a game's answer to the same
    /// event.</para></summary>
    private void MakeContact(int mapNum, int slot, MapNpcRecord npc, EntityHandle target)
    {
        bool arriving = !npc.HasMadeContact;
        npc.HasMadeContact = true;
        npc.ChaseSprinting = false;

        if (!arriving) return;
        var (spawnMap, spawnSlot) = npc.GetSpawnIdentity(mapNum, slot);
        _events.Contact(EntityHandle.ForNpc(spawnMap, spawnSlot), target);
    }

    /// <summary>Legs-pass step for a native NPC holding a PLAYER, gated by the per-NPC step-clock.  A fleeing
    /// NPC retreats; a pursuing one holds position when already in reach (facing its target) and otherwise closes
    /// at run/walk pace — including across a map seam.  Runs while SP > 0 (draining it per tile), walks
    /// otherwise, so the sprint gasses out and the player pulls away.</summary>
    private void AdvanceNativeChaseStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        if (now < mn.NextMoveMs) return;                             // step-clock / magic-push not ready
        int target = mn.Target;
        if (!_pm[target].IsPlaying) return;                          // target gone — brain drops it next tick
        var vp = _pm[target].Char;
        if (_world.Npcs[mn.Num].Behavior == NpcBehavior.Flee)
        {
            TryLegsFlee(mapNum, slot, mn, vp.Map, vp.X, vp.Y, now);
            return;
        }
        if (_world.Npcs[mn.Num].Behavior == NpcBehavior.Shadow)
        {
            if (TryLegsShadow(mapNum, slot, mn, vp.Map, vp.X, vp.Y, vp.Layer, targetSize: 1, now))
                MakeContact(mapNum, slot, mn, EntityHandle.ForPlayer(target));
            return;
        }
        if (_queries.IsWithinReach(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, mn.Layer, vp.Map, vp.X, vp.Y, vp.Layer) && !ChaserVacatesRampFor(mapNum, mn, vp.Layer))
        {
            MakeContact(mapNum, slot, mn, EntityHandle.ForPlayer(target));
            FaceNpcToward(mapNum, slot, mn, vp.Map, vp.X, vp.Y);
            return;
        }  // in reach — orient toward it now (post-slide); end the sprint (walk-follow until it re-opens the gap). On a ramp with a same-layer body: fall through to step OFF (don't camp the 1-wide mount).
        // An off-map body (on an adjacent map) is handled here rather than by the brain: the legs pass runs the
        // same run/walk step toward its map, crossing the seam via StepNpcTowardObservableArea.

        var npc = _world.Npcs[mn.Num];
        int gap = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, vp.Map, vp.X, vp.Y, 1);
        if (gap == int.MaxValue) return;                            // target left the 3×3 observable area — the brain warp-follows, not the legs
        bool running = NpcWantsChaseRun(mn, npc, gap);
        int beforeX = mn.X, beforeY = mn.Y;
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        StepNpcTowardObservableArea(mapNum, slot, mn, vp.Map, vp.X, vp.Y, vp.Layer);
        FinishChaseStep(mn, npc.MoveSpeed, running, beforeX, beforeY, now);
    }

    /// <summary>Legs-pass step for a native NPC holding another NPC in its observable area.  Same run/walk-by-
    /// stamina rule; holds when already in reach and steps across a seam toward an off-map body.</summary>
    private void AdvanceNativeNpcChaseStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        if (now < mn.NextMoveMs) return;
        var resolved = _queries.ResolveNpc(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot);
        if (resolved is null) return;                               // victim gone — brain drops it
        var (victimMap, _, victimMn) = resolved.Value;
        if (_world.Npcs[mn.Num].Behavior == NpcBehavior.Flee)
        {
            TryLegsFlee(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, now);
            return;
        }
        if (_world.Npcs[mn.Num].Behavior == NpcBehavior.Shadow)
        {
            if (TryLegsShadow(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, victimMn.Layer,
                              _world.Npcs[victimMn.Num].EffectiveSize, now))
                MakeContact(mapNum, slot, mn, EntityHandle.ForNpc(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot));
            return;
        }
        if (_queries.IsWithinReach(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, mn.Layer, victimMap, victimMn.X, victimMn.Y, victimMn.Layer) && !ChaserVacatesRampFor(mapNum, mn, victimMn.Layer))
        {
            MakeContact(mapNum, slot, mn, EntityHandle.ForNpc(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot));
            FaceNpcToward(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y);
            return;
        }  // in reach — orient now; end the sprint (but vacate a ramp for a same-layer body)
        // An off-map body is handled here too — the legs pass runs the same run/walk step across the seam.

        var npc = _world.Npcs[mn.Num];
        int gap = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, victimMap, victimMn.X, victimMn.Y, _world.Npcs[victimMn.Num].EffectiveSize);
        if (gap == int.MaxValue) return;                            // victim left the 3×3 observable area — the brain drops it (NPC targets don't warp-follow)
        bool running = NpcWantsChaseRun(mn, npc, gap);
        int beforeX = mn.X, beforeY = mn.Y;
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        StepNpcTowardObservableArea(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, victimMn.Layer,
                                    targetSize: _world.Npcs[victimMn.Num].EffectiveSize);
        FinishChaseStep(mn, npc.MoveSpeed, running, beforeX, beforeY, now);
    }

    /// <summary>Legs-pass step for a traversal GUEST (player or NPC lock).  Steps toward or away at run/walk
    /// pace, including across a map seam — full parity with the native steppers.</summary>
    private void AdvanceGuestChaseStep(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        if (now < t.NextMoveMs) return;
        int targetMap, targetX, targetY, targetSize = 1;
        var targetLayer = WorldLayer.Ground;
        bool flees = _world.Npcs[t.Num].Behavior == NpcBehavior.Flee;
        bool shadows = _world.Npcs[t.Num].Behavior == NpcBehavior.Shadow;
        if (t.Target > 0)
        {
            if (!_pm[t.Target].IsPlaying) return;                   // target gone — brain drops it
            var vp = _pm[t.Target].Char;
            if (flees)
            {
                TryLegsFlee(mapNum, listIndex, t, vp.Map, vp.X, vp.Y, now);
                return;
            }
            if (shadows)
            {
                if (TryLegsShadow(mapNum, listIndex, t, vp.Map, vp.X, vp.Y, vp.Layer, targetSize: 1, now))
                    MakeContact(mapNum, 0, t, EntityHandle.ForPlayer(t.Target));
                return;
            }
            if (_queries.IsWithinReach(mapNum, t.X, t.Y, _world.Npcs[t.Num].EffectiveSize, t.Layer, vp.Map, vp.X, vp.Y, vp.Layer) && !ChaserVacatesRampFor(mapNum, t, vp.Layer))
            {
                MakeContact(mapNum, 0, t, EntityHandle.ForPlayer(t.Target));
                FaceNpcToward(mapNum, 0, t, vp.Map, vp.X, vp.Y);
                return;
            }  // in reach — orient now; end the sprint (but vacate a ramp for a same-layer body)
            targetMap = vp.Map;
            targetX = vp.X;
            targetY = vp.Y;
            targetLayer = vp.Layer;  // off-map targets are chased too — the legs cross the seam (parity with natives)
        }
        else
        {
            var resolved = _queries.ResolveNpc(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot);
            if (resolved is null) return;
            var (victimMap, _, victimMn) = resolved.Value;
            if (flees)
            {
                TryLegsFlee(mapNum, listIndex, t, victimMap, victimMn.X, victimMn.Y, now);
                return;
            }
            if (shadows)
            {
                if (TryLegsShadow(mapNum, listIndex, t, victimMap, victimMn.X, victimMn.Y, victimMn.Layer,
                                  _world.Npcs[victimMn.Num].EffectiveSize, now))
                    MakeContact(mapNum, 0, t, EntityHandle.ForNpc(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot));
                return;
            }
            if (_queries.IsWithinReach(mapNum, t.X, t.Y, _world.Npcs[t.Num].EffectiveSize, t.Layer, victimMap, victimMn.X, victimMn.Y, victimMn.Layer) && !ChaserVacatesRampFor(mapNum, t, victimMn.Layer))
            {
                MakeContact(mapNum, 0, t, EntityHandle.ForNpc(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot));
                FaceNpcToward(mapNum, 0, t, victimMap, victimMn.X, victimMn.Y);
                return;
            }  // in reach — orient now; end the sprint (but vacate a ramp for a same-layer body)
            targetMap = victimMap;
            targetX = victimMn.X;
            targetY = victimMn.Y;
            targetSize = _world.Npcs[victimMn.Num].EffectiveSize;
            targetLayer = victimMn.Layer;
        }

        var npc = _world.Npcs[t.Num];
        int gap = WorldDistanceTo(mapNum, t.X, t.Y, npc.EffectiveSize, targetMap, targetX, targetY, targetSize);
        if (gap == int.MaxValue) return;                            // target left the 3×3 observable area — the brain warp-follows/drops, not the legs
        bool running = NpcWantsChaseRun(t, npc, gap);
        int beforeX = t.X, beforeY = t.Y;
        t.MoveType = running ? MovementType.Running : MovementType.Walking;
        StepGuestTowardObservableArea(mapNum, listIndex, t, targetMap, targetX, targetY, targetLayer, targetSize: targetSize);
        FinishChaseStep(t, npc.MoveSpeed, running, beforeX, beforeY, now);
    }

    /// <summary>Shared tail for a legs chase-step: resets the one-shot run MoveType and advances the
    /// per-NPC step-clock by the pace just used. Advances even on a blocked tick so the legs don't re-BFS
    /// every 100ms.</summary>
    private static void FinishChaseStep(MapNpcRecord mn, int moveSpeed, bool running, int beforeX, int beforeY, long now)
    {
        mn.MoveType = MovementType.Walking;                         // reset — everything else steps at walk
        mn.NextMoveMs = now + (long)MathF.Round(running ? MovementFormulas.NpcRunMsPerTile(moveSpeed) : MovementFormulas.NpcWalkMsPerTile);
    }

    /// <summary>Run-vs-walk decision for a step that CLOSES a gap. A retreat does not consult this at
    /// all.
    ///
    /// <para>OPENING approach, before first contact: the NPC strolls in ONLY while stalking within
    /// <see cref="Constants.NpcApproachWalkMaxGap"/> tiles having lost the per-engagement charge roll
    /// (<see cref="MapNpcRecord.RushCommitted"/>); spotted farther, or opening past that ceiling, it
    /// RUSHES — the <see cref="MapNpcRecord.ChaseSprinting"/> latch holds the charge all the way in,
    /// where the in-reach early-return clears it into the hysteresis below.</para>
    ///
    /// <para>RE-CLOSE, after <see cref="MapNpcRecord.HasMadeContact"/>: a run/walk HYSTERESIS. It walks
    /// while close and sprints only once its target opens
    /// <see cref="Constants.NpcChaseSprintGapTiles"/>, holding the sprint until it regains reach — so it
    /// bursts stamina instead of gluing, and a running player can slip past.</para></summary>
    private static bool NpcWantsChaseRun(MapNpcRecord mn, NpcRecord npc, int gap)
    {
        if (!mn.HasMadeContact)
        {
            if (mn.RushCommitted || gap > Constants.NpcApproachWalkMaxGap)
            {
                mn.ChaseSprinting = true;
                return true;
            }
            return mn.ChaseSprinting;
        }
        if (gap >= Constants.NpcChaseSprintGapTiles) mn.ChaseSprinting = true;
        return mn.ChaseSprinting;
    }

    // Scratch list for the sweep below: the due doors are collected before any is shut, because closing
    // one removes the entry it was read from. Reused across maps and ticks.
    private readonly List<(int X, int Y, WorldLayer Layer)> _dueDoors = [];

    // Each open door ages out on ITS OWN stamp: opening a second door leaves the first one's window
    // untouched, and doors never all slam shut together.  Reads the map's open doors rather than its tiles,
    // so a map with no door standing open costs one count check however large it is.
    private void CheckDoorAutoClose(int mapNum, long now)
    {
        var temp = _world.TempTiles[mapNum];
        if (temp.OpenDoors.Count == 0) return;

        var map = _world.Maps[mapNum];
        _dueDoors.Clear();
        foreach (var ((x, y, layer), openedAt) in temp.OpenDoors)
        {
            if (now - openedAt < DoorAutoCloseMs) continue;

            // A tile the editor has since retyped away from Key keeps its stale stamp rather than
            // broadcasting a close for a door the client no longer draws.  Inert either way: every
            // door check is gated on TileType.Door first.
            if (!map.Contains(x, y) || LayerLogic.AttrFor(map.Tile[x, y], layer).Type != TileType.Door) continue;

            _dueDoors.Add((x, y, layer));
        }

        foreach (var (x, y, layer) in _dueDoors)
        {
            temp.CloseDoor(x, y, layer);
            SendToMap(_world, mapNum, new MapKeyPacket { MapNum = mapNum, X = x, Y = y, Open = false, Layer = layer });
        }
    }
}

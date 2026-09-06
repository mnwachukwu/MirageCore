using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Per-map NPC AI loop: acquires targets, advances chases and kites, casts, ticks regen,
/// and drives idle wander for every native NPC and visiting guest on the map. Also sweeps open
/// doors shut, which rides the same per-map tick.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly CombatSystem _combat;
    private readonly MovementSystem _movement;
    private readonly SpawnSystem _spawn;
    private readonly ItemSystem _items;
    private readonly BloodSystem _blood;

    public NpcAiSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       CombatSystem combat, MovementSystem movement, SpawnSystem spawn, ItemSystem items, BloodSystem blood,
                       IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _world = world;
        _pm = pm;
        _combat = combat;
        _movement = movement;
        _spawn = spawn;
        _items = items;
        _blood = blood;
        _occupancyCache = new byte[_world.Limits.Maps + 1][];
        _occupancyCacheTicks = new long[_world.Limits.Maps + 1];
    }

    // A badly wounded NPC/guest (<= BloodTrailHpThreshold of max HP) drips onto each fresh tile it moves to.
    private void NpcBloodTrail(int mapNum, int x, int y, int hp, int npcNum, WorldLayer layer)
    {
        if (hp <= _world.EffectiveNpcMaxHp(_world.Npcs[npcNum]) * Constants.BloodTrailHpThreshold)
            _blood.DepositTrail(mapNum, x, y, _world.Npcs[npcNum].EffectiveSize, layer);
    }

    private const long DoorAutoCloseMs = 5_000;  // a door swings shut this long after it opens
    // NPC regen tick — every 5 s, matching the player HP cadence on a normal map (combat
    // status still gates HP regen for both sides; only HP is combat-suppressed, MP/SP regen
    // during combat too).  Matched to the player cadence so NPC throughput stays close to the
    // player's per-second rate.
    private const long NpcHpRegenMs = 5_000;

    // AoS-only unreachable give-up window: an AttackOnSight NPC that goes this long without
    // taking ANY damaging action against its target (melee landed, chase step, or cast) drops
    // the lock and reverts to scan/wander.  Casts DO count — a high-Int AoS casting from afar
    // is still "engaging" — so this timer only fires when the NPC literally can't act on the
    // target (out-of-mana melee NPC pinned by impassable terrain, or an exhausted caster who
    // also can't path).  Protects against perpetual lock-on without penalizing ranged combat.
    private const long NpcAosUnreachableGiveUpMs = 10_000;

    private long _giveNpcHpTimer;

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
    // flood; Footprint (npc.EffectiveSize) and Behavior — which folds in ignoreNpcAvoid, i.e.
    // NpcIgnoresNpcAvoid(b) == (b == Guard) — drive walkability.  That is the complete set of inputs
    // FillPathField reads, so the key is exhaustive by construction: the chaser's own spawn map is not an
    // input to the flood at all, which is what lets a gang converging from different home maps share one
    // field (locked by NpcPathCacheTests).
    // The attack-slot ring is deliberately NOT keyed: it is frozen per _pathNow via _attackSlotMemo
    // and is chaser-independent, so it bakes into the field consistently.  selfSpawnMap/selfSpawnSlot are
    // omitted because they are read only in the stalled planAroundActors branch, which never uses this cache.
    // If a future per-chaser or per-destination rule is ever added inside the flood, it must be added here too.
    private readonly record struct PathFieldKey(
        int CenterMap, int TargetMap, int ToX, int ToY, int TargetLayer, int Footprint, int Behavior, int TargetFootprint);

    /// <summary>The 500ms NPC "brain" pass over every map: target acquisition, magic/kite decisions, attacks,
    /// give-up, warp-follow, wander, and (on its own 5s cadence) regen. An observed map gets the full
    /// player-scanning AI; an unobserved one gets only combat resolution and upkeep, since nothing there can
    /// acquire a target. Visiting guests tick on every map either way, so a chaser can't be stranded by luring
    /// it somewhere nobody is watching.</summary>
    public void RunForAllMaps(long now)
    {
        _aiNow = now;
        _pathNow = now;
        bool regenTick = now > _giveNpcHpTimer + NpcHpRegenMs;

        for (int mapNum = 1; mapNum <= _world.Limits.Maps; mapNum++)
        {
            // Seamless world: run the full, player-scanning AI only on maps someone can SEE (it or a
            // neighbor of their map) — an unobserved map can't have a player in target range, so its
            // native NPCs have nothing to acquire and nothing to broadcast.
            if (_world.MapObservers[mapNum].Count > 0)
            {
                RunAiForMap(mapNum, now, regenTick);
                _spawn.CheckNpcRespawn(mapNum, now);
                CheckDoorAutoClose(mapNum, now);
            }
            else
            {
                RunUnobservedCombat(mapNum, now);
                RunUnobservedUpkeep(mapNum);
            }

            // Visiting guests are ticked on EVERY map, observed or not, so a player can't "stick" a
            // chaser by luring it into space nobody is currently watching — it keeps pursuing (incl.
            // through warps) or returns home.  Free where there are no guests (empty list, no scan).
            RunTraversalAi(mapNum, now, regenTick);
        }

        if (regenTick)
            _giveNpcHpTimer = now;
    }

    /// <summary>Fast per-NPC MOVEMENT pass (GameLoop.NpcMoveTick, Constants.NpcMoveIntervalMs — finer than the 500ms brain).
    /// Executes chase-STEPS for target-holding NPCs (native + guest, player + NPC target) on each NPC's own SPD
    /// step-clock, so a chasing NPC runs (SPD-scaled, capped just under player max) while it has stamina and walks
    /// once SP runs out.  The step includes crossing a map seam toward a target on an adjacent map (the BFS routes
    /// to the border and converts a native into a guest), so an NPC keeps its run pace through a boundary.  Only the
    /// step lives here — the brain (<see cref="RunForAllMaps"/> @ 500ms) still does acquisition, magic/kite, attack,
    /// give-up AND warp-follow (a target that has left the 3×3 observable area).  Cheap: observed maps only, chasers
    /// only, and the BFS reuses the brain tick's occupancy snapshot (no roster-scan rebuild between brain ticks).</summary>
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

    /// <summary>Legs-pass chase-step for a native NPC with a PLAYER target.  Gated by the per-NPC step-clock
    /// (which also absorbs the magic-push, so a kiting caster is left alone).  Skips when adjacent (brain
    /// swings); otherwise steps toward the target at run/walk pace — including across a map seam when the
    /// target is on an adjacent map.  Runs while SP > 0 (draining it per tile), walks otherwise — so the
    /// sprint gasses out and the player pulls away.</summary>
    private void AdvanceNativeChaseStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        if (now < mn.NextMoveMs) return;                             // step-clock / magic-push not ready
        int target = mn.Target;
        if (!_pm[target].IsPlaying) return;                          // target gone — brain drops it next tick
        var vp = _pm[target].Char;
        if (mn.WantsKite)
        {
            TryLegsKite(mapNum, slot, mn, vp.Map, vp.X, vp.Y, now);
            return;
        }  // caster retreat (run pace)
        if (_combat.NpcInMeleeRangeOfPlayer(mapNum, mn, target) && !ChaserVacatesRampFor(mapNum, mn, vp.Layer))
        {
            mn.HasMadeContact = true;
            mn.ChaseSprinting = false;
            FaceNpcToward(mapNum, slot, mn, vp.Map, vp.X, vp.Y);
            return;
        }  // adjacent — orient toward the target now (post-slide), brain swings; end the sprint (walk-follow until it re-opens the gap). On a ramp with a same-layer target: fall through to step OFF (don't camp the 1-wide mount).
        // An off-map target (on an adjacent map) is handled here rather than by the brain: the legs pass runs the
        // same run/walk step toward the target's map, crossing the seam via StepNpcTowardObservableArea.
        // A caster only HOLDS at cast range for a SAME-map target; an off-map target is out of spell range, so it closes.
        if (vp.Map == mapNum && CasterHoldsAtCastRange(mapNum, mn, vp.X, vp.Y, vp.Layer))
        {
            FaceNpcToward(mapNum, slot, mn, vp.Map, vp.X, vp.Y);
            return;
        }  // in cast position — hold; brain casts

        var npc = _world.Npcs[mn.Num];
        int gap = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, vp.Map, vp.X, vp.Y, 1);
        if (gap == int.MaxValue) return;                            // target left the 3×3 observable area — the brain warp-follows, not the legs
        bool running = NpcCanRun(mapNum, mn) && NpcWantsChaseRun(mn, npc, gap);
        int beforeX = mn.X, beforeY = mn.Y, spBefore = mn.Sp;
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        if (running) mn.Sp = Math.Max(mn.Sp - NpcRunSpDrain(mapNum), 0);   // drain BEFORE the step so a seam cross carries the cost onto the new guest (parity with the guest stepper + kite path); FinishChaseStep refunds if blocked
        StepNpcTowardObservableArea(mapNum, slot, mn, vp.Map, vp.X, vp.Y, vp.Layer);
        FinishChaseStep(mn, npc.Spd, running, beforeX, beforeY, spBefore, now);
    }

    /// <summary>Legs-pass chase-step for a native NPC chasing another NPC in its observable area.  Same run/
    /// walk-by-stamina rule; skips adjacent victims (brain attacks) and steps across a seam toward an off-map victim.</summary>
    private void AdvanceNativeNpcChaseStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        if (now < mn.NextMoveMs) return;
        var resolved = _combat.ResolveNpcByIdentity(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot);
        if (resolved is null) return;                               // victim gone — brain drops it
        var (victimMap, _, victimMn) = resolved.Value;
        if (mn.WantsKite)
        {
            TryLegsKite(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, now, _world.Npcs[victimMn.Num].EffectiveSize);
            return;
        }  // caster retreat
        if (_combat.NpcInMeleeRangeOfNpc(mapNum, mn, victimMap, victimMn) && !ChaserVacatesRampFor(mapNum, mn, victimMn.Layer))
        {
            mn.HasMadeContact = true;
            mn.ChaseSprinting = false;
            FaceNpcToward(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y);
            return;
        }  // adjacent — orient now, brain attacks; end the sprint (but vacate a ramp for a same-layer victim)
        // An off-map victim is handled here too — the legs pass runs the same run/walk step across the seam.
        // A caster only HOLDS at cast range for a SAME-map victim; an off-map victim is out of spell range, so it closes.
        if (victimMap == mapNum && CasterHoldsAtCastRange(mapNum, mn, victimMn.X, victimMn.Y, victimMn.Layer, _world.Npcs[victimMn.Num].EffectiveSize))
        {
            FaceNpcToward(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y);
            return;
        }  // in cast position — hold; brain casts

        var npc = _world.Npcs[mn.Num];
        int gap = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, victimMap, victimMn.X, victimMn.Y, _world.Npcs[victimMn.Num].EffectiveSize);
        if (gap == int.MaxValue) return;                            // victim left the 3×3 observable area — the brain drops it (NPC targets don't warp-follow)
        bool running = NpcCanRun(mapNum, mn) && NpcWantsChaseRun(mn, npc, gap);
        int beforeX = mn.X, beforeY = mn.Y, spBefore = mn.Sp;
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        if (running) mn.Sp = Math.Max(mn.Sp - NpcRunSpDrain(mapNum), 0);   // drain BEFORE the step (seam-cross parity, see AdvanceNativeChaseStep); FinishChaseStep refunds if blocked
        StepNpcTowardObservableArea(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, victimMn.Layer,
                                    targetSize: _world.Npcs[victimMn.Num].EffectiveSize);
        FinishChaseStep(mn, npc.Spd, running, beforeX, beforeY, spBefore, now);
    }

    /// <summary>Legs-pass chase-step for a traversal GUEST (player or NPC target).  Steps toward the target at
    /// run/walk pace, including across a map seam (full parity with the native steppers).  Same run/walk-by-
    /// stamina rule.</summary>
    private void AdvanceGuestChaseStep(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        if (now < t.NextMoveMs) return;
        int targetMap, targetX, targetY, targetSize = 1;
        var targetLayer = WorldLayer.Ground;
        if (t.Target > 0)
        {
            if (!_pm[t.Target].IsPlaying) return;                   // target gone — brain drops it
            var vp = _pm[t.Target].Char;
            if (t.WantsKite)
            {
                TryLegsKite(mapNum, listIndex, t, vp.Map, vp.X, vp.Y, now);
                return;
            }  // caster retreat
            if (_combat.NpcInMeleeRangeOfPlayer(mapNum, t, t.Target) && !ChaserVacatesRampFor(mapNum, t, vp.Layer))
            {
                t.HasMadeContact = true;
                t.ChaseSprinting = false;
                FaceNpcToward(mapNum, 0, t, vp.Map, vp.X, vp.Y);
                return;
            }  // adjacent — orient now, brain attacks; end the sprint (but vacate a ramp for a same-layer target)
            targetMap = vp.Map;
            targetX = vp.X;
            targetY = vp.Y;
            targetLayer = vp.Layer;  // off-map targets are chased too — the legs cross the seam (parity with natives)
        }
        else
        {
            var resolved = _combat.ResolveNpcByIdentity(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot);
            if (resolved is null) return;
            var (victimMap, _, victimMn) = resolved.Value;
            if (t.WantsKite)
            {
                TryLegsKite(mapNum, listIndex, t, victimMap, victimMn.X, victimMn.Y, now, _world.Npcs[victimMn.Num].EffectiveSize);
                return;
            }  // caster retreat
            if (_combat.NpcInMeleeRangeOfNpc(mapNum, t, victimMap, victimMn) && !ChaserVacatesRampFor(mapNum, t, victimMn.Layer))
            {
                t.HasMadeContact = true;
                t.ChaseSprinting = false;
                FaceNpcToward(mapNum, 0, t, victimMap, victimMn.X, victimMn.Y);
                return;
            }  // adjacent — orient now, brain attacks; end the sprint (but vacate a ramp for a same-layer victim)
            targetMap = victimMap;
            targetX = victimMn.X;
            targetY = victimMn.Y;
            targetSize = _world.Npcs[victimMn.Num].EffectiveSize;
            targetLayer = victimMn.Layer;
        }

        // A caster only HOLDS at cast range for a SAME-map target; an off-map target is out of spell range, so it closes.
        if (targetMap == mapNum && CasterHoldsAtCastRange(mapNum, t, targetX, targetY, targetLayer, targetSize))
        {
            FaceNpcToward(mapNum, 0, t, targetMap, targetX, targetY);
            return;
        }  // in cast position — hold; brain casts

        var npc = _world.Npcs[t.Num];
        int gap = WorldDistanceTo(mapNum, t.X, t.Y, npc.EffectiveSize, targetMap, targetX, targetY, targetSize);
        if (gap == int.MaxValue) return;                            // target left the 3×3 observable area — the brain warp-follows/drops, not the legs
        bool running = NpcCanRun(mapNum, t) && NpcWantsChaseRun(t, npc, gap);
        int beforeX = t.X, beforeY = t.Y, spBefore = t.Sp;
        t.MoveType = running ? MovementType.Running : MovementType.Walking;
        if (running) t.Sp = Math.Max(t.Sp - NpcRunSpDrain(mapNum), 0);      // drain BEFORE the step (seam-cross parity, see AdvanceNativeChaseStep); FinishChaseStep refunds if blocked
        StepGuestTowardObservableArea(mapNum, listIndex, t, targetMap, targetX, targetY, targetLayer, targetSize: targetSize);
        FinishChaseStep(t, npc.Spd, running, beforeX, beforeY, spBefore, now);
    }

    /// <summary>Shared tail for a legs chase-step.  The run-SP drain is applied by the CALLER *before* the step
    /// (so a seam cross carries the cost onto the new guest, matching the guest stepper + the kite path); this
    /// REFUNDS it when the step didn't actually move (a blocked/facing tick — a stuck NPC must not bleed run SP),
    /// resets the one-shot run MoveType, and advances the per-NPC step-clock by the pace just used.  A cross
    /// (<c>mn.Num == 0</c> — the native converted to a guest, which owns the drained SP AND the new position)
    /// counts as MOVED.  Advances even on a blocked tick so the legs don't re-BFS every 100ms.</summary>
    private static void FinishChaseStep(MapNpcRecord mn, int spd, bool running, int beforeX, int beforeY, int spBefore, long now)
    {
        bool moved = mn.Num == 0 || mn.X != beforeX || mn.Y != beforeY;
        if (running && !moved) mn.Sp = spBefore;                    // blocked, no move → refund the speculative pre-step drain
        mn.MoveType = MovementType.Walking;                         // reset — everything else steps at walk
        mn.NextMoveMs = now + (long)MathF.Round(running ? MovementFormulas.NpcRunMsPerTile(spd) : MovementFormulas.NpcWalkMsPerTile);
    }

    // Per-run-tile SP drain, DOUBLED under Heat Wave — the NPC mirror of the player's Heat-Wave run-stamina tax
    // (MovementSystem) and of the ×2 block/crit/dodge SP cost NPCs already pay (CombatSystem.WeatherSpCostMult).
    private int NpcRunSpDrain(int map) =>
        Constants.NpcRunSpDrainPerTile * (_world.WeatherOn(map) == WeatherType.HeatWave ? Constants.WeatherHeatWaveSpCostMultiplier : 1);

    /// <summary>Whether an NPC may RUN right now: it needs stamina (SP > 0).  Every chase/kite run-vs-walk
    /// decision routes through this, so the reservoir rule is applied uniformly.</summary>
    private bool NpcCanRun(int mapNum, MapNpcRecord mn)
    {
        if (mn.Sp <= 0)
        {
            mn.RunReservoirLow = true;   // just drained — must rebuild a reservoir before sprinting again
            return false;
        }
        if (mn.RunReservoirLow)
        {
            // Rebuilding: keep walking until SP climbs back to the reservoir fraction, so the NPC commits to
            // one sustained run per reservoir instead of burning each regen trickle the instant it lands
            // (which flickered run/walk every regen tick and snapped the slide).
            int reservoir = Math.Max((int)(_world.EffectiveNpcMaxSp(_world.Npcs[mn.Num]) * Constants.NpcRunReservoirFraction), 1);
            if (mn.Sp < reservoir) return false;
            mn.RunReservoirLow = false;  // reservoir refilled — free to sprint again
        }
        return true;
    }

    /// <summary>Run-vs-walk decision for a CHASE step. SP gating is separate, in
    /// <see cref="NpcCanRun"/>, and kiting does not consult this at all — a caster opening distance is not
    /// closing a gap.
    ///
    /// <para>OPENING approach, before first contact: non-AoS always runs. An AoS mob strolls ONLY while
    /// stalking within <see cref="Constants.NpcApproachWalkMaxGap"/> tiles having lost the per-engagement
    /// charge roll (<see cref="MapNpcRecord.RushCommitted"/>); spotted farther, or opening past that
    /// ceiling, it RUSHES — the <see cref="MapNpcRecord.ChaseSprinting"/> latch holds the charge to melee,
    /// where the adjacency early-return clears it into the hysteresis below.</para>
    ///
    /// <para>RE-CLOSE, after <see cref="MapNpcRecord.HasMadeContact"/>: a run/walk HYSTERESIS. The mob
    /// walks while close and sprints only once the target opens
    /// <see cref="Constants.NpcChaseSprintGapTiles"/>, holding the sprint until it regains melee — so it
    /// bursts stamina instead of gluing, and a running player can slip past. EXCEPT guards, which stay
    /// sticky as a deterrent, and a spell-primary caster (Int > Str, with mana), which closes to spell
    /// range and holds there.</para></summary>
    private static bool NpcWantsChaseRun(MapNpcRecord mn, NpcRecord npc, int gap)
    {
        // Opening approach (before first contact): non-AoS runs in (provoked).  An AoS mob strolls in ONLY while
        // it's stalking a CLOSE target (gap within NpcApproachWalkMaxGap) and didn't win the charge roll; a target
        // spotted farther — or a stalked one that OPENS the gap past the stroll ceiling — is RUSHED, latching
        // ChaseSprinting so the charge holds to melee (the adjacency early-return then clears it into the hysteresis).
        if (!mn.HasMadeContact)
        {
            if (npc.Behavior != NpcBehavior.AttackOnSight) return true;
            if (mn.RushCommitted || gap > Constants.NpcApproachWalkMaxGap)
            {
                mn.ChaseSprinting = true;
                return true;
            }
            return mn.ChaseSprinting;
        }
        // A REAL (spell-primary) caster with mana keeps always-run-to-close: it closes to SPELL range and holds/
        // kites there, so the melee-adjacency latch doesn't fit it.  "Spell-primary" = Int > Str (under the combat
        // mirror it hits harder with spells than melee) — NOT merely Int > 0, so a STR bruiser with a splash of
        // INT (99 STR / 1 INT) is the melee chaser it is and DOES get the hysteresis.  An out-of-mana caster
        // (Mp < 1) also falls through to the melee hysteresis.
        if (npc.Int > npc.Str && mn.Mp >= 1) return true;
        // Guards stay STICKY (always-run re-close) — a deterrent that shouldn't be slippable.
        if (npc.Behavior == NpcBehavior.Guard) return true;
        // All other melee chasers (AoS, AttackWhenAttacked): run/walk HYSTERESIS.  Sprint once the target opens
        // the gap; keep sprinting until adjacent (the stepper's adjacency early-return clears the latch), else walk.
        if (gap >= Constants.NpcChaseSprintGapTiles) mn.ChaseSprinting = true;
        return mn.ChaseSprinting;
    }

    /// <summary>Whether an Int NPC can AFFORD a SubHp cast right now — the SINGLE source of truth for cast
    /// affordability, shared by the cast decision (<see cref="TryNpcMagicActionCore"/>'s hasMana) and the
    /// hold-at-cast-range check (<see cref="CasterHoldsAtCastRange"/>) so the two can never drift.  The cost is
    /// the player's trivial pool-fraction (<see cref="CombatFormulas.GetSubHpSpellMpCost"/> = round(maxMp/20)),
    /// which in-combat regen out-paces — so a caster normally sustains and only fails this gate when its pool is
    /// genuinely near zero (e.g. Snow cuts max MP).  When it does, BOTH gates must agree it can't cast so the legs
    /// close in for melee instead of holding at range and standing idle.  Int=0 NPCs never cast.</summary>
    private bool NpcCanAffordCast(MapNpcRecord mn, NpcRecord npc) =>
        npc.Int > 0 && mn.Mp >= CombatFormulas.GetSubHpSpellMpCost(_world.EffectiveNpcMaxMp(npc));

    /// <summary>Roll cast-vs-melee for a fresh weave commitment.  Base P(cast) = Int/(Int+Str) — a Str-dominant
    /// NPC mostly swings, an Int-dominant one mostly casts, and a pure caster (Str=0) always casts (melee is only
    /// its OOM last resort).  An INT-PRIMARY hybrid (Int>Str AND Str>0) additionally TAPERS P(cast) by its current
    /// mana fraction (Mp / max) so it leans on magic while its pool is deep and shifts toward melee as the pool
    /// drains — a soft ramp that sits on top of the hard OOM cutoff.  A Str>=Int mob keeps the flat ratio.</summary>
    private bool RollCastModality(MapNpcRecord mn, NpcRecord npc)
    {
        double pCast = (double)npc.Int / (npc.Int + npc.Str);        // Str==0 => 1.0 (pure caster always casts)
        if (npc.Str > 0 && npc.Int > npc.Str)                        // INT-primary hybrid: cast more at high mana, melee more at low
            pCast *= (double)mn.Mp / Math.Max(_world.EffectiveNpcMaxMp(npc), 1);
        return Rng.NextDouble() < pCast;
    }

    /// <summary>Legs-pass gate: true when a magic-capable NPC is ALREADY positioned to cast (in spell
    /// range, clear LoS, enough mana) and so must NOT take a chase step — the 500ms brain will cast or
    /// hold at range on its next tick.
    ///
    /// <para>Without it the fast movement pass sprints a caster forward in the window between a reactive
    /// target acquisition and the brain's first cast, visibly closing a gap it does not need to close.</para>
    ///
    /// <para>Mirrors the in-range hold in <see cref="TryNpcMagicActionCore"/> — same range, LoS and mana
    /// test plus the per-beat weave decision — so legs and brain agree. Out of mana, out of range,
    /// LoS-blocked or meleeing this beat returns false and falls through to the chase, so the NPC can
    /// still close to melee at 0 MP or reposition to regain range. Same-map only: callers invoke this
    /// after their own off-map early-return.</para></summary>
    private bool CasterHoldsAtCastRange(int mapNum, MapNpcRecord mn, int targetX, int targetY, WorldLayer targetLayer, int targetSize = 1)
    {
        var npc = _world.Npcs[mn.Num];
        if (!NpcCanAffordCast(mn, npc)) return false;   // not a caster, or can't AFFORD a cast — do NOT hold at range; fall through so the legs close in for melee (an out-of-mana caster must never stand idle)
        if (npc.Int <= npc.Str) return false;           // melee-primary/balanced (Str>=Int): fights AT melee, so it never holds at cast range — only an INT-primary kiter (Int>Str) does
        if (!mn.WeaveCastThisBeat) return false;        // this beat the weave chose melee — let the legs close in, don't hold at range
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var (tgtWX, tgtWY) = grid.CenterToWorld(targetX, targetY);
        // Footprint-aware: an oversize caster holds when its BODY is in cast range of the target's body.
        if (!WorldCoordHelper.IsInSpellRange(npcWX, npcWY, npc.EffectiveSize, tgtWX, tgtWY, targetSize)) return false;
        // Two-plane: only HOLD to cast if the caster could ACTUALLY cast — same layer, or ramp-bridged. Otherwise a
        // ground caster would camp at 2-D spell range against a target up on the fringe it can't hit (the cast is
        // layer-gated in TryNpcMagicActionCore), never closing to a ramp to follow it. Mirror the cast's exact gate:
        // LayerConnects + a ramp-blocks-the-line LoS for a cross-layer shot.
        if (!LayerLogic.LayerConnects(new ServerTileView(_world, grid), npcWX, npcWY, mn.Layer, tgtWX, tgtWY, targetLayer))
            return false;
        return WorldCoordHelper.HasClearSpellLineOfSight(npcWX, npcWY, tgtWX, tgtWY,
            new WorldLosPredicate(_world, grid, mn.Layer, blockRamps: mn.Layer != targetLayer));
    }

    /// <summary>Regen one NPC's HP/MP/SP for a regen tick (weather-scaled).  HP is combat-suppressed; MP/SP
    /// regen unconditionally (player parity).  Shared by native slot NPCs and traversal guests so both recover
    /// identically — a guest that drains MP casting refills it exactly like a native would at home.</summary>
    private void RegenNpcVitals(int mapNum, MapNpcRecord mn, NpcRecord npc, long now)
    {
        // Heat Wave / Snow halve regen magnitude; Snow also shrinks the max pools (Effective*).
        double regenMult = WeatherEffects.RegenMultiplier(_world.WeatherOn(mapNum));
        int maxHp = _world.EffectiveNpcMaxHp(npc);
        if (mn.Hp < maxHp && mn.Hp > 0
            && (mn.CombatExpiresAt == 0 || now >= mn.CombatExpiresAt))
        {
            mn.Hp = Math.Min(mn.Hp + StatFormulas.GetNpcHpRegen(npc, regenMult), maxHp);
        }

        int maxMp = _world.EffectiveNpcMaxMp(npc);
        if (mn.Mp < maxMp && mn.Hp > 0)
            mn.Mp = Math.Min(mn.Mp + StatFormulas.GetNpcMpRegen(npc, regenMult), maxMp);
        int maxSp = _world.EffectiveNpcMaxSp(npc);
        if (mn.Sp < maxSp && mn.Hp > 0)
            mn.Sp = Math.Min(mn.Sp + StatFormulas.GetNpcSpRegen(npc, regenMult), maxSp);
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
            // door check is gated on TileType.Key first.
            if (!map.Contains(x, y) || LayerLogic.AttrFor(map.Tile[x, y], layer).Type != TileType.Key) continue;

            _dueDoors.Add((x, y, layer));
        }

        foreach (var (x, y, layer) in _dueDoors)
        {
            temp.CloseDoor(x, y, layer);
            SendToMap(_world, mapNum, new MapKeyPacket { MapNum = mapNum, X = x, Y = y, Open = false, Layer = layer });
        }
    }
}

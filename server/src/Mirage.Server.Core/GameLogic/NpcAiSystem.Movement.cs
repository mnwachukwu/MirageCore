using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Committing a decision to the world: the native and guest step primitives, facing,
/// and the bookkeeping that tracks whether a chase is actually closing distance or has stalled
/// against an obstacle.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Attempts a single planned step for a native (slot) NPC — either a within-map move or a
    // one-tile border cross (converting the NPC to a traversal guest).  Returns true on success;
    // false when the planned tile is blocked (transient mob/player on it, or a cross blocked by
    // missing link / occupied landing).  The caller re-plans on false.
    private bool TryNativeStep(int mapNum, int slot, MapNpcRecord mn, Direction dir)
    {
        var npc = _world.Npcs[mn.Num];
        int neighborMap = NeighborInDir(mapNum, dir);
        if (StepLeavesMap(_world.Maps[mapNum], _world.Maps[neighborMap > 0 ? neighborMap : mapNum],
                          mn.X, mn.Y, dir, out int destX, out int destY))
        {
            if (neighborMap > 0
                // Same ramp corridor + fit + layer gate as a within-map step (world-space, reads the neighbor's
                // tiles across the seam), then land at the RESOLVED layer — so a bridge at a seam obeys the same
                // ramp rules a within-map one does (no perpendicular/under mount at the seam).
                && _movement.NpcStepPassesRampGate(mapNum, mn, dir, out var crossLayer)
                && _movement.IsNpcFootprintLandingFree(neighborMap, destX, destY, npc.EffectiveSize, MovementSystem.NpcIgnoresNpcAvoid(npc.Behavior), mn, crossLayer))
            {
                NativeNpcCrossBorder(mapNum, slot, mn, neighborMap, destX, destY, dir, stepped: true, crossLayer);
                return true;
            }
            return false;
        }
        if (_movement.CanNpcMove(mapNum, slot, dir))
        {
            _movement.NpcMove(mapNum, slot, dir, mn.MoveType);
            return true;
        }
        return false;
    }

    // Guest equivalent of TryNativeStep.
    private bool TryGuestStep(int mapNum, int listIndex, TraversalNpcRecord t, Direction dir)
    {
        var npc = _world.Npcs[t.Num];
        int neighborMap = NeighborInDir(mapNum, dir);
        if (StepLeavesMap(_world.Maps[mapNum], _world.Maps[neighborMap > 0 ? neighborMap : mapNum],
                          t.X, t.Y, dir, out int destX, out int destY))
        {
            if (neighborMap > 0
                && _movement.NpcStepPassesRampGate(mapNum, t, dir, out var crossLayer)
                && _movement.IsNpcFootprintLandingFree(neighborMap, destX, destY, npc.EffectiveSize, MovementSystem.NpcIgnoresNpcAvoid(npc.Behavior), t, crossLayer))
            {
                MoveGuestToMap(mapNum, listIndex, t, neighborMap, destX, destY, dir, stepped: true, crossLayer);
                return true;
            }
            return false;
        }
        return TryApplyGuestStep(mapNum, t, dir);
    }

    // ── Native/guest movement abstraction ─────────────────────────────────────
    // The magic-action + kite DECISION logic is shared between native NPCs and traversal guests; these route the
    // two movement PRIMITIVES to the right implementation by record type, so one decision path drives both.
    // `slot` is the native slot for a native, or the guest's transient list index for a guest.

    /// <summary>Step the NPC one tile in <paramref name="dir"/> (within-map or across a seam), routing to the
    /// native or guest primitive. Returns true if it moved.</summary>
    private bool StepNpc(int mapNum, int slot, MapNpcRecord mn, Direction dir)
        => mn is TraversalNpcRecord t ? TryGuestStep(mapNum, slot, t, dir) : TryNativeStep(mapNum, slot, mn, dir);

    /// <summary>Set + broadcast the NPC's facing, routing to the native (NpcDir packet) or guest (traversal
    /// state) primitive.</summary>
    private void FaceNpc(int mapNum, int slot, MapNpcRecord mn, Direction dir)
    {
        if (mn is TraversalNpcRecord t)
        {
            t.Dir = dir;
            BroadcastTraversalState(t);
        }
        else
        {
            BroadcastNpcDir(mapNum, slot, dir);
        }
    }

    /// <summary>Turn the NPC to face its target (if not already facing it) via <see cref="FaceNpc"/>.</summary>
    private void FaceNpcToward(int mapNum, int slot, MapNpcRecord mn, int victimMap, int victimX, int victimY)
    {
        var dir = FaceTargetDir(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, victimMap, victimX, victimY, mn.Dir);
        if (mn.Dir != dir) FaceNpc(mapNum, slot, mn, dir);
    }

    // Native NPC: BFS the 3×3 observable area for the shortest walkable route to (targetMap,
    // targetX, targetY) and take the first step (within-map move OR border cross).  The plan runs on
    // static geometry plus the occupied attack-slot ring; other live actors are NOT walls unless this
    // chaser has stalled (see NpcChasePlansAroundLiveActors), so it bumps and re-plans rather than
    // pre-routing.  When BFS finds no walkable path (target sealed
    // off by walls, or NPC walled in), best-effort walk straight toward target on the world axes
    // — the NPC visibly closes distance until the impassable wall stops it, rather than standing
    // at spawn looking idle.  Once it's against the wall (both axes blocked), faces and waits.
    private void StepNpcTowardObservableArea(int mapNum, int slot, MapNpcRecord mn, int targetMap, int targetX, int targetY,
                                      WorldLayer targetLayer, Direction? precomputedStep = null, int targetSize = 1)
    {
        var npc = _world.Npcs[mn.Num];
        // precomputedStep lets the caller hand in a BFS result already computed elsewhere (e.g. the
        // unreachable-cast check in TryNpcMagicActionCore).  Skips a duplicate BFS when supplied.
        int preDistance = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, targetMap, targetX, targetY, targetSize);
        int beforeX = mn.X, beforeY = mn.Y;
        bool isChase = mn.Target > 0 || mn.NpcTargetSpawnSlot > 0;
        bool stalled = isChase && IsChaseStalled(mn);

        // Momentum: while actively chasing (not stalled, not handed a precomputed step), keep the last heading
        // if it still closes world-distance to the target and the tile ahead is clear.  A fresh BFS every fast-
        // pass tick flip-flops between the two equal shortest-path axes on an off-axis target — a frantic
        // staircase at run speed (and a flickering facing).  Continuing the heading gives a natural straight
        // sprint; the BFS below still supplies the initial heading, the turn once an axis aligns, and any route
        // around a wall (a blocked momentum step just falls through to it).  Gated to a SAME-LAYER target:
        // StepClosesWorldDistance is pure 2D, so a cross-layer target must go through the layer-aware BFS to
        // find a ramp; a momentum step that happens to cross a ramp just changes mn.Layer, and the mismatch
        // then disables momentum next tick (self-correcting).  CanNpcMoveFrom still forbids walking off a deck.
        if (isChase && !stalled && precomputedStep is null && mn.ChaseHasLastStep && mn.Layer == targetLayer
            && StepClosesWorldDistance(mapNum, mn.X, mn.Y, npc.EffectiveSize, mn.ChaseLastStepDir, targetMap, targetX, targetY)
            && TryNativeStep(mapNum, slot, mn, mn.ChaseLastStepDir))
        {
            MarkReachedIfClosedNative(mn, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            AfterChaseStep(mn, mapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }

        // When stalled, re-plan occupancy-aware (route AROUND a mid-path blocker); pass our own identity so
        // the plan still runs into — and holds behind — any actor that is chasing US (see the BFS helper).
        var (selfSpawnMap, selfSpawnSlot) = mn.GetSpawnIdentity(mapNum, slot);
        // Non-stalled, blind chase: read this pass's SHARED direction field (one flood per target for the whole
        // gang, then O(1) per chaser) — equivalent to the single-source BFS but deduplicated.  The stalled
        // re-plan (occupancy + pursuer identity) and the occupancy-aware global both stay on the un-cached
        // single-source BFS, hence the !stalled && !NpcChasePlansAroundLiveActors gate.
        var step = precomputedStep ?? (!stalled && !NpcChasePlansAroundLiveActors
            ? CachedStepTowardObservableArea(mapNum, mn.X, mn.Y, mn.Layer, targetMap, targetX, targetY, targetLayer, npc, targetSize)
            : FindStepTowardObservableArea(mapNum, mn.X, mn.Y, mn.Layer, targetMap, targetX, targetY, targetLayer, npc,
                                           planAroundActors: stalled, selfSpawnMap, selfSpawnSlot, targetSize));

        // Reachable target: follow the plan.  The BFS masks occupied attack-slots, so a lined-up trailer
        // already routes to an OPEN flank here rather than stalling behind the occupant it can't see.
        if (step.HasValue && TryNativeStep(mapNum, slot, mn, step.Value))
        {
            MarkReachedIfClosedNative(mn, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            if (isChase) AfterChaseStep(mn, mapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }

        // No usable planned step (blocked this tick, or the target is unreachable).  Best-effort toward
        // it.  Suppress ONLY the exact reversal of the last step, and ONLY when the target is unreachable
        // (no BFS path) AND we've stalled — the wall-pacing case, where mirroring a sealed moving target
        // would otherwise pace back and forth.  A REACHABLE target is never held: that is what stops a
        // chaser freezing on the wrong side of an idle player until it takes a step.
        Direction? avoid = step is null && stalled && mn.ChaseHasLastStep ? OppositeDir(mn.ChaseLastStepDir) : null;
        if (TryBestEffortWalkToward(mapNum, slot, mn, targetMap, targetX, targetY, out Direction facing, avoid))
        {
            MarkReachedIfClosedNative(mn, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            if (isChase) AfterChaseStep(mn, mapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }
        BroadcastNpcDir(mapNum, slot, facing);
        if (isChase) AfterChaseStep(mn, mapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
    }

    // Guest twin of StepNpcTowardObservableArea; spawn map flows in from the traversal record.
    private void StepGuestTowardObservableArea(int mapNum, int listIndex, TraversalNpcRecord t, int targetMap, int targetX, int targetY,
                                        WorldLayer targetLayer, Direction? precomputedStep = null, int targetSize = 1)
    {
        var npc = _world.Npcs[t.Num];
        int preDistance = WorldDistanceTo(mapNum, t.X, t.Y, npc.EffectiveSize, targetMap, targetX, targetY, targetSize);
        int beforeX = t.X, beforeY = t.Y;
        bool isChase = t.Target > 0 || t.NpcTargetSpawnSlot > 0;
        bool stalled = isChase && IsChaseStalled(t);

        // Momentum — see StepNpcTowardObservableArea for the rationale (incl. the same-layer gate).
        if (isChase && !stalled && precomputedStep is null && t.ChaseHasLastStep && t.Layer == targetLayer
            && StepClosesWorldDistance(mapNum, t.X, t.Y, npc.EffectiveSize, t.ChaseLastStepDir, targetMap, targetX, targetY)
            && TryGuestStep(mapNum, listIndex, t, t.ChaseLastStepDir))
        {
            MarkReachedIfClosedGuest(t, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            AfterChaseStep(t, t.CurrentMapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }

        // Guest twin of the native stalled re-plan (see StepNpcTowardObservableArea): route around a
        // mid-path blocker while still holding behind our own pursuers, keyed on our spawn identity.
        var (selfSpawnMap, selfSpawnSlot) = t.GetSpawnIdentity(mapNum, 0);
        // See StepNpcTowardObservableArea: non-stalled blind chase shares the per-pass direction field.
        var step = precomputedStep ?? (!stalled && !NpcChasePlansAroundLiveActors
            ? CachedStepTowardObservableArea(mapNum, t.X, t.Y, t.Layer, targetMap, targetX, targetY, targetLayer, npc, targetSize)
            : FindStepTowardObservableArea(mapNum, t.X, t.Y, t.Layer, targetMap, targetX, targetY, targetLayer, npc,
                                           planAroundActors: stalled, selfSpawnMap, selfSpawnSlot, targetSize));

        // Reachable target: follow the plan (see StepNpcTowardObservableArea).
        if (step.HasValue && TryGuestStep(mapNum, listIndex, t, step.Value))
        {
            MarkReachedIfClosedGuest(t, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            if (isChase) AfterChaseStep(t, t.CurrentMapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }

        // No usable planned step — reversal-averse best-effort only for the unreachable + stalled pacing
        // case; a reachable target is never held.  See StepNpcTowardObservableArea for the full rationale.
        Direction? avoid = step is null && stalled && t.ChaseHasLastStep ? OppositeDir(t.ChaseLastStepDir) : null;
        if (TryGuestBestEffortWalkToward(mapNum, listIndex, t, targetMap, targetX, targetY, out Direction facing, avoid))
        {
            MarkReachedIfClosedGuest(t, mapNum, preDistance, targetMap, targetX, targetY, targetSize);
            if (isChase) AfterChaseStep(t, t.CurrentMapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
            return;
        }
        BroadcastTraversalFacing(t, facing);
        if (isChase) AfterChaseStep(t, t.CurrentMapNum, beforeX, beforeY, targetMap, targetX, targetY, targetSize);
    }

    // ── Chase stall-tracking helpers ──────────────────────────────────────────
    // Shared by the native and guest steppers (TraversalNpcRecord inherits the stall fields).  Stall
    // tracking only engages for true chase targets (player / hostile NPC); janitor and warp-to-tile
    // goals are stationary, so those callers never set isChase.  "Stalled" gates only the momentum
    // shortcut and the unreachable-target anti-pacing (see the field docs on MapNpcRecord).

    /// <summary>Refreshes the stall key for the NPC's current chase target (resetting the counters
    /// on a target change) and reports whether the chaser has now stalled for
    /// <see cref="Constants.NpcChaseStallTicks"/> ticks.</summary>
    private static bool IsChaseStalled(MapNpcRecord mn)
    {
        int key = mn.Target > 0
            ? mn.Target
            : -MapNpcRecord.EncodeNpcId(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot);
        if (key != mn.ChaseTargetKey)
        {
            mn.ChaseTargetKey = key;
            mn.ResetChaseStall();
            mn.BeginEngagement();
        }
        return mn.ChaseStallTicks >= Constants.NpcChaseStallTicks;
    }

    /// <summary>Post-step bookkeeping for a chasing NPC: record the cardinal direction actually
    /// stepped (for reversal detection) and update the stall counters from the new world-distance.</summary>
    private void AfterChaseStep(MapNpcRecord mn, int npcMap, int beforeX, int beforeY, int targetMap, int targetX, int targetY, int targetSize)
    {
        if (mn.Num == 0) return;  // native vacated its slot on a border cross; the fresh guest owns the clock
        RecordChaseStep(mn, beforeX, beforeY);
        int dist = WorldDistanceTo(npcMap, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, targetMap, targetX, targetY, targetSize);
        if (dist < mn.ChaseBestWorldDist)
        {
            mn.ChaseBestWorldDist = dist;
            mn.ChaseStallTicks = 0;
        }
        else
        {
            mn.ChaseStallTicks++;
        }
    }

    /// <summary>Stamps <see cref="MapNpcRecord.ChaseLastStepDir"/> with the world-direction of the
    /// step just taken — for reversal detection.  Handles both a within-map tile step AND a seam
    /// cross, where the position wraps to the opposite edge of a neighbor map: the surviving single-
    /// axis delta is then the full map span with the sign OPPOSITE to travel (a rightward cross reads
    /// x: 15→0, i.e. dx = -15).  Recovering the true direction keeps reversal detection correct across
    /// map borders, so a dance straddling a seam is damped the same as one in open map interior.  A
    /// hold (no movement) leaves the recorded direction unchanged.</summary>
    private static void RecordChaseStep(MapNpcRecord mn, int beforeX, int beforeY)
    {
        int dx = mn.X - beforeX, dy = mn.Y - beforeY;
        // Within-map step: exactly one axis moves by one tile.
        if (dx == 1 && dy == 0)
        {
            mn.ChaseLastStepDir = Direction.Right;
            mn.ChaseHasLastStep = true;
        }
        else if (dx == -1 && dy == 0)
        {
            mn.ChaseLastStepDir = Direction.Left;
            mn.ChaseHasLastStep = true;
        }
        else if (dx == 0 && dy == 1)
        {
            mn.ChaseLastStepDir = Direction.Down;
            mn.ChaseHasLastStep = true;
        }
        else if (dx == 0 && dy == -1)
        {
            mn.ChaseLastStepDir = Direction.Up;
            mn.ChaseHasLastStep = true;
        }
        // Seam cross: one axis wraps a full map span (sign opposite to travel), the other stays 0.
        else if (dy == 0 && dx != 0)
        {
            mn.ChaseLastStepDir = dx < 0 ? Direction.Right : Direction.Left;
            mn.ChaseHasLastStep = true;
        }
        else if (dx == 0 && dy != 0)
        {
            mn.ChaseLastStepDir = dy < 0 ? Direction.Down : Direction.Up;
            mn.ChaseHasLastStep = true;
        }
    }

    /// <summary>World-Manhattan distance from a hypothetical NPC position to a hypothetical target
    /// position, using the NPC's current map as the centered cell of the observable grid.  Returns
    /// <c>int.MaxValue</c> when the target is outside the NPC's observable area — the only legal
    /// callers already gated on that, so this is just defensive.</summary>
    private int WorldDistanceTo(int npcMap, int npcX, int npcY, int npcSize,
                                int targetMap, int targetX, int targetY, int targetSize)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, npcMap);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        if (tw is null) return int.MaxValue;
        var (npcWX, npcWY) = grid.CenterToWorld(npcX, npcY);
        // Edge to edge: every threshold this feeds — chase pace, "did the step close ground", the stall
        // counter — has to mean the same thing on all four sides of an oversize body.
        return WorldCoordHelper.FootprintManhattan(npcWX, npcWY, npcSize, tw.Value.worldX, tw.Value.worldY, targetSize);
    }

    /// <summary>Refresh the AoS give-up clock only when the within-map step actually closed
    /// world-distance to the target.  A perpendicular step that succeeded but left the NPC pacing
    /// along a wall does NOT refresh it — a step only
    /// counts as "reached" if it shortened the path.  Cross-border crosses
    /// leave <c>mn.Num == 0</c> (the home slot is vacated); the guest carries the pre-cross clock
    /// from <see cref="NativeNpcCrossBorder"/> and updates its own clock on subsequent ticks via
    /// <see cref="MarkReachedIfClosedGuest"/>.</summary>
    private void MarkReachedIfClosedNative(MapNpcRecord mn, int mapBeforeStep, int preDistance, int targetMap, int targetX, int targetY, int targetSize)
    {
        if (mn.Num == 0) return;  // crossed border — guest owns the clock now
        int postDistance = WorldDistanceTo(mapBeforeStep, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, targetMap, targetX, targetY, targetSize);
        if (postDistance < preDistance)
            mn.MarkReachedTarget(_aiNow);
    }

    /// <summary>Guest twin of <see cref="MarkReachedIfClosedNative"/>.  Uses <c>t.CurrentMapNum</c>
    /// (which may have changed mid-step when the guest crossed into another map) to compute the
    /// post-step distance; a cross-border move counts as real progress regardless of the pre/post
    /// arithmetic.</summary>
    private void MarkReachedIfClosedGuest(TraversalNpcRecord t, int mapBeforeStep, int preDistance, int targetMap, int targetX, int targetY, int targetSize)
    {
        if (t.CurrentMapNum != mapBeforeStep)
        {
            // Guest crossed a seam this tick — that's real progress; refresh.
            t.MarkReachedTarget(_aiNow);
            return;
        }
        int postDistance = WorldDistanceTo(mapBeforeStep, t.X, t.Y, _world.Npcs[t.Num].EffectiveSize, targetMap, targetX, targetY, targetSize);
        if (postDistance < preDistance)
            t.MarkReachedTarget(_aiNow);
    }

    // No-path fallback for a native NPC: try a single step on the world axis pointing at target,
    // then the perpendicular axis toward target if the first is blocked.  Returns true when a
    // step actually happened; on false, <paramref name="facing"/> holds the direction to face for
    // a broadcast-only update so the NPC at least looks at the target.  Each tick the BFS replans
    // — if a path opens (player on the other side of a door, blocker moves), normal pathing
    // resumes; meanwhile the NPC walks as close as the geometry allows.
    // <paramref name="avoid"/> (when set) forbids one direction — used while stalled to keep advancing
    // toward an unreachable target WITHOUT taking the reverse of the last step (which would be the
    // wall-pacing oscillation half).  Null (the default) imposes no restriction, so the normal
    // not-stalled caller is unaffected.
    private bool TryBestEffortWalkToward(int mapNum, int slot, MapNpcRecord mn, int targetMap, int targetX, int targetY,
                                          out Direction facing, Direction? avoid = null)
    {
        if (!TryComputeWorldDeltas(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, targetMap, targetX, targetY,
                                    out Direction primary, out int dx, out int dy))
        {
            facing = mn.Dir;
            return false;
        }
        facing = primary;
        if (primary != avoid && TryNativeStep(mapNum, slot, mn, primary)) return true;
        if (TryPerpStep(mapNum, slot, mn, primary, dx, dy, avoid)) return true;
        return false;
    }

    private bool TryGuestBestEffortWalkToward(int mapNum, int listIndex, TraversalNpcRecord t, int targetMap, int targetX, int targetY,
                                                out Direction facing, Direction? avoid = null)
    {
        if (!TryComputeWorldDeltas(mapNum, t.X, t.Y, _world.Npcs[t.Num].EffectiveSize, targetMap, targetX, targetY,
                                    out Direction primary, out int dx, out int dy))
        {
            facing = t.Dir;
            return false;
        }
        facing = primary;
        if (primary != avoid && TryGuestStep(mapNum, listIndex, t, primary)) return true;
        if (TryGuestPerpStep(mapNum, listIndex, t, primary, dx, dy, avoid)) return true;
        return false;
    }

    // Resolves the world-axis direction toward target for the no-path fallback.  Returns false
    // when the target slipped out of the observable area entirely (caller faces in its current
    // direction); otherwise yields the primary direction along with the world delta to target,
    // which the perp helper uses to pick the perpendicular axis productively.
    private bool TryComputeWorldDeltas(int mapNum, int fromX, int fromY, int fromSize,
                                         int targetMap, int targetX, int targetY,
                                         out Direction primary, out int dx, out int dy)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        if (tw is null)
        {
            primary = Direction.Up;
            dx = dy = 0;
            return false;
        }
        var (srcWX, srcWY) = grid.CenterToWorld(fromX, fromY);
        // Offsets from the BODY, so an axis the body already spans reads 0 and the perp helper does not
        // try to "close" a gap that is not there.
        (dx, dy) = WorldCoordHelper.FootprintOffsetTo(srcWX, srcWY, fromSize, tw.Value.worldX, tw.Value.worldY);
        primary = WorldCoordHelper.FootprintFacingToward(srcWX, srcWY, fromSize, tw.Value.worldX, tw.Value.worldY);
        return true;
    }

    // True if a single step in `dir` reduces the world (Manhattan) distance to the target — i.e. it heads
    // toward it on that axis.  Powers the chase-momentum shortcut in the two steppers.
    private bool StepClosesWorldDistance(int mapNum, int fromX, int fromY, int fromSize, Direction dir, int targetMap, int targetX, int targetY)
    {
        if (!TryComputeWorldDeltas(mapNum, fromX, fromY, fromSize, targetMap, targetX, targetY, out _, out int dx, out int dy))
            return false;
        return dir switch
        {
            Direction.Right => dx > 0,
            Direction.Left => dx < 0,
            Direction.Down => dy > 0,
            Direction.Up => dy < 0,
            _ => false,
        };
    }

    private bool TryPerpStep(int mapNum, int slot, MapNpcRecord mn, Direction primary, int dx, int dy, Direction? avoid = null)
    {
        bool horizontalPrimary = primary == Direction.Left || primary == Direction.Right;
        Direction perp;
        if (horizontalPrimary)
        {
            if (dy == 0) return false;
            perp = dy > 0 ? Direction.Down : Direction.Up;
        }
        else
        {
            if (dx == 0) return false;
            perp = dx > 0 ? Direction.Right : Direction.Left;
        }
        if (perp == avoid) return false;
        return TryNativeStep(mapNum, slot, mn, perp);
    }

    private bool TryGuestPerpStep(int mapNum, int listIndex, TraversalNpcRecord t, Direction primary, int dx, int dy, Direction? avoid = null)
    {
        bool horizontalPrimary = primary == Direction.Left || primary == Direction.Right;
        Direction perp;
        if (horizontalPrimary)
        {
            if (dy == 0) return false;
            perp = dy > 0 ? Direction.Down : Direction.Up;
        }
        else
        {
            if (dx == 0) return false;
            perp = dx > 0 ? Direction.Right : Direction.Left;
        }
        if (perp == avoid) return false;
        return TryGuestStep(mapNum, listIndex, t, perp);
    }

    // Best facing direction toward target across the observable area — the "give up" gesture when BFS finds
    // no walkable path.  Falls back to the current facing if the target slipped outside the
    // observable area entirely (a transient state the caller usually handles via warp follow).
    private Direction FaceTargetDir(int mapNum, int fromX, int fromY, int fromSize,
                                     int targetMap, int targetX, int targetY, Direction fallback)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        if (tw is null) return fallback;
        var (srcWX, srcWY) = grid.CenterToWorld(fromX, fromY);
        // Measured from the whole body, so it agrees with the reach gate on a target beside a corner column.
        return WorldCoordHelper.FootprintFacingToward(srcWX, srcWY, fromSize, tw.Value.worldX, tw.Value.worldY);
    }

    private void BroadcastNpcDir(int mapNum, int slot, Direction dir)
    {
        _world.MapNpcs[mapNum, slot].Dir = dir;
        SendToMap(_world, mapNum, new NpcDirPacket { MapNum = mapNum, NpcSlot = slot, Dir = dir });
    }
}

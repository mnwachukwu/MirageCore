using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Chasing past the edge of a map. A native that follows its quarry over a border or
/// through a warp becomes a traversal GUEST on the destination; this holds that conversion, the
/// guest's own idle/chase/step lifecycle, and the return home that ends it.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>True when stepping one tile in <paramref name="dir"/> from (x,y) crosses the edge of
    /// <paramref name="from"/>; on true, (destX,destY) is the landing tile on <paramref name="to"/> — that
    /// map's own opposite edge, which the link rule keeps equal to this one's.</summary>
    private static bool StepLeavesMap(MapRecord from, MapRecord to, int x, int y, Direction dir, out int destX, out int destY)
    {
        destX = x;
        destY = y;
        switch (dir)
        {
            case Direction.Up: if (y == 0) { destY = to.Height - 1; return true; } break;
            case Direction.Down: if (y == from.Height - 1) { destY = 0; return true; } break;
            case Direction.Left: if (x == 0) { destX = to.Width - 1; return true; } break;
            case Direction.Right: if (x == from.Width - 1) { destX = 0; return true; } break;
        }
        return false;
    }

    /// <summary>The cardinal neighbor map number in a direction (0 = none linked).</summary>
    private int NeighborInDir(int mapNum, Direction dir)
    {
        var m = _world.Maps[mapNum];
        return dir switch
        {
            Direction.Up => m.Up,
            Direction.Down => m.Down,
            Direction.Left => m.Left,
            Direction.Right => m.Right,
            _ => 0,
        };
    }

    /// <summary>Clear an NPC's player lock and notify observers.</summary>
    private void DropNativeTarget(int mapNum, int slot, MapNpcRecord mn)
    {
        mn.Target = 0;
        SendToMap(_world, mapNum,
            new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = false });
    }

    private void NativeChaseAcrossBorder(int mapNum, int slot, MapNpcRecord mn, int target, long now,
                                          Direction? precomputedStep = null, bool legsStep = false)
    {
        var npc = _world.Npcs[mn.Num];
        var vp = _pm[target].Char;

        // Target outside the 3×3 observable area — only entry point for warp follow.  It may
        // have stepped through a doorway (e.g. a PKer ducking into a building); otherwise give up.
        if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, vp.Map) is null)
        {
            if (!TryNativeWarpFollow(mapNum, slot, mn, target, now))
                DropNativeTarget(mapNum, slot, mn);
            return;
        }

        // On an OBSERVED map the fast legs pass (AdvanceNativeChaseStep) runs the cross-seam STEP at run/walk
        // pace — parity with the same-map chase, so an NPC keeps its sprint through a boundary.  The light-AI
        // path for UNOBSERVED maps has no legs pass, so it steps here instead.
        if (!legsStep)
            StepNpcTowardObservableArea(mapNum, slot, mn, vp.Map, vp.X, vp.Y, vp.Layer, precomputedStep);
    }

    /// <summary>
    /// Converts the native slot NPC into a <see cref="TraversalNpcRecord"/> standing on
    /// <paramref name="toMap"/> at (destX,destY) — its FIRST hop off home.  The home slot is vacated
    /// and reserved (no respawn while away).  Observers of both maps receive the traversal state so the
    /// home-slot sprite is removed and the guest appears on the neighbor (the double-send is idempotent
    /// client-side).  Subsequent hops (guest→guest) go through <see cref="MoveGuestToMap"/> instead.
    /// </summary>
    private void NativeNpcCrossBorder(int fromMap, int slot, MapNpcRecord mn, int toMap, int destX, int destY, Direction dir, bool stepped, WorldLayer crossLayer)
    {
        var t = new TraversalNpcRecord
        {
            SpawnMapNum = fromMap,
            SpawnSlot = slot,
            CurrentMapNum = toMap,
            Num = mn.Num,
            Target = mn.Target,
            X = destX,
            Y = destY,
            Dir = dir,
            // Two-layer world: carry the RESOLVED layer across the seam — NpcStepPassesRampGate ascended/descended
            // it if the cross stepped onto/off a ramp at the seam; otherwise it's mn.Layer.  The landing was
            // validated at this same layer, so a bridge continues onto the neighbor map.
            Layer = crossLayer,
            Moving = MovementType.Walking,
            AttackTimer = mn.AttackTimer,
            LastSpokeTo = mn.LastSpokeTo,
            // The noticed-NPC identity rides along, so a pursuit that crosses a seam continues
            // against the same body.
            NpcTargetSpawnMap = mn.NpcTargetSpawnMap,
            NpcTargetSpawnSlot = mn.NpcTargetSpawnSlot,
            LastSpokeToNpc = mn.LastSpokeToNpc,
            // Carry the give-up clock across the seam so a chase doesn't get a fresh lease every
            // time it crosses a border.
            LastReachedTargetMs = mn.LastReachedTargetMs,
            // Carry chase-stall damping state too, so a dance that straddles this seam is damped from
            // the FIRST guest tick instead of re-accumulating ~3 ticks of stall on the guest side.
            // The world-distance metric is re-center-invariant (re-centering is a rigid translation;
            // the cross is the NPC's own 1-tile move), so the carried best-distance stays comparable.
            // ChaseLastStepDir is this very cross's direction, so a reversal (cross straight back) is caught.
            ChaseBestWorldDist = mn.ChaseBestWorldDist,
            ChaseStallTicks = mn.ChaseStallTicks,
            ChaseTargetKey = mn.ChaseTargetKey,
            ChaseLastStepDir = dir,
            ChaseHasLastStep = true,
            // Carry the approach-commitment + reservoir state so the seam doesn't reset the engagement: a
            // conserving mob keeps walking its approach (and a rusher keeps rushing) after crossing, and a
            // reservoir mid-rebuild stays walking instead of getting a free sprint on the guest's first tick.
            RushCommitted = mn.RushCommitted,
            HasMadeContact = mn.HasMadeContact,
            ChaseSprinting = mn.ChaseSprinting,
            // A body sent after somebody was sent after them wherever they go. Dropped at the seam it would
            // hand itself back to a record with no noticing rule, which stops the chase one tile over a
            // border — the hardest kind of bug to see, because it looks like the quarry got away.
            Roused = mn.Roused,
            // Count this cross as the guest's action for THIS pass.  Maps tick in ascending order, so a
            // native crossing UP into a higher-numbered map (e.g. 1→2) lands in the destination's
            // traversal list before that map ticks; without this stamp RunTraversalAi would give it a
            // free second step the same pass (cross + step in one ~5ms burst → the client collapses both
            // into one slide and the sprite skips a tile across the seam).  MoveGuestToMap needs no
            // equivalent: RunTraversalAi already stamps LastAiTick before a guest→guest hop.
            LastAiTick = _aiNow,
            // The game's own values go with the body, not with the slot it vacated. The bag moves
            // rather than being copied: this is the same NPC, and a copy would let the two drift.
            Attributes = mn.Attributes,
        };
        // The whole damage ledger crosses atomically — see MapNpcRecord.CopyCombatLedgerTo.
        mn.CopyCombatLedgerTo(t);
        _world.MapTraversalNpcs[toMap].Add(t);

        // Vacate but reserve the home slot — blocks respawn until the traveler returns or dies.
        mn.Num = 0;
        mn.Target = 0;
        mn.NpcTargetSpawnMap = 0;
        mn.NpcTargetSpawnSlot = 0;
        mn.Roused = false;
        mn.IsReservedSlot = true;
        mn.LastSpokeTo = 0;
        mn.LastSpokeToNpc = 0;
        mn.LastReachedTargetMs = 0;
        mn.ChaseTargetKey = 0;   // chase-stall state handed to the guest; clear the vacated home slot
        mn.ResetChaseStall();
        mn.DamageByNpc = null;  // ledger handed to the guest; nothing for the home slot to clear
        mn.ClearDamageCredit();

        // Any player locked onto the native slot keeps tracking this same monster as a guest.
        _selection.FollowNpcAcrossSeam(fromMap, slot, toMap);

        var pkt = BuildTraversalPacket(t, stepped);
        SendToMap(_world, fromMap, pkt);
        SendToMap(_world, toMap, pkt);
    }

    // Follows a player who just stepped through a warp tile, using the EXACT doorway they took (the
    // warp mark on ServerPlayer) rather than scanning for one — so the NPC never guesses a wrong-but-
    // same-destination warp.  The mark is only trusted when the player warped FROM our map TO where
    // they now stand; otherwise it's stale and we give up.  Walks to that warp tile and steps through
    // once adjacent (warp tiles aren't NPC-walkable), becoming a guest on the destination.  Stops a
    // PKer from shaking guards by ducking into a building.  Returns false when there's no usable mark.
    private bool TryNativeWarpFollow(int mapNum, int slot, MapNpcRecord mn, int target, long now)
    {
        var npc = _world.Npcs[mn.Num];
        var sp = _pm[target];
        var vp = sp.Char;
        // Self-validate the mark: the player must have warped from THIS map to where they are now.
        if (sp.WarpFromMap != mapNum || sp.WarpToMap != vp.Map) return false;
        // No destination gate: a hostile NPC WILL follow a player through a warp into a town. See
        // ResetNativeNpc for why that is safe (the drag-to-town exploit is closed by the reset, not by
        // refusing entry).

        Direction toward = DirectionToward(new MapPos(mn.X, mn.Y), new MapPos(sp.WarpFromX, sp.WarpFromY));

        // Warp tiles aren't NPC-walkable, so step through once adjacent (like walking into a doorway).
        if (Math.Abs(mn.X - sp.WarpFromX) + Math.Abs(mn.Y - sp.WarpFromY) <= 1)
        {
            if (FindWarpLanding(sp.WarpToMap, sp.WarpToX, sp.WarpToY, npc.EffectiveSize, ignoreNpcAvoid: false, mn, out int lx, out int ly))
                NativeNpcCrossBorder(mapNum, slot, mn, sp.WarpToMap, lx, ly, mn.Dir, stepped: false, mn.Layer);  // warp = teleport, keep layer
            else
                BroadcastNpcDir(mapNum, slot, toward);  // dest + all neighbors blocked — wait it out
            return true;
        }

        StepNpcTowardObservableArea(mapNum, slot, mn, mapNum, sp.WarpFromX, sp.WarpFromY, WorldLayer.Ground);
        return true;
    }

    // Where a chaser lands when it follows a player through a warp.  A PLAYER can warp onto any tile,
    // but an NPC needs an NPC-walkable, unoccupied one — so a player camping the exit, several chasers
    // piling through one doorway, or a non-walkable landing would otherwise leave them clustering at the
    // warp forever.  Prefer the exact destination the player took; otherwise spill onto the first free
    // cardinal neighbor.  False only when the destination and all four neighbors are blocked.
    private bool FindWarpLanding(int toMap, int destX, int destY, int size, bool ignoreNpcAvoid, MapNpcRecord mover, out int lx, out int ly)
    {
        if (_movement.IsNpcFootprintLandingFree(toMap, destX, destY, size, ignoreNpcAvoid, mover))
        {
            lx = destX;
            ly = destY;
            return true;
        }
        if (_movement.IsNpcFootprintLandingFree(toMap, destX, destY - 1, size, ignoreNpcAvoid, mover))
        {
            lx = destX;
            ly = destY - 1;
            return true;
        }
        if (_movement.IsNpcFootprintLandingFree(toMap, destX, destY + 1, size, ignoreNpcAvoid, mover))
        {
            lx = destX;
            ly = destY + 1;
            return true;
        }
        if (_movement.IsNpcFootprintLandingFree(toMap, destX - 1, destY, size, ignoreNpcAvoid, mover))
        {
            lx = destX - 1;
            ly = destY;
            return true;
        }
        if (_movement.IsNpcFootprintLandingFree(toMap, destX + 1, destY, size, ignoreNpcAvoid, mover))
        {
            lx = destX + 1;
            ly = destY;
            return true;
        }
        lx = ly = 0;
        return false;
    }

    // stepped: only the cross paths pass true (a contiguous one-tile border step), so the client slides
    // the sprite across the seam.  Same-map updates and warps leave it false.
    private TraversalNpcPacket BuildTraversalPacket(TraversalNpcRecord t, bool stepped = false)
    {
        var npc = _world.Npcs[t.Num];
        long now = Environment.TickCount64;
        return new TraversalNpcPacket
        {
            SpawnMapNum = t.SpawnMapNum,
            SpawnSlot = t.SpawnSlot,
            CurrentMapNum = t.CurrentMapNum,
            Num = t.Num,
            X = t.X,
            Y = t.Y,
            Dir = t.Dir,
            Movement = t.Moving,
            Stepped = stepped,
            MsSinceCombat = PacketBuilder.MsSinceCombat(t.CombatExpiresAt, now, 0),
            HasTarget = t.Target > 0,
            Attacking = t.Attacking,
            Layer = t.Layer,
        };
    }

    private void BroadcastTraversalState(TraversalNpcRecord t)
        => SendToMap(_world, t.CurrentMapNum, BuildTraversalPacket(t));

    /// <summary>Per-tick AI for visiting (chasing) NPCs on a map. Iterated backward for safe removal.</summary>
    private void RunTraversalAi(int mapNum, long now)
    {
        var list = _world.MapTraversalNpcs[mapNum];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            if (t.Num <= 0)
            {
                list.RemoveAt(i);
                continue;
            }  // killed elsewhere — drop the husk
            // Maps tick in ascending order; a guest that just crossed UP into this map already acted
            // this tick.  Skip it so every guest gets exactly one action per tick regardless of which
            // direction it crossed (no free extra step on an upward cross).
            if (t.LastAiTick == now) continue;
            t.LastAiTick = now;
            var npc = _world.Npcs[t.Num];
            t.Attacking = false;  // cleared each tick; the attack path re-sets it for one swing

            // A guest that has had nobody for the give-up window goes home (fresh respawn on the home
            // slot).  This is the ONLY path that ends a guest's trip — losing a target alone does not,
            // so one that loses its quarry gets the rest of the window to find another before the walk
            // back.  RunGuestIdle restamps the clock the moment it notices somebody, and every closing
            // step restamps it too, so the window only runs out on a guest with genuinely nothing to do.
            if (t.Target == 0 && t.NpcTargetSpawnSlot == 0
                && t.LastReachedTargetMs > 0 && now - t.LastReachedTargetMs > NpcUnreachedGiveUpMs)
            {
                ReturnTraversalHome(mapNum, i, t);
                continue;
            }

            if (t.NpcTargetSpawnSlot > 0 && t.Target == 0)
            {
                RunGuestNoticedNpcStep(mapNum, i, t, now);
                continue;
            }

            int target = t.Target;
            bool targetValid = target > 0 && _pm[target].IsPlaying && !_pm[target].GettingMap;
            if (!targetValid)
            {
                // Target gone (offline / leaving game).  Drop the lock but do NOT return home — the
                // guest reverts to idle (scan / wander) and the gate above walks it home if it finds
                // nobody else before the window runs out.
                if (target > 0)
                {
                    t.Target = 0;
                    BroadcastTraversalState(t);  // notify clients the target outline cleared
                }
                RunGuestIdle(mapNum, i, t, now);
                continue;
            }

            // Unreached give-up — the same clock natives run.  A guest that cannot reach its quarry
            // goes straight home rather than lingering abroad.
            if (ShouldGiveUpUnreachedTarget(t, now))
            {
                ReturnTraversalHome(mapNum, i, t);
                continue;
            }

            var vp = _pm[target].Char;
            // The guest pursues onto ANY map, including back onto its own spawn map — there is no depth
            // cap.  If the target has slipped out of the guest's observable area it may have warped,
            // so follow the exact doorway it took; if even that fails, drop the target and idle in
            // place (same rule as target-offline above).
            if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, vp.Map) is null)
            {
                if (TryGuestWarpFollow(mapNum, i, t, target, now)) continue;
                t.Target = 0;
                BroadcastTraversalState(t);
                RunGuestIdle(mapNum, i, t, now);
                continue;
            }

            // A fleeing guest lets go once the gap is wider than what it can notice — the retreat
            // worked.  A pursuing one closes instead.
            if (npc.Behavior == NpcBehavior.Flee)
            {
                if (!IsWithinNoticeRange(mapNum, t, npc, vp))
                {
                    t.Target = 0;
                    BroadcastTraversalState(t);
                    RunGuestIdle(mapNum, i, t, now);
                }
                continue;
            }

            TraversalChaseStep(mapNum, i, t, vp, now);
        }
    }

    /// <summary>Idle behavior for a guest with nobody: scan the current map's 9-map area for a player
    /// and then for a non-kin NPC; if neither turns up, amble in committed strides (see
    /// <see cref="WanderStep"/>).  Does NOT return home — the idle gate in
    /// <see cref="RunTraversalAi"/> is the only path that ends a guest's trip, so an idle guest strolls
    /// out its window here.  Only <see cref="NpcBehavior.Pursue"/> and <see cref="NpcBehavior.Flee"/>
    /// guests exist: nothing else ever leaves its home map.</summary>
    private void RunGuestIdle(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        var npc = _world.Npcs[t.Num];
        int playerTarget = FindNoticeablePlayer(mapNum, t, npc.Range);
        if (playerTarget > 0)
        {
            t.Target = playerTarget;
            t.MarkReachedTarget(now);
            BroadcastTraversalState(t);
            return;
        }

        // A fleeing guest never goes looking for an NPC to run from: it retreats from what noticed it,
        // and an NPC it has not met is not chasing it.
        if (npc.Behavior == NpcBehavior.Pursue)
        {
            var (npcSpawnMap, npcSpawnSlot) = FindNoticeableNpc(mapNum, 0, t);
            if (npcSpawnSlot > 0)
            {
                t.NpcTargetSpawnMap = npcSpawnMap;
                t.NpcTargetSpawnSlot = npcSpawnSlot;
                t.MarkReachedTarget(now);
                BroadcastTraversalState(t);
                return;
            }
        }

        // Nobody in range — amble in committed strides (see WanderStep), so a guest running out its
        // window strolls deliberately instead of twitching.  slot is unused on the guest path.
        WanderStep(mapNum, 0, t);
    }

    private void TraversalChaseStep(int mapNum, int listIndex, TraversalNpcRecord t, PlayerRecord vp, long now)
    {
        // Single chase-step path — same-map and cross-map both flow through observable-area BFS, which
        // produces a within-map move or a one-tile border cross as needed.  RunTraversalAi already
        // checked the target is observable; the defensive fallback drops it and idles in place, since
        // the idle gate in RunTraversalAi is the only path that sends a guest home.
        if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, vp.Map) is null)
        {
            t.Target = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }
        // On an OBSERVED map the fast legs pass (AdvanceGuestChaseStep) runs the WHOLE chase-STEP — same-map
        // AND cross-seam — at run/walk pace.  The legs skip UNOBSERVED maps (RunMovement bails on observers==0)
        // but guests tick everywhere, so on an unwatched map the brain still steps here.  (A same-map player
        // would make the map observed, so observers==0 here always implies an off-map target — a seam cross.)
        if (_world.MapObservers[mapNum].Count == 0)
            StepGuestTowardObservableArea(mapNum, listIndex, t, vp.Map, vp.X, vp.Y, vp.Layer);
    }

    private bool TryApplyGuestStep(int mapNum, TraversalNpcRecord t, Direction dir)
    {
        if (!_movement.CanNpcMoveFrom(mapNum, t, dir)) return false;
        // Commit the resulting layer (ascend/descend across a ramp) BEFORE moving — LayerLogic reads the pre-move
        // anchor, so a guest that mounts/dismounts a ramp on its guest map changes layer just like a native does.
        _movement.NpcStepPassesRampGate(mapNum, t, dir, out var newLayer);
        t.Layer = newLayer;
        t.Dir = dir;
        switch (dir)
        {
            case Direction.Up:
                t.Y--;
                break;
            case Direction.Down:
                t.Y++;
                break;
            case Direction.Left:
                t.X--;
                break;
            case Direction.Right:
                t.X++;
                break;
        }
        t.Moving = t.MoveType;
        BroadcastTraversalState(t);
        return true;
    }

    /// <summary>
    /// Relocates an existing guest from its current map to <paramref name="toMap"/> at (destX,destY) —
    /// a subsequent chase hop (border cross or warp).  The record is moved between the per-map traversal
    /// lists; observers of the new map get its updated state, while observers who could see the OLD map
    /// but not the new one are told to drop it, so no stale sprite/entry lingers.  No observer receives
    /// both, so there's no flicker.  (The FIRST hop off home goes through <see cref="NativeNpcCrossBorder"/>.)
    /// </summary>
    private void MoveGuestToMap(int fromMap, int listIndex, TraversalNpcRecord t, int toMap, int destX, int destY, Direction dir, bool stepped,
        WorldLayer? crossLayer = null)
    {
        var fromObs = _world.MapObservers[fromMap];
        _world.MapTraversalNpcs[fromMap].RemoveAt(listIndex);
        t.CurrentMapNum = toMap;
        t.X = destX;
        t.Y = destY;
        t.Dir = dir;
        // Carry the RESOLVED layer across a seam step (ascend/descend onto/off a ramp at the seam); a warp-follow
        // hop passes none and keeps the guest's current layer.
        t.Layer = crossLayer ?? t.Layer;
        t.Moving = MovementType.Walking;
        _world.MapTraversalNpcs[toMap].Add(t);

        var toObs = _world.MapObservers[toMap];
        _dispatcher.SendToObservers(toObs, BuildTraversalPacket(t, stepped));
        // Observers who lose sight of the departing guest drop it; those who still see it (new map in
        // their observable area) already got the state packet above — so nobody gets both.
        var despawn = new NpcDespawnPacket { SpawnMapNum = t.SpawnMapNum, SpawnSlot = t.SpawnSlot };
        foreach (int idx in fromObs)
        {
            if (!toObs.Contains(idx))
                _dispatcher.SendTo(idx, despawn);
        }
    }

    // Guest equivalent of TryNativeWarpFollow: the target slipped out of view, so follow the exact warp
    // it took (the ServerPlayer mark) and relocate to the destination.  Same self-validation, and the same
    // absence of a destination gate.  Returns false when there's no usable mark, ending the chase (the
    // caller then returns home).
    private bool TryGuestWarpFollow(int mapNum, int listIndex, TraversalNpcRecord t, int target, long now)
    {
        var npc = _world.Npcs[t.Num];
        var sp = _pm[target];
        var vp = sp.Char;
        if (sp.WarpFromMap != mapNum || sp.WarpToMap != vp.Map) return false;

        Direction toward = DirectionToward(new MapPos(t.X, t.Y), new MapPos(sp.WarpFromX, sp.WarpFromY));

        // Warp tiles aren't NPC-walkable, so step through once adjacent (like walking into a doorway).
        if (Math.Abs(t.X - sp.WarpFromX) + Math.Abs(t.Y - sp.WarpFromY) <= 1)
        {
            if (FindWarpLanding(sp.WarpToMap, sp.WarpToX, sp.WarpToY, npc.EffectiveSize, ignoreNpcAvoid: false, t, out int lx, out int ly))
                MoveGuestToMap(mapNum, listIndex, t, sp.WarpToMap, lx, ly, t.Dir, stepped: false);
            else
                BroadcastTraversalFacing(t, toward);  // dest + all neighbors blocked — wait it out
            return true;
        }

        StepGuestTowardObservableArea(mapNum, listIndex, t, mapNum, sp.WarpFromX, sp.WarpFromY, WorldLayer.Ground);
        return true;
    }

    private void BroadcastTraversalFacing(TraversalNpcRecord t, Direction dir)
    {
        t.Dir = dir;
        t.Moving = MovementType.None;
        BroadcastTraversalState(t);
    }

    /// <summary>
    /// Despawn-and-respawn return: silently removes the guest from its current map (no death, no loot),
    /// wherever it ended up, and respawns the native NPC fresh on its home slot.  This is the single
    /// "reset" routine for a chase that ends without a kill — lost target, target unreachable, combat
    /// expiry, or an unobserved abandonment — so no walk-back pathing is ever needed.
    /// <para>Guest counterpart to <see cref="ResetNativeNpc"/>, and the other half of why the chase code
    /// needs no cross-map entry gate: however far a guest was lured, this returns it. See
    /// <see cref="ResetNativeNpc"/> for the full rationale.</para>
    /// </summary>
    private void ReturnTraversalHome(int mapNum, int listIndex, TraversalNpcRecord t)
    {
        SendToMap(_world, t.CurrentMapNum,
            new NpcDespawnPacket { SpawnMapNum = t.SpawnMapNum, SpawnSlot = t.SpawnSlot });
        _world.MapTraversalNpcs[mapNum].RemoveAt(listIndex);
        // This guest instance is over (it reappears as a fresh native respawn) — clear any locks on it.
        _selection.ClearSelectionsOfVisitor(t.SpawnMapNum, t.SpawnSlot);
        // Other NPCs that targeted this guest mid-fight would otherwise resolve through to the freshly
        // respawned native at the same identity and silently keep fighting it; clear them too.

        var home = _world.MapNpcs[t.SpawnMapNum, t.SpawnSlot];
        home.IsReservedSlot = false;
        home.Num = 0;          // force SpawnNpc to fully re-initialize the slot
        home.SpawnWait = 0;
        _spawn.SpawnNpc(t.SpawnSlot, t.SpawnMapNum);
    }

    private static Direction DirectionToward(MapPos from, MapPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? Direction.Right : Direction.Left;
        return dy > 0 ? Direction.Down : Direction.Up;
    }
}

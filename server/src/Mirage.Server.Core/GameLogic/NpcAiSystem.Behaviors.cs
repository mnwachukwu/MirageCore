using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The per-map AI passes and the behavior brains they drive: the observed-map pass, the
/// cheaper unobserved upkeep, and the pursue / flee / scavenge / wander routines an NPC runs
/// depending on what it is and whether anything is in reach.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Light AI for a map with no observers: a vacated town's scavengers still tidy player-dropped
    // litter so it's clean when someone returns.  No observers means no players in range, so there is
    // nothing to notice and nothing to broadcast — we skip the whole notice/wander path and run only
    // the scavenge sweep.  A single litter check up front skips the per-NPC item scan entirely on an
    // already-clean map (the common steady state), so an idle vacated town costs one MaxMapItems scan
    // per tick, not N.
    private void RunUnobservedUpkeep(int mapNum)
    {
        if (!HasDroppedItems(mapNum)) return;
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num <= 0 || mn.IsReservedSlot) continue;
            if (_world.Npcs[mn.Num].Behavior != NpcBehavior.Scavenge) continue;
            RunScavengeAi(mapNum, slot, mn);
        }
    }

    // True if the map holds any voluntary-player-dropped item — a scavenger's only concern.  Death
    // drops (PlayerDeathDropped) and NPC loot (NpcDropped) are deliberately excluded so what a body
    // left behind stays recoverable.  One list scan, used to skip the per-NPC litter search on a
    // vacated map that's already clean.
    private bool HasDroppedItems(int mapNum)
    {
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
            if (list[i].Source == ItemSource.PlayerDropped) return true;
        return false;
    }

    // Upkeep for a map with no observers.  A native NPC here can still hold a player it noticed who
    // just LEFT or WARPED away — a true warp un-observes this map, which would otherwise freeze the
    // pursuer the instant its quarry teleported out of sight.  Driving the warp-follow here keeps it
    // pursuing out through a warp; a player who has left the game drops the lock.
    private void RunUnobservedPursuit(int mapNum, long now)
    {
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num == 0 || mn.Target == 0) continue;
            if (_pm[mn.Target].IsPlaying)
                NativeChaseAcrossBorder(mapNum, slot, mn, mn.Target, now);
            else
                DropNativeTarget(mapNum, slot, mn);
        }
        // An NpcTarget needs no driving here: an unobserved map has no players to draw NPCs into each
        // other's viewport, so a lingering one means the other body despawned or moved on, and the
        // observable-area check in RunNoticedNpcStep clears it when the map is next watched.
    }

    // Brain pass for one observed map: regen, then a per-behavior branch.
    private void RunAiForMap(int mapNum, long now, bool regenTick)
    {
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num <= 0) continue;

            var npc = _world.Npcs[mn.Num];

            if (regenTick)
                RegenNpcVitals(mapNum, mn, npc, now);

            switch (npc.Behavior)
            {
                case NpcBehavior.Pursue:
                    RunPursuitAi(mapNum, slot, mn, now);
                    // Nobody to chase → look for a non-kin NPC within Range instead.
                    if (mn.Target == 0 && mn.NpcTargetSpawnSlot == 0)
                        TryNoticeNpc(mapNum, slot, mn, now);
                    if (mn.NpcTargetSpawnSlot > 0)
                        RunNoticedNpcStep(mapNum, slot, mn, now);
                    break;

                case NpcBehavior.Flee:
                    RunFleeAi(mapNum, slot, mn, now);
                    break;

                case NpcBehavior.Scavenge:
                    RunScavengeAi(mapNum, slot, mn);
                    if (mn.JanitorTarget == 0) WanderStep(mapNum, slot, mn);
                    break;

                case NpcBehavior.Stationary:
                    break;

                default: // Wander — amble in committed strides (see WanderStep).
                    WanderStep(mapNum, slot, mn);
                    break;
            }
        }
    }

    /// <summary>Brain tick for a <see cref="NpcBehavior.Pursue"/> native: notice a player if it has
    /// none, let go of one it cannot hold, warp-follow one that left this map, and wander when it has
    /// nobody at all. The chase STEP itself — same-map and cross-seam — runs on the fast legs pass
    /// (<see cref="AdvanceNativeChaseStep"/>) at the NPC's own pace, not on this 500ms tick.</summary>
    private void RunPursuitAi(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var npc = _world.Npcs[mn.Num];

        if (mn.Target == 0 && mn.NpcTargetSpawnSlot == 0)
        {
            mn.Target = FindNoticeablePlayer(mapNum, mn, npc.Range);
            if (mn.Target > 0)
            {
                mn.JanitorTarget = 0;
                mn.MarkReachedTarget(now);
                SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
                AnnounceNotice(mapNum, slot, mn, npc, mn.Target);
            }
        }

        if (mn.Target == 0)
        {
            // Wander only when fully idle: an NPC that has noticed another NPC would otherwise stroll
            // off mid-chase and fight the BFS pursuit RunNoticedNpcStep runs right after — visible as
            // two NPCs dancing instead of closing on each other cleanly.
            if (mn.NpcTargetSpawnSlot == 0) WanderStep(mapNum, slot, mn);
            return;
        }

        if (!_pm[mn.Target].IsPlaying)
        {
            DropNativeTarget(mapNum, slot, mn);
            return;
        }

        if (ShouldGiveUpUnreachedTarget(mn, now))
        {
            DropNativeTarget(mapNum, slot, mn);
            ResetNativeNpc(mn, mapNum, slot, npc);
            return;
        }

        // On another observable map → chase across the border, which converts this native into a
        // traversal guest, or drops the lock when that map is unreachable.
        if (_pm[mn.Target].Char.Map != mapNum)
            NativeChaseAcrossBorder(mapNum, slot, mn, mn.Target, now, legsStep: true);
    }

    /// <summary>Brain tick for a <see cref="NpcBehavior.Flee"/> native: notice on the same terms as a
    /// pursuer, then let go the moment whoever it noticed is out of <see cref="NpcRecord.Range"/> —
    /// the retreat has worked and there is nothing left to run from. The retreat STEP runs on the fast
    /// legs pass (<see cref="AdvanceNativeChaseStep"/>), which routes a fleeing NPC through
    /// <see cref="TryLegsFlee"/>.</summary>
    private void RunFleeAi(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var npc = _world.Npcs[mn.Num];

        if (mn.Target == 0)
        {
            mn.Target = FindNoticeablePlayer(mapNum, mn, npc.Range);
            if (mn.Target > 0)
            {
                mn.MarkReachedTarget(now);
                SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
                AnnounceNotice(mapNum, slot, mn, npc, mn.Target);
            }
            else
            {
                WanderStep(mapNum, slot, mn);
            }
            return;
        }

        // Let go once the gap is bigger than what it can notice — the retreat worked, or the player
        // left.  Measured with the same footprint-aware reach the scan used, so noticing and letting
        // go agree on where the edge is.
        bool stillClose = _pm[mn.Target].IsPlaying && IsWithinNoticeRange(mapNum, mn, npc, _pm[mn.Target].Char);
        if (!stillClose) DropNativeTarget(mapNum, slot, mn);
    }

    /// <summary>Whether <paramref name="p"/> is still inside <paramref name="npc"/>'s
    /// <see cref="NpcRecord.Range"/>, measured body to body across the observable area. False when the
    /// player has left that area entirely.</summary>
    private bool IsWithinNoticeRange(int mapNum, MapNpcRecord mn, NpcRecord npc, PlayerRecord p)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var gp = grid.PositionOf(p.Map);
        if (gp is null) return false;
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var (pwx, pwy) = grid.ToWorld(gp.Value.col, gp.Value.row, p.X, p.Y);
        return WorldCoordHelper.AreFootprintsWithin(npcWX, npcWY, npc.EffectiveSize, pwx, pwy, 1, npc.Range);
    }

    /// <summary>Say the NPC's line to whoever it just noticed, once per player. Silent when the record
    /// carries no line.</summary>
    private void AnnounceNotice(int mapNum, int slot, MapNpcRecord mn, NpcRecord npc, int target)
    {
        if (mn.LastAttackSayTarget == target || string.IsNullOrWhiteSpace(npc.AttackSay)) return;
        mn.LastAttackSayTarget = target;
        _dispatcher.SendLocalizedChatTo(target, ServerStrings.NpcAiSystem_NpcSays,
            new ChatMetadata(GameColor.Npc, ChatChannel.Say),
            ("NpcName", npc.TrimmedName), ("Say", npc.AttackSay.TrimEnd()));
        _dispatcher.SendTo(target, PacketBuilder.NpcChatBubble(mapNum, slot, npc.AttackSay.TrimEnd(), kind: 0));
    }

    // Walk to player-dropped litter and clear it.  Claims one item at a time so two scavengers on the
    // same map don't converge on the same tile.
    private void RunScavengeAi(int mapNum, int slot, MapNpcRecord mn)
    {
        // Step 1: service an existing claim.
        if (mn.JanitorTarget > 0)
        {
            var mi = _world.MapItemBySlot(mapNum, mn.JanitorTarget);
            if (mi is null || mi.Source != ItemSource.PlayerDropped)
            {
                mn.JanitorTarget = 0;  // stale — fall through to search
            }
            else if (mn.X == mi.X && mn.Y == mi.Y && mn.Layer == mi.Layer)
            {
                // Reached the item — on its own layer (a bridge-top drop is cleared from the deck, not from
                // under it) — clear it and save.
                int targetSlot = mn.JanitorTarget;
                mn.JanitorTarget = 0;
                _items.RemoveMapItem(mapNum, targetSlot);
                _items.EnqueueSaveDroppedItems(mapNum);
                return;
            }
            else
            {
                // Route toward the litter on ITS layer — the layer-aware BFS climbs a ramp to reach a bridge-top
                // drop, so a ground scavenger still tidies fringe litter instead of being blind to it.
                StepNpcTowardObservableArea(mapNum, slot, mn, mapNum, mi.X, mi.Y, mi.Layer);
                return;
            }
        }

        // Step 2: find an unclaimed dropped item anywhere on the map.
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
        {
            var mi = list[i];
            if (mi.Num == 0 || mi.Source != ItemSource.PlayerDropped) continue;
            // Check that no other scavenger on this map has already claimed this slot id.
            bool claimed = false;
            for (int g = 1; g <= Constants.MaxMapNpcs; g++)
            {
                if (g == slot) continue;
                if (_world.MapNpcs[mapNum, g].JanitorTarget == mi.Slot)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed) continue;
            mn.JanitorTarget = mi.Slot;
            return;
        }
    }

    // ── Wander (committed-stride ambling) ───────────────────────────────────────
    // Idle NPCs (native or guest) stroll in strides instead of taking isolated random steps: on a
    // NpcWanderStartChancePerTick roll, commit to a heading and a length
    // of NpcWanderStride{Min,Max}Tiles, then walk it one tile per AI tick (gapless at the tick-matched NPC
    // walk-slide).  Mid-stride each step may bend a right angle (NpcWanderTurnChancePerStep) — never a
    // reversal — so paths form Ls and gentle zigzags, not dead-straight lines.  Confined to the map by
    // CanNpcMove's bounds check, so an NPC never wanders across a border (only the chase code turns a native
    // into a traversal guest).  Polymorphic over native slots and traversal guests via the same step / face
    // primitives the chase steppers use.
    private void WanderStep(int mapNum, int slot, MapNpcRecord mn)
    {
        if (mn.WanderStepsLeft > 0)
        {
            // Mid-stride: occasionally turn a right angle so the stroll bends instead of running dead straight.
            if (Rng.Next(Constants.NpcWanderTurnChancePerStep) == 0)
                mn.WanderDir = RandomPerpendicular(mn.WanderDir);
            TakeWanderStep(mapNum, slot, mn);
            return;
        }
        // Idle — begin a fresh stride on the 1-in-N cadence; otherwise keep loitering this tick.
        if (Rng.Next(Constants.NpcWanderStartChancePerTick) != 0) return;
        mn.WanderDir = (Direction)Rng.Next(Constants.NumDirections);
        mn.WanderStepsLeft = Rng.Next(Constants.NpcWanderStrideMinTiles, Constants.NpcWanderStrideMaxTiles + 1);
        TakeWanderStep(mapNum, slot, mn);   // first step keeps the freshly-picked heading (no turn)
    }

    // One stride step in mn.WanderDir.  Clear tile: step and decrement.  Blocked: end the stride early and
    // face the obstacle (a failed wander step still turns the NPC).  Native vs guest via the same
    // step / face primitives as the chase steppers.
    private void TakeWanderStep(int mapNum, int slot, MapNpcRecord mn)
    {
        bool moved;
        if (mn is TraversalNpcRecord t)
        {
            moved = TryApplyGuestStep(mapNum, t, mn.WanderDir);
        }
        else if (_movement.CanNpcMove(mapNum, slot, mn.WanderDir))
        {
            _movement.NpcMove(mapNum, slot, mn.WanderDir, MovementType.Walking);
            moved = true;
        }
        else
        {
            moved = false;
        }

        if (moved)
        {
            mn.WanderStepsLeft--;
        }
        else
        {
            mn.WanderStepsLeft = 0;
            if (mn is TraversalNpcRecord tg) BroadcastTraversalFacing(tg, mn.WanderDir);
            else BroadcastNpcDir(mapNum, slot, mn.WanderDir);
        }
    }

    // A random 90° turn from dir (never a 180° reversal): Up/Down bend to Left/Right and vice-versa.
    private Direction RandomPerpendicular(Direction dir) =>
        dir is Direction.Up or Direction.Down
            ? (Rng.Next(2) == 0 ? Direction.Left : Direction.Right)
            : (Rng.Next(2) == 0 ? Direction.Up : Direction.Down);
}

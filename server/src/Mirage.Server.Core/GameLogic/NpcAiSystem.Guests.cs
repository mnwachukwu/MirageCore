using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Traversal-guest NPC-versus-NPC stepping: the guest twin of the native combat step.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>Guest twin of <see cref="RunNpcVsNpcStep"/>: a traversal NPC pursuing an NPC target.
    /// When the victim dies, despawns, or flees outside the guest's 9-map observable area, the target
    /// is dropped and the guest falls into idle (<see cref="RunGuestIdle"/>) rather than immediately
    /// returning home — the unified combat-expire gate in <see cref="RunTraversalAi"/> handles the
    /// actual return-home + reset when combat lapses.  Adjacent → strike; not adjacent → BFS-step.
    /// Refreshes combat each chase step (relentless by design for acquired NPC engagements).</summary>
    private void RunGuestNpcVsNpcStep(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        var npc = _world.Npcs[t.Num];
        var resolved = _queries.ResolveNpc(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot);
        if (resolved is null)
        {
            // Victim died/despawned — drop target and revert to idle scan/wander.  Combat keeps
            // ticking; the expire gate in RunTraversalAi sends the guest home when time runs out.
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }
        var (victimMap, victimSlot, victimMn) = resolved.Value;

        if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, victimMap) is null)
        {
            // Victim slipped out of observable area — same drop-and-idle as victim-gone.
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }

        // AoS guest unreachable give-up — same 10s rule as the player-target guest path.  A guest
        // give-up unifies into ReturnTraversalHome (drop targets + relocate to spawn + vitals
        // refill); we never linger as an idle guest after the AoS chase fails.
        if (ShouldGiveUpUnreachableAosTarget(t, now))
        {
            ReturnTraversalHome(mapNum, listIndex, t);
            return;
        }

        // Cast decision first (Int>0 guests) — mirrors RunNpcVsNpcStep's magic→melee order. Consumes the tick
        // on a cast/kite/hold; a non-caster or out-of-range guest falls through to the melee + chase below.
        if (TryNpcMagicActionVsNpc(mapNum, listIndex, t, victimMap, victimSlot, victimMn, now))
        {
            if (!t.WantsKite) t.NextMoveMs = now + Constants.AiTickIntervalMs;
            return;
        }

        // Refresh combat each chase step for relentless behaviors (AoS, Guard) — AWA guests stay
        // yield-able so the combat-expire gate at the top of RunTraversalAi can eventually fire.
        if (npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.Guard)
        // On an OBSERVED map the fast legs pass (AdvanceGuestChaseStep) runs the WHOLE chase-STEP — same-map
        // AND cross-seam — at run/walk pace.  The legs skip UNWATCHED maps (RunMovement bails on observers==0)
        // and guests (unlike natives) are ticked everywhere, so on an unwatched map the brain still steps here
        // (both a same-map victim and a seam cross), keeping a guest closing on an NPC while nobody watches.
        if (_world.MapObservers[mapNum].Count == 0)
            StepGuestTowardObservableArea(mapNum, listIndex, t, victimMap, victimMn.X, victimMn.Y, victimMn.Layer,
                targetSize: _world.Npcs[victimMn.Num].EffectiveSize);
    }
}

using Mirage.Shared;
using Mirage.Server.Core.World;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The traversal-guest twin of <see cref="RunNoticedNpcStep"/>.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>Guest brain tick for a traversal NPC that has noticed another NPC. When that body
    /// dies, despawns, or leaves the guest's 9-map observable area the lock is dropped and the guest
    /// falls into idle (<see cref="RunGuestIdle"/>). Closing the gap runs on the legs pass
    /// (<see cref="AdvanceGuestChaseStep"/>) wherever anyone is watching; on an unwatched map the legs
    /// do not run, so the step is taken here instead — a guest keeps closing while nobody looks.</summary>
    private void RunGuestNoticedNpcStep(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        var resolved = _queries.ResolveNpc(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot);
        if (resolved is null)
        {
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }
        var (otherMap, _, otherMn) = resolved.Value;

        if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, otherMap) is null)
        {
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }

        // A guest that gives up unifies into ReturnTraversalHome (drop locks, relocate to spawn, refill
        // vitals) rather than lingering abroad as an idle guest.
        if (ShouldGiveUpUnreachedTarget(t, now))
        {
            ReturnTraversalHome(mapNum, listIndex, t);
            return;
        }

        if (_world.MapObservers[mapNum].Count == 0)
            StepGuestTowardObservableArea(mapNum, listIndex, t, otherMap, otherMn.X, otherMn.Y, otherMn.Layer,
                targetSize: _world.Npcs[otherMn.Num].EffectiveSize);
    }
}

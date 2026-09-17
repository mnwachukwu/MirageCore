using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The standoff stepper: how a <see cref="NpcBehavior.Shadow"/> NPC keeps the gap it wants.
///
/// <para>Neither of the other two answers. A pursuer closes until it is touching and a fleeing body runs
/// until it has forgotten you; this one closes to a distance and STOPS there, giving ground when
/// something walks into it and following when it walks away.</para></summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>Which side of its standoff a shadowing body is on this beat.</summary>
    private enum Station
    {
        /// <summary>Too far. Close, the way a pursuer would.</summary>
        Approach,

        /// <summary>Where it wants to be. Hold and face.</summary>
        Held,

        /// <summary>Too close. Give ground.</summary>
        Withdraw,
    }

    /// <summary>Reads the gap against the standoff, with <see cref="Constants.NpcStandoffSlackTiles"/> of
    /// neutral band either side so a body holds station instead of stepping on every beat its target
    /// does.</summary>
    private static Station StationFor(int gap, int standoff)
    {
        if (gap > standoff + Constants.NpcStandoffSlackTiles) return Station.Approach;
        if (gap < standoff - Constants.NpcStandoffSlackTiles) return Station.Withdraw;
        return Station.Held;
    }

    /// <summary>Legs-pass step for a shadowing NPC, toward a player or another creature alike.
    ///
    /// <para>Approaching runs on the same run/walk rule a chase does, because closing a gap is closing a
    /// gap. Withdrawing WALKS: a body giving a step of ground has not been startled, and a sprint
    /// backward would open far more than the tile it wanted.</para>
    ///
    /// <para>⚠ Cornered is a legitimate outcome and not an error. A body backed against a wall by
    /// something walking into it holds where it is and faces what is coming, as a cornered animal
    /// does, and as the retreat stepper already answers for a fleeing one.</para>
    ///
    /// <para>True once it is standing where it meant to stand, so the caller raises contact on the beat it
    /// arrives at the distance it wanted rather than on every beat it holds there.</para></summary>
    private bool TryLegsShadow(int mapNum, int slot, MapNpcRecord mn, int targetMap, int targetX, int targetY,
                               WorldLayer targetLayer, int targetSize, long now)
    {
        var npc = _world.Npcs[mn.Num];
        int gap = WorldDistanceTo(mapNum, mn.X, mn.Y, npc.EffectiveSize, targetMap, targetX, targetY, targetSize);
        // Outside the observable area entirely — the brain warp-follows or drops it, and stepping toward a
        // body whose distance is unknown would be stepping at random.
        if (gap == int.MaxValue) return false;

        // Acting on the gap IS reaching its quarry, whichever way it is about to step. Only the Held
        // branch used to say so, which meant a body giving ground every beat - because somebody kept
        // walking into it - never refreshed the clock and went home after ten seconds of doing exactly
        // what it was authored to do.
        mn.MarkReachedTarget(now);

        switch (StationFor(gap, npc.EffectiveStandoff))
        {
            case Station.Held:
                FaceNpcToward(mapNum, slot, mn, targetMap, targetX, targetY);
                mn.ChaseSprinting = false;
                return true;

            case Station.Withdraw:
                {
                    var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
                    var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
                    var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
                    // A step back is a step back: no sprint, and the step-clock advances at walking pace.
                    if (!TryFleeStepAwayFrom(mapNum, slot, mn, npcWX, npcWY, tw))
                    {
                        // Cornered. Face what walked into it and stand there.
                        FaceNpcToward(mapNum, slot, mn, targetMap, targetX, targetY);
                        return true;
                    }

                    mn.NextMoveMs = now + (long)MathF.Round(MovementFormulas.NpcWalkMsPerTile);
                    return false;
                }

            default:
                {
                    bool running = NpcWantsChaseRun(mn, npc, gap);
                    int beforeX = mn.X, beforeY = mn.Y;
                    mn.MoveType = running ? MovementType.Running : MovementType.Walking;
                    if (mn is TraversalNpcRecord guest)
                        StepGuestTowardObservableArea(mapNum, slot, guest, targetMap, targetX, targetY, targetLayer, targetSize: targetSize);
                    else
                        StepNpcTowardObservableArea(mapNum, slot, mn, targetMap, targetX, targetY, targetLayer, targetSize: targetSize);
                    FinishChaseStep(mn, npc.MoveSpeed, running, beforeX, beforeY, now);
                    return false;
                }
        }
    }
}

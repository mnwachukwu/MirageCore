using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The retreat stepper: how a <see cref="NpcBehavior.Flee"/> NPC backs away from whatever it
/// has noticed.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>One retreat step away from <paramref name="tw"/>, zig-zagged. Tries every direction
    /// except toward — straight-away first, then both perpendiculars — with a ~33% chance to lead
    /// with a perpendicular so the path is not a straight line an interceptor can read. Returns
    /// false only when all three are blocked (truly cornered), which leaves the caller to stand and
    /// face.</summary>
    private bool TryFleeStepAwayFrom(int mapNum, int slot, MapNpcRecord mn,
                                     int npcWX, int npcWY, (int worldX, int worldY)? tw)
    {
        if (tw is null) return false;   // whoever it noticed slipped outside the observable area — no direction to compute
        int tgtWX = tw.Value.worldX;
        int tgtWY = tw.Value.worldY;

        // Direction TOWARD, so away is its opposite. The three retreat candidates are every cardinal
        // EXCEPT toward: straight-away, and the two perpendiculars (perpAway leaves the other body's
        // secondary-axis side; perpOther is its opposite).
        Direction towardDir = WorldCoordHelper.WorldDirectionFrom(npcWX, npcWY, tgtWX, tgtWY);
        Direction awayDir = OppositeDir(towardDir);
        Direction perpAwayDir = PerpAwayDir(towardDir, tgtWX - npcWX, tgtWY - npcWY);
        // Computed from the ORIGINAL perpAwayDir, before the zigzag swap below, so it is always the
        // other perpendicular — never accidentally OppositeDir(awayDir) == towardDir.
        Direction perpOtherDir = OppositeDir(perpAwayDir);

        Direction firstDir = awayDir, secondDir = perpAwayDir;
        if (Rng.Next(3) == 0)
            (firstDir, secondDir) = (perpAwayDir, awayDir);

        return StepNpc(mapNum, slot, mn, firstDir)
               || StepNpc(mapNum, slot, mn, secondDir)
               || StepNpc(mapNum, slot, mn, perpOtherDir);
    }

    /// <summary>Legs-pass retreat for a fleeing NPC: keeps opening the gap at the SPD run cadence
    /// (walking once SP runs out) while the brain holds the notice. <paramref name="slot"/> is the
    /// native slot or the guest list index.</summary>
    private void TryLegsFlee(int mapNum, int slot, MapNpcRecord mn, int targetMap, int targetX, int targetY, long now)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        int moveSpeed = _world.Npcs[mn.Num].MoveSpeed;                       // capture before the step — a native-to-guest cross zeroes mn.Num
        const bool running = true;
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        if (!TryFleeStepAwayFrom(mapNum, slot, mn, npcWX, npcWY, tw))
        {
            mn.MoveType = MovementType.Walking;
            return;
        }
        mn.MoveType = MovementType.Walking;
        mn.NextMoveMs = now + (long)MathF.Round(running ? MovementFormulas.NpcRunMsPerTile(moveSpeed) : MovementFormulas.NpcWalkMsPerTile);
    }

    private static Direction OppositeDir(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        _ => d,
    };

    /// <summary>Perpendicular-axis direction pointing away from the other body. If the primary toward
    /// axis is horizontal, picks Up/Down by its Y; if vertical, Left/Right by its X. When the
    /// perpendicular delta is zero BOTH perpendiculars are equally "away", so pick one at RANDOM. A
    /// fixed default here would funnel wall-pinned NPCs toward one corner: one shoved against a side
    /// wall could only sidestep Down, one against the top or bottom wall could only sidestep Right.
    /// Randomizing the tie alone removes the drift without changing which tiles the retreat can
    /// reach.</summary>
    private Direction PerpAwayDir(Direction primaryToward, int dx, int dy)
    {
        bool primaryHorizontal = primaryToward == Direction.Left || primaryToward == Direction.Right;
        if (primaryHorizontal)
            return dy > 0 ? Direction.Up : dy < 0 ? Direction.Down : RandomPerpendicular(primaryToward);
        return dx > 0 ? Direction.Left : dx < 0 ? Direction.Right : RandomPerpendicular(primaryToward);
    }
}

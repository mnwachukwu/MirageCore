using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Advances XOffset/YOffset for all players and NPCs toward 0 each frame,
/// producing smooth tile-to-tile movement interpolation.
///
/// Advances each entity's per-tile pixel offset as it slides between tiles.
/// Timing: at 50ms/frame (20fps) and WalkSpeed=4px/frame → 32/4×50 = 400ms/tile.
/// Float delta-time ensures this is exact at any frame rate, with no integer truncation.
///
/// SPD → run-speed: the LOCAL player's run interpolation (which also gates how soon the next tile-move can
/// fire, since a move waits for the offset to clear) is scaled by their SPD via
/// <see cref="MovementFormulas.RunMsPerTile"/>, so a higher-SPD build kites/closes a slower one.  Other
/// players and NPCs keep the base run interpolation — their true positions arrive as server/move packets, so
/// only their local slide visual (not the gap-control gameplay) uses the default rate.
/// </summary>
public static class MovementProcessor
{
    public static void Process(ClientState state, float deltaMs)
    {
        // The local player's SPD-scaled run speed (base for everyone else — visual-only interpolation).
        float myRunMs = state.MyIndex > 0
            ? MovementFormulas.RunMsPerTile(state.Players[state.MyIndex].Spd)
            : MovementFormulas.BaseRunMsPerTile;

        // Players
        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            var p = state.Players[i];
            if (p.Name.Length == 0) continue;
            AdvanceOffset(p, deltaMs, i == state.MyIndex ? myRunMs : MovementFormulas.BaseRunMsPerTile);
        }

        // Center-map NPCs
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var n = state.MapNpcs[i];
            if (n.Num == 0) continue;
            AdvanceNpcOffset(n, deltaMs, NpcRunMs(state, n.Num));
        }

        // Neighbor-map NPCs — interpolate their move offsets too, else they animate choppily.
        // Skip cells with no loaded map (world edges, not-yet-loaded neighbors): their NPC arrays are
        // empty, so there's nothing to advance — avoids 8×MaxMapNpcs no-op iterations per frame.
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (c == 1 && r == 1) continue;
                if (state.NeighborMaps[c, r] is null) continue;
                var cell = state.NeighborNpcs[c, r];
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = cell[i];
                    if (n.Num == 0) continue;
                    AdvanceNpcOffset(n, deltaMs, NpcRunMs(state, n.Num));
                }
            }
        }

        // Visiting (chasing) NPCs interpolate identically.
        foreach (var t in state.TraversalNpcs.Values)
        {
            if (t.Num == 0) continue;
            AdvanceNpcOffset(t, deltaMs, NpcRunMs(state, t.Num));
        }
    }

    private static void AdvanceOffset(PlayerRecord p, float deltaMs, float runMsPerTile)
    {
        if (p.XOffset == 0f && p.YOffset == 0f)
        {
            // Nothing in flight. A carry held across a pause would make the next step jump, so it goes.
            p.SlideCarryMs = 0f;
            if (p.Moving != MovementType.Walking && p.Moving != MovementType.Running)
                return;
            p.Moving = MovementType.None;
            return;
        }

        float perTile = p.Moving == MovementType.Running ? runMsPerTile : MovementFormulas.BaseWalkMsPerTile;
        // Time left over from the tile that finished mid-frame comes back here rather than being dropped.
        float ms = deltaMs + p.SlideCarryMs;
        p.SlideCarryMs = 0f;
        float step = Constants.PicX / perTile * ms;

        // How much slide is actually left. When the step covers it, the tile lands exactly on zero and the
        // unused remainder is banked as time, so the next tile starts where this one really ended.
        float remaining = MathF.Max(MathF.Abs(p.XOffset), MathF.Abs(p.YOffset));
        if (step >= remaining)
        {
            p.XOffset = 0f;
            p.YOffset = 0f;
            p.SlideCarryMs = MathF.Min((step - remaining) * perTile / Constants.PicX, perTile);
            p.Moving = MovementType.None;
            return;
        }

        if (p.XOffset < 0f) p.XOffset += step;
        else if (p.XOffset > 0f) p.XOffset -= step;

        if (p.YOffset < 0f) p.YOffset += step;
        else if (p.YOffset > 0f) p.YOffset -= step;
    }

    // A chasing NPC's run-slide is a FLAT baseline (NpcRunMsPerTile is SPD-independent now — players outrun
    // NPCs by speccing SPD, NPCs don't scale), so the slide simply matches the server's flat run cadence.  The
    // flat 200 ms divides the movement tick cleanly, so the slide ends as the next step lands — no snap.
    private static float NpcRunMs(ClientState state, int num)
    {
        _ = state;
        _ = num;  // NPC run speed is flat — SPD does not scale it; params kept for signature stability
        return MovementFormulas.NpcRunMsPerTile(0);
    }

    private static void AdvanceNpcOffset(ClientMapNpc n, float deltaMs, float runMsPerTile)
    {
        if (n.XOffset == 0f && n.YOffset == 0f) return;

        // NPCs walk on a tick-matched slide (NpcWalkMsPerTile == server AI tick) so a chasing/striding NPC
        // glides continuously instead of freezing between steps.  A running (chasing) NPC slides at its
        // SPD-scaled run pace (runMsPerTile) so it matches the server's SPD-paced step cadence — no jump/gap.
        float step = Constants.PicX / (n.Moving == MovementType.Running ? runMsPerTile : MovementFormulas.NpcWalkMsPerTile) * deltaMs;

        if (n.XOffset < 0f) n.XOffset = MathF.Min(n.XOffset + step, 0f);
        else if (n.XOffset > 0f) n.XOffset = MathF.Max(n.XOffset - step, 0f);

        if (n.YOffset < 0f) n.YOffset = MathF.Min(n.YOffset + step, 0f);
        else if (n.YOffset > 0f) n.YOffset = MathF.Max(n.YOffset - step, 0f);

        if (n.XOffset == 0f && n.YOffset == 0f)
            n.Moving = MovementType.None;
    }
}

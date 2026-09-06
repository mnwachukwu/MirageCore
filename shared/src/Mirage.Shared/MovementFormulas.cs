namespace Mirage.Shared;

/// <summary>SPD → movement-speed math (the gap-control mechanic).  SPD scales RUN speed only — walk stays a
/// fixed baseline — so a higher-SPD build kites/closes a slower one.  BASELINE-PRESERVING: the base run speed
/// is a hard FLOOR (a 0-SPD / low-level build runs at exactly today's 5 t/s, never slower — the earlier
/// both-ways power curve made low levels crawl), and SPD is a pure additive bonus that raises run speed
/// LINEARLY up to a cap.  Shared so the client (local player) and server compute the player run identically.
/// NPC run is a separate FLAT baseline (no SPD scaling) — see <see cref="NpcRunMsPerTile"/>.</summary>
public static class MovementFormulas
{
    /// <summary>Base run / walk ms-per-tile at zero SPD (run 8 px/frame, walk 4 px/frame @ 50 ms).
    /// Base run is also the SLOW FLOOR: SPD never makes you slower than this.</summary>
    public const float BaseRunMsPerTile = 200f;
    public const float BaseWalkMsPerTile = 400f;

    /// <summary>NPC walk-slide ms-per-tile.  Bound to the server AI tick (<see cref="Constants.AiTickIntervalMs"/>):
    /// a moving NPC is issued one walk step per AI tick, so sliding each step over exactly that interval makes NPC
    /// walking GAPLESS (the slide finishes as the next step arrives) instead of stuttering.  At the 500 ms AI tick
    /// this is a hair slower than the player's <see cref="BaseWalkMsPerTile"/> (400 ms) walk — intended, so players
    /// can always step away from a walking NPC.  This is the WALK cadence only — a chasing NPC's run steps
    /// are issued by the faster run-chase movement pass, not by this tick.</summary>
    public const float NpcWalkMsPerTile = Constants.AiTickIntervalMs;

    // Run speed rises LINEARLY with SPD from 1.0x (at 0 SPD) to MaxRunSpeedMult (at SpdAtMaxRunSpeed), then caps.
    // Gentle on purpose: SPD already compounds through the SP pool (bigger reserve + longer runs).  Both the cap
    // and the SPD needed to reach it are playtest-tuned (target: a big SPD edge shifts a duel ~60-70%, not 90%+).
    private const float MaxRunSpeedMult = 1.5f;    // top run speed = 1.5x base (7.5 t/s → 133 ms/tile fast cap)
    private const float SpdAtMaxRunSpeed = 150f;    // SPD at which the cap is reached (a heavy SPD investment)

    /// <summary>Run ms-per-tile for a given SPD.  Speed multiplier = <c>1 + (0.5) * min(spd / 150, 1)</c> (linear,
    /// floored at 1.0 so run is never slower than the base, capped at 1.5x); run ms = <c>200 / multiplier</c>.
    /// So 0 SPD = 200 ms (today's 5 t/s), 75 SPD = 160 ms (~6.25 t/s), 150+ SPD = 133 ms (7.5 t/s cap).</summary>
    public static float RunMsPerTile(int spd)
    {
        float mult = 1f + (MaxRunSpeedMult - 1f) * Math.Min(Math.Max(spd, 0) / SpdAtMaxRunSpeed, 1f);
        return BaseRunMsPerTile / mult;
    }

    /// <summary>What SPD adds to a sprint, as a percent bonus over the sprint everyone starts with, for
    /// display ("Sprint"): +0% at 0 SPD, rising to +50% at <see cref="SpdAtMaxRunSpeed"/> and stopping there.
    ///
    /// <para>Measured against the 0-SPD RUN, so the number and the cap are the same statement: the cap is
    /// 1.5x base run, and the label reads +50%. A bonus that starts at zero also cannot be read as a
    /// multiplier — the reader is being told what their SPD bought, not how fast they move.</para></summary>
    public static int SprintBonusPercent(int spd) =>
        (int)Math.Round((BaseRunMsPerTile / RunMsPerTile(spd) - 1f) * 100f, MidpointRounding.AwayFromZero);

    // NPCs run at a FLAT baseline — the player's 0-SPD run speed — with NO SPD scaling.  A player outruns any
    // chasing NPC purely by investing SPD (their run drops below this base while the NPC stays pinned at it), so
    // a moving player can always pull away by speccing speed.  SPD for an NPC therefore governs only run
    // DURATION (its SP pool, drained per tile), never top speed.  Because the cadence is a flat 200 ms it divides
    // any movement tick cleanly, so the client slide matches server delivery with no snap — no quantization
    // helper needed.  Kept as a spd-taking method so SPD-scaled NPC run can be restored in one line (mirror
    // RunMsPerTile) if that's ever wanted again.
    private const float NpcBaseRunMsPerTile = 200f;   // NPC run = the player's base run (5 t/s), flat

    /// <summary>Run ms-per-tile for a chasing NPC — a FLAT baseline (the player's 0-SPD run), SPD-independent.
    /// Any player who invests SPD outruns any NPC; an NPC's SPD buys run stamina (SP pool), not speed.</summary>
    public static float NpcRunMsPerTile(int spd)
    {
        _ = spd;   // SPD deliberately does NOT scale NPC run speed (flat) — see the note above
        return NpcBaseRunMsPerTile;
    }
}

namespace Mirage.Shared;

/// <summary>
/// How long one tile takes.
///
/// <para><b>Shared because the client predicts the move and the server bills it.</b> A player's own step
/// is drawn locally the moment the key goes down and confirmed by the server a round trip later; if the
/// two computed the pace differently the character would rubber-band on every step. So this is one
/// answer read from both sides, not two that agree today.</para>
///
/// <para><b>Baseline-preserving.</b> The base run is a hard FLOOR — a body with no
/// <see cref="Records.PlayerRecord.MoveSpeed"/> at all runs at exactly the base pace, never slower —
/// and move speed is a pure additive bonus on top of it, rising linearly to a cap. A world that sets no
/// speed anywhere is therefore a world where everything moves at the same honest pace, which is what a
/// game with no notion of speed should get.</para>
/// </summary>
public static class MovementFormulas
{
    /// <summary>Base run / walk ms-per-tile at zero move speed. Base run is also the SLOW FLOOR: move
    /// speed never makes a body slower than this.</summary>
    public const float BaseRunMsPerTile = 200f;
    public const float BaseWalkMsPerTile = 400f;

    /// <summary>NPC walk-slide ms-per-tile. Bound to the server AI tick
    /// (<see cref="Constants.AiTickIntervalMs"/>): a moving NPC is issued one walk step per AI tick, so
    /// sliding each step over exactly that interval makes NPC walking GAPLESS — the slide finishes as the
    /// next step arrives — instead of stuttering. At the 500 ms AI tick this is a hair slower than
    /// <see cref="BaseWalkMsPerTile"/>, which is intended: a player can always step away from a walking
    /// NPC. This is the WALK cadence only; a chasing NPC's run steps come from the faster chase pass.</summary>
    public const float NpcWalkMsPerTile = Constants.AiTickIntervalMs;

    /// <summary>Move speed at which <see cref="MaxRunSpeedMult"/> is reached, and the multiplier there.
    ///
    /// <para>The two together are the whole shape of the curve, and they are deliberately gentle: a
    /// speed advantage should decide a chase, not end it before it starts. A game wanting a different
    /// curve reads <see cref="RunMsPerTile"/>'s inputs and writes its own — these are Core's defaults,
    /// not a rule.</para></summary>
    private const float MaxRunSpeedMult = 1.5f;
    private const float MoveSpeedAtMaxRun = 150f;

    /// <summary>Run ms-per-tile for a given move speed. The multiplier is
    /// <c>1 + 0.5 * min(moveSpeed / 150, 1)</c> — linear, floored at 1.0 so a run is never slower than
    /// the base, capped at 1.5x — and the pace is <c>200 / multiplier</c>. So 0 gives 200 ms (5 tiles a
    /// second), 75 gives 160 ms, and 150 or more gives the 133 ms cap.</summary>
    public static float RunMsPerTile(int moveSpeed)
    {
        float mult = 1f + (MaxRunSpeedMult - 1f) * Math.Min(Math.Max(moveSpeed, 0) / MoveSpeedAtMaxRun, 1f);
        return BaseRunMsPerTile / mult;
    }

    // NPCs run at a FLAT baseline — the zero-speed run — with NO scaling. A body that invests in speed
    // therefore outruns any chasing NPC, which is what keeps a chase escapable: the player's pace drops
    // below this while the NPC stays pinned at it. Because the cadence is a flat 200 ms it divides any
    // movement tick cleanly, so the client slide matches server delivery with no snap. Kept as a
    // speed-taking method so scaled NPC run can be restored in one line by mirroring RunMsPerTile.
    private const float NpcBaseRunMsPerTile = 200f;

    /// <summary>Run ms-per-tile for a chasing NPC — a FLAT baseline, independent of its move speed.
    /// Any body that invests in speed outruns any NPC.</summary>
    public static float NpcRunMsPerTile(int moveSpeed)
    {
        _ = moveSpeed;   // deliberately does NOT scale NPC run — see the note above
        return NpcBaseRunMsPerTile;
    }
}

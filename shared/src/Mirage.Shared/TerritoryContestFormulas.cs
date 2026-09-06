namespace Mirage.Shared;

/// <summary>Pure king-of-the-hill contest math: how many capture points a territory gets, the
/// signed capture-meter physics, per-tick scoring with the defender edge, and final-winner resolution. Kept
/// side-effect-free so the contest engine (GuildTerritorySystem) is thin glue over unit-tested rules.</summary>
public static class TerritoryContestFormulas
{
    /// <summary>Capture-point labels, in order (the first N are used for a contest).</summary>
    public static readonly string[] PointLabels = { "Alpha", "Bravo", "Charlie", "Delta", "Echo" };

    /// <summary>Number of capture points for a territory of <paramref name="mapCount"/> maps: 1 per
    /// <see cref="Constants.TerritoryCapturePointMapsPer"/>, clamped to [min, max].</summary>
    public static int PointCount(int mapCount) => Math.Clamp(
        mapCount / Constants.TerritoryCapturePointMapsPer,
        Constants.TerritoryMinCapturePoints, Constants.TerritoryMaxCapturePoints);

    /// <summary>Whether (x,y) is within <paramref name="radius"/> tiles (Euclidean, inclusive) of a capture
    /// point at (cx,cy) on the same map — the single source for "in a capture radius" (scoring, setup walls,
    /// push-out).</summary>
    public static bool WithinRadius(int x, int y, int cx, int cy, int radius)
    {
        int dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>The result of one capture-meter tick: the new meter and the (possibly flipped) owner + the
    /// guild currently pushing the point.</summary>
    public readonly record struct MeterResult(int Meter, int Owner, int Challenger);

    /// <summary>Advance a point's capture meter one 5s tick given <paramref name="majorityGuild"/> — the
    /// strict-plurality guild standing in its radius this tick (0 = contested/empty). The owner pushes the
    /// meter toward secure ownership (-Full); a challenger pushes it up (+1); a contested/empty point drifts
    /// toward neutral (0). Reaching +Full flips the point to the challenger (reset to -Full). Pure.</summary>
    public static MeterResult AdvanceMeter(int meter, int owner, int challenger, int majorityGuild)
    {
        int full = Constants.TerritoryCaptureFull;

        if (majorityGuild == 0)   // contested (tie for top) or empty: drift toward neutral
        {
            int m = meter > 0 ? meter - 1 : meter < 0 ? meter + 1 : 0;
            return new MeterResult(m, owner, m == 0 ? 0 : challenger);
        }
        if (majorityGuild == owner)   // owner reinforces: push back toward secure hold
        {
            int m = Math.Max(-full, meter - 1);
            return new MeterResult(m, owner, m <= 0 ? 0 : challenger);
        }
        // A challenger (not the owner) pushes toward capture — keeping the meter position, so a full
        // owner→challenger swing spans 2*Full ticks even if the pushing guild changes mid-swing.
        int pushed = meter + 1;
        if (pushed >= full)
            return new MeterResult(-full, majorityGuild, 0);   // captured: new owner, now securely held
        return new MeterResult(pushed, owner, majorityGuild);
    }

    /// <summary>The guild that scores this point this tick, or 0 (neutral). An owned point scores only while
    /// its meter is at/below -NeutralBand (securely held); the band around 0 scores nobody. An unowned
    /// (owner 0) point never scores.</summary>
    public static int ScorerThisTick(int meter, int owner) =>
        owner != 0 && meter <= -Constants.TerritoryCaptureNeutralBand ? owner : 0;

    /// <summary>Points added this tick for a scoring point: the base per-tick, plus the defender edge when the
    /// scorer is the territory's defender (a held point pays 2/tick vs an attacker's 1/tick).</summary>
    public static int ScoreDelta(int scorerGuild, int defenderGuild)
    {
        if (scorerGuild == 0) return 0;
        int s = Constants.TerritoryOwnedScorePerTick;
        if (scorerGuild == defenderGuild) s += Constants.TerritoryDefenderScoreBonus;
        return s;
    }

    /// <summary>The winning guild of a finished contest (0 = stays/becomes unclaimed). The strict top scorer
    /// wins; any tie goes to the defender (<paramref name="defenderGuild"/> > 0) or, for an unclaimed
    /// contest (defender 0), stays unclaimed. (The unclaimed-tie war-kills tiebreak arrives with combat
    /// integration.) An empty score set keeps the defender.</summary>
    public static int DetermineWinner(IReadOnlyDictionary<int, long> scores, int defenderGuild)
    {
        if (scores.Count == 0) return defenderGuild;
        long max = long.MinValue;
        foreach (long v in scores.Values) if (v > max) max = v;
        int topGuild = 0, topCount = 0;
        foreach (var kv in scores) if (kv.Value == max) { topGuild = kv.Key; topCount++; }
        return topCount == 1 ? topGuild : defenderGuild;   // tie → defender keeps (0 = stays unclaimed)
    }
}

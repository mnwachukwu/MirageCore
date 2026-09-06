namespace Mirage.Shared;

/// <summary>Pure death/respawn timing: the escalating, decaying, capped non-war respawn
/// penalty. The guild-war override (a flat timer) is applied by the caller, not here.</summary>
public static class DeathFormulas
{
    /// <summary>The penalty-step count for a death: decay the previous steps by the whole minutes elapsed
    /// since the last death (10s of penalty shed per minute = 1 step), add one for this death, and clamp to
    /// [1, <see cref="Constants.RespawnMaxPenaltySteps"/>]. A first death (no prior death time) is 1 step.</summary>
    public static int NextPenaltySteps(int prevSteps, long lastDeathUtc, long nowUtc)
    {
        long minutes = lastDeathUtc > 0 && nowUtc > lastDeathUtc ? (nowUtc - lastDeathUtc) / 60 : 0;
        int decayed = prevSteps - (int)Math.Min(minutes, prevSteps);   // shed one step per minute, never below 0
        return Math.Clamp(decayed + 1, 1, Constants.RespawnMaxPenaltySteps);
    }

    /// <summary>The respawn delay in seconds for a step count (steps x the 10s base).</summary>
    public static int RespawnDelaySeconds(int steps) => steps * Constants.RespawnPenaltyStepSeconds;
}

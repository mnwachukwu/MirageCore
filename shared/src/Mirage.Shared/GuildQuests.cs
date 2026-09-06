namespace Mirage.Shared;

/// <summary>Pure guild-quest math: pick a target NPC weighted toward the guild's level, and scale the
/// kill count + rewards by mob difficulty and guild level. "Difficulty" is an NPC's Str+Def+Int (the
/// same stats that drive its EXP value). All bounds are named <see cref="Constants"/> (playtest-tunable).</summary>
public static class GuildQuests
{
    /// <summary>Weighted-random pick from <paramref name="candidates"/> (NPC number + difficulty), biased
    /// toward difficulties near the guild's level target — a higher-level guild trends toward tougher
    /// mobs, but not strictly. <paramref name="roll01"/> is a [0,1) roll. Returns 0 if empty.</summary>
    public static int PickQuestNpc(IReadOnlyList<(int NpcId, int Difficulty)> candidates, int guildLevel, double roll01)
    {
        if (candidates.Count == 0) return 0;
        int minD = int.MaxValue, maxD = int.MinValue;
        foreach (var (_, diff) in candidates)
        {
            if (diff < minD) minD = diff;
            if (diff > maxD) maxD = diff;
        }
        // The guild's level places its target across the difficulty range (L0 → easiest, max → hardest).
        double target = minD + (maxD - minD) * (guildLevel / (double)Constants.GuildMaxLevel);

        double total = 0;
        var weights = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = 1.0 / (1.0 + Math.Abs(candidates[i].Difficulty - target));   // peaks at target, falls off with distance
            total += weights[i];
        }
        double pick = roll01 * total;
        double acc = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            acc += weights[i];
            if (pick < acc) return candidates[i].NpcId;
        }
        return candidates[^1].NpcId;
    }

    /// <summary>The gold a Leader pays to acquire a quest: <see cref="Constants.GuildQuestCostPerLevel"/> per
    /// guild level (L0 = free). Centralized here so the reward can guarantee it beats the cost.</summary>
    public static long AcquireCost(int guildLevel) =>
        Math.Max(0, guildLevel) * (long)Constants.GuildQuestCostPerLevel;

    /// <summary>A quest's random size factor from a [0,1) roll: maps to
    /// [1 - <see cref="Constants.GuildQuestVariationPercent"/>%, 1 + that%], so 0.5 = the flat baseline. The
    /// SAME roll drives the kill count AND the rewards, so a quest that asks for more kills pays proportionally
    /// more (and one that asks fewer pays less) — the variation the objective and rewards share.</summary>
    public static double VariationFactor(double roll01)
    {
        double v = Constants.GuildQuestVariationPercent / 100.0;
        return 1.0 - v + Math.Clamp(roll01, 0.0, 1.0) * 2.0 * v;
    }

    /// <summary>Kill count for a quest against a mob of the given difficulty and a [0,1) variation
    /// <paramref name="roll01"/>: a baseline that scales UP with difficulty (tougher mobs → more kills),
    /// spread +/- by the roll (so it's not a flat, linear count). A normal mob uses the big curve (hundreds of
    /// kills, clamped to <see cref="Constants.GuildQuestMaxKills"/>); a <paramref name="isBoss"/> mob uses a
    /// COMPRESSED curve (tens, clamped to <see cref="Constants.GuildQuestBossMaxKills"/>) so "kill hundreds of
    /// bosses" can never happen — while still sloping with the boss's strength.</summary>
    public static int KillCount(int difficulty, double roll01, bool isBoss)
    {
        double baseline = isBoss
            ? Constants.GuildQuestBossBaseKills + Math.Max(0, difficulty) * Constants.GuildQuestBossKillsPer100Difficulty / 100.0
            : Constants.GuildQuestBaseKills + Math.Max(0, difficulty) * Constants.GuildQuestKillsAddedPerDifficulty;
        int max = isBoss ? Constants.GuildQuestBossMaxKills : Constants.GuildQuestMaxKills;
        int kills = (int)Math.Round(baseline * VariationFactor(roll01), MidpointRounding.AwayFromZero);
        return Math.Clamp(kills, 1, max);
    }

    /// <summary>Guild XP a quest awards on completion — scaled by guild level (to chase the ballooning level
    /// curve), mob difficulty, and the same <paramref name="roll01"/> variation as the kill count (more kills
    /// asked → more XP). A <paramref name="isBoss"/> quest pays <see cref="Constants.GuildQuestBossRewardPercent"/>%
    /// of that (fewer kills → slighter reward). At MAX guild level XP is worthless, so it is eschewed entirely
    /// (0) — the gold bonus in <see cref="RewardGold"/> replaces it.</summary>
    public static long RewardExp(int difficulty, int guildLevel, double roll01, bool isBoss)
    {
        if (guildLevel >= Constants.GuildMaxLevel) return 0;   // can't level: no XP (gold bonus stands in)
        double baseExp = Constants.GuildQuestBaseExp * (guildLevel + 1) + (long)Math.Max(0, difficulty) * Constants.GuildQuestExpPerDifficulty;
        double reward = baseExp * VariationFactor(roll01);
        if (isBoss) reward = reward * Constants.GuildQuestBossRewardPercent / 100.0;
        return (long)Math.Round(reward, MidpointRounding.AwayFromZero);
    }

    /// <summary>Vault gold a quest awards on completion — scaled the same way as the XP: a <paramref name="isBoss"/>
    /// quest pays <see cref="Constants.GuildQuestBossRewardPercent"/>%, and at MAX guild level it gets a
    /// <see cref="Constants.GuildQuestMaxLevelGoldBonusPercent"/>% bump (standing in for the eschewed XP). Always
    /// at least the acquire cost plus a base margin so completing a quest is a net vault gain (never a loss).
    ///
    /// <para>Keyed on GUILD level only, like the acquire cost it has to beat: anything paid from a vault
    /// must not be priced by whichever member happened to click — see <see cref="GuildWarFormulas"/>.</para></summary>
    public static long RewardGold(int difficulty, int guildLevel, double roll01, bool isBoss)
    {
        double baseGold = Constants.GuildQuestBaseGold * (guildLevel + 1) + (long)Math.Max(0, difficulty) * Constants.GuildQuestGoldPerDifficulty;
        double reward = baseGold * VariationFactor(roll01);
        if (isBoss) reward = reward * Constants.GuildQuestBossRewardPercent / 100.0;
        if (guildLevel >= Constants.GuildMaxLevel) reward = reward * (100 + Constants.GuildQuestMaxLevelGoldBonusPercent) / 100.0;
        long final = (long)Math.Round(reward, MidpointRounding.AwayFromZero);
        return Math.Max(final, AcquireCost(guildLevel) + Constants.GuildQuestBaseGold);   // guaranteed > cost
    }
}

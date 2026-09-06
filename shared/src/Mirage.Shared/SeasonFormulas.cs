namespace Mirage.Shared;

/// <summary>Pure seasonal-leaderboard math: the weekly hold score with its consecutive-week
/// bonus, season-week counting, and the placing payout table. Side-effect-free so the season settlement and
/// its tests share one source of truth.</summary>
public static class SeasonFormulas
{
    /// <summary>Whole weeks elapsed from <paramref name="start"/> to <paramref name="today"/> (never negative).</summary>
    public static int WeeksElapsed(DateOnly start, DateOnly today) =>
        Math.Max(0, (today.DayNumber - start.DayNumber) / 7);

    /// <summary>Leaderboard points a guild earns for holding a territory this week, held
    /// <paramref name="weeksHeld"/> consecutive weeks: the base per week times a compounding hold bonus
    /// (+<see cref="Constants.TerritorySeasonHoldBonusPercentPerWeek"/>% per streak week, capped at
    /// <see cref="Constants.TerritorySeasonHoldBonusCapWeeks"/>). Rounded to a whole point.</summary>
    public static long WeeklyHoldScore(int weeksHeld)
    {
        int streak = Math.Clamp(weeksHeld, 0, Constants.TerritorySeasonHoldBonusCapWeeks);
        double mult = 1.0 + Constants.TerritorySeasonHoldBonusPercentPerWeek / 100.0 * streak;
        return (long)Math.Round(Constants.TerritorySeasonPointsPerWeek * mult, MidpointRounding.AwayFromZero);
    }

    /// <summary>The season-end payout for a guild finishing at <paramref name="placing"/> (1-based rank among
    /// the SCORING guilds): the per-active-member gold and the guild-vault gold. A non-scorer (placing <= 0)
    /// gets nothing; 4th and below get the flat "scorer" payout.</summary>
    public static (long Member, long Vault) PlacingPayout(int placing) => placing switch
    {
        1 => (Constants.TerritorySeason1stMemberGold, Constants.TerritorySeason1stVaultGold),
        2 => (Constants.TerritorySeason2ndMemberGold, Constants.TerritorySeason2ndVaultGold),
        3 => (Constants.TerritorySeason3rdMemberGold, Constants.TerritorySeason3rdVaultGold),
        >= 4 => (Constants.TerritorySeasonScorerMemberGold, Constants.TerritorySeasonScorerVaultGold),
        _ => (0, 0),
    };

    /// <summary>Whether a member counts as ACTIVE for the season payout: online for at least the
    /// required seconds within the trailing window. Reads the rolling <c>GuildMember.ActiveSeconds</c> +
    /// <c>LastSeenUtc</c>; the window bound also discards a member who has gone quiet since. Pure.</summary>
    public static bool IsActiveMember(long activeSeconds, long lastSeenUtc, long nowUtc) =>
        lastSeenUtc > 0
        && nowUtc - lastSeenUtc <= Constants.GuildActiveMemberWindowSeconds
        && activeSeconds >= Constants.GuildActiveMemberMinSeconds;
}

using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>Pure guild-leveling math: maps cumulative guild XP to a level and back, against the
/// ballooning curve in <see cref="Constants"/> (levels 0-<see cref="Constants.GuildMaxLevel"/>).</summary>
public static class GuildLeveling
{
    /// <summary>The level a guild with <paramref name="exp"/> cumulative XP has reached (0-5).</summary>
    public static int LevelForExp(long exp)
    {
        if (exp >= Constants.GuildLevel5Exp) return 5;
        if (exp >= Constants.GuildLevel4Exp) return 4;
        if (exp >= Constants.GuildLevel3Exp) return 3;
        if (exp >= Constants.GuildLevel2Exp) return 2;
        if (exp >= Constants.GuildLevel1Exp) return 1;
        return 0;
    }

    /// <summary>Cumulative XP needed to REACH <paramref name="level"/> (0 for level 0; clamped to the max
    /// tier above it). Used to show progress toward the next level.</summary>
    public static long ExpForLevel(int level) => level switch
    {
        <= 0 => 0,
        1 => Constants.GuildLevel1Exp,
        2 => Constants.GuildLevel2Exp,
        3 => Constants.GuildLevel3Exp,
        4 => Constants.GuildLevel4Exp,
        _ => Constants.GuildLevel5Exp,
    };
}

/// <summary>Whether a guild's level perks are currently in force. A perk applies only when the guild
/// exists, is at/above the perk's unlock level, AND its weekly tax is paid
/// (<see cref="GuildRecord.PerksActive"/>, toggled by the daily settlement). Single source of truth for
/// every perk gate (drop rate, wear/reagent skip, bonus EXP, double drop, vault gold).</summary>
public static class GuildPerks
{
    public static bool IsActive(GuildRecord? guild, int perkLevel)
        => guild is not null && guild.PerksActive && guild.Level >= perkLevel;
}

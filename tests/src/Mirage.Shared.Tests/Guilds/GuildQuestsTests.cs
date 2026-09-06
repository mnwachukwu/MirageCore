using NUnit.Framework;

namespace Mirage.Shared.Tests.Guilds;

/// <summary>Guild-quest math, including the boss and max-level scaling: the level-weighted NPC pick, the
/// difficulty/level-scaled kill count + rewards, the compressed BOSS curve, and the max-level XP=0/gold-bonus.</summary>
[TestFixture]
public class GuildQuestsTests
{
    [Test]
    public void PickQuestNpc_HigherGuildLevel_FavorsTougherMobs()
    {
        var candidates = new List<(int, int)> { (1, 10), (2, 100) };   // an easy mob and a tough one
        // Same mid roll: a low-level guild lands on the easy mob, a max-level guild on the tough one.
        Assert.That(GuildQuests.PickQuestNpc(candidates, guildLevel: 0, roll01: 0.5), Is.EqualTo(1));
        Assert.That(GuildQuests.PickQuestNpc(candidates, guildLevel: Constants.GuildMaxLevel, roll01: 0.5), Is.EqualTo(2));
    }

    [Test]
    public void PickQuestNpc_Empty_ReturnsZero()
    {
        Assert.That(GuildQuests.PickQuestNpc(new List<(int, int)>(), 0, 0.5), Is.EqualTo(0));
    }

    // roll 0.5 = the flat baseline (VariationFactor == 1), so these lock the un-varied scaling.
    private const double Mid = 0.5;
    // A mid guild level: below max, so XP is awarded (the max-level path zeroes it).
    private const int MidLevel = 2;

    [Test]
    public void KillCount_Normal_RisesWithDifficulty_Capped()
    {
        Assert.That(GuildQuests.KillCount(0, Mid, isBoss: false), Is.EqualTo(Constants.GuildQuestBaseKills));   // weakest mob = base
        // Each point of difficulty adds GuildQuestKillsAddedPerDifficulty kills (tougher = more).
        Assert.That(GuildQuests.KillCount(50, Mid, isBoss: false),
            Is.EqualTo(Constants.GuildQuestBaseKills + 50 * Constants.GuildQuestKillsAddedPerDifficulty));
        Assert.That(GuildQuests.KillCount(1_000_000, Mid, isBoss: false), Is.EqualTo(Constants.GuildQuestMaxKills));   // capped
    }

    [Test]
    public void KillCount_Boss_UsesCompressedCurve_Capped_AndFarBelowNormal()
    {
        // A boss's baseline is tens, not hundreds — and it caps far lower than a normal mob.
        Assert.That(GuildQuests.KillCount(0, Mid, isBoss: true), Is.EqualTo(Constants.GuildQuestBossBaseKills));   // weakest boss = base
        Assert.That(GuildQuests.KillCount(1_000_000, Mid, isBoss: true), Is.EqualTo(Constants.GuildQuestBossMaxKills));   // capped
        // Same tough mob: the boss version asks for a fraction of the normal kills — the whole point of the flag.
        Assert.That(GuildQuests.KillCount(765, Mid, isBoss: true),
            Is.LessThan(GuildQuests.KillCount(765, Mid, isBoss: false)));
        // Still slopes with strength within the boss band.
        Assert.That(GuildQuests.KillCount(400, Mid, isBoss: true),
            Is.GreaterThan(GuildQuests.KillCount(100, Mid, isBoss: true)));
    }

    [Test]
    public void Rewards_RiseWithDifficultyAndGuildLevel()
    {
        Assert.That(GuildQuests.RewardExp(100, 2, Mid, isBoss: false), Is.GreaterThan(GuildQuests.RewardExp(100, 1, Mid, isBoss: false)));   // higher guild level
        Assert.That(GuildQuests.RewardExp(200, 1, Mid, isBoss: false), Is.GreaterThan(GuildQuests.RewardExp(100, 1, Mid, isBoss: false)));   // tougher mob
        Assert.That(GuildQuests.RewardGold(200, 2, Mid, isBoss: false), Is.GreaterThan(GuildQuests.RewardGold(100, 1, Mid, isBoss: false)));
    }

    [Test]
    public void Boss_RewardsAreTheConfiguredPercentageOfNormal()
    {
        // A boss quest pays GuildQuestBossRewardPercent% of the same-difficulty normal reward, XP and gold alike
        // (diff 400 at a mid level keeps the gold above its always-beats-cost floor, so the ratio is exact).
        long pct = Constants.GuildQuestBossRewardPercent;
        long normalExp = GuildQuests.RewardExp(400, MidLevel, Mid, isBoss: false);
        long normalGold = GuildQuests.RewardGold(400, MidLevel, Mid, isBoss: false);
        Assert.That(GuildQuests.RewardExp(400, MidLevel, Mid, isBoss: true), Is.EqualTo(normalExp * pct / 100).Within(1));
        Assert.That(GuildQuests.RewardGold(400, MidLevel, Mid, isBoss: true), Is.EqualTo(normalGold * pct / 100).Within(1));
    }

    [Test]
    public void MaxLevel_EschewsXp_AndBonusesGold()
    {
        // At max guild level XP is 0 (can't level) for both normal and boss quests...
        Assert.That(GuildQuests.RewardExp(400, Constants.GuildMaxLevel, Mid, isBoss: false), Is.EqualTo(0));
        Assert.That(GuildQuests.RewardExp(400, Constants.GuildMaxLevel, Mid, isBoss: true), Is.EqualTo(0));
        // ...and the gold takes the max-level bonus (diff 500 so the always-beats-cost floor doesn't bind).
        long baseGold = Constants.GuildQuestBaseGold * (Constants.GuildMaxLevel + 1) + 500 * Constants.GuildQuestGoldPerDifficulty;
        long expected = baseGold * (100 + Constants.GuildQuestMaxLevelGoldBonusPercent) / 100;
        Assert.That(GuildQuests.RewardGold(500, Constants.GuildMaxLevel, Mid, isBoss: false), Is.EqualTo(expected));
    }

    [Test]
    public void Variation_ScalesKillsAndRewardsTogether()
    {
        // A bigger roll = a bigger objective, and the XP + gold rise in lockstep (more effort → more reward).
        Assert.That(GuildQuests.KillCount(100, 1.0, isBoss: false), Is.GreaterThan(GuildQuests.KillCount(100, 0.0, isBoss: false)));
        Assert.That(GuildQuests.RewardExp(100, 1, 1.0, isBoss: false), Is.GreaterThan(GuildQuests.RewardExp(100, 1, 0.0, isBoss: false)));
        Assert.That(GuildQuests.RewardGold(100, 1, 1.0, isBoss: false), Is.GreaterThan(GuildQuests.RewardGold(100, 1, 0.0, isBoss: false)));
    }

    [Test]
    public void RewardGold_AlwaysExceedsAcquireCost()
    {
        // Across levels, difficulties, the full variation range, AND boss/normal, completing a quest nets gold.
        for (int level = 0; level <= Constants.GuildMaxLevel; level++)
        {
            foreach (double roll in new[] { 0.0, 0.5, 1.0 })
            {
                foreach (int diff in new[] { 0, 100, 500 })
                {
                    foreach (bool boss in new[] { false, true })
                    {
                        Assert.That(GuildQuests.RewardGold(diff, level, roll, boss),
                            Is.GreaterThan(GuildQuests.AcquireCost(level)),
                            $"level {level}, diff {diff}, roll {roll}, boss {boss}");
                    }
                }
            }
        }
    }
}

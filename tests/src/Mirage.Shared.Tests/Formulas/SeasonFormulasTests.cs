using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The seasonal-leaderboard math: whole-week counting, the weekly hold score with its
/// capped consecutive-hold bonus, and the placing payout table.</summary>
[TestFixture]
public class SeasonFormulasTests
{
    [Test]
    public void WeeksElapsed_WholeWeeks_FloorAndNonNegative()
    {
        var start = new DateOnly(2026, 1, 4);   // a Sunday
        Assert.Multiple(() =>
        {
            Assert.That(SeasonFormulas.WeeksElapsed(start, start), Is.EqualTo(0));
            Assert.That(SeasonFormulas.WeeksElapsed(start, start.AddDays(6)), Is.EqualTo(0));   // < 1 week
            Assert.That(SeasonFormulas.WeeksElapsed(start, start.AddDays(7)), Is.EqualTo(1));
            Assert.That(SeasonFormulas.WeeksElapsed(start, start.AddDays(13)), Is.EqualTo(1));  // floors
            Assert.That(SeasonFormulas.WeeksElapsed(start, start.AddDays(91)), Is.EqualTo(13)); // a full season
            Assert.That(SeasonFormulas.WeeksElapsed(start, start.AddDays(-7)), Is.EqualTo(0));  // clamped >= 0
        });
    }

    [Test]
    public void WeeklyHoldScore_BaseTimesCappedStreakBonus()
    {
        long b = Constants.TerritorySeasonPointsPerWeek;
        double step = Constants.TerritorySeasonHoldBonusPercentPerWeek / 100.0;
        Assert.Multiple(() =>
        {
            Assert.That(SeasonFormulas.WeeklyHoldScore(0), Is.EqualTo(b));                                  // fresh → base
            Assert.That(SeasonFormulas.WeeklyHoldScore(1), Is.EqualTo((long)Math.Round(b * (1 + step))));   // +1 week
            Assert.That(SeasonFormulas.WeeklyHoldScore(4), Is.EqualTo((long)Math.Round(b * (1 + step * 4))));
            int cap = Constants.TerritorySeasonHoldBonusCapWeeks;
            long capped = (long)Math.Round(b * (1 + step * cap));
            Assert.That(SeasonFormulas.WeeklyHoldScore(cap), Is.EqualTo(capped));
            Assert.That(SeasonFormulas.WeeklyHoldScore(cap + 50), Is.EqualTo(capped));                      // clamped at the cap
        });
    }

    [Test]
    public void PlacingPayout_TieredThenFlatScorerThenZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeasonFormulas.PlacingPayout(1), Is.EqualTo((Constants.TerritorySeason1stMemberGold, Constants.TerritorySeason1stVaultGold)));
            Assert.That(SeasonFormulas.PlacingPayout(2), Is.EqualTo((Constants.TerritorySeason2ndMemberGold, Constants.TerritorySeason2ndVaultGold)));
            Assert.That(SeasonFormulas.PlacingPayout(3), Is.EqualTo((Constants.TerritorySeason3rdMemberGold, Constants.TerritorySeason3rdVaultGold)));
            Assert.That(SeasonFormulas.PlacingPayout(4), Is.EqualTo((Constants.TerritorySeasonScorerMemberGold, Constants.TerritorySeasonScorerVaultGold)));
            Assert.That(SeasonFormulas.PlacingPayout(99), Is.EqualTo((Constants.TerritorySeasonScorerMemberGold, Constants.TerritorySeasonScorerVaultGold)));
            Assert.That(SeasonFormulas.PlacingPayout(0), Is.EqualTo((0L, 0L)));   // non-scorer
        });
    }
}

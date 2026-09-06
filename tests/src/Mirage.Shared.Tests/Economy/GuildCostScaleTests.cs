using NUnit.Framework;

namespace Mirage.Shared.Tests.Economy;

/// <summary>The guild gold family is a CLOSED SUB-ECONOMY and must be retuned as a unit.
///
/// <para>Every gold figure below is one internally consistent set, anchored on a 35,000-gold guild. The
/// ratios are deliberate, so the family is rescaled as a unit or not at all.</para>
///
/// <para>The failure mode this fixture exists for is a member being LEFT BEHIND by such a rescale. A guild
/// constant sitting at a fraction of everything around it produces no error, no failing test and no
/// visible symptom — just a mechanic that quietly does nothing. A bare literal at a call site rather than
/// a constant is how one gets missed.</para>
///
/// <para>These ratios are DELIBERATE, not incidental. A failure here is not automatically a bug; it means
/// somebody moved one member of the family. If that was intended, update the table and say why.</para></summary>
[TestFixture]
public class GuildCostScaleTests
{
    // Everything is quoted against the cost of founding a guild, which is the family's anchor.
    const double Anchor = Constants.GuildCreationCost;

    // The level the flat 35,000 was sized against: about 30 hours of at-level grinding by
    // .Tools/Simulations/FightSim. NOT a level gate — nothing in the engine requires level 30 to found a
    // guild — it is only the player the number was priced for.
    const int AnchorLevel = 30;

    [Test]
    public void EveryGuildCost_HoldsItsRatioToTheAnchor()
    {
        (string Name, double Actual, double ExpectedRatio)[] family =
        [
            // Anchor-scale: the once-per-thing commitments.
            ("GuildTaxPerLevel",               Constants.GuildTaxPerLevel,               1.0),
            ("GuildWarDeclareBaseCost",        Constants.GuildWarDeclareBaseCost,        1.0),
            ("TerritoryUnclaimedChallengeCost", Constants.TerritoryUnclaimedChallengeCost, 1.0),
            // Sub-costs and per-use fees.
            ("GuildQuestCostPerLevel",         Constants.GuildQuestCostPerLevel,         0.5),
            ("GuildQuestBaseGold",             Constants.GuildQuestBaseGold,             0.25),
            ("GuildWarDeclareLevelStep",       Constants.GuildWarDeclareLevelStep,       0.1),
            ("GuildWarDeclareMinCost",         Constants.GuildWarDeclareMinCost,         0.1),
            ("GuildGoldPerTaxDiscount",        Constants.GuildGoldPerTaxDiscount,        0.1),
            ("TerritoryIncomeDailyCap",        Constants.TerritoryIncomeDailyCap,        0.5),
            ("GuildQuestGoldPerDifficulty",    Constants.GuildQuestGoldPerDifficulty,    0.005),
            // Season payouts, which are the family's biggest numbers by design.
            ("TerritorySeason1stVaultGold",    Constants.TerritorySeason1stVaultGold,    20.0),
            ("TerritorySeason1stMemberGold",   Constants.TerritorySeason1stMemberGold,   5.0),
            ("TerritorySeason3rdVaultGold",    Constants.TerritorySeason3rdVaultGold,    5.0),
            ("TerritorySeasonScorerVaultGold", Constants.TerritorySeasonScorerVaultGold, 1.0),
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, actual, expected) in family)
                Assert.That(actual / Anchor, Is.EqualTo(expected).Within(0.001),
                    $"{name} is {actual:N0}, which is {actual / Anchor:0.###}x the {Anchor:N0} guild anchor "
                    + $"rather than {expected}x — the guild gold family moves as a unit");
        });
    }

    [Test]
    public void PerKillVaultTrickles_MoveTogether()
    {
        // Two separate mechanics drip gold into a vault on a mob KO: territory income and the L5 perk.
        // They are deliberately the same size, and the perk is the one that lived as a literal for years.
        Assert.Multiple(() =>
        {
            Assert.That(Constants.GuildPerkVaultGold, Is.EqualTo(Constants.TerritoryIncomeNonOwnerGold),
                "the L5 trickle and the non-owner territory drip are one mechanic's worth of gold");
            Assert.That(Constants.TerritoryIncomeOwnerGold, Is.EqualTo(Constants.TerritoryIncomeNonOwnerGold * 2),
                "owning the territory is worth double, and that ratio predates the rescale");
        });
    }

    [Test]
    public void TheAnchorStillMatchesTheLevelItWasSizedFor()
    {
        // The link nothing else asserts. The guild costs are FLAT — deliberately, because a cost keyed on
        // the acting player's level is paid by whichever level-1 alt presses the button — but a flat number
        // still has to be sized against somebody. If the backbone curve is ever refitted (it is a fit to
        // the drop tables, and the bestiary moves), this fires to say the guild economy no longer costs
        // what it was meant to cost.
        long incomeAtAnchor = EconomyFormulas.ExpectedGoldPerLevel(AnchorLevel);
        Assert.That(Constants.GuildCreationCost, Is.EqualTo(incomeAtAnchor).Within(20).Percent,
            $"founding a guild ({Constants.GuildCreationCost:N0}) should stay near one level's income at "
            + $"level {AnchorLevel} ({incomeAtAnchor:N0}) — retune the family if the backbone moved");
    }

    [Test]
    public void NoGuildCostIsTriviallySmallAgainstRealIncome()
    {
        // The blunt version of the same guard, and the one that survives a deliberate retune: whatever the
        // ratios become, a guild-scale sink may never decay back into pocket change. A constant left a
        // whole rescale behind lands around 2.8% here and fails.
        long incomeAtAnchor = EconomyFormulas.ExpectedGoldPerLevel(AnchorLevel);
        (string Name, long Value)[] sinks =
        [
            ("GuildCreationCost", Constants.GuildCreationCost),
            ("GuildTaxPerLevel", Constants.GuildTaxPerLevel),
            ("GuildWarDeclareBaseCost", Constants.GuildWarDeclareBaseCost),
            ("TerritoryUnclaimedChallengeCost", Constants.TerritoryUnclaimedChallengeCost),
            ("GuildQuestCostPerLevel", Constants.GuildQuestCostPerLevel),
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, value) in sinks)
                Assert.That(value, Is.GreaterThan(incomeAtAnchor / 10),
                    $"{name} is {value:N0} against {incomeAtAnchor:N0} of income for one level — a guild "
                    + "sink worth under a tenth of a level is not a commitment");
        });
    }
}

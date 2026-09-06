using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Formulas;

/// <summary>Territory income math: the weeks-held multiplier, per-kill gold, the daily accrual
/// clamp, and the settlement credit that moves a day's income into the weekly tally.</summary>
[TestFixture]
public class TerritoryFormulasTests
{
    [Test]
    public void WeeksHeldMultiplier_RisesThenCapsAtFour()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryFormulas.WeeksHeldMultiplier(0), Is.EqualTo(1));   // fresh
            Assert.That(TerritoryFormulas.WeeksHeldMultiplier(1), Is.EqualTo(2));
            Assert.That(TerritoryFormulas.WeeksHeldMultiplier(2), Is.EqualTo(3));
            Assert.That(TerritoryFormulas.WeeksHeldMultiplier(3), Is.EqualTo(4));   // cap (= 1 "month")
            Assert.That(TerritoryFormulas.WeeksHeldMultiplier(10), Is.EqualTo(4));  // stays capped beyond
        });
    }

    [Test]
    public void IncomeForKill_OwnerBeatsNonOwner_ScaledByWeeks()
    {
        Assert.Multiple(() =>
        {
            // Quoted against the constants: the guild gold family is rescaled as a unit, and what this
            // test protects is the owner/non-owner gap and the weeks-held multiplier, neither of which a
            // rescale touches.
            int nonOwner = Constants.TerritoryIncomeNonOwnerGold, owner = Constants.TerritoryIncomeOwnerGold;
            Assert.That(TerritoryFormulas.IncomeForKill(false, 0), Is.EqualTo(nonOwner));    // fresh, non-owner: x1
            Assert.That(TerritoryFormulas.IncomeForKill(true, 0), Is.EqualTo(owner));        // fresh, owner: x1
            Assert.That(TerritoryFormulas.IncomeForKill(false, 3), Is.EqualTo(nonOwner * 4));// held 3+ wk: x4
            Assert.That(TerritoryFormulas.IncomeForKill(true, 5), Is.EqualTo(owner * 4));    // held 3+ wk, owner: x4
            Assert.That(owner, Is.GreaterThan(nonOwner), "owning the territory must beat farming someone else's");
        });
    }

    [Test]
    public void AccruePending_ClampsAtDailyCap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryFormulas.AccruePending(0, 5, 10), Is.EqualTo(5));
            Assert.That(TerritoryFormulas.AccruePending(8, 5, 10), Is.EqualTo(10));  // clamped to cap
            Assert.That(TerritoryFormulas.AccruePending(10, 3, 10), Is.EqualTo(10)); // already at cap
        });
    }

    // ── War-night scheduling + resolution ─────────────────────────────────────
    [Test]
    public void NextWarNight_FindsTheUpcomingSlot()
    {
        // 2026-07-18 is a Saturday (the war-night day); 07-15 is the Wednesday before it.
        var wed = new DateTime(2026, 7, 15, 10, 0, 0);
        var satBefore = new DateTime(2026, 7, 18, 10, 0, 0);
        var satAfter = new DateTime(2026, 7, 18, 21, 0, 0);
        var expected = new DateTime(2026, 7, 18, 20, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryFormulas.NextWarNight(wed, DayOfWeek.Saturday, 20), Is.EqualTo(expected));       // later this week
            Assert.That(TerritoryFormulas.NextWarNight(satBefore, DayOfWeek.Saturday, 20), Is.EqualTo(expected)); // today, slot still ahead
            Assert.That(TerritoryFormulas.NextWarNight(satAfter, DayOfWeek.Saturday, 20),
                Is.EqualTo(expected.AddDays(7)));                                                                 // today's slot passed → next week
        });
    }

    [Test]
    public void ResolveWinner_DefenderHolds_LoneClaimantTakes_TieStaysUnclaimed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryFormulas.ResolveWinner(5, new[] { 6, 7 }), Is.EqualTo(5), "present defender holds a contest (tie -> defender)");
            Assert.That(TerritoryFormulas.ResolveWinner(5, Array.Empty<int>()), Is.EqualTo(5), "unchallenged defender keeps");
            Assert.That(TerritoryFormulas.ResolveWinner(0, new[] { 6 }), Is.EqualTo(6), "lone claimant takes an unclaimed territory");
            Assert.That(TerritoryFormulas.ResolveWinner(0, new[] { 6, 7 }), Is.EqualTo(0), "unclaimed + 2 claimants ties -> stays unclaimed");
            Assert.That(TerritoryFormulas.ResolveWinner(0, Array.Empty<int>()), Is.EqualTo(0), "no defender, no claimant -> unclaimed");
        });
    }

    [Test]
    public void BaseDeclareCost_HasNoLevel0TargetDoubling()
    {
        // Challenging an owned territory uses BaseDeclareCost; unlike DeclareCost it never doubles vs an L0 owner.
        Assert.Multiple(() =>
        {
            Assert.That(GuildWarFormulas.BaseDeclareCost(5, 0), Is.LessThan(GuildWarFormulas.DeclareCost(5, 0)),
                "DeclareCost doubles vs an L0 target; BaseDeclareCost does not");
            Assert.That(GuildWarFormulas.BaseDeclareCost(3, 3), Is.EqualTo(GuildWarFormulas.DeclareCost(3, 3)),
                "for a same-level (non-L0) target the two agree");
            Assert.That(GuildWarFormulas.BaseDeclareCost(5, 1), Is.EqualTo(GuildWarFormulas.DeclareCost(5, 1)),
                "for a non-L0 target the two agree");
        });
    }

    [Test]
    public void CreditTerritoryIncome_MovesPendingIntoWeeklyTally_AndZeroes()
    {
        var g = new TerritoryRecord { MapGroup = 1, ControllingGuild = 1, PendingTerritoryIncome = 42, IncomeThisWeek = 100 };
        long credited = GuildScheduleSystem.CreditTerritoryIncome(g);
        Assert.Multiple(() =>
        {
            Assert.That(credited, Is.EqualTo(42));
            Assert.That(g.PendingTerritoryIncome, Is.Zero, "pending zeroed after the daily credit");
            Assert.That(g.IncomeThisWeek, Is.EqualTo(142), "the credited amount rolls into the weekly tally");
        });
        // Nothing pending → no-op (doesn't touch the weekly tally).
        Assert.That(GuildScheduleSystem.CreditTerritoryIncome(g), Is.Zero);
        Assert.That(g.IncomeThisWeek, Is.EqualTo(142));
    }

    // ── Disband: what a dissolved guild gives up ──────────────────────────────
    [Test]
    public void ReleaseTerritory_OwnedTerritory_FallsUnclaimed_AndLosesItsHoldStreak()
    {
        var owned = new TerritoryRecord { MapGroup = 1, ControllingGuild = 7, WeeksHeld = 3 };
        Assert.That(GuildSystem.ReleaseTerritory(owned, 7), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(owned.ControllingGuild, Is.Zero, "no dangling owner id left behind");
            Assert.That(owned.WeeksHeld, Is.Zero, "an unclaimed territory has no consecutive-hold streak");
        });
    }

    [Test]
    public void ReleaseTerritory_DropsOnlyTheDissolvedGuildsChallenge()
    {
        var contested = new TerritoryRecord { MapGroup = 1, ControllingGuild = 4 };
        contested.Challengers.AddRange(new[] { 7, 9 });
        Assert.That(GuildSystem.ReleaseTerritory(contested, 7), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(contested.ControllingGuild, Is.EqualTo(4), "another guild's ownership is untouched");
            Assert.That(contested.Challengers, Is.EqualTo(new[] { 9 }), "a dissolved guild contests nothing");
        });
    }

    [Test]
    public void ReleaseTerritory_UninvolvedTerritory_ReportsNoChange()
    {
        var other = new TerritoryRecord { MapGroup = 1, ControllingGuild = 4, WeeksHeld = 2 };
        other.Challengers.Add(9);
        Assert.That(GuildSystem.ReleaseTerritory(other, 7), Is.False, "no change -> the caller persists nothing");
        Assert.Multiple(() =>
        {
            Assert.That(other.ControllingGuild, Is.EqualTo(4));
            Assert.That(other.WeeksHeld, Is.EqualTo(2), "an untouched owner keeps its streak");
            Assert.That(other.Challengers, Is.EqualTo(new[] { 9 }));
        });
    }
}

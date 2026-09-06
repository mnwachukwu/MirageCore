using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Guilds;

/// <summary>The guild daily-settlement logic (#3): wall-clock catch-up date enumeration, founding-weekday
/// tax gating, and the tax/perks state machine (pay, suspend on shortfall, restore, no back taxes).</summary>
[TestFixture]
public class GuildScheduleTests
{
    private static GuildRecord Guild(int level, long vault, DayOfWeek founding, bool perks = true) =>
        new() { Index = 1, Level = level, VaultGold = vault, FoundingWeekday = founding, PerksActive = perks };

    // Vault seeds below are quoted in WEEKS OF TAX rather than in literal gold. The guild gold family is
    // rescaled as a unit (see Constants), and every one of these tests is about the state machine — pay,
    // suspend, restore, charge once — not about what a week costs. Literal vaults make a pure rescale
    // look like a dozen broken tests.
    private static long Tax(int level) => level * (long)Constants.GuildTaxPerLevel;

    /// <summary>Gold that <paramref name="valor"/> buys off a tax bill, in whole increments — the same
    /// arithmetic GuildScheduleSystem.GuildValorTaxOffset does, stated once here.</summary>
    private static long ValorGold(int valor) =>
        valor / Constants.GuildValorPerTaxDiscount * (long)Constants.GuildGoldPerTaxDiscount;

    // ── Catch-up date enumeration ─────────────────────────────────────────────

    [Test]
    public void DatesToSettle_FirstBoot_IsEmpty()   // cursor unset → no retroactive settlement
    {
        Assert.That(GuildScheduleSystem.DatesToSettle(default, new DateOnly(2026, 7, 17)), Is.Empty);
    }

    [Test]
    public void DatesToSettle_SameDay_IsEmpty()
    {
        var today = new DateOnly(2026, 7, 17);
        Assert.That(GuildScheduleSystem.DatesToSettle(today, today), Is.Empty);
    }

    [Test]
    public void DatesToSettle_OneDay_ReturnsToday()
    {
        var today = new DateOnly(2026, 7, 17);
        Assert.That(GuildScheduleSystem.DatesToSettle(today.AddDays(-1), today), Is.EqualTo(new[] { today }));
    }

    [Test]
    public void DatesToSettle_Downtime_CatchesUpEveryMissedDay()
    {
        var last = new DateOnly(2026, 7, 14);
        var today = new DateOnly(2026, 7, 17);
        Assert.That(GuildScheduleSystem.DatesToSettle(last, today), Is.EqualTo(new[]
        {
            new DateOnly(2026, 7, 15),
            new DateOnly(2026, 7, 16),
            new DateOnly(2026, 7, 17),
        }));
    }

    [Test]
    public void DatesToSettle_ClockWentBackward_IsEmpty()
    {
        var today = new DateOnly(2026, 7, 17);
        Assert.That(GuildScheduleSystem.DatesToSettle(today.AddDays(1), today), Is.Empty);
    }

    // ── Founding-weekday gating ───────────────────────────────────────────────

    [Test]
    public void SettleGuild_TaxesOnlyOnFoundingWeekday()
    {
        var taxDay = new DateOnly(2026, 7, 17);
        var otherDay = taxDay.AddDays(1);                          // guaranteed a different weekday
        var g = Guild(level: 2, vault: Tax(2) * 2, founding: taxDay.DayOfWeek);

        Assert.That(GuildScheduleSystem.SettleGuild(g, otherDay, nowUtc: 0).Tax, Is.EqualTo(TaxOutcome.None));
        Assert.That(g.VaultGold, Is.EqualTo(Tax(2) * 2));         // untouched off the founding weekday

        Assert.That(GuildScheduleSystem.SettleGuild(g, taxDay, nowUtc: 0).Tax, Is.EqualTo(TaxOutcome.Paid));
        Assert.That(g.VaultGold, Is.EqualTo(Tax(2)));
    }

    // ── Daily income credit (L5 perk gold) ────────────────────────────────────

    [Test]
    public void CreditDailyGold_MovesPendingIntoVault_AndZeroes()
    {
        var g = Guild(level: 0, vault: 100, founding: DayOfWeek.Monday);
        g.PendingPerkIncome = 37;
        Assert.That(GuildScheduleSystem.CreditPerkIncome(g), Is.EqualTo(37));
        Assert.That(g.VaultGold, Is.EqualTo(137));
        Assert.That(g.PendingPerkIncome, Is.EqualTo(0));
    }

    [Test]
    public void CreditDailyGold_NothingPending_IsNoOp()
    {
        var g = Guild(level: 0, vault: 100, founding: DayOfWeek.Monday);
        Assert.That(GuildScheduleSystem.CreditPerkIncome(g), Is.EqualTo(0));
        Assert.That(g.VaultGold, Is.EqualTo(100));
    }

    [Test]
    public void SettleGuild_DebitsBeforeCredits_SameDayIncomeCannotCoverTax()
    {
        var taxDay = new DateOnly(2026, 7, 17);
        var g = Guild(level: 1, vault: 600, founding: taxDay.DayOfWeek);   // owes 1000, has 600
        g.PendingPerkIncome = 500;                                          // today's income would total 1100

        var result = GuildScheduleSystem.SettleGuild(g, taxDay, nowUtc: 0);
        Assert.That(result.Tax, Is.EqualTo(TaxOutcome.Missed));            // tax ran first on 600 → unaffordable
        Assert.That(g.PerksActive, Is.False);                             // perks suspended despite the pending income
        Assert.That(result.GoldCredited, Is.EqualTo(500));               // income still credited afterward
        Assert.That(g.VaultGold, Is.EqualTo(1100));                      // 600 + 500, no tax taken
    }

    // ── Tax / perks state machine ─────────────────────────────────────────────

    [Test]
    public void ApplyWeeklyTax_Level0_IsFree()
    {
        var g = Guild(level: 0, vault: 5_000, founding: DayOfWeek.Monday);
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.None));
        Assert.That(g.VaultGold, Is.EqualTo(5_000));
    }

    [Test]
    public void ApplyWeeklyTax_Affordable_DeductsOneWeek()
    {
        var g = Guild(level: 3, vault: Tax(3) * 2, founding: DayOfWeek.Monday);
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Paid));
        Assert.That(g.VaultGold, Is.EqualTo(Tax(3)), "two weeks' worth, minus the one week charged");
        Assert.That(g.PerksActive, Is.True);
    }

    [Test]
    public void ApplyWeeklyTax_Unaffordable_SuspendsPerks_TakesNothing()
    {
        var g = Guild(level: 2, vault: 500, founding: DayOfWeek.Monday);   // owes 2000, has 500
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Missed));
        Assert.That(g.VaultGold, Is.EqualTo(500));                          // whole-or-nothing
        Assert.That(g.PerksActive, Is.False);
    }

    [Test]
    public void ApplyWeeklyTax_AlreadySuspendedStillBroke_IsSilentNoOp()
    {
        var g = Guild(level: 2, vault: 500, founding: DayOfWeek.Monday, perks: false);
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.None));
        Assert.That(g.PerksActive, Is.False);
    }

    [Test]
    public void ApplyWeeklyTax_PaysAfterMiss_RestoresPerks_OneWeekOnly()
    {
        var g = Guild(level: 2, vault: 500, founding: DayOfWeek.Monday);
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Missed));   // week 1: suspended

        g.VaultGold = Tax(2) * 2;                                                            // donations arrive
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.RestoredAndPaid));
        Assert.That(g.VaultGold, Is.EqualTo(Tax(2)), "exactly one week, no back taxes");
        Assert.That(g.PerksActive, Is.True);
    }

    // ── Valor tax relief ──────────────────────────────────────────────────────

    [Test]
    public void GuildValorTaxOffset_CapsAtHalfTheTax()
    {
        // 250 valor is exactly the 50% cap on an L5 bill, by construction of the two constants — so this
        // also asserts the relationship the Constants comment claims.
        long l5 = Tax(5);
        Assert.That(ValorGold(250), Is.EqualTo(l5 / 2), "250 valor is sized to be exactly the cap at L5");
        Assert.That(GuildScheduleSystem.GuildValorTaxOffset(250, l5), Is.EqualTo((250, ValorGold(250))));
        Assert.That(GuildScheduleSystem.GuildValorTaxOffset(1000, l5), Is.EqualTo((250, ValorGold(250))));   // extra valor unused
    }

    [Test]
    public void GuildValorTaxOffset_WholeIncrementsOnly()
    {
        long l5 = Tax(5);
        Assert.That(GuildScheduleSystem.GuildValorTaxOffset(30, l5), Is.EqualTo((30, ValorGold(30))));   // 3 chunks
        Assert.That(GuildScheduleSystem.GuildValorTaxOffset(5, l5), Is.EqualTo((0, 0L)));       // < 10 valor = nothing
        Assert.That(GuildScheduleSystem.GuildValorTaxOffset(0, l5), Is.EqualTo((0, 0L)));
    }

    [Test]
    public void GuildTaxFormulas_EffectiveTax_IsBaseMinusValorDiscount()
    {
        // The shared math the Vault dashboard shows: L0 is free; a level's base = level * GuildTaxPerLevel;
        // vault valor reduces the gold actually owed (same offset the settlement applies).
        Assert.That(GuildTaxFormulas.WeeklyTax(0), Is.EqualTo(0L));
        long l5Base = GuildTaxFormulas.WeeklyTax(5);
        Assert.That(GuildTaxFormulas.EffectiveTax(5, 0), Is.EqualTo(l5Base));                        // no valor → full base
        Assert.That(GuildTaxFormulas.EffectiveTax(5, 250), Is.EqualTo(l5Base - ValorGold(250)));     // the 50% cap
        Assert.That(GuildTaxFormulas.EffectiveTax(5, 30), Is.EqualTo(l5Base - ValorGold(30)));       // 3 whole chunks
    }

    [Test]
    public void ApplyWeeklyTax_ValorOffsetsGold_AndIsConsumed()
    {
        var g = Guild(level: 5, vault: Tax(5) - ValorGold(250) + 500, founding: DayOfWeek.Monday);
        g.VaultValor = 250;                                    // buys off half the bill
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Paid));
        Assert.That(g.VaultGold, Is.EqualTo(500));            // only the valor-reduced remainder was taken
        Assert.That(g.VaultValor, Is.EqualTo(0));             // spent
    }

    [Test]
    public void ApplyWeeklyTax_ValorMakesUnaffordableTaxAffordable()
    {
        // Gold alone is short of the full bill; valor closes exactly the gap.
        var g = Guild(level: 2, vault: Tax(2) - ValorGold(100), founding: DayOfWeek.Monday);
        g.VaultValor = 100;
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Paid));
        Assert.That(g.VaultGold, Is.EqualTo(0));
        Assert.That(g.VaultValor, Is.EqualTo(0));
    }

    [Test]
    public void ApplyWeeklyTax_UnaffordableEvenWithValor_ConsumesNothing()
    {
        var g = Guild(level: 2, vault: 500, founding: DayOfWeek.Monday);     // owes 2000
        g.VaultValor = 100;                                    // 1000 off → goldDue 1000, but only 500 gold
        Assert.That(GuildScheduleSystem.ApplyWeeklyTax(g), Is.EqualTo(TaxOutcome.Missed));
        Assert.That(g.VaultGold, Is.EqualTo(500));            // atomic: nothing deducted
        Assert.That(g.VaultValor, Is.EqualTo(100));           // valor untouched
    }

    // ── War daily maintenance ─────────────────────────────────────────────────

    // A live one-sided-aggressor war entry: only these pay daily maintenance.
    private static GuildWar Aggressor(int opp, long declareCost, long goLiveUtc) =>
        new() { OpponentIndex = opp, OpponentName = "Foe", WeDeclared = true, DeclareCost = declareCost, GoLiveUtc = goLiveUtc };

    [Test]
    public void ApplyWarMaintenance_LiveAggressor_ChargesHalfDeclareCost()
    {
        var g = Guild(level: 1, vault: 5_000, founding: DayOfWeek.Monday);
        g.Wars.Add(Aggressor(opp: 2, declareCost: 1400, goLiveUtc: 100));
        var r = GuildScheduleSystem.ApplyWarMaintenance(g, nowUtc: 1000);
        Assert.That(r.Paid, Is.EqualTo(700));                  // 50% of 1400
        Assert.That(g.VaultGold, Is.EqualTo(4_300));
        Assert.That(r.DroppedOpponents, Is.Empty);
        Assert.That(g.WeeklyWarCosts, Is.EqualTo(700), "war maintenance accrues to the weekly war-cost total");
    }

    // ── Vault-dashboard weekly running totals ─────────────────────────────────
    [Test]
    public void SettleGuild_WeeklyReset_ZeroesTotalsThenAccruesTodaysIncome()
    {
        var sunday = new DateOnly(2026, 7, 19);
        Assume.That(sunday.DayOfWeek, Is.EqualTo(ServerConfig.Default.Schedule.WeekResetDay), "test date must be the weekly reset day");
        var g = Guild(level: 0, vault: 0, founding: DayOfWeek.Monday);   // L0 = no tax; not the founding day anyway
        g.WeeklyIncome = 100;
        g.WeeklyDonations = 50;
        g.WeeklyWarCosts = 30;
        g.PendingPerkIncome = 7;   // today's L5 trickle, credited into the fresh week

        var result = GuildScheduleSystem.SettleGuild(g, sunday, nowUtc: 0);

        Assert.Multiple(() =>
        {
            Assert.That(g.WeeklyDonations, Is.Zero, "donations reset at the week boundary");
            Assert.That(g.WeeklyWarCosts, Is.Zero, "war costs reset at the week boundary");
            Assert.That(g.WeeklyIncome, Is.EqualTo(7), "reset to 0, then today's L5 credit starts the new week");
            Assert.That(g.VaultGold, Is.EqualTo(7), "L5 gold credited to the vault");
            Assert.That(result.Changed, Is.True);
        });
    }

    [Test]
    public void SettleGuild_NonResetDay_AddsIncomeWithoutZeroing()
    {
        var friday = new DateOnly(2026, 7, 17);
        Assume.That(friday.DayOfWeek, Is.Not.EqualTo(ServerConfig.Default.Schedule.WeekResetDay));
        var g = Guild(level: 0, vault: 0, founding: DayOfWeek.Monday);
        g.WeeklyIncome = 100;
        g.PendingPerkIncome = 5;

        GuildScheduleSystem.SettleGuild(g, friday, nowUtc: 0);

        Assert.That(g.WeeklyIncome, Is.EqualTo(105), "L5 credit adds to the running weekly income (no reset)");
    }

    [Test]
    public void ApplyWarMaintenance_MutualWar_IsWaived()
    {
        var g = Guild(level: 1, vault: 5_000, founding: DayOfWeek.Monday);
        var w = Aggressor(2, 1400, 100);
        w.TheyDeclared = true;                                 // → mutual, upkeep waived
        g.Wars.Add(w);
        var r = GuildScheduleSystem.ApplyWarMaintenance(g, nowUtc: 1000);
        Assert.That(r.Paid, Is.EqualTo(0));
        Assert.That(g.VaultGold, Is.EqualTo(5_000));
    }

    [Test]
    public void ApplyWarMaintenance_Defender_PaysNothing()
    {
        var g = Guild(level: 1, vault: 5_000, founding: DayOfWeek.Monday);
        g.Wars.Add(new GuildWar { OpponentIndex = 2, TheyDeclared = true, GoLiveUtc = 100 });
        var r = GuildScheduleSystem.ApplyWarMaintenance(g, nowUtc: 1000);
        Assert.That(r.Paid, Is.EqualTo(0));
        Assert.That(g.VaultGold, Is.EqualTo(5_000));
    }

    [Test]
    public void ApplyWarMaintenance_StillInWarmup_NotYetCharged()
    {
        var g = Guild(level: 1, vault: 5_000, founding: DayOfWeek.Monday);
        g.Wars.Add(Aggressor(2, 1400, goLiveUtc: 2000));      // go-live is in the future
        var r = GuildScheduleSystem.ApplyWarMaintenance(g, nowUtc: 1000);
        Assert.That(r.Paid, Is.EqualTo(0));
        Assert.That(g.VaultGold, Is.EqualTo(5_000));
    }

    [Test]
    public void ApplyWarMaintenance_Unaffordable_DropsWar_WholeOrNothing()
    {
        var g = Guild(level: 1, vault: 100, founding: DayOfWeek.Monday);
        g.Wars.Add(Aggressor(2, declareCost: 1400, goLiveUtc: 100));   // upkeep 700 > 100
        var r = GuildScheduleSystem.ApplyWarMaintenance(g, nowUtc: 1000);
        Assert.That(r.Paid, Is.EqualTo(0));
        Assert.That(g.VaultGold, Is.EqualTo(100));            // whole-or-nothing: nothing taken
        Assert.That(g.Wars, Is.Empty);                        // dropped from this guild's list
        Assert.That(r.DroppedOpponents, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void SettleGuild_TaxThenWarUpkeepThenCredit_InThatOrder()
    {
        var taxDay = new DateOnly(2026, 7, 17);
        // Exactly enough to cover the tax and the 700 upkeep with 100 to spare, so the order is visible in
        // the final balance rather than merely plausible.
        long opening = Tax(1) + 700 + 100;
        var g = Guild(level: 1, vault: opening, founding: taxDay.DayOfWeek);
        g.PendingPerkIncome = 5_000;                                          // credited AFTER debits
        g.Wars.Add(Aggressor(opp: 2, declareCost: 1400, goLiveUtc: 100));    // upkeep 700

        var result = GuildScheduleSystem.SettleGuild(g, taxDay, nowUtc: 1000);

        Assert.That(result.Tax, Is.EqualTo(TaxOutcome.Paid));
        Assert.That(result.WarMaintenancePaid, Is.EqualTo(700));
        Assert.That(result.GoldCredited, Is.EqualTo(5_000));
        Assert.That(g.VaultGold, Is.EqualTo(100 + 5_000));    // opening - tax - upkeep + income
    }

    // ── Settlement idempotency (/guildreset) ──────────────────────────────────
    [Test]
    public void SettleGuild_SameDateTwice_ChargesTaxAndMaintenanceOnce()
    {
        var taxDay = new DateOnly(2026, 7, 17);
        var g = Guild(level: 2, vault: Tax(2) * 2, founding: taxDay.DayOfWeek);
        g.Wars.Add(Aggressor(opp: 2, declareCost: 1400, goLiveUtc: 100));     // upkeep 700

        var first = GuildScheduleSystem.SettleGuild(g, taxDay, nowUtc: 1000);
        long afterFirst = g.VaultGold;                                        // one week's tax and one upkeep
        var second = GuildScheduleSystem.SettleGuild(g, taxDay, nowUtc: 1000);   // re-run the SAME date

        Assert.Multiple(() =>
        {
            Assert.That(first.Tax, Is.EqualTo(TaxOutcome.Paid));
            Assert.That(first.WarMaintenancePaid, Is.EqualTo(700));
            Assert.That(second.Tax, Is.EqualTo(TaxOutcome.None), "tax not re-charged on the same date");
            Assert.That(second.WarMaintenancePaid, Is.EqualTo(0), "maintenance not re-charged on the same date");
            Assert.That(g.VaultGold, Is.EqualTo(afterFirst), "vault unchanged by the idempotent re-run");
        });
    }

    [Test]
    public void ResetWeeklyTotalsIfDue_OncePerDate_ThenIdempotent()
    {
        var sunday = new DateOnly(2026, 7, 19);
        Assume.That(sunday.DayOfWeek, Is.EqualTo(ServerConfig.Default.Schedule.WeekResetDay));
        var g = Guild(level: 0, vault: 0, founding: DayOfWeek.Monday);
        g.WeeklyIncome = 100;
        g.WeeklyDonations = 50;

        Assert.That(GuildScheduleSystem.ResetWeeklyTotalsIfDue(g, sunday, force: false), Is.True);   // reset happened
        g.WeeklyIncome = 42;                                                                          // new accrual after reset
        Assert.That(GuildScheduleSystem.ResetWeeklyTotalsIfDue(g, sunday, force: false), Is.False, "same date -> no second reset");
        Assert.That(g.WeeklyIncome, Is.EqualTo(42), "the re-run must not wipe income accrued since the reset");
    }

    // ── Active-member gate ─────────────────────────────────────────────────────
    [Test]
    public void IsActiveMember_RequiresEnoughTimeWithinWindow()
    {
        long now = 1_000_000;
        long win = Constants.GuildActiveMemberWindowSeconds;
        long min = Constants.GuildActiveMemberMinSeconds;
        Assert.Multiple(() =>
        {
            Assert.That(SeasonFormulas.IsActiveMember(min, now - 100, now), Is.True);
            Assert.That(SeasonFormulas.IsActiveMember(min - 1, now - 100, now), Is.False, "below the time floor");
            Assert.That(SeasonFormulas.IsActiveMember(min, now - win - 1, now), Is.False, "last seen outside the window");
            Assert.That(SeasonFormulas.IsActiveMember(min, 0, now), Is.False, "never seen");
        });
    }

    [Test]
    public void SettleGuild_WarUpkeep_SameDayIncomeCannotCoverIt()   // debits-before-credits, war edition
    {
        var day = new DateOnly(2026, 7, 17);
        var g = Guild(level: 1, vault: 400, founding: day.AddDays(1).DayOfWeek);   // not the founding weekday → no tax
        g.PendingPerkIncome = 5_000;
        g.Wars.Add(Aggressor(opp: 2, declareCost: 1400, goLiveUtc: 100));          // upkeep 700 > 400

        var result = GuildScheduleSystem.SettleGuild(g, day, nowUtc: 1000);

        Assert.That(result.Tax, Is.EqualTo(TaxOutcome.None));
        Assert.That(result.WarMaintenancePaid, Is.EqualTo(0));   // couldn't pay from 400 before the credit
        Assert.That(result.Dropped, Is.EqualTo(new[] { 2 }));    // war dropped despite the pending income
        Assert.That(g.Wars, Is.Empty);
        Assert.That(g.VaultGold, Is.EqualTo(5_400));             // 400 + 5000 income, upkeep never taken
    }
}

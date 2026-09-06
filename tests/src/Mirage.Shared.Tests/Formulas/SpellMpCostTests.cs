using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>Invariants of <see cref="CombatFormulas.GetSpellMpCost"/>, and above all the one AddMp has to
/// satisfy: <b>a cast must never restore more mana than it costs.</b> AddMp is the only spell type whose
/// output currency is its input currency, so a single profitable cast is not a good trade but an unbounded
/// loop — cast, profit, repeat. The bug this guards was real: priced off the authored amount alone, a
/// level-1 Sorcerer self-cast from 18 MP to 100.
/// <para>The reason no amount-only cost can work is worth stating, because it is what makes this a
/// structural test rather than a tuning one: the restore carries <c>SpellPower(casterInt)</c>, which grows
/// as <c>(Int + shift)^1.5</c> without bound, while a cost read off the spell's own metadata is a CONSTANT
/// for a fixed amount. No constant outruns a superlinear curve, so any fix that leaves cost
/// caster-independent has a break-even Int — it just moves it. Hence the sweep below spans Int far past
/// anything the starter world reaches.</para></summary>
[TestFixture]
public class SpellMpCostTests
{
    private static SpellRecord Spell(SpellType type, int vitalAmount) =>
        new() { Name = "probe", Type = type, VitalAmount = (short)vitalAmount };

    // The margin that pays for spell crits. CritDamage averages 1.5x raw + 1.5 and SpellCriticalChancePerMille
    // caps at 35, so the EXPECTED restore reaches ~1.18x raw — which is why bare parity (cost == restore) is
    // not enough and the multiplier has to clear it.
    private const double MaxCritCap = 0.35;

    private static double ExpectedRestoreWithCrits(int raw) => raw * (1 + 0.5 * MaxCritCap) + 1.5 * MaxCritCap;

    // ── The load-bearing invariant ───────────────────────────────────────────────

    [Test]
    public void AddMp_NeverRestoresMoreThanItCosts()
    {
        // Int to 300 and amount to 120 deliberately overshoot the starter world: the failure mode being
        // guarded is a leak that only opens at high Int, so a sweep stopping at level-20 values would have
        // passed against the old formula too.
        for (int casterInt = 0; casterInt <= 300; casterInt++)
        {
            for (int amount = 1; amount <= 120; amount++)
            {
                var spell = Spell(SpellType.AddMp, amount);
                int restore = CombatFormulas.RawSpellPower(casterInt, amount);
                int cost = CombatFormulas.GetSpellMpCost(spell, casterInt);

                Assert.That(cost, Is.GreaterThan(restore),
                    $"self-cast AddMp profits at Int {casterInt}, amount {amount}: restores {restore} for {cost}");
                Assert.That(cost, Is.GreaterThan(ExpectedRestoreWithCrits(restore)),
                    $"AddMp profits once spell crits are counted at Int {casterInt}, amount {amount}");
            }
        }
    }

    // Cross-casting is the same loop with two people in it, so "forbid self-target" was never a fix. The
    // invariant above already covers it — a caster who pays more than they hand over cannot be one half of a
    // pair that profits — but assert the pairwise form directly so the reasoning is on the record.
    [Test]
    public void AddMp_TwoCastersAlternating_CannotProfitEither()
    {
        foreach (int casterInt in new[] { 5, 10, 15, 30, 60, 150 })
        {
            var spell = Spell(SpellType.AddMp, 6);
            int gained = CombatFormulas.RawSpellPower(casterInt, 6);
            int paid = CombatFormulas.GetSpellMpCost(spell, casterInt);
            Assert.That(gained - paid, Is.LessThan(0),
                $"two Int-{casterInt} casters trading AddMp would each net {gained - paid}");
        }
    }

    // ── Scope: nothing else changed ──────────────────────────────────────────────

    // AddHp and AddSp restore a different vital than they spend, so neither can loop and neither was
    // repriced. If someone later routes them through the restore-based path, this fails and asks why.
    [Test]
    public void EveryTypeExceptAddMp_IgnoresCasterInt()
    {
        foreach (SpellType type in Enum.GetValues<SpellType>())
        {
            if (type == SpellType.AddMp) continue;
            var spell = Spell(type, 20);
            spell.IntReq = 20;   // GiveItem gates off IntReq rather than VitalAmount
            Assert.That(CombatFormulas.GetSpellMpCost(spell, 100), Is.EqualTo(CombatFormulas.GetSpellMpCost(spell, 1)),
                $"{type} must stay class-independent");
        }
    }

    [Test]
    public void AddSp_KeepsItsUndoPremium_OverTheMatchingDrain()
        => Assert.That(CombatFormulas.GetSpellMpCost(Spell(SpellType.AddSp, 20), 0),
            Is.GreaterThan(CombatFormulas.GetSpellMpCost(Spell(SpellType.SubSp, 20), 0)));

    [Test]
    public void AddHp_StaysUntaxed_MatchingTheDrainCurveExactly()
        => Assert.That(CombatFormulas.GetSpellMpCost(Spell(SpellType.AddHp, 20), 0),
            Is.EqualTo(CombatFormulas.GetSpellMpCost(Spell(SpellType.SubMp, 20), 0)));

    // ── Shape ────────────────────────────────────────────────────────────────────

    // Both terms of the restore rise with Int, so the price of undoing that rises with it too. This is the
    // deliberate departure from class-independent pricing: a charge proportional to benefit, not a perk.
    [Test]
    public void AddMp_CostRisesWithCasterInt()
    {
        var spell = Spell(SpellType.AddMp, 10);
        int prev = 0;
        foreach (int casterInt in new[] { 0, 5, 10, 20, 40, 80 })
        {
            int cost = CombatFormulas.GetSpellMpCost(spell, casterInt);
            Assert.That(cost, Is.GreaterThanOrEqualTo(prev), $"cost fell going into Int {casterInt}");
            prev = cost;
        }
    }

    [Test]
    public void AddMp_CostRisesWithAmount()
    {
        int prev = 0;
        foreach (int amount in new[] { 1, 6, 13, 19, 40, 80 })
        {
            int cost = CombatFormulas.GetSpellMpCost(Spell(SpellType.AddMp, amount), 25);
            Assert.That(cost, Is.GreaterThanOrEqualTo(prev), $"cost fell going into amount {amount}");
            prev = cost;
        }
    }

    // Casting on an ally is the point of the column, and it has to stay worth doing: the ally gains the full
    // restore while the caster pays the surcharge, so mana moves at a real but not punitive loss.
    [Test]
    public void AddMp_TransfersToAnAllyAtRoughlySeventySevenPercent()
    {
        foreach (int casterInt in new[] { 10, 15, 30, 60 })
        {
            int gained = CombatFormulas.RawSpellPower(casterInt, 13);
            int paid = CombatFormulas.GetSpellMpCost(Spell(SpellType.AddMp, 13), casterInt);
            double efficiency = gained / (double)paid;
            Assert.That(efficiency, Is.InRange(0.70, 0.80), $"Int {casterInt} transfers at {efficiency:P0}");
        }
    }

    // A spell with no magnitude is misconfigured and fizzles before cost is read (SpellSystem guards it), but
    // the formula must not hand back 0 and make a free cast look legal.
    [Test]
    public void EveryType_CostsAtLeastOne() =>
        Assert.That(Enum.GetValues<SpellType>().Select(t => CombatFormulas.GetSpellMpCost(Spell(t, 0), 0)),
            Is.All.GreaterThanOrEqualTo(1));
}

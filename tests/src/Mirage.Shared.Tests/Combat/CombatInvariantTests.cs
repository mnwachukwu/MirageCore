using NUnit.Framework;

namespace Mirage.Shared.Tests.Combat;

// Deterministic locks on the damage/vital invariants that Simulations/CombatSim only checks by printed
// eyeball (the sim reimplements the math standalone, so it can silently drift from Mirage.Shared).  These
// pin the REAL formulas.  Parity/mirror properties (STR==INT offense, NPC↔player pools/regen/mit) already
// live in NpcPlayerFormulaParityTests; this file covers the pieces that fixture doesn't: the damage floors,
// crit/variance shape, and the player HP==MP mirror.
[TestFixture]
public class CombatInvariantTests
{
    static readonly int[] StatSweep = { 0, 1, 5, 10, 30, 60, 100, 150, 200, 255 };
    static readonly int[] LevelSweep = { 1, 10, 30, 60, 100, 150, 200, 255 };
    static readonly int[] RawSweep = { 1, 5, 10, 50, 100, 500, 1000 };
    const int HugeProtection = 1_000_000;   // more mitigation than any real hit → the floor always binds

    // A stacked-DEF defender is never fully immune: through unlimited mitigation the hit still lands the
    // MinDamageFloorPercent (12%) fraction of the varied raw.  Locks the "no immortal tank" guardrail.
    [Test]
    public void ResolveDamage_FloorsAtMinDamagePercent_ThroughAnyMitigation()
    {
        foreach (int raw in RawSweep)
        {
            int expectedFloor = (int)Math.Round(raw * CombatFormulas.MinDamageFloorPercent, MidpointRounding.AwayFromZero);
            Assert.That(CombatFormulas.ResolveDamage(raw, HugeProtection), Is.EqualTo(expectedFloor),
                $"through huge mitigation the hit floors at 12% of raw={raw}");
        }
        // Any hit of real size (>= 5 raw) always deals at least 1 through any DEF.
        Assert.That(CombatFormulas.ResolveDamage(5, HugeProtection), Is.GreaterThanOrEqualTo(1));
        Assert.That(CombatFormulas.ResolveDamage(1000, HugeProtection), Is.GreaterThanOrEqualTo(1));
    }

    // Player-vs-NPC HP damage uses a HIGHER floor (35%) so a low-offense build still chips a tanky mob
    // instead of walling — and it is never below the standard floor.
    [Test]
    public void ResolvePlayerVsNpcDamage_FloorsHigher_AndNeverBelowStandard()
    {
        foreach (int raw in RawSweep)
        {
            int expectedPve = (int)Math.Round(raw * CombatFormulas.PveMinDamageFloorPercent, MidpointRounding.AwayFromZero);
            Assert.That(CombatFormulas.ResolvePlayerVsNpcDamage(raw, HugeProtection), Is.EqualTo(expectedPve),
                $"PvE HP damage floors at 35% of raw={raw}");
            Assert.That(CombatFormulas.ResolvePlayerVsNpcDamage(raw, HugeProtection),
                Is.GreaterThanOrEqualTo(CombatFormulas.ResolveDamage(raw, HugeProtection)),
                "the PvE floor is never below the standard floor");
        }
    }

    // A crit always beats the raw hit it upgrades (1.25x + positive noise + 1), for every sample.
    [Test]
    public void CritDamage_AlwaysExceedsRaw()
    {
        foreach (int raw in RawSweep)
        {
            for (int i = 0; i < 2000; i++)
                Assert.That(CombatFormulas.CritDamage(raw), Is.GreaterThan(raw), $"a crit must exceed raw={raw}");
        }
    }

    // Variance stays inside the +/-DamageVariance band around the input, for every sample.
    [Test]
    public void Vary_StaysWithinVarianceBand()
    {
        double v = CombatFormulas.DamageVariance;
        foreach (int dmg in RawSweep)
        {
            int lo = (int)Math.Floor(dmg * (1.0 - v));
            int hi = (int)Math.Ceiling(dmg * (1.0 + v));
            for (int i = 0; i < 2000; i++)
            {
                int varied = CombatFormulas.Vary(dmg);
                Assert.That(varied, Is.InRange(lo, hi), $"Vary({dmg}) must stay within +/-{v:P0}");
            }
        }
    }

    // HP and MP are one 1:1 pool at equal investment: at the same level and stat, DEF-driven HP equals
    // INT-driven MP (the mirror's defining pool property).
    [Test]
    public void PlayerMaxHp_EqualsMaxMp_AtEqualInvestment()
    {
        foreach (int level in LevelSweep)
        {
            foreach (int stat in StatSweep)
            {
                Assert.That(StatFormulas.GetPlayerMaxHp(level, stat, 0), Is.EqualTo(StatFormulas.GetPlayerMaxMp(level, stat, 0)),
                    $"HP (DEF) must equal MP (INT) at level={level}, stat={stat}");
            }
        }
    }

    // Caster reagent parity: the on-death reagent loss = per-cast cost x multiplier, scaled by
    // the death's wear percent (a PK/war 20% death costs double a normal 10% death), and 0 with no tier.
    [Test]
    public void CasterDeathReagentLoss_ScalesWithTierAndWearPercent()
    {
        double perCast = CombatFormulas.SubHpReagentCostExact(100);
        int normal = CombatFormulas.CasterDeathReagentLoss(tierLevel: 100, wearPercent: 10);
        Assert.That(normal, Is.EqualTo((int)Math.Round(perCast * Constants.CasterDeathReagentMultiplier, MidpointRounding.AwayFromZero)));
        Assert.That(CombatFormulas.CasterDeathReagentLoss(100, 20), Is.EqualTo(normal * 2));   // PK/war doubles it
        Assert.That(CombatFormulas.CasterDeathReagentLoss(tierLevel: 0, wearPercent: 10), Is.EqualTo(0));   // no tier
    }

    /// <summary>Scripted rolls for <see cref="CombatFormulas.RollReagents"/>. NextDouble is the only member
    /// exercised, so the rest throw rather than quietly returning zero.</summary>
    sealed class Doubles(params double[] values) : IRandomSource
    {
        private int _i;
        public double NextDouble() => values[_i++];
        public int Next(int maxExclusive) => throw new NotSupportedException();
        public int Next(int minInclusive, int maxExclusive) => throw new NotSupportedException();
        public long NextInt64(long minInclusive, long maxExclusive) => throw new NotSupportedException();
    }

    // A cast takes its whole reagent count or nothing, exactly as a swing takes 1 durability or none.
    // Asserted against scripted rolls on both sides of the threshold, so the test reads as "this roll
    // charged this much" — and the amount is never a fraction of an item.
    [Test]
    public void RollReagents_ChargesTheFullCount_OrNothing()
    {
        // Level 1: one reagent on 9.6% of casts, so nearly every cast is free.
        double level1 = CombatFormulas.SubHpReagentCostExact(1);
        Assert.That(CombatFormulas.ReagentCostPerCast(level1), Is.EqualTo(1));
        Assert.That(CombatFormulas.RollReagents(level1, new Doubles(0.99)), Is.EqualTo(0), "roll above the odds: free");
        Assert.That(CombatFormulas.RollReagents(level1, new Doubles(0.01)), Is.EqualTo(1), "roll under the odds: charged");

        // Level 255: four reagents on 93.9% of casts — the count is flat, only the odds moved.
        double level255 = CombatFormulas.SubHpReagentCostExact(255);
        Assert.That(CombatFormulas.ReagentCostPerCast(level255), Is.EqualTo(4));
        Assert.That(CombatFormulas.RollReagents(level255, new Doubles(0.99)), Is.EqualTo(0));
        Assert.That(CombatFormulas.RollReagents(level255, new Doubles(0.01)), Is.EqualTo(4));

        // The displayed odds ARE the odds rolled against, at both ends of the ladder.
        Assert.That(CombatFormulas.ReagentDepleteChancePercent(level1), Is.EqualTo(level1 * 100).Within(0.0001));
        Assert.That(CombatFormulas.ReagentDepleteChancePercent(level255), Is.EqualTo(level255 / 4 * 100).Within(0.0001));

        // A free cast (arena waiver) draws no randomness at all.
        Assert.That(CombatFormulas.RollReagents(0, new Doubles()), Is.EqualTo(0));
    }
}

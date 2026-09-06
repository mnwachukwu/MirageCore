using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The min-damage floors — the rule that mitigation is never immunity, and the boss variant that
/// keeps a dedicated tank under pressure.
///
/// <para>A tank's mitigation outgrows NPC damage at every level: a level-255 Knight carries ~1898 MIT
/// against a boss's ~1385 raw, so <em>every</em> boss hit lands on the floor and nothing about the fight
/// can threaten them. Raising the floor for bosses alone restores that pressure. The property worth
/// pinning is that it is surgical — it changes what a tank takes and nothing else.</para></summary>
[TestFixture]
public class DamageFloorTests
{
    // A tank: mitigation far above the attacker's raw, so the floor is what lands.
    const int TankRaw = 1385, TankMit = 1898;
    // A squishy target: raw comfortably clears mitigation, so subtraction wins and the floor never binds.
    const int SquishyRaw = 1385, SquishyMit = 400;

    [Test]
    public void Floors_AreOrdered()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CombatFormulas.MinDamageFloorPercent, Is.EqualTo(0.12).Within(1e-9));
            Assert.That(CombatFormulas.BossMinDamageFloorPercent, Is.EqualTo(0.19).Within(1e-9));
            Assert.That(CombatFormulas.BossMinDamageFloorPercent,
                Is.GreaterThan(CombatFormulas.MinDamageFloorPercent), "a boss must hit at least as hard as a mob");
        });
    }

    [Test]
    public void AgainstATank_ABossHitsHarderThanAnOrdinaryMob()
    {
        int mob = CombatFormulas.ResolveNpcVsPlayerDamage(TankRaw, TankMit, isBoss: false);
        int boss = CombatFormulas.ResolveNpcVsPlayerDamage(TankRaw, TankMit, isBoss: true);
        Assert.Multiple(() =>
        {
            Assert.That(mob, Is.EqualTo((int)Math.Round(TankRaw * CombatFormulas.MinDamageFloorPercent)));
            Assert.That(boss, Is.EqualTo((int)Math.Round(TankRaw * CombatFormulas.BossMinDamageFloorPercent)));
            Assert.That(boss, Is.GreaterThan(mob), "the whole point of the boss floor");
        });
    }

    // The surgical claim, and the reason this change was safe to make: the floor only binds once
    // mitigation has already eaten ~81% of the raw. Below that, subtraction wins and boss-ness is
    // irrelevant — an ordinary player's damage taken does not move at all.
    [Test]
    public void AgainstASquishyTarget_BossnessChangesNothing()
    {
        int mob = CombatFormulas.ResolveNpcVsPlayerDamage(SquishyRaw, SquishyMit, isBoss: false);
        int boss = CombatFormulas.ResolveNpcVsPlayerDamage(SquishyRaw, SquishyMit, isBoss: true);
        Assert.Multiple(() =>
        {
            Assert.That(boss, Is.EqualTo(mob), "the floor does not bind here, so nothing should change");
            Assert.That(mob, Is.EqualTo(SquishyRaw - SquishyMit), "plain subtraction wins");
        });
    }

    // Where exactly the two regimes meet, swept rather than asserted at one point — the crossover is the
    // claim, so it should be visible as a boundary and not a single lucky value.
    [Test]
    public void TheFloorBindsOnlyOnceMitigationIsOverwhelming()
    {
        const int raw = 1000;
        bool sawSubtraction = false, sawFloor = false;
        for (int mit = 0; mit <= raw; mit += 10)
        {
            int boss = CombatFormulas.ResolveNpcVsPlayerDamage(raw, mit, isBoss: true);
            int mob = CombatFormulas.ResolveNpcVsPlayerDamage(raw, mit, isBoss: false);
            if (raw - mit > raw * CombatFormulas.BossMinDamageFloorPercent)
            {
                Assert.That(boss, Is.EqualTo(mob), $"above the floor at mit {mit}, boss-ness must not matter");
                sawSubtraction = true;
            }
            else
            {
                Assert.That(boss, Is.GreaterThanOrEqualTo(mob), $"on the floor at mit {mit}");
                sawFloor = true;
            }
        }
        Assert.That(sawSubtraction && sawFloor, Is.True, "the sweep should cross both regimes");
    }

    [Test]
    public void NoAmountOfMitigationGrantsImmunity()
    {
        foreach (bool isBoss in new[] { false, true })
            Assert.That(CombatFormulas.ResolveNpcVsPlayerDamage(500, 100_000, isBoss), Is.GreaterThan(0),
                "stacked mitigation is never full immunity");
    }

    // The PvE floor is the mirror on the other side — a low-offense hybrid whose raw sits under a tanky
    // mob's mitigation would otherwise grind roughly ten times a pure's kill time.
    [Test]
    public void PlayerVsNpc_UsesItsOwnHigherFloor()
    {
        const int raw = 1000, mit = 5000;
        Assert.Multiple(() =>
        {
            Assert.That(CombatFormulas.PveMinDamageFloorPercent, Is.GreaterThan(CombatFormulas.MinDamageFloorPercent));
            Assert.That(CombatFormulas.ResolvePlayerVsNpcDamage(raw, mit),
                Is.EqualTo((int)Math.Round(raw * CombatFormulas.PveMinDamageFloorPercent)));
        });
    }
}

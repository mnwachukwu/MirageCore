using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>Stable structural invariants of the player vital/regen formulas — the properties that must hold
/// across balance retunes (so these don't pin exact tuned magnitudes, which the parity/combat suites already
/// exercise): pools grow monotonically with their stat and with level, HP and MP share one pool shape, the
/// full-strength regen floor, and the weather-reduced tick's round-DOWN + lower floor behavior.</summary>
[TestFixture]
public class StatFormulasTests
{
    // ── Monotonicity ─────────────────────────────────────────────────────────────

    [Test]
    public void PlayerMaxPools_GrowWithTheirStat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.GetPlayerMaxHp(10, 40, 5), Is.GreaterThan(StatFormulas.GetPlayerMaxHp(10, 20, 5)), "HP rises with DEF");
            Assert.That(StatFormulas.GetPlayerMaxMp(10, 40, 5), Is.GreaterThan(StatFormulas.GetPlayerMaxMp(10, 20, 5)), "MP rises with INT");
            Assert.That(StatFormulas.GetPlayerMaxSp(10, 40, 5), Is.GreaterThan(StatFormulas.GetPlayerMaxSp(10, 20, 5)), "SP rises with SPD");
        });
    }

    [Test]
    public void PlayerMaxHp_GrowsWithLevel()
        => Assert.That(StatFormulas.GetPlayerMaxHp(20, 20, 5), Is.GreaterThan(StatFormulas.GetPlayerMaxHp(10, 20, 5)));

    // HP (off DEF) and MP (off INT) sit on ONE mirrored pool shape: the same numeric stat yields the same pool.
    [Test]
    public void PlayerMaxHp_And_MaxMp_MirrorOnTheSameStat()
    {
        for (int stat = 0; stat <= 60; stat += 15)
        {
            Assert.That(StatFormulas.GetPlayerMaxMp(12, stat, 7), Is.EqualTo(StatFormulas.GetPlayerMaxHp(12, stat, 7)),
                $"HP and MP pools must match at stat {stat}");
        }
    }

    // ── Regen floors + reduced-tick rounding ─────────────────────────────────────

    // At full strength a low stat still gets its vital's floor (SP shows it cleanly: linear + low magnitude).
    [Test]
    public void PlayerSpRegen_FullStrength_FloorsAtFour()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.GetPlayerSpRegen(0, 1.0), Is.EqualTo(4), "0-SPD still ticks the floor");
            Assert.That(StatFormulas.GetPlayerSpRegen(4, 1.0), Is.EqualTo(4), "still at the floor here");
        });
    }

    /// <summary>SP's floor is the only one a player ever actually sits on, which is why it is its own
    /// number rather than the shared one.
    ///
    /// <para>HP and MP regen are quadratic in their stat and shifted by 15, so even a stat of ZERO
    /// produces 3 — their floor is a safety net nothing reaches. SP is linear and starts at nothing, so
    /// its floor is the live resting rate for every build that bought no SPD. That makes it a comfort
    /// dial: what stops a sprint being free is the held tick, not a small rate, so this can be set purely
    /// by how long such a build should wait.</para></summary>
    [Test]
    public void SpFloorIsTheOnlyOneReached_SoItIsItsOwnNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.GetPlayerSpRegen(0, 1.0), Is.EqualTo(4), "SP sits on its floor");
            Assert.That(StatFormulas.GetPlayerHpRegen(0, 1.0), Is.GreaterThan(2), "HP clears its floor at once");
            Assert.That(StatFormulas.GetPlayerMpRegen(0, 1.0), Is.GreaterThan(2), "so does MP");
            // SPD still has to pay: a real investment must out-tick the floor.
            Assert.That(StatFormulas.GetPlayerSpRegen(75, 1.0),
                Is.GreaterThan(StatFormulas.GetPlayerSpRegen(0, 1.0)),
                "a mid SPD investment must beat no investment at all");
        });
    }

    // A weather-reduced tick (mult < 1) rounds DOWN (2.5 → 2, not 3) so the penalty always bites...
    [Test]
    public void PlayerSpRegen_ReducedTick_RoundsDown()
        => Assert.That(StatFormulas.GetPlayerSpRegen(10, 0.5), Is.EqualTo(2),
            "10*0.25(weight)*2(pool scale)*0.5(weather) = 2.5 floors to 2, not rounds to 3");

    // ...and a reduced tick can fall BELOW the normal floor of 2, but never to 0. Shown on HP, which is
    // the vital with room to fall — SP already sits at 1, so it has none left.
    [Test]
    public void PlayerRegen_ReducedTick_DipsBelowNormalFloor_ButNotZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.GetPlayerHpRegen(0, 1.0), Is.GreaterThan(2), "full strength clears the floor");
            Assert.That(StatFormulas.GetPlayerHpRegen(0, 0.5), Is.EqualTo(1), "a reduced tick falls under it");
            Assert.That(StatFormulas.GetPlayerSpRegen(0, 0.5), Is.EqualTo(1), "and never to 0");
        });
    }

    // A reduced tick is never stronger than the full tick, at any stat.
    [Test]
    public void PlayerRegen_ReducedNeverExceedsFull()
    {
        for (int stat = 0; stat <= 60; stat += 3)
        {
            Assert.That(StatFormulas.GetPlayerHpRegen(stat, 0.5), Is.LessThanOrEqualTo(StatFormulas.GetPlayerHpRegen(stat, 1.0)), $"HP regen @def {stat}");
            Assert.That(StatFormulas.GetPlayerMpRegen(stat, 0.5), Is.LessThanOrEqualTo(StatFormulas.GetPlayerMpRegen(stat, 1.0)), $"MP regen @int {stat}");
            Assert.That(StatFormulas.GetPlayerSpRegen(stat, 0.5), Is.LessThanOrEqualTo(StatFormulas.GetPlayerSpRegen(stat, 1.0)), $"SP regen @spd {stat}");
        }
    }

    // Every regen tick is at least 1, even at 0 stat on a reduced tick — resting never fully stalls.
    [Test]
    public void PlayerRegen_NeverZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.GetPlayerHpRegen(0, 0.5), Is.GreaterThanOrEqualTo(1));
            Assert.That(StatFormulas.GetPlayerMpRegen(0, 0.5), Is.GreaterThanOrEqualTo(1));
            Assert.That(StatFormulas.GetPlayerSpRegen(0, 0.5), Is.GreaterThanOrEqualTo(1));
        });
    }
}

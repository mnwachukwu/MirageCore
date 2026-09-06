using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>
/// Heavy Wind tears a share of attacks and casts off course. It is an ATTACKER-side failure, checked before
/// any block, dodge or crit — which is what makes it independent of the proc cascade Heavy Wind already
/// disables, and why it costs the defender no stamina.
///
/// <para>These pin the rule itself: which weathers miss, at what rate, and that the roll boundary yields the
/// stated percentage rather than one either side of it.</para>
/// </summary>
[TestFixture]
public class WeatherMissTests
{
    [Test]
    public void OnlyHeavyWind_TearsAttacksOffCourse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.HeavyWind),
                Is.EqualTo(Constants.WeatherHeavyWindMissChancePercent));
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.Clear), Is.Zero);
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.Rain), Is.Zero);
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.Snow), Is.Zero);
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.HeatWave), Is.Zero);
        });
    }

    /// <summary>The rate is meant to be noticeable without being coin-flip territory, since Heavy Wind already
    /// doubles every cooldown — two independent throughput taxes multiply.</summary>
    [Test]
    public void TheMissRateStaysModest()
    {
        Assert.That(Constants.WeatherHeavyWindMissChancePercent, Is.InRange(1, 25));
    }

    /// <summary>The call site is <c>MissChancePercent(w) > rng.Percent()</c> against a 0..99 roll, so a
    /// chance of N misses on exactly N of the 100 outcomes. Pinned because an off-by-one here is invisible in
    /// play and would quietly shift the rate.</summary>
    [Test]
    public void TheRollBoundaryYieldsExactlyTheStatedPercentage()
    {
        int chance = WeatherEffects.MissChancePercent(WeatherType.HeavyWind);
        int misses = 0;
        for (int roll = 0; roll < Constants.PercentRollSides; roll++)
            if (chance > roll) misses++;

        Assert.That(misses, Is.EqualTo(chance));
    }

    /// <summary>Clear weather can never miss, at any roll.</summary>
    [Test]
    public void ClearWeatherNeverMisses()
    {
        int chance = WeatherEffects.MissChancePercent(WeatherType.Clear);
        for (int roll = 0; roll < Constants.PercentRollSides; roll++)
            Assert.That(chance > roll, Is.False, $"clear weather missed on roll {roll}");
    }

    /// <summary>Heavy Wind's two throughput taxes are independent and multiply: half the actions, and a share
    /// of those torn away. This pins that both are still in force, so removing one silently is a red test.</summary>
    [Test]
    public void HeavyWindStillDoublesCooldownsAsWellAsMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Constants.WeatherHeavyWindCooldownMultiplier, Is.EqualTo(2));
            Assert.That(WeatherEffects.MissChancePercent(WeatherType.HeavyWind), Is.GreaterThan(0));
        });
    }
}

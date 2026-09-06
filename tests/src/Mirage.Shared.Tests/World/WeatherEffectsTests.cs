using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>Weather's effect on vital regen (WeatherEffects): Heat Wave and Snow halve the regen magnitude;
/// every other weather leaves it at full. This one multiplier gates the regen amount in RegenerationSystem,
/// NpcAiSystem, and the StatsPanel "effective regen" preview, so the three must agree on it.</summary>
[TestFixture]
public class WeatherEffectsTests
{
    [Test]
    public void ReducesRegen_OnlyHeatWaveAndSnow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WeatherEffects.ReducesRegen(WeatherType.HeatWave), Is.True);
            Assert.That(WeatherEffects.ReducesRegen(WeatherType.Snow), Is.True);
            Assert.That(WeatherEffects.ReducesRegen(WeatherType.Clear), Is.False);
            Assert.That(WeatherEffects.ReducesRegen(WeatherType.Rain), Is.False);
            Assert.That(WeatherEffects.ReducesRegen(WeatherType.HeavyWind), Is.False);
        });
    }

    [Test]
    public void RegenMultiplier_HalvesUnderHeatWaveAndSnow_FullOtherwise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WeatherEffects.RegenMultiplier(WeatherType.HeatWave), Is.EqualTo(Constants.WeatherReducedRegenMultiplier));
            Assert.That(WeatherEffects.RegenMultiplier(WeatherType.Snow), Is.EqualTo(Constants.WeatherReducedRegenMultiplier));
            Assert.That(WeatherEffects.RegenMultiplier(WeatherType.Clear), Is.EqualTo(1.0));
            Assert.That(WeatherEffects.RegenMultiplier(WeatherType.Rain), Is.EqualTo(1.0));
            Assert.That(WeatherEffects.RegenMultiplier(WeatherType.HeavyWind), Is.EqualTo(1.0));
        });
    }
}

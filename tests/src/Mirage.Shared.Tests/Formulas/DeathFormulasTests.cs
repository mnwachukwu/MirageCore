using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The escalating / decaying / capped non-war respawn penalty.</summary>
[TestFixture]
public class DeathFormulasTests
{
    [Test]
    public void FirstDeath_IsOneStep()
    {
        Assert.That(DeathFormulas.NextPenaltySteps(prevSteps: 0, lastDeathUtc: 0, nowUtc: 1_000_000), Is.EqualTo(1));
        Assert.That(DeathFormulas.RespawnDelaySeconds(1), Is.EqualTo(Constants.RespawnPenaltyStepSeconds));
    }

    [Test]
    public void RapidDeaths_EscalateOneStepEach()
    {
        long t = 1_000_000;
        Assert.That(DeathFormulas.NextPenaltySteps(1, t, t), Is.EqualTo(2));   // 2nd death, no time passed
        Assert.That(DeathFormulas.NextPenaltySteps(2, t, t), Is.EqualTo(3));
    }

    [Test]
    public void Decay_ShedsOneStepPerFullMinute()
    {
        long last = 1_000_000;
        Assert.That(DeathFormulas.NextPenaltySteps(5, last, last + 3 * 60), Is.EqualTo(3));   // decay 3 → 2, +1 → 3
        Assert.That(DeathFormulas.NextPenaltySteps(5, last, last + 60 * 60), Is.EqualTo(1));  // fully decayed, floor 1
    }

    [Test]
    public void Cap_AtMaxSteps()
    {
        Assert.That(DeathFormulas.NextPenaltySteps(Constants.RespawnMaxPenaltySteps, 1_000_000, 1_000_000),
            Is.EqualTo(Constants.RespawnMaxPenaltySteps));
        Assert.That(DeathFormulas.RespawnDelaySeconds(Constants.RespawnMaxPenaltySteps),
            Is.EqualTo(Constants.RespawnMaxPenaltySteps * Constants.RespawnPenaltyStepSeconds));   // 120s cap
    }
}

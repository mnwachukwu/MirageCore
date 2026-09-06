using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// The rule that decides whether an NPC may act this tick.
///
/// <para>An NPC only acts on an AI tick, so a cooldown can only be satisfied at a multiple of
/// <see cref="Constants.AiTickIntervalMs"/>. Every NPC cooldown in the game is 1000 ms and the tick is 500,
/// which puts every deadline EXACTLY on a tick boundary — the one place a plain "has it been long enough?"
/// is decided by microseconds rather than by design.</para>
///
/// <para>Left alone, that cost every mob a third of its attacks: two ticks is 1000 ms, a strict comparison
/// rejects it, and the swing lands on the third tick at 1500 ms. These pin the beat at two ticks and pin it
/// against a tick arriving slightly early or slightly late, which is what made the NPC-vs-NPC path flip
/// between the two depending on how much work the tick had already done.</para>
/// </summary>
[TestFixture]
public class AiCadenceTests
{
    private const long Tick = Constants.AiTickIntervalMs;          // 500
    private const long Cooldown = Constants.NpcAttackCooldownMs;   // 1000

    [Test]
    public void ATickLandingExactlyOnTheDeadline_Counts()
    {
        Assert.That(AiCadence.Elapsed(now: 1000, since: 0, Cooldown), Is.True);
    }

    /// <summary>The case that made it intermittent. The gate is reached partway through a tick's work, so
    /// the clock reads a little past the boundary — or, on a quiet tick, a little before it. Both are the
    /// same beat and both have to answer the same way.</summary>
    [TestCase(-40, TestName = "a tick arriving early still counts")]
    [TestCase(0, TestName = "a tick arriving on the beat counts")]
    [TestCase(40, TestName = "a tick arriving late counts")]
    public void ATickEitherSideOfTheDeadline_Counts(long skew)
    {
        Assert.That(AiCadence.Elapsed(now: 1000 + skew, since: 0, Cooldown), Is.True);
    }

    [Test]
    public void TheTickBefore_DoesNot()
    {
        Assert.That(AiCadence.Elapsed(now: 500, since: 0, Cooldown), Is.False,
            "one tick in is half the cooldown, however the clock jitters");
    }

    /// <summary>The tolerance is half a tick and not more. A full tick of slack would let the beat land one
    /// tick early, which is the same bug pointing the other way.</summary>
    [Test]
    public void ItNeverLetsABeatLandAWholeTickEarly()
    {
        Assert.That(AiCadence.TickToleranceMs, Is.LessThan(Tick));
        Assert.That(AiCadence.Elapsed(now: 1000 - Tick, since: 0, Cooldown), Is.False);
    }

    /// <summary>Walked out over a run of ticks: the swing lands on every second one, which is the cooldown
    /// the constant declares. Two would be 1000 ms; three would be the 1500 ms this exists to prevent.</summary>
    [Test]
    public void OverARunOfTicks_TheBeatIsTwoTicksAndStaysThere()
    {
        long attackTimer = 0;
        var beats = new List<long>();
        for (long now = Tick; now <= 20 * Tick; now += Tick)
        {
            if (!AiCadence.Elapsed(now, attackTimer, Cooldown)) continue;
            beats.Add(now - attackTimer);
            attackTimer = now;
        }

        Assert.That(beats, Is.Not.Empty);
        Assert.That(beats, Is.All.EqualTo(2 * Tick));
    }

    /// <summary>Heavy Wind doubles the cooldown, which is still a whole number of ticks and so still lands
    /// on a boundary — the scaled deadline needs the same tolerance as the plain one.</summary>
    [Test]
    public void ADoubledCooldown_LandsOnItsOwnBoundaryToo()
    {
        long doubled = Cooldown * Constants.WeatherHeavyWindCooldownMultiplier;
        Assert.Multiple(() =>
        {
            Assert.That(AiCadence.Elapsed(now: doubled, since: 0, doubled), Is.True);
            Assert.That(AiCadence.Elapsed(now: doubled - Tick, since: 0, doubled), Is.False);
        });
    }
}

using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>
/// A creature's cooldown, asked on the beat it acts on.
///
/// <para>🔴 <b>The case this exists for is the exact boundary.</b> A cooldown a whole number of beats
/// long ends ON a beat, and every other creature cooldown in a world tends to be exactly that — so the
/// boundary is not a rare case, it is the normal one.</para>
/// </summary>
[TestFixture]
public sealed class AiCadenceTests
{
    private const long Beat = Constants.AiTickIntervalMs;
    private const long Cooldown = Constants.NpcAttackCooldownMs;

    /// <summary>A beat arriving a hair EARLY still counts. Without this the creature is refused and
    /// waits another whole beat, so its swings land at one second or one and a half at random.</summary>
    [Test]
    public void ABeatThatArrivesEarlyStillCounts()
    {
        Assert.That(AiCadence.Elapsed(now: Cooldown - 1, since: 0, Cooldown), Is.True);
    }

    [Test]
    public void ABeatExactlyOnTheDeadlineCounts()
    {
        Assert.That(AiCadence.Elapsed(now: Cooldown, since: 0, Cooldown), Is.True);
    }

    /// <summary>⚠ The slack is half a beat and no more. A whole beat of it would let a creature act one
    /// beat early every time, which is a different cooldown rather than a steadier one.</summary>
    [Test]
    public void TheBeatBeforeIsStillTooEarly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AiCadence.Elapsed(now: Cooldown - Beat, since: 0, Cooldown), Is.False);
            Assert.That(AiCadence.Elapsed(now: 0, since: 0, Cooldown), Is.False);
        });
    }

    /// <summary>The slack is exactly half an interval, so a deadline resolves to the nearest beat rather
    /// than the next one after it.</summary>
    [Test]
    public void TheSlackIsHalfABeat()
    {
        Assert.That(AiCadence.ToleranceMs, Is.EqualTo(Beat / 2));
    }

    /// <summary>Two creatures on the same cooldown swing on the same beat, however much work the beat
    /// did before it reached either of them. That is the property the rounding buys: without it, the one
    /// reached later in the sweep can fall a whole beat behind the one reached first.</summary>
    [Test]
    public void TwoCreaturesOnOneCooldownSwingTogether()
    {
        // The same deadline, reached at slightly different moments within one beat.
        const long deadline = Cooldown;

        Assert.Multiple(() =>
        {
            Assert.That(AiCadence.Elapsed(deadline - 40, since: 0, Cooldown), Is.True, "reached early in the beat");
            Assert.That(AiCadence.Elapsed(deadline + 40, since: 0, Cooldown), Is.True, "and late in the same beat");
        });
    }
}

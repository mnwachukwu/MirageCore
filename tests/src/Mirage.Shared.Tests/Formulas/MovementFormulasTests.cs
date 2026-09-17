using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>
/// Move speed to a per-tile pace.
///
/// <para>🔴 The client predicts a step and the server bills it, both through here, so these numbers are
/// a contract between two processes rather than a tuning table. A change that looks like a retune
/// desynchronizes every running player until both sides ship — which shows up as rubber-banding, not as
/// anything that says "the formula moved".</para></summary>
[TestFixture]
public class MovementFormulasTests
{
    [Test]
    public void RunIsExactlyTwiceWalkPaceBeforeAnyMoveSpeed()
    {
        Assert.That(MovementFormulas.BaseWalkMsPerTile, Is.EqualTo(MovementFormulas.BaseRunMsPerTile * 2f));
    }

    [Test]
    public void RunMsPerTile_MatchesTheDocumentedCadences()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementFormulas.RunMsPerTile(0), Is.EqualTo(200f).Within(0.5f));
            Assert.That(MovementFormulas.RunMsPerTile(75), Is.EqualTo(160f).Within(0.5f));
            Assert.That(MovementFormulas.RunMsPerTile(150), Is.EqualTo(133.3f).Within(0.5f));
        });
    }

    /// <summary>The base run is a hard floor. Move speed is a pure additive bonus, so a world that sets
    /// none — which is every world until a game does — moves at exactly the baseline rather than at
    /// nothing.</summary>
    [Test]
    public void NoMoveSpeed_IsTheBaselineAndNeverSlower()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementFormulas.RunMsPerTile(0), Is.EqualTo(MovementFormulas.BaseRunMsPerTile));
            Assert.That(MovementFormulas.RunMsPerTile(-50), Is.EqualTo(MovementFormulas.BaseRunMsPerTile),
                "a negative speed is a bug upstream, and must not be faster OR slower than the baseline");
        });
    }

    [Test]
    public void PastTheCap_AddsNothingFurther()
    {
        Assert.That(MovementFormulas.RunMsPerTile(1_000), Is.EqualTo(MovementFormulas.RunMsPerTile(150)));
    }

    /// <summary>An NPC's run is flat, whatever its move speed, which keeps a chase escapable: a
    /// body that invests in speed pulls away, because its pace drops below the one the NPC is pinned
    /// at.</summary>
    [Test]
    public void AnNpcRunsAtTheBaseline_WhateverItsMoveSpeed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementFormulas.NpcRunMsPerTile(0), Is.EqualTo(MovementFormulas.BaseRunMsPerTile));
            Assert.That(MovementFormulas.NpcRunMsPerTile(150), Is.EqualTo(MovementFormulas.BaseRunMsPerTile));
            Assert.That(MovementFormulas.RunMsPerTile(150),
                Is.LessThan(MovementFormulas.NpcRunMsPerTile(150)),
                "a fast body must out-pace any NPC, or investing in speed buys nothing");
        });
    }
}

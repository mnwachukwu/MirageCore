using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>SPD to movement speed, and the display figure derived from it.
///
/// <para>The display half is pinned because it is the half that can be quietly wrong: the number is a
/// COMPARISON, and a comparison against the wrong baseline still looks plausible. Measured against
/// walking, sprint is +100% at 0 SPD and +200% at the cap; measured against the 0-SPD RUN it would be
/// 100% and 150%. Those agree at 0 SPD and nowhere else, so a fresh character cannot tell the two
/// apart and an invested one is understated by a whole walk's worth of speed.</para></summary>
[TestFixture]
public class MovementFormulasTests
{
    [Test]
    public void RunIsExactlyTwiceWalkPaceBeforeAnySpd()
    {
        // The premise the Sprint figure rests on. If these two constants ever drift apart, +100% stops
        // being the right thing to show a brand-new character and this is where that shows up.
        Assert.That(MovementFormulas.BaseWalkMsPerTile, Is.EqualTo(MovementFormulas.BaseRunMsPerTile * 2f));
    }

    // The label reports what SPD BOUGHT, over the sprint everyone starts with — so it and the 1.5x run cap
    // are the same statement, and a reader cannot mistake it for a multiplier.
    [TestCase(0, 0, TestName = "0 SPD has bought nothing yet (+0%)")]
    [TestCase(75, 25, TestName = "Half the cap's SPD is half the bonus (+25%)")]
    [TestCase(135, 45, TestName = "Just short of the cap (+45%)")]
    [TestCase(150, 50, TestName = "Cap SPD is the full bonus (+50%), matching the 1.5x run cap")]
    [TestCase(300, 50, TestName = "Past the cap adds nothing further")]
    public void SprintBonusPercent_IsMeasuredAgainstTheBaseRun(int spd, int expected)
    {
        Assert.That(MovementFormulas.SprintBonusPercent(spd), Is.EqualTo(expected));
    }

    /// <summary>The label and the cap have to agree, or one of them is lying about the same fact.</summary>
    [Test]
    public void TheLabelAtTheCap_MatchesTheRunCapItself()
    {
        float capMultiplier = MovementFormulas.BaseRunMsPerTile / MovementFormulas.RunMsPerTile(1_000);

        Assert.That(MovementFormulas.SprintBonusPercent(1_000),
            Is.EqualTo((int)Math.Round((capMultiplier - 1f) * 100f)));
    }

    [Test]
    public void SprintBonusPercent_NeverDropsBelowTheFloor()
    {
        // Base run is a hard floor: SPD is a pure additive bonus and a negative or zero SPD must not
        // make a character slower than the baseline everyone starts at.
        Assert.That(MovementFormulas.SprintBonusPercent(-50), Is.Zero);
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
}

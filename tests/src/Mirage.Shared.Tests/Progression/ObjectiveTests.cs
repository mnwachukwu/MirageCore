using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Progression;

/// <summary>The shared objective primitive (kernel #5): kind/target-gated advance, the single
/// completion edge, progress clamping, and independent Clone.</summary>
[TestFixture]
public class ObjectiveTests
{
    private static Objective KillN(int target, int count) =>
        new() { Kind = ObjectiveKind.Kill, Target = target, Count = count };

    [Test]
    public void Advance_MatchingKindAndTarget_IncrementsProgress()
    {
        var o = KillN(target: 5, count: 3);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5), Is.False);   // 1/3 — advanced but not complete
        Assert.That(o.Progress, Is.EqualTo(1));
        Assert.That(o.IsComplete, Is.False);
    }

    [Test]
    public void Advance_WrongKind_DoesNothing()
    {
        var o = KillN(5, 3);
        Assert.That(o.TryAdvance(ObjectiveKind.Fetch, 5), Is.False);
        Assert.That(o.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Advance_WrongTarget_DoesNothing()
    {
        var o = KillN(5, 3);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 6), Is.False);
        Assert.That(o.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Advance_WildcardTarget_MatchesAnyTarget()
    {
        var o = KillN(target: 0, count: 2);   // 0 = any target of this kind
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 42), Is.False);   // 1/2
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 99), Is.True);    // 2/2 — completes on any NPC
        Assert.That(o.Progress, Is.EqualTo(2));
    }

    [Test]
    public void Advance_CompletingCall_ReturnsTrueExactlyOnce()
    {
        var o = KillN(5, 2);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5), Is.False);  // 1/2
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5), Is.True);   // 2/2 — the one completion edge
        Assert.That(o.IsComplete, Is.True);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5), Is.False);  // already complete — never re-fires
        Assert.That(o.Progress, Is.EqualTo(2));                      // and never overshoots
    }

    [Test]
    public void Advance_LargeAmount_ClampsAtCount()
    {
        var o = KillN(5, 3);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5, amount: 10), Is.True);
        Assert.That(o.Progress, Is.EqualTo(3));
    }

    [Test]
    public void Advance_NonPositiveAmount_DoesNothing()
    {
        var o = KillN(5, 3);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5, amount: 0), Is.False);
        Assert.That(o.TryAdvance(ObjectiveKind.Kill, 5, amount: -4), Is.False);
        Assert.That(o.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Clone_IsIndependentCopy()
    {
        var o = KillN(5, 3);
        o.TryAdvance(ObjectiveKind.Kill, 5);   // original → 1
        var c = o.Clone();
        c.TryAdvance(ObjectiveKind.Kill, 5);   // clone → 2
        Assert.That(o.Progress, Is.EqualTo(1));  // original untouched
        Assert.That(c.Progress, Is.EqualTo(2));
    }
}

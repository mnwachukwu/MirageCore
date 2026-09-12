using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The editor's advice about an NPC's reach.
///
/// <para>Nothing here refuses a value — Range is free, and a mob that notices the whole map is a legal
/// thing to author. What the editor owes is a word about what the number will feel like in play.</para>
/// </summary>
[TestFixture]
public class NpcRangeWarningTests
{
    private static NpcRowViewModel Row(NpcBehavior behavior, int range) =>
        new(1, new NpcRecord { Name = "Thing", Behavior = behavior, Range = range }, isLoaded: false);

    [TestCase(NpcBehavior.Pursue, 2)]
    [TestCase(NpcBehavior.Pursue, 6)]
    [TestCase(NpcBehavior.Flee, 2)]
    [TestCase(NpcBehavior.Wander, 0)]
    public void AnOrdinaryReach_SaysNothing(NpcBehavior behavior, int range)
    {
        Assert.That(Row(behavior, range).HasRangeWarning, Is.False);
    }

    /// <summary>Past what a player can see, whatever the behavior: the surprise is the same.</summary>
    [TestCase(NpcBehavior.Pursue)]
    [TestCase(NpcBehavior.Flee)]
    [TestCase(NpcBehavior.Wander)]
    public void AReachPastTheViewport_IsCalledOut(NpcBehavior behavior)
    {
        var row = Row(behavior, Constants.NpcRangeSoftCap + 1);

        Assert.Multiple(() =>
        {
            Assert.That(row.HasRangeWarning, Is.True);
            Assert.That(row.RangeWarning, Does.Contain((Constants.NpcRangeSoftCap + 1).ToString()));
        });
    }

    /// <summary>Only the two that notice. A reach this short leaves one unable to see somebody standing
    /// beside it, which is an NPC that never does the one thing its behavior names.</summary>
    [TestCase(NpcBehavior.Pursue)]
    [TestCase(NpcBehavior.Flee)]
    public void ANoticerThatNoticesNothing_IsCalledOut(NpcBehavior behavior)
    {
        Assert.That(Row(behavior, 1).HasRangeWarning, Is.True);
    }

    /// <summary>Nothing else reads Range at all, so a short one says nothing about them.</summary>
    [TestCase(NpcBehavior.Wander)]
    [TestCase(NpcBehavior.Scavenge)]
    [TestCase(NpcBehavior.Stationary)]
    public void AShortReachOnAnythingElse_SaysNothing(NpcBehavior behavior)
    {
        Assert.That(Row(behavior, 0).HasRangeWarning, Is.False);
    }

    /// <summary>The warning follows the fields it is about, so an author sees it as they type.</summary>
    [Test]
    public void TheWarning_TracksBothFields()
    {
        var row = Row(NpcBehavior.Pursue, 4);
        Assume.That(row.HasRangeWarning, Is.False);

        row.Range = Constants.NpcRangeSoftCap + 5;
        Assert.That(row.HasRangeWarning, Is.True, "raising the reach past the cap");

        row.Range = 1;
        Assert.That(row.HasRangeWarning, Is.True, "dropping it below the floor");

        row.Behavior = NpcBehavior.Wander;
        Assert.That(row.HasRangeWarning, Is.False, "the floor only concerns a behavior that notices");
    }
}

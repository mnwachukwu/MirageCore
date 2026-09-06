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

    [TestCase(NpcBehavior.AttackOnSight, 2)]
    [TestCase(NpcBehavior.AttackOnSight, 6)]
    [TestCase(NpcBehavior.AttackWhenAttacked, 1)]
    [TestCase(NpcBehavior.Friendly, 0)]
    public void AnOrdinaryReach_SaysNothing(NpcBehavior behavior, int range)
    {
        Assert.That(Row(behavior, range).HasRangeWarning, Is.False);
    }

    /// <summary>Past what a player can see, whatever the behavior: the surprise is the same.</summary>
    [TestCase(NpcBehavior.AttackOnSight)]
    [TestCase(NpcBehavior.AttackWhenAttacked)]
    [TestCase(NpcBehavior.Guard)]
    public void AReachPastTheViewport_IsCalledOut(NpcBehavior behavior)
    {
        var row = Row(behavior, Constants.NpcRangeSoftCap + 1);

        Assert.Multiple(() =>
        {
            Assert.That(row.HasRangeWarning, Is.True);
            Assert.That(row.RangeWarning, Does.Contain((Constants.NpcRangeSoftCap + 1).ToString()));
        });
    }

    /// <summary>Attack-on-sight only. A Guard never reads its Range, and everything else waits to be
    /// struck, so a short reach says nothing about either.</summary>
    [Test]
    public void AnAttackOnSightMobThatNoticesNothing_IsCalledOut()
    {
        Assert.That(Row(NpcBehavior.AttackOnSight, 1).HasRangeWarning, Is.True);
    }

    [TestCase(NpcBehavior.Guard)]
    [TestCase(NpcBehavior.AttackWhenAttacked)]
    [TestCase(NpcBehavior.Stationary)]
    public void AShortReachOnAnythingElse_SaysNothing(NpcBehavior behavior)
    {
        Assert.That(Row(behavior, 0).HasRangeWarning, Is.False);
    }

    /// <summary>The warning follows the fields it is about, so an author sees it as they type.</summary>
    [Test]
    public void TheWarning_TracksBothFields()
    {
        var row = Row(NpcBehavior.AttackOnSight, 4);
        Assume.That(row.HasRangeWarning, Is.False);

        row.Range = Constants.NpcRangeSoftCap + 5;
        Assert.That(row.HasRangeWarning, Is.True, "raising the reach past the cap");

        row.Range = 1;
        Assert.That(row.HasRangeWarning, Is.True, "dropping it below the floor");

        row.Behavior = NpcBehavior.Friendly;
        Assert.That(row.HasRangeWarning, Is.False, "the floor is an attack-on-sight matter");
    }
}

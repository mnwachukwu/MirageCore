using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Progression;

/// <summary>The objective kernel's tracker + mob-kill hook (#5): predicate-gated crediting, one KO =
/// one advance, per-objective credit on a shared kill, the one-shot completion callback + auto-untrack,
/// and early Stop.</summary>
[TestFixture]
public class ObjectiveSystemTests
{
    private static Objective KillN(int target, int count) =>
        new() { Kind = ObjectiveKind.Kill, Target = target, Count = count };

    [Test]
    public void Kill_MatchingTarget_AdvancesWhenAContributorCounts()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(target: 7, count: 3);
        sys.Track(obj, contributor => contributor == 1, () => { });   // only player 1 counts

        sys.RecordNpcKill(npcNum: 7, contributors: new[] { 1, 2 });
        Assert.That(obj.Progress, Is.EqualTo(1));
    }

    [Test]
    public void Kill_MultipleCountingContributors_AdvancesByExactlyOne()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(7, 5);
        sys.Track(obj, _ => true, () => { });   // every contributor counts

        sys.RecordNpcKill(7, new[] { 1, 2, 3 });
        Assert.That(obj.Progress, Is.EqualTo(1));   // one KO = +1, not +3
    }

    [Test]
    public void Kill_WrongNpc_DoesNotAdvance()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(7, 3);
        sys.Track(obj, _ => true, () => { });

        sys.RecordNpcKill(npcNum: 8, contributors: new[] { 1 });
        Assert.That(obj.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Kill_NoCountingContributor_DoesNotAdvance()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(7, 3);
        sys.Track(obj, contributor => contributor == 99, () => { });

        sys.RecordNpcKill(7, new[] { 1, 2, 3 });
        Assert.That(obj.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Kill_SharedByTwoObjectives_CreditsEachIndependently()
    {
        var sys = new ObjectiveSystem();
        var guildA = KillN(7, 5);
        var guildB = KillN(7, 5);
        sys.Track(guildA, c => c == 1, () => { });   // player 1 is in guild A
        sys.Track(guildB, c => c == 2, () => { });   // player 2 is in guild B

        sys.RecordNpcKill(7, new[] { 1, 2 });        // both guilds contributed to one kill
        Assert.That(guildA.Progress, Is.EqualTo(1));
        Assert.That(guildB.Progress, Is.EqualTo(1));
    }

    [Test]
    public void Completion_FiresCallbackOnce_AndAutoUntracks()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(target: 7, count: 2);
        int completed = 0;
        sys.Track(obj, _ => true, () => completed++);

        sys.RecordNpcKill(7, new[] { 1 });   // 1/2
        Assert.That(completed, Is.EqualTo(0));
        Assert.That(sys.ActiveCount, Is.EqualTo(1));

        sys.RecordNpcKill(7, new[] { 1 });   // 2/2 → completes, callback fires, untracks
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(sys.ActiveCount, Is.EqualTo(0));

        sys.RecordNpcKill(7, new[] { 1 });   // nothing tracked → no re-fire
        Assert.That(completed, Is.EqualTo(1));
    }

    [Test]
    public void Stop_BeforeCompletion_HaltsTracking()
    {
        var sys = new ObjectiveSystem();
        var obj = KillN(7, 3);
        var handle = sys.Track(obj, _ => true, () => { });

        sys.RecordNpcKill(7, new[] { 1 });   // 1/3
        handle.Stop();
        sys.RecordNpcKill(7, new[] { 1 });   // stopped → ignored + swept

        Assert.That(obj.Progress, Is.EqualTo(1));
        Assert.That(sys.ActiveCount, Is.EqualTo(0));
    }
}

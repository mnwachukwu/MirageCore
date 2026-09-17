using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// Who lets go of a lock it cannot make progress on, and when.
///
/// <para>One clock answers it: an NPC stamps <see cref="MapNpcRecord.LastReachedTargetMs"/> when it
/// notices somebody and again on every step that closed the gap, so the stamp only goes stale on one
/// that genuinely cannot act. Nothing else stops a pursuer being parked somewhere it does not
/// belong — nothing in the chase code refuses to cross a border —
/// so it is pinned on its own.</para>
///
/// <para>Only <see cref="NpcBehavior.Pursue"/> consults it. A fleeing NPC lets go on distance rather
/// than on time, and the rest never hold a lock at all.</para>
///
/// <para>The method is private and the assembly has no InternalsVisibleTo, so it is invoked by
/// reflection on a minimally-wired system — it reads only the template's behavior and the record's
/// stamp, so the other constructor dependencies can be null.</para>
/// </summary>
[TestFixture]
public class NpcGiveUpTests
{
    const long Window = 10_000;   // NpcAiSystem.NpcUnreachedGiveUpMs

    [Test]
    public void APursuer_LetsGoOnceItHasNotReachedForTheWholeWindow()
    {
        Assert.That(GivesUp(NpcBehavior.Pursue, stampedAt: 1_000, now: 1_000 + Window + 1), Is.True);
    }

    [Test]
    public void APursuer_HoldsOnWhileTheWindowIsStillOpen()
    {
        Assert.That(GivesUp(NpcBehavior.Pursue, stampedAt: 1_000, now: 1_000 + Window), Is.False,
            "the window is inclusive — one that reached its target exactly a window ago has not failed yet");
    }

    /// <summary>An unstamped record has never noticed anybody, so an arbitrarily large clock reading must
    /// not make it look like a lock that has gone stale. Zero means "no clock running", not "running
    /// since the epoch".</summary>
    [Test]
    public void AnUnstampedRecord_NeverReadsAsGivenUp()
    {
        Assert.That(GivesUp(NpcBehavior.Pursue, stampedAt: 0, now: long.MaxValue / 2), Is.False);
    }

    [TestCase(NpcBehavior.Stationary)]
    [TestCase(NpcBehavior.Wander)]
    [TestCase(NpcBehavior.Flee)]
    [TestCase(NpcBehavior.Scavenge)]
    public void NothingElse_ConsultsTheClock(NpcBehavior behavior)
    {
        Assert.That(GivesUp(behavior, stampedAt: 1_000, now: 1_000 + Window * 100), Is.False,
            behavior + " answered the pursuit give-up gate, which it never reaches");
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    static bool GivesUp(NpcBehavior behavior, long stampedAt, long now)
    {
        var world = new GameWorld();
        world.Npcs[1].Behavior = behavior;
        var mn = new MapNpcRecord { Num = 1, LastReachedTargetMs = stampedAt };
        var ai = new NpcAiSystem(world, new PlayerManager(), null!, null!, null!, null!);
        return (bool)typeof(NpcAiSystem)
            .GetMethod("ShouldGiveUpUnreachedTarget", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ai, [mn, now])!;
    }
}

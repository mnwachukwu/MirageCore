using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// Who keeps a fleeing target, and who lets it go.
///
/// <para>The combat timer is refreshed by FIGHTING. One behavior also refreshes it by merely
/// CHASING, and that one never lets go: while the clock keeps being pushed forward it can never
/// lapse, so the disengage path further down is unreachable for it. That makes
/// <c>IsRelentlessPursuit</c> the real retention rule — the target drop on expiry is only its
/// consequence — and it is worth pinning on its own, because reading the drop code alone tells you
/// the opposite of what happens.</para>
///
/// <para>A guard is the only relentless pursuer, and only against a criminal. Everything else,
/// hostile mobs included, gives up on the same clock: break contact for the combat window and the
/// chase is over, whatever attacked you.</para>
///
/// <para>The method is private and the assembly has no InternalsVisibleTo, so it is invoked by
/// reflection on a minimally-wired system — it reads only the template's behavior and the target's
/// player record, so the other constructor dependencies can be null.</para>
/// </summary>
[TestFixture]
public class NpcPursuitRetentionTests
{
    const int Map = 1;
    const int Target = 1;      // player index being chased

    [TestCase(NpcBehavior.AttackOnSight)]
    [TestCase(NpcBehavior.AttackWhenAttacked)]
    [TestCase(NpcBehavior.Friendly)]
    [TestCase(NpcBehavior.Stationary)]
    public void OnlyAGuardHoundsAnInnocent(NpcBehavior behavior)
    {
        var (world, pm) = Setup(behavior);

        Assert.That(IsRelentless(world, pm, behavior), Is.False,
            $"{behavior} refreshed combat by chasing, so its target could never break away");
    }

    /// <summary>The point of the whole change: a hostile mob is no more tenacious than one that only
    /// ever hit back. Outrun either for the combat window and both go home.</summary>
    [Test]
    public void AttackOnSight_LetsGoOnTheSameTermsAsAttackWhenAttacked()
    {
        var (world, pm) = Setup(NpcBehavior.AttackOnSight);
        bool aos = IsRelentless(world, pm, NpcBehavior.AttackOnSight);
        bool awa = IsRelentless(world, pm, NpcBehavior.AttackWhenAttacked);

        Assert.That(aos, Is.EqualTo(awa));
    }

    [Test]
    public void AGuardYieldsToAnInnocent()
    {
        var (world, pm) = Setup(NpcBehavior.Guard);

        Assert.That(IsRelentless(world, pm, NpcBehavior.Guard), Is.False,
            "a guard chasing someone who is neither a PK nor a PvP aggressor should return to its post");
    }

    [Test]
    public void AGuardHoundsAPk()
    {
        var (world, pm) = Setup(NpcBehavior.Guard);
        pm[Target].Char.PkExpiryUtc = long.MaxValue;
        pm[Target].PkGraceUntilUtc = 0;            // grace already elapsed, so the flag counts

        Assert.That(IsRelentless(world, pm, NpcBehavior.Guard), Is.True);
    }

    /// <summary>A PK still inside their grace window is not yet fair game — the flag alone is not the
    /// test, which is why the grace stamp is read beside it.</summary>
    [Test]
    public void AGuardYieldsToAPkStillInGrace()
    {
        var (world, pm) = Setup(NpcBehavior.Guard);
        pm[Target].Char.PkExpiryUtc = long.MaxValue;
        pm[Target].PkGraceUntilUtc = long.MaxValue;

        Assert.That(IsRelentless(world, pm, NpcBehavior.Guard), Is.False);
    }

    [Test]
    public void AGuardHoundsAnActivePvpAggressor()
    {
        var (world, pm) = Setup(NpcBehavior.Guard);
        pm[Target].PvpAttackerUntil = long.MaxValue;

        Assert.That(IsRelentless(world, pm, NpcBehavior.Guard), Is.True);
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    static (GameWorld World, PlayerManager Pm) Setup(NpcBehavior behavior)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = behavior;
        var sp = pm[Target];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        return (world, pm);
    }

    static bool IsRelentless(GameWorld world, PlayerManager pm, NpcBehavior behavior)
    {
        var npc = new NpcRecord { Behavior = behavior };
        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        return (bool)typeof(NpcAiSystem)
            .GetMethod("IsRelentlessPursuit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ai, [npc, Target, 0L])!;
    }
}

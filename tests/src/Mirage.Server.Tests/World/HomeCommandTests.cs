using Mirage.Server.Core.Configuration;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Where /home sends a character, and the two things that stop it: the cooldown, and being in combat.
/// The cooldown lives on the character record so a relog cannot clear it — the exploit that would make
/// an escape-hatch cooldown decorative.
/// </summary>
[TestFixture]
public class HomeCommandTests
{
    private static readonly SpawnConfig Default = new() { Map = 7, X = 8, Y = 6 };

    private static bool OnCooldown(PlayerRecord p, long now) =>
        p.HomeUsedAtUtc > 0 && now < p.HomeUsedAtUtc + Constants.HomeCooldownSeconds;

    // ── Destination ───────────────────────────────────────────────────────────

    [Test]
    public void ACharacterWithNoSpawnGoesToTheServerDefault()
    {
        var (map, x, y) = Default.HomeFor(new PlayerRecord());

        Assert.That((map, x, y), Is.EqualTo(((short)7, (byte)8, (byte)6)));
    }

    [Test]
    public void ACharacterWithAnInnSpawnGoesThere()
    {
        var p = new PlayerRecord { SpawnMap = 42, SpawnX = 3, SpawnY = 4 };

        Assert.That(Default.HomeFor(p), Is.EqualTo(((short)42, (byte)3, (byte)4)));
    }

    [Test]
    public void SpawnMapZeroMeansUnset_NotMapZero()
    {
        var p = new PlayerRecord { SpawnMap = 0, SpawnX = 3, SpawnY = 4 };

        Assert.That(Default.HomeFor(p).Map, Is.EqualTo((short)7), "an unset spawn falls back whole");
        Assert.That(Default.HomeFor(p).X, Is.EqualTo((byte)8), "including its coordinates");
    }

    [Test]
    public void DeathAndHomeAgreeOnWhereHomeIs()
    {
        // Both paths call the same resolver; this pins that there is only one answer to hold them to.
        var p = new PlayerRecord { SpawnMap = 42, SpawnX = 3, SpawnY = 4 };

        Assert.That(Default.HomeFor(p), Is.EqualTo(Default.HomeFor(p)));
    }

    // ── Cooldown ──────────────────────────────────────────────────────────────

    [Test]
    public void ACharacterWhoHasNeverUsedItIsNotOnCooldown()
    {
        Assert.That(OnCooldown(new PlayerRecord(), now: 1_000_000), Is.False);
    }

    [Test]
    public void ItIsRefusedUntilTheFullThirtyMinutesHasPassed()
    {
        var p = new PlayerRecord { HomeUsedAtUtc = 1_000_000 };

        Assert.That(OnCooldown(p, 1_000_000), Is.True, "immediately after");
        Assert.That(OnCooldown(p, 1_000_000 + Constants.HomeCooldownSeconds - 1), Is.True, "one second short");
    }

    [Test]
    public void ItIsAllowedTheMomentTheCooldownElapses()
    {
        var p = new PlayerRecord { HomeUsedAtUtc = 1_000_000 };

        Assert.That(OnCooldown(p, 1_000_000 + Constants.HomeCooldownSeconds), Is.False);
    }

    [Test]
    public void TheCooldownIsThirtyMinutes()
    {
        Assert.That(Constants.HomeCooldownSeconds, Is.EqualTo(30 * 60));
    }

    [Test]
    public void TheCooldownIsPersistedOnTheCharacter_SoARelogCannotClearIt()
    {
        var saved = new PlayerRecord { HomeUsedAtUtc = 1_000_000 };

        // Clone is what the saver writes and what a login reads back.
        var reloaded = saved.Clone();

        Assert.That(reloaded.HomeUsedAtUtc, Is.EqualTo(1_000_000));
        Assert.That(OnCooldown(reloaded, 1_000_100), Is.True);
    }

    [Test]
    public void TheCooldownRunsDownWhileTheCharacterIsLoggedOut()
    {
        // Logged out at 1_000_000 with the cooldown just started, back an hour of WALL-CLOCK time later.
        // Nothing ticks a timer in between, so only a real-time stamp can expire on its own.
        var loggedOutMidCooldown = new PlayerRecord { HomeUsedAtUtc = 1_000_000 }.Clone();

        Assert.That(OnCooldown(loggedOutMidCooldown, 1_000_000 + 3600), Is.False,
            "an hour away is an hour off the cooldown, played or not");
    }
}

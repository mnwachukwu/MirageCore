using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// The machine-ban store: salting, matching, and the two rules that make the difference between a
/// working last-resort ban and one that quietly does the wrong thing.
///
/// <list type="bullet">
/// <item>An EMPTY key is never a value. A client that could not identify its machine must not collide
/// with every other client that also could not — treating blank as an identity would ban all of them the
/// first time any one was banned.</item>
/// <item>The salt is PERSISTED. It is generated on first use and written into the same file as the bans;
/// if a restart minted a new one, every ban already recorded would stop matching, silently.</item>
/// </list>
/// </summary>
[TestFixture]
public class HardwareBanTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    private JsonPersistenceService NewService() =>
        new(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-hwban-" + Guid.NewGuid().ToString("N"));
        _svc = NewService();
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    // ── Hashing ───────────────────────────────────────────────────────────────

    [Test]
    public async Task EmptyKey_HashesToEmpty_AndNeverMatches()
    {
        Assert.That(await _svc.HashMachineKeyAsync(""), Is.Empty);
        // Even with a ban on file, a keyless client is not a match for it.
        await _svc.HardwareBanAsync(await _svc.HashMachineKeyAsync("abc"), "Nuisance", "spam");
        Assert.That(await _svc.FindHardwareBanAsync(""), Is.Null);
    }

    [Test]
    public async Task Hashing_IsStable_AndDoesNotEchoTheClientKey()
    {
        string first = await _svc.HashMachineKeyAsync("client-key");
        string again = await _svc.HashMachineKeyAsync("client-key");

        Assert.That(first, Is.EqualTo(again));
        // The stored value must not be the value the client sent: the salt is what stops one server's
        // list identifying a player at another.
        Assert.That(first, Is.Not.EqualTo("client-key"));
        Assert.That(first, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task DifferentServers_ProduceDifferentHashes_ForTheSameClientKey()
    {
        string here = await _svc.HashMachineKeyAsync("client-key");

        string otherDir = Path.Combine(Path.GetTempPath(), "mirage-hwban-" + Guid.NewGuid().ToString("N"));
        try
        {
            var other = new JsonPersistenceService(otherDir, otherDir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
            Assert.That(await other.HashMachineKeyAsync("client-key"), Is.Not.EqualTo(here));
        }
        finally
        {
            try { if (Directory.Exists(otherDir)) Directory.Delete(otherDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // The one that turns every recorded ban into a no-op if it regresses: the salt has to survive a
    // restart, or a reloaded server can no longer reproduce the hashes it wrote.
    [Test]
    public async Task Salt_SurvivesAReload_SoExistingBansStillMatch()
    {
        string hashed = await _svc.HashMachineKeyAsync("client-key");
        await _svc.HardwareBanAsync(hashed, "Nuisance", "spam");

        var reloaded = NewService();
        string afterReload = await reloaded.HashMachineKeyAsync("client-key");

        Assert.That(afterReload, Is.EqualTo(hashed));
        Assert.That(await reloaded.FindHardwareBanAsync(afterReload), Is.Not.Null);
    }

    // ── Applying and lifting ──────────────────────────────────────────────────

    [Test]
    public async Task Ban_ThenFind_ReturnsTheEntry()
    {
        string hashed = await _svc.HashMachineKeyAsync("client-key");
        Assert.That(await _svc.HardwareBanAsync(hashed, "Nuisance", "harassment"), Is.True);

        var found = await _svc.FindHardwareBanAsync(hashed);
        Assert.That(found, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(found!.Login, Is.EqualTo("Nuisance"));
            Assert.That(found.Reason, Is.EqualTo("harassment"));
            Assert.That(found.BannedAtUtc, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Ban_IsIdempotent_AndReportsTheSecondAttemptAsNoChange()
    {
        string hashed = await _svc.HashMachineKeyAsync("client-key");
        await _svc.HardwareBanAsync(hashed, "Nuisance", "first");

        Assert.That(await _svc.HardwareBanAsync(hashed, "Nuisance", "second"), Is.False);
        Assert.That(await _svc.LoadHardwareBanListAsync(), Has.Count.EqualTo(1));
        // The original reason stands — a repeat ban must not quietly rewrite the record of why.
        Assert.That((await _svc.FindHardwareBanAsync(hashed))!.Reason, Is.EqualTo("first"));
    }

    [Test]
    public async Task Unban_ByLogin_ClearsEveryMachineUnderIt()
    {
        await _svc.HardwareBanAsync(await _svc.HashMachineKeyAsync("desktop"), "Nuisance", "spam");
        await _svc.HardwareBanAsync(await _svc.HashMachineKeyAsync("laptop"), "Nuisance", "spam");
        await _svc.HardwareBanAsync(await _svc.HashMachineKeyAsync("stranger"), "Someone", "spam");

        Assert.That(await _svc.HardwareUnbanAsync("nuisance"), Is.EqualTo(2), "the lift is case-insensitive");
        var left = await _svc.LoadHardwareBanListAsync();
        Assert.That(left, Has.Count.EqualTo(1));
        Assert.That(left[0].Login, Is.EqualTo("Someone"));
    }

    [Test]
    public async Task Unban_WithNothingToLift_ReportsZero()
    {
        Assert.That(await _svc.HardwareUnbanAsync("Nobody"), Is.Zero);
    }

    [Test]
    public async Task LoadList_OnAServerThatHasNeverBanned_IsEmptyRatherThanAFailure()
    {
        Assert.That(await _svc.LoadHardwareBanListAsync(), Is.Empty);
        Assert.That(await _svc.FindHardwareBanAsync("anything"), Is.Null);
    }

    // ── The enforcement mode ──────────────────────────────────────────────────

    /// <summary>A stock server REFUSES a machine-ban match. Pinned in three places at once because the
    /// default is the whole behaviour of the feature and it can be changed by accident from any of them:
    /// reordering the enum, editing the record, or shipping a config without the node.</summary>
    [Test]
    public void BlockIsTheDefault_HoweverAServerArrivesAtIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Mirage.Server.Core.Configuration.ServerConfig.Default.HardwareBans.Mode,
                Is.EqualTo(Mirage.Server.Core.Configuration.HardwareBanMode.Block));
            Assert.That(new Mirage.Server.Core.Configuration.HardwareBanConfig().Mode,
                Is.EqualTo(Mirage.Server.Core.Configuration.HardwareBanMode.Block));
            // Zero value too, so a config that names no mode at all cannot land on the permissive one.
            Assert.That(default(Mirage.Server.Core.Configuration.HardwareBanMode),
                Is.EqualTo(Mirage.Server.Core.Configuration.HardwareBanMode.Block));
        });
    }

    [Test]
    public void AConfigFileWithNoHardwareBansSection_StillBlocks()
    {
        string path = Path.Combine(_dir, "serverconfig.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "port": 4000, "language": "en" }""");

        var (config, error) = Mirage.Server.Core.Configuration.ServerConfigStore.Load(path);

        Assert.That(error, Is.Null);
        Assert.That(config.HardwareBans.Mode,
            Is.EqualTo(Mirage.Server.Core.Configuration.HardwareBanMode.Block));
    }

    // ── The client's half ─────────────────────────────────────────────────────

    /// <summary>The key is read from whatever identifier this OS exposes, so the test asserts the SHAPE
    /// rather than a value — CI runs this on all three platforms, and a container can legitimately have
    /// no machine id at all.</summary>
    [Test]
    public void ClientKey_IsEitherAbsentOrAWellFormedHash()
    {
        string key = MachineKey.Compute();
        if (key.Length == 0) Assert.Pass("this machine exposes no identifier; an empty key is the correct answer");

        Assert.That(key, Has.Length.EqualTo(64));
        Assert.That(key, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(MachineKey.Compute(), Is.EqualTo(key), "a machine's key cannot change while it runs");
    }
}

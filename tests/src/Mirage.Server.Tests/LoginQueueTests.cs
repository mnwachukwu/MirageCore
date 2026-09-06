using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Players;
using Mirage.Server.Host.Net;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>
/// The parts of the full-server path that can be checked without sockets: how many slots the public may
/// take, and who a connection says it is before it has a session.
/// </summary>
[TestFixture]
public sealed class LoginQueueTests
{
    // ── Reserved slots ────────────────────────────────────────────────────────

    private static PlayerManager Manager(int slots) =>
        new(ServerConfig.Default with { MaxPlayers = slots });

    private static void Occupy(PlayerManager pm, int count)
    {
        for (int i = 1; i <= count; i++) pm[i].IsConnected = true;
    }

    [Test]
    public void ThePublicCannotTakeTheReservedSlots()
    {
        var pm = Manager(5);
        Occupy(pm, 3);   // 2 free, 2 reserved

        Assert.That(pm.FindOpenSlot(keepFree: 2), Is.Zero);
    }

    [Test]
    public void StaffCanTakeThemBecauseTheyHoldNoneBack()
    {
        var pm = Manager(5);
        Occupy(pm, 3);

        Assert.That(pm.FindOpenSlot(keepFree: 0), Is.EqualTo(4));
    }

    [Test]
    public void ThePublicGetsTheLowestSlotWhileMoreThanTheReserveIsFree()
    {
        var pm = Manager(5);
        Occupy(pm, 2);   // 3 free, 2 reserved — one to spare

        Assert.That(pm.FindOpenSlot(keepFree: 2), Is.EqualTo(3));
    }

    [Test]
    public void ReservingNothingIsTheOldBehaviourExactly()
    {
        var pm = Manager(3);
        Occupy(pm, 3);

        Assert.That(pm.FindOpenSlot(), Is.Zero);
        Assert.That(pm.FindOpenSlot(keepFree: 0), Is.Zero);
    }

    [Test]
    public void ACombatGhostIsNotAFreeSlot()
    {
        // A ghost has no connection but is still fighting on that slot; handing it out would evict them.
        var pm = Manager(2);
        pm[1].IsGhost = true;

        Assert.That(pm.FindOpenSlot(keepFree: 1), Is.Zero);
        Assert.That(pm.FindOpenSlot(keepFree: 0), Is.EqualTo(2));
    }

    // ── Clamping ──────────────────────────────────────────────────────────────

    [Test]
    public void ReservingEverySlotWouldLockThePublicOutSoItIsClamped()
    {
        var config = ServerConfig.Default with { MaxPlayers = 4, ReservedSlots = 9 };

        Assert.That(config.EffectiveReservedSlots, Is.EqualTo(3));
    }

    [Test]
    public void TheClampDoesNotDependOnWhichOrderTheFileListsThem()
    {
        // JSON promises no property order, so the clamp cannot live in the setter. Both ways round must
        // land on the same answer.
        var reservedFirst = ServerConfig.Default with { ReservedSlots = 9, MaxPlayers = 4 };
        var limitFirst = ServerConfig.Default with { MaxPlayers = 4, ReservedSlots = 9 };

        Assert.That(reservedFirst.EffectiveReservedSlots, Is.EqualTo(limitFirst.EffectiveReservedSlots));
    }

    [Test]
    public void AQueueDepthOfZeroTurnsQueueingOff()
    {
        Assert.That(new QueueConfig { MaxDepth = 0 }.IsEnabled, Is.False);
        Assert.That(new QueueConfig().IsEnabled, Is.True);
    }

    // ── Identity ──────────────────────────────────────────────────────────────
    // The client only connects once Login is pressed, so the packet a connection opens with already says
    // who it is and what language to answer in. That is the only identity the queue ever gets.

    [Test]
    public void ReadsTheAccountAndLocaleOffALogin()
    {
        string line = PacketSerializer.Serialize(new LoginPacket
        {
            Username = "  Bramble  ",
            Password = "hunter2",
            Locale = "fr",
        }).TrimEnd('\n');

        var who = LoginQueue.Identity.From(line);

        Assert.That(who.Account, Is.EqualTo("Bramble"));
        Assert.That(who.Secret, Is.EqualTo("hunter2"));
        Assert.That(who.HasAccount, Is.True);
        Assert.That(who.Locale, Is.EqualTo("fr"));
    }

    [Test]
    public void FallsBackToEnglishForALanguageNobodyShipped()
    {
        // A client is free to ask for anything. Answering in a locale with no table would mean answering
        // in key names.
        string line = PacketSerializer.Serialize(new LoginPacket { Username = "x", Locale = "zz" }).TrimEnd('\n');

        Assert.That(LoginQueue.Identity.From(line).LocaleOrDefault, Is.EqualTo("en"));
    }

    [Test]
    public void ARegistrationHasALocaleButNoAccountYet()
    {
        string line = PacketSerializer.Serialize(
            new NewAccountPacket { Username = "Bramble", Password = "hunter2", Locale = "pt" }).TrimEnd('\n');

        var who = LoginQueue.Identity.From(line);

        Assert.That(who.HasAccount, Is.False, "the account does not exist until the server makes it");
        Assert.That(who.Locale, Is.EqualTo("pt"));
    }

    [Test]
    public void AnythingElseIsAnAnonymousConnection()
    {
        // A tool, or a client that opened with something unexpected. It waits as an ordinary player and
        // gets no reconnect grace, because there is nothing to key one on.
        foreach (string line in new[]
        {
            PacketSerializer.Serialize(new GetClassesPacket()).TrimEnd('\n'),
            "{\"cmd\":\"nonsense\"}",
            "not json at all",
        })
        {
            var who = LoginQueue.Identity.From(line);
            Assert.That(who.HasAccount, Is.False, line);
            Assert.That(who.Secret, Is.Empty, line);
        }
    }
}

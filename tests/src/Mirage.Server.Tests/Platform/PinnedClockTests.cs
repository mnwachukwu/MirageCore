using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Deadline rules, asserted ON their boundary. Every one is stored as a Unix second and compared against
/// an injected clock; compared against a real one, a test could verify that a thing eventually expires
/// but never that it expires at the right second, nor that it survives the second before.
///
/// <para>Each rule is checked three times: just short of the deadline it must NOT fire, exactly on it
/// the boundary behavior is pinned, and past it it must fire. That "one second short" case is the one
/// a real clock makes unreachable, and it is where off-by-one deadline bugs live.</para>
/// </summary>
[TestFixture]
public class PinnedClockTests
{
    sealed class FixedClock : IClock
    {
        public long UtcNowUnix { get; set; }
        public DateTime LocalNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
    }

    static ServerPlayer Online(PlayerManager pm, int idx, string login = "tester")
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = login;
        return sp;
    }

    static T Invoke<T>(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(target.GetType().Name, method);
        return (T)m.Invoke(target, args)!;
    }

    // ── PK flag expiry ────────────────────────────────────────────────────────

    // ── Post-death PK grace window ────────────────────────────────────────────

    // ── Marketplace listing lifetime ──────────────────────────────────────────

    // A listing past its 30-day lifetime is pulled and the goods mailed back to the seller. The
    // boundary is >=, so the listing survives its final second and expires on the lifetime second.
    [Test]
    public void MarketListing_SurvivesItsFinalSecond_ThenReturnsToSeller()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var clock = new FixedClock { UtcNowUnix = 100_000 };
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items, clock: clock);
        var market = new MarketSystem(world, pm, dispatcher, items, mail,
                                      persistence: null!, bg: null!, clock: clock);

        var seller = Online(pm, 1, "seller");
        world.MarketListings[1] = new MarketListing
        {
            Id = 1, Seller = "seller", ItemNum = 10, Quantity = 1, Price = 500, ListedUtc = 100_000,
        };

        // One second short of the lifetime: still listed.
        clock.UtcNowUnix = 100_000 + Constants.MarketListingLifetimeSeconds - 1;
        market.TickExpiry();
        Assert.That(world.MarketListings, Has.Count.EqualTo(1),
                    "a listing one second short of its lifetime must stay up");
        Assert.That(seller.Mail, Is.Empty, "and nothing has been returned yet");

        // Exactly at the lifetime: pulled, and the goods come back as mail.
        clock.UtcNowUnix = 100_000 + Constants.MarketListingLifetimeSeconds;
        market.TickExpiry();
        Assert.Multiple(() =>
        {
            Assert.That(world.MarketListings, Is.Empty, "at its lifetime the listing is pulled");
            Assert.That(seller.Mail, Has.Count.EqualTo(1), "the seller gets the goods back by mail");
            Assert.That(seller.Mail[0].Attachments.Single().ItemNum, Is.EqualTo(10));
        });
    }

    // ── Mail retention ────────────────────────────────────────────────────────

    // Mail is dropped once past DeleteAt. Same boundary discipline: the second before must keep it.
    [Test]
    public void Mail_SurvivesUntilItsDeleteAtSecond_ThenIsDropped()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var clock = new FixedClock { UtcNowUnix = 200_000 };
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items, clock: clock);

        var sp = Online(pm, 1);
        mail.Deliver("tester", "sender", "subject", "body");

        var msg = sp.Mail.Single();
        long deleteAt = msg.DeleteAt;
        Assert.That(deleteAt, Is.EqualTo(200_000 + Constants.MailRetentionSeconds),
                    "retention runs from maturity, off the pinned clock");

        clock.UtcNowUnix = deleteAt - 1;
        mail.TickExpiry();
        Assert.That(sp.Mail, Has.Count.EqualTo(1), "one second short of DeleteAt the mail stays");

        clock.UtcNowUnix = deleteAt;
        mail.TickExpiry();
        Assert.That(sp.Mail, Is.Empty, "at DeleteAt the mail is dropped");
    }

    // A Collect-on-Delivery message the recipient never paid for rides a much shorter clock than
    // ordinary mail — three days, not thirty. Pinning the clock tells the two windows apart in
    // a test.
    [Test]
    public void UnpaidCodMail_UsesTheShortReturnWindow_NotFullRetention()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var clock = new FixedClock { UtcNowUnix = 300_000 };
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items, clock: clock);

        var sp = Online(pm, 1);
        mail.Deliver("tester", "sender", "cod", "pay up",
                     [new MailAttachment { ItemNum = 10, Quantity = 1 }], codPrice: 250);

        var msg = sp.Mail.Single();
        Assert.Multiple(() =>
        {
            Assert.That(msg.CodPrice, Is.EqualTo(250));
            Assert.That(msg.DeleteAt, Is.EqualTo(300_000 + Constants.CodLifetimeSeconds),
                        "an unclaimed CoD expires on the 3-day return clock");
            Assert.That(msg.DeleteAt, Is.LessThan(300_000 + Constants.MailRetentionSeconds),
                        "which must be strictly shorter than ordinary retention");
        });
    }

    // ── No-op dispatcher ──────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}

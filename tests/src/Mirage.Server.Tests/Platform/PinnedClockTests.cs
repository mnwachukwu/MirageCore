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

    // A player's PK flag clears once PkExpiryUtc passes. The sweep is rate-gated to once a minute, so
    // the test advances the clock past both the gate and the deadline.
    [Test]
    public void PkFlag_SurvivesUntilItsExpirySecond_ThenClears()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock { UtcNowUnix = 1_000_000 };
        var pk = new PkExpirySystem(pm, new NoOpDispatcher(), world, clock: clock);

        var sp = Online(pm, 1);
        sp.Char.Map = 1;
        sp.Char.PkExpiryUtc = 1_000_500;

        // One second short of expiry (and past the 60s rate gate): the flag must hold.
        clock.UtcNowUnix = 1_000_499;
        pk.Tick();
        Assert.That(sp.Char.PkExpiryUtc, Is.EqualTo(1_000_500),
                    "a PK flag one second short of expiry must not be cleared");
        Assert.That(sp.Char.IsPk(clock.UtcNowUnix), Is.True);

        // At the expiry second the flag is no longer "in the future", so the sweep clears it.
        clock.UtcNowUnix = 1_000_560;   // also clears the once-a-minute gate
        pk.Tick();
        Assert.That(sp.Char.PkExpiryUtc, Is.Zero, "at/after PkExpiryUtc the flag must clear");
        Assert.That(sp.Char.IsPk(clock.UtcNowUnix), Is.False);
    }

    // The sweep is a full roster scan, so it self-limits to once a minute. Pinning the clock is the
    // only way to observe that gate — with a real clock the second call always lands inside it.
    [Test]
    public void PkSweep_IsRateGatedToOncePerMinute()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock { UtcNowUnix = 5_000 };
        var pk = new PkExpirySystem(pm, new NoOpDispatcher(), world, clock: clock);

        var sp = Online(pm, 1);
        sp.Char.Map = 1;

        pk.Tick();                       // first call takes the gate

        sp.Char.PkExpiryUtc = 4_000;     // already expired
        clock.UtcNowUnix = 5_059;        // 59s later — still inside the gate
        pk.Tick();
        Assert.That(sp.Char.PkExpiryUtc, Is.EqualTo(4_000),
                    "inside the one-minute gate the sweep must not run at all");

        clock.UtcNowUnix = 5_060;        // 60s later — the gate opens
        pk.Tick();
        Assert.That(sp.Char.PkExpiryUtc, Is.Zero, "once the gate opens the expired flag clears");
    }

    // ── Post-death PK grace window ────────────────────────────────────────────

    // A PK player who respawns gets a fixed grace window measured from now. The window's end is
    // arithmetic on the clock, so pinning the clock pins the exact second it lapses.
    [Test]
    public void PostDeathGrace_IsMeasuredFromTheInjectedClock()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock { UtcNowUnix = 777_000 };
        var combat = new CombatSystem(world, pm, new NoOpDispatcher(),
            items: null!, movement: null!, joinLeave: null!, blood: null!,
            objectives: null!, guilds: null!, guildWar: null!, territory: null!, clock: clock);

        var sp = Online(pm, 1);
        sp.Char.Map = 1;
        sp.Char.PkExpiryUtc = 800_000;          // flagged PK, so grace applies

        combat.BeginPostDeathGrace(1);

        Assert.That(sp.PkGraceUntilUtc, Is.EqualTo(777_000 + Constants.PkGraceDurationSeconds),
                    "the grace window ends exactly PkGraceDurationSeconds after the clock's now");
    }

    // A player who is NOT PK-flagged gets no grace window — the early return fires before the stamp.
    [Test]
    public void PostDeathGrace_IsNotGrantedToANonPkPlayer()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock { UtcNowUnix = 777_000 };
        var combat = new CombatSystem(world, pm, new NoOpDispatcher(),
            items: null!, movement: null!, joinLeave: null!, blood: null!,
            objectives: null!, guilds: null!, guildWar: null!, territory: null!, clock: clock);

        var sp = Online(pm, 1);
        sp.Char.Map = 1;
        sp.Char.PkExpiryUtc = 0;                // not flagged

        combat.BeginPostDeathGrace(1);

        Assert.That(sp.PkGraceUntilUtc, Is.Zero, "grace is a PK-only concession");
    }

    // A PK flag that has ALREADY lapsed grants no grace either — IsPk is evaluated against the same
    // pinned now, so this pins the interaction between the two deadlines.
    [Test]
    public void PostDeathGrace_IsNotGrantedWhenThePkFlagHasAlreadyLapsed()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock { UtcNowUnix = 900_000 };
        var combat = new CombatSystem(world, pm, new NoOpDispatcher(),
            items: null!, movement: null!, joinLeave: null!, blood: null!,
            objectives: null!, guilds: null!, guildWar: null!, territory: null!, clock: clock);

        var sp = Online(pm, 1);
        sp.Char.Map = 1;
        sp.Char.PkExpiryUtc = 899_999;          // lapsed one second ago

        combat.BeginPostDeathGrace(1);

        Assert.That(sp.PkGraceUntilUtc, Is.Zero,
                    "a flag that lapsed before the death grants nothing");
    }

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
    // ordinary mail — three days, not thirty. Pinning the clock is what makes the two windows
    // distinguishable in a test.
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

    // ── Guild weekly tax date arithmetic ──────────────────────────────────────

    // Days-until-tax counts forward from today to the guild's founding weekday, and "today" counts as a
    // full 7 rather than 0 (today's settlement already ran). Both halves are pure calendar arithmetic
    // off the clock's LOCAL date — untestable before, since it read DateTime.Now.
    [Test]
    public void DaysUntilTax_CountsForwardToTheFoundingWeekday()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock();
        var guilds = new GuildSystem(world, pm, new NoOpDispatcher(),
            persistence: null!, bg: null!, saver: null!, items: null!, mail: null!, objectives: null!,
            NullLogger<GuildSystem>.Instance, clock: clock);

        // Wednesday 2026-01-07.
        clock.LocalNow = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Local);
        Assert.That(clock.LocalNow.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday), "sanity: the fixture date");

        var guild = new GuildRecord { Index = 1, Name = "G" };

        guild.FoundingWeekday = DayOfWeek.Friday;
        Assert.That(Invoke<int>(guilds, "ComputeDaysUntilTax", guild), Is.EqualTo(2),
                    "Wednesday to Friday is two days");

        guild.FoundingWeekday = DayOfWeek.Tuesday;
        Assert.That(Invoke<int>(guilds, "ComputeDaysUntilTax", guild), Is.EqualTo(6),
                    "Wednesday to next Tuesday wraps the week");

        guild.FoundingWeekday = DayOfWeek.Wednesday;
        Assert.That(Invoke<int>(guilds, "ComputeDaysUntilTax", guild), Is.EqualTo(7),
                    "today counts as a full week out — today's tax has already run");
    }

    // Moving only the clock, with the guild unchanged, must move the answer — so the test above is not
    // reading a stored value.
    [Test]
    public void DaysUntilTax_MovesWithTheClock()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var clock = new FixedClock();
        var guilds = new GuildSystem(world, pm, new NoOpDispatcher(),
            persistence: null!, bg: null!, saver: null!, items: null!, mail: null!, objectives: null!,
            NullLogger<GuildSystem>.Instance, clock: clock);

        var guild = new GuildRecord { Index = 1, Name = "G", FoundingWeekday = DayOfWeek.Sunday };

        clock.LocalNow = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Local);   // Monday
        Assert.That(Invoke<int>(guilds, "ComputeDaysUntilTax", guild), Is.EqualTo(6));

        clock.LocalNow = new DateTime(2026, 1, 9, 12, 0, 0, DateTimeKind.Local);   // Friday
        Assert.That(Invoke<int>(guilds, "ComputeDaysUntilTax", guild), Is.EqualTo(2));
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

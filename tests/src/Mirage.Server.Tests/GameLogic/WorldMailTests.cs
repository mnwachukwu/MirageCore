using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// The post: what a game can say that outlives the moment.
///
/// <para>🔴 <b>Everything else a game says is spoken to somebody standing there.</b> A line of chat is
/// gone when they log out and a thing handed over needs a bag with room in it — so a reward that was
/// EARNED rather than picked up had nowhere to go. A letter waits, and what is attached to it waits
/// with it.</para>
///
/// <para>And a guild is the only roster the engine keeps that outlives its members' sessions, which is
/// why paying one is its own call rather than a loop over bodies a game can see.</para>
/// </summary>
[TestFixture]
public class WorldMailTests
{
    private const int Map = 1, Gold = 2, Ann = 1, Bob = 2, TheGuild = 1;

    private static (IWorld World, GameWorld Game, PlayerManager Pm) Build()
    {
        var game = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(game, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);

        game.Maps[Map] = new MapRecord(16, 12);
        game.Items[Gold].Name = "Gold";
        game.Items[Gold].Type = ItemType.Currency;

        foreach (int index in (int[])[Ann, Bob])
        {
            var sp = pm[index];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            sp.Login = index == Ann ? "ann" : "bob";
            sp.Char.Map = Map;
        }

        var world = new ServerWorld(game, pm, attributes: null!, deaths: null!, movement: null!,
                                    items: items, joinLeave: null!, decals: null!, ai: null!,
                                    guilds: null!, dispatcher, markers: null, spawns: null,
                                    spread: null, mail: mail);

        return (world, game, pm);
    }

    private static List<MailMessage> Inbox(PlayerManager pm, int index) => pm[index].Mail;

    // ── One letter ────────────────────────────────────────────────────────────

    [Test]
    public void ALetterReachesThem()
    {
        var (world, _, pm) = Build();

        bool sent = world.Mail(EntityHandle.ForPlayer(Ann), "A word", "About that thing.");

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.True);
            Assert.That(Inbox(pm, Ann), Has.Count.EqualTo(1));
            Assert.That(Inbox(pm, Ann)[0].Subject, Is.EqualTo("A word"));
            Assert.That(Inbox(pm, Bob), Is.Empty, "and nobody else");
        });
    }

    [Test]
    public void SomethingCanRideWithIt()
    {
        var (world, _, pm) = Build();

        world.Mail(EntityHandle.ForPlayer(Ann), "Your share", "Well fought.", Gold, 500);

        var letter = Inbox(pm, Ann)[0];

        Assert.Multiple(() =>
        {
            Assert.That(letter.Attachments, Has.Count.EqualTo(1));
            Assert.That(letter.Attachments[0].ItemNum, Is.EqualTo(Gold));
            Assert.That(letter.Attachments[0].Quantity, Is.EqualTo(500));
        });
    }

    /// <summary>⚠ An item of nothing is a letter on its own, which is an ordinary thing for a game to
    /// send — a notice, a summons, a thank-you.</summary>
    [Test]
    public void AnItemOfNothing_IsALetterOnItsOwn()
    {
        var (world, _, pm) = Build();

        world.Mail(EntityHandle.ForPlayer(Ann), "A notice", "Be somewhere on Saturday.", itemNum: 0);

        Assert.That(Inbox(pm, Ann)[0].Attachments, Is.Empty);
    }

    [Test]
    public void AnAmountOfNothing_SendsNoParcelEither()
    {
        var (world, _, pm) = Build();

        world.Mail(EntityHandle.ForPlayer(Ann), "A notice", "Nothing enclosed.", Gold, quantity: 0);

        Assert.That(Inbox(pm, Ann)[0].Attachments, Is.Empty);
    }

    [Test]
    public void ABodyThatIsNotThere_IsPostedNothing()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.Mail(EntityHandle.None, "x", "y"), Is.False);
            Assert.That(world.Mail(EntityHandle.ForNpc(1, 1), "x", "y"), Is.False,
                "a creature has no account to post to");
            Assert.That(world.Mail(EntityHandle.ForPlayer(900), "x", "y"), Is.False);
        });
    }

    // ── Naming somebody who is not here ───────────────────────────────────────

    /// <summary>🔴 <b>The one identity here that outlives a session.</b> A handle stops meaning anything
    /// the moment they log out, so a game promising to settle up later writes the account down while it
    /// still has the person in front of it.</summary>
    [Test]
    public void ABodyNamesTheAccountBehindIt()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.AccountOf(EntityHandle.ForPlayer(Ann)), Is.EqualTo("ann"));
            Assert.That(world.AccountOf(EntityHandle.ForNpc(1, 1)), Is.Empty, "a creature has no account");
            Assert.That(world.AccountOf(EntityHandle.None), Is.Empty);
        });
    }

    /// <summary>And the way back, which is the answer that tells a game to post rather than to tell.</summary>
    [Test]
    public void AnAccountNamesWhoeverIsSignedInToIt()
    {
        var (world, _, pm) = Build();

        Assert.That(world.WhoIs("ann"), Is.EqualTo(EntityHandle.ForPlayer(Ann)));

        pm[Ann].IsConnected = false;
        pm[Ann].InGame = false;

        Assert.Multiple(() =>
        {
            Assert.That(world.WhoIs("ann"), Is.EqualTo(EntityHandle.None), "and nobody once they have gone");
            Assert.That(world.WhoIs("nobody"), Is.EqualTo(EntityHandle.None));
            Assert.That(world.WhoIs(""), Is.EqualTo(EntityHandle.None));
        });
    }

    /// <summary>🔴 <b>What a handle could never do.</b> Ann logged out, so nothing in the seam names her
    /// any more — but the account a game wrote down still does, and the letter is waiting when she comes
    /// back.</summary>
    [Test]
    public void AnAccountIsPostedToAfterTheyHaveGone()
    {
        var (world, _, pm) = Build();

        string kept = world.AccountOf(EntityHandle.ForPlayer(Ann));

        pm[Ann].IsConnected = false;
        pm[Ann].InGame = false;
        pm[Ann].Mail.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(world.Mail(EntityHandle.ForPlayer(Ann), "x", "y"), Is.False,
                "the body is not there to be told");
            Assert.That(world.MailTo(kept, "Your sale", "It went through.", Gold, 250), Is.True,
                "and the account still is");
        });
    }

    [Test]
    public void AnAccountNobodyNamed_IsPostedNothing()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.MailTo("", "x", "y"), Is.False);
            Assert.That(world.MailTo("   ", "x", "y"), Is.False);
        });
    }

    // ── A guild ───────────────────────────────────────────────────────────────

    private static void AGuildOf(GameWorld game, params (string Login, long Active, long LastSeen)[] members)
    {
        var guild = new GuildRecord { Index = TheGuild, Name = "The Gathering" };

        foreach (var (login, active, lastSeen) in members)
        {
            guild.Members.Add(new GuildMember { Login = login, ActiveSeconds = active, LastSeenUtc = lastSeen });
        }

        game.Guilds[TheGuild] = guild;
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>🔴 The only way to pay somebody who is not here. Bob is offline and has no handle, so
    /// nothing else in the seam could reach him — and what the guild earned is his as much as hers.</summary>
    [Test]
    public void EveryMemberIsPaid_ThePresentAndTheAbsent()
    {
        var (world, game, pm) = Build();
        AGuildOf(game, ("ann", 0, 0), ("bob", 0, 0), ("carol", 0, 0));

        // Only Ann is in the world at all.
        pm[Bob].IsConnected = false;
        pm[Bob].InGame = false;

        int reached = world.MailMembers(TheGuild, Gold, 100, "Your share", "Well held.");

        Assert.Multiple(() =>
        {
            Assert.That(reached, Is.EqualTo(3), "all three, whether or not they were here");
            Assert.That(Inbox(pm, Ann), Has.Count.EqualTo(1), "and the one who was here has it now");
        });
    }

    /// <summary>⚠ A payout split among a hundred names nobody has used is a payout nobody feels, so a
    /// game may narrow it to members who have really been playing: online for long enough, recently
    /// enough. Both halves are needed — the seconds alone keep somebody who played hard a year ago, and
    /// the last-seen alone keeps somebody who logs in for a minute a day.</summary>
    [Test]
    public void NarrowedToTheLive_ItSkipsANameNobodyHasUsed()
    {
        var (world, game, _) = Build();

        AGuildOf(game,
            ("ann", Constants.GuildActiveMemberMinSeconds, Now),
            ("bob", Constants.GuildActiveMemberMinSeconds - 1, Now),
            ("carol", Constants.GuildActiveMemberMinSeconds, Now - Constants.GuildActiveMemberWindowSeconds - 1),
            ("dave", 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(world.MailMembers(TheGuild, Gold, 100, "s", "b", onlyActive: true), Is.EqualTo(1),
                "only the one who is both");
            Assert.That(world.MailMembers(TheGuild, Gold, 100, "s", "b"), Is.EqualTo(4),
                "and all of them when it is not narrowed");
        });
    }

    /// <summary>🔴 A guild's roster outlives its members' sessions, and <c>MembersOf</c> answers only
    /// with the part of it that is here. Anything about the GUILD rather than about the people in front
    /// of you starts from the accounts.</summary>
    [Test]
    public void TheRosterIsEverybody_NotJustWhoIsHere()
    {
        var (world, game, pm) = Build();
        AGuildOf(game, ("ann", 0, 0), ("bob", 0, 0), ("carol", 0, 0));

        pm[Ann].Guild = TheGuild;
        pm[Bob].IsConnected = false;
        pm[Bob].InGame = false;

        Assert.Multiple(() =>
        {
            Assert.That(world.AccountsIn(TheGuild), Is.EquivalentTo(new[] { "ann", "bob", "carol" }));
            Assert.That(world.MembersOf(TheGuild), Has.Count.EqualTo(1),
                "where the bodies are only the one who is here");
            Assert.That(world.AccountsIn(99), Is.Empty);
        });
    }

    /// <summary>The live-roster question on its own, for a rule that wants it somewhere other than a
    /// payout — who votes, who makes a quorum.</summary>
    [Test]
    public void OneAccountCanBeAskedWhetherItIsLive()
    {
        var (world, game, _) = Build();

        AGuildOf(game,
            ("ann", Constants.GuildActiveMemberMinSeconds, Now),
            ("bob", 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(world.IsActiveIn(TheGuild, "ann"), Is.True);
            Assert.That(world.IsActiveIn(TheGuild, "bob"), Is.False);
            Assert.That(world.IsActiveIn(TheGuild, "stranger"), Is.False, "somebody not on the roster");
            Assert.That(world.IsActiveIn(99, "ann"), Is.False);
        });
    }

    [Test]
    public void AGuildThatIsNotThere_IsPaidNothing()
    {
        var (world, _, _) = Build();

        Assert.That(world.MailMembers(99, Gold, 100, "s", "b"), Is.Zero);
    }

    [Test]
    public void NothingWorthSending_IsNotSent()
    {
        var (world, game, _) = Build();
        AGuildOf(game, ("ann", 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(world.MailMembers(TheGuild, itemNum: 0, 100, "s", "b"), Is.Zero);
            Assert.That(world.MailMembers(TheGuild, Gold, quantity: 0, "s", "b"), Is.Zero);
        });
    }

    private sealed class NoOpDispatcher : IPacketDispatcher
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

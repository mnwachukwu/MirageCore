using Mirage.Server.Core.GameLogic;
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
/// A player actually dies.
///
/// <para>🔴 <b>Every other test of this path asks a double, and the double says yes.</b> That is exactly
/// what hid the creature-death hole for so long: a recording world returns true for any handle, so a
/// rule calling Kill looks like it worked whatever the engine would really have done. These run the
/// real <see cref="DeathSystem"/> with a real world under it.</para>
///
/// <para>What has to be true when a body reaches nothing: the death is allowed, the policies are asked
/// in order, the body is moved, and the caller is told it happened. A game that is told "yes" and left
/// standing where it fell has no way to notice.</para>
/// </summary>
[TestFixture]
public class PlayerDeathRunsTests
{
    private const int Home = 1, Away = 2, Index = 1;

    /// <summary>A policy that records what it was asked and can refuse on command.</summary>
    private sealed class Watcher(bool allow = true) : IDeathPolicy
    {
        public List<string> Asked { get; } = [];

        public Refusal MayDie(in Death death)
        {
            Asked.Add("mayDie");
            return allow ? Refusal.Allow : Refusal.Deny("not today");
        }

        public void OnDied(in Death death) => Asked.Add("onDied");
    }

    /// <summary>A policy that names where the body comes back, the way a game's respawn rule does.</summary>
    private sealed class SendsHome(int map, int x, int y) : IDeathPolicy
    {
        public Respawn RespawnFor(in Death death) => new(map, x, y);
    }

    private static (DeathSystem Deaths, PlayerManager Pm) Build(params IDeathPolicy[] policies)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        foreach (int mapNum in (int[])[Home, Away])
        {
            world.Maps[mapNum] = new MapRecord(16, 12);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 12; y++)
                    world.Maps[mapNum].Tile[x, y] = new TileRecord { Type = TileType.Walkable };
        }

        var movement = new MovementSystem(world, pm, dispatcher);
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var spawns = new SpawnSystem(world, pm, dispatcher, items);

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Matt";
        sp.Char.Map = Away;
        sp.Char.X = 9;
        sp.Char.Y = 9;
        world.MapObservers[Away].Add(Index);

        return (new DeathSystem(world, pm, dispatcher, movement, spawns, policies), pm);
    }

    private static EntityHandle Who => EntityHandle.ForPlayer(Index);

    [Test]
    public void APlayerReachingNothingIsActuallyKilled()
    {
        var (deaths, _) = Build();

        Assert.That(deaths.Kill(Who, EntityHandle.None, "slain"), Is.True);
    }

    /// <summary>Refuse, then cost, then move. A policy that sheds inventory has to run while the body is
    /// still where it fell.</summary>
    [Test]
    public void ThePoliciesAreAskedInOrder()
    {
        var watcher = new Watcher();
        var (deaths, _) = Build(watcher);

        deaths.Kill(Who, EntityHandle.None, "slain");

        Assert.That(watcher.Asked, Is.EqualTo(new[] { "mayDie", "onDied" }));
    }

    /// <summary>The body MOVES. A game told the death happened while the body stands where it fell has
    /// no way to notice, and the player is left standing on the tile that killed them.</summary>
    [Test]
    public void TheBodyIsMoved()
    {
        var (deaths, pm) = Build(new SendsHome(Home, 4, 4));

        deaths.Kill(Who, EntityHandle.None, "slain");

        var p = pm[Index].Char;
        Assert.Multiple(() =>
        {
            Assert.That(p.Map, Is.EqualTo(Home));
            Assert.That((p.X, p.Y), Is.EqualTo((4, 4)));
        });
    }

    /// <summary>A refusal stops everything after it: nothing is taken and nothing is moved.</summary>
    [Test]
    public void ARefusalStopsTheWholeThing()
    {
        var refuser = new Watcher(allow: false);
        var (deaths, pm) = Build(refuser, new SendsHome(Home, 4, 4));

        bool died = deaths.Kill(Who, EntityHandle.None, "slain");

        var p = pm[Index].Char;
        Assert.Multiple(() =>
        {
            Assert.That(died, Is.False, "the caller is told it did not happen");
            Assert.That(refuser.Asked, Is.EqualTo(new[] { "mayDie" }), "and nothing after the refusal ran");
            Assert.That(p.Map, Is.EqualTo(Away), "the body stayed where it was");
        });
    }

    /// <summary>Killing the same body twice in a row must not wedge it. A rule that fires on a beat can
    /// reach a body that is already going.</summary>
    [Test]
    public void KillingTwiceIsNotAWedge()
    {
        var (deaths, pm) = Build(new SendsHome(Home, 4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(deaths.Kill(Who, EntityHandle.None, "slain"), Is.True, "the first");
            Assert.That(deaths.Kill(Who, EntityHandle.None, "slain"), Is.True, "and the second");
            Assert.That(pm[Index].Char.Map, Is.EqualTo(Home));
        });
    }

    /// <summary>A body nobody is playing cannot die, so a stale handle never takes a slot
    /// apart.</summary>
    [Test]
    public void ABodyNobodyIsPlayingDoesNotDie()
    {
        var (deaths, pm) = Build();
        pm[Index].InGame = false;

        Assert.That(deaths.Kill(Who, EntityHandle.None, "slain"), Is.False);
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

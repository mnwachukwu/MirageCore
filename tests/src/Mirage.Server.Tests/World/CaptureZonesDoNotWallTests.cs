using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A live contest changes who may hit whom, not where anybody may walk.
///
/// <para>🔴 Capture points held an invisible radius wall while a contest was setting up, and it walled the
/// ground in both directions: it kept non-defenders out, and it kept anyone already standing inside from
/// stepping out. A player caught by the start of war night was trapped on their own tiles with nothing on
/// screen saying why, which is worse than the crowding it was meant to prevent.</para>
///
/// <para>The zone still exists for the things that are not walls — the entry warning and NPC suppression —
/// so this walks a player straight across a capture point with a contest live, in both directions.</para>
/// </summary>
[TestFixture]
public class CaptureZonesDoNotWallTests
{
    const int Map = 1, Me = 1, Territory = 3, MyGuild = 7, DefendingGuild = 9;

    private static (MovementSystem move, PlayerManager pm) Setup(int guild)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var chat = new NoOpDispatcher();
        var move = new MovementSystem(world, pm, chat, new BloodSystem(world, chat));

        var sp = pm[Me];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Guild = guild;
        var p = sp.Char;
        p.Map = Map;
        p.X = 5;
        p.Y = 5;
        p.MaxHp = 100;
        p.Hp = 100;                        // full HP => no blood trail during the step
        world.MapObservers[Map].Add(Me);

        // A contest running over this map, with a capture point on the tile the player stands on.
        world.ContestZones.Add(new ContestZone
        {
            TerritoryIndex = Territory,
            Name = "Ashfall",
            Participants = [DefendingGuild],
            Maps = [Map],
        });
        return (move, pm);
    }

    /// <summary>The case that trapped somebody: standing on a capture point when the contest starts, and
    /// walking off it.</summary>
    [Test]
    public void ANonParticipantStandingOnAPoint_CanWalkOut()
    {
        var (move, pm) = Setup(guild: 0);

        move.PlayerMove(Me, Direction.Down, MovementType.Walking);

        Assert.That(pm[Me].Char.Y, Is.EqualTo(6), "a contest zone held the player where they stood");
    }

    [Test]
    public void ANonParticipant_CanWalkIn()
    {
        var (move, pm) = Setup(guild: 0);
        pm[Me].Char.Y = 4;                 // just outside, stepping toward the point

        move.PlayerMove(Me, Direction.Down, MovementType.Walking);

        Assert.That(pm[Me].Char.Y, Is.EqualTo(5));
    }

    /// <summary>A guild taking part in the contest walks the same ground as everyone else.</summary>
    [Test]
    public void AChallenger_WalksFreelyToo()
    {
        var (move, pm) = Setup(guild: MyGuild);

        move.PlayerMove(Me, Direction.Right, MovementType.Walking);

        Assert.That(pm[Me].Char.X, Is.EqualTo(6));
    }

    /// <summary>The zone itself is still published — it carries the entry warning and the NPC suppression,
    /// which are not walls and are not being removed.</summary>
    [Test]
    public void TheZoneIsStillThere_ForTheThingsThatArentWalls()
    {
        var (move, _) = Setup(guild: 0);
        var world = (GameWorld)typeof(MovementSystem)
            .GetField("_world", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(move)!;

        Assert.Multiple(() =>
        {
            Assert.That(world.ContestZones, Has.Count.EqualTo(1));
            Assert.That(world.IsContestSuppressedMap(Map), Is.True, "NPC suppression went with the walls");
        });
    }

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

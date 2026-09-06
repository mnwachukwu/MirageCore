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
/// A dead player does not hold a tile. Walking over a corpse is how you reach whoever killed them, or the
/// body itself — a corpse that blocks can wall a doorway shut until its owner respawns.
///
/// <para>The map here is Moral None, where living players DO collide, so the corpse case is the rule doing
/// something rather than a safe zone letting everyone through for another reason.</para>
///
/// <para>The client predicts this same rule locally; the two have to agree, or the step is refused on one
/// side and allowed on the other. See CorpsesAreWalkedOverTests in the client suite.</para>
/// </summary>
[TestFixture]
public class CorpsesDoNotBlockTests
{
    const int Map = 1, Me = 1, Them = 2;

    /// <summary>Me at (5,5) facing down, somebody else standing on (5,6).</summary>
    private static (MovementSystem move, PlayerManager pm) Setup(bool theyAreDead)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var chat = new NoOpDispatcher();
        var move = new MovementSystem(world, pm, chat, new BloodSystem(world, chat));

        Stand(pm, world, Me, 5, 5);
        var them = Stand(pm, world, Them, 5, 6);
        them.Dead = theyAreDead;
        return (move, pm);
    }

    private static Mirage.Shared.Records.PlayerRecord Stand(PlayerManager pm, GameWorld world, int index, int x, int y)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.X = x;
        p.Y = y;
        p.MaxHp = 100;
        p.Hp = 100;                       // full HP => no blood trail during the step
        world.MapObservers[Map].Add(index);
        return p;
    }

    /// <summary>The control. Without this the test below would pass on a map where nobody blocks anybody.</summary>
    [Test]
    public void ALivingPlayer_HoldsTheTile()
    {
        var (move, pm) = Setup(theyAreDead: false);

        move.PlayerMove(Me, Direction.Down, MovementType.Walking);

        Assert.That(pm[Me].Char.Y, Is.EqualTo(5), "a living player should still block");
    }

    [Test]
    public void ACorpse_IsWalkedOver()
    {
        var (move, pm) = Setup(theyAreDead: true);

        move.PlayerMove(Me, Direction.Down, MovementType.Walking);

        Assert.That(pm[Me].Char.Y, Is.EqualTo(6), "a corpse blocked the tile");
    }

    /// <summary>Standing back up puts the tile back under them.</summary>
    [Test]
    public void RespawningMakesThemSolidAgain()
    {
        var (move, pm) = Setup(theyAreDead: true);
        pm[Them].Char.Dead = false;

        move.PlayerMove(Me, Direction.Down, MovementType.Walking);

        Assert.That(pm[Me].Char.Y, Is.EqualTo(5));
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

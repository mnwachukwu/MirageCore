using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Whether two players standing on one tile is allowed, and who decides.
///
/// <para>🔴 The server owns this and the client PREDICTS it, so the two have to agree exactly or an
/// honest player rubber-bands on every step into a crowd. The rule is deliberately small — a map says
/// yes or no, and nothing about who is walking enters into it — because a rule the client cannot
/// compute from what it has is a rule it will get wrong.</para>
///
/// <para>A crowd standing on the one tile everybody has to cross is a problem in any game with more
/// than a few players in a room, so this is the engine's, not a game's. Which rooms those are is the
/// map author's call.</para>
/// </summary>
[TestFixture]
public class PlayersPassThroughTests
{
    const int Map = 1, Mover = 1, Blocker = 2;

    static (MovementSystem Move, GameWorld World, PlayerManager Pm) Setup(bool? passThrough)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var move = new MovementSystem(world, pm, new SilentDispatcher());

        world.Maps[Map].PlayersPassThrough = passThrough;

        foreach (int i in new[] { Mover, Blocker })
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            sp.Char.Name = $"P{i}";
            sp.Char.Map = Map;
            world.MapObservers[Map].Add(i);
        }

        // The mover at (5,5) facing a blocker standing one tile down.
        pm[Mover].Char.X = 5;
        pm[Mover].Char.Y = 5;
        pm[Blocker].Char.X = 5;
        pm[Blocker].Char.Y = 6;
        return (move, world, pm);
    }

    static int StepDownAndReportY(MovementSystem move, PlayerManager pm)
    {
        move.PlayerMove(Mover, Direction.Down, MovementType.Walking);
        return pm[Mover].Char.Y;
    }

    [Test]
    public void ByDefaultABodyBlocksTheTile()
    {
        var (move, _, pm) = Setup(passThrough: null);

        Assert.That(StepDownAndReportY(move, pm), Is.EqualTo(5), "a map that says nothing collides");
    }

    [Test]
    public void AMapThatSaysSoLetsThemThrough()
    {
        var (move, _, pm) = Setup(passThrough: true);

        Assert.That(StepDownAndReportY(move, pm), Is.EqualTo(6));
    }

    /// <summary>An explicit no is a real answer, not an absence: it overrides a group that said yes.</summary>
    [Test]
    public void AnExplicitNoOverridesAGroupThatSaidYes()
    {
        var (move, world, pm) = Setup(passThrough: false);
        world.Maps[Map].MapGroup = 3;
        world.MapGroups[3] = new MapGroupRecord { Index = 3, PlayersPassThrough = true };

        Assert.That(StepDownAndReportY(move, pm), Is.EqualTo(5));
    }

    [Test]
    public void AGroupAnswersForAMapThatDidNot()
    {
        var (move, world, pm) = Setup(passThrough: null);
        world.Maps[Map].MapGroup = 3;
        world.MapGroups[3] = new MapGroupRecord { Index = 3, PlayersPassThrough = true };

        Assert.That(StepDownAndReportY(move, pm), Is.EqualTo(6), "a whole building can say it once");
    }

    /// <summary>🔴 Nothing about the MOVER decides this. The client predicting a step knows what the map
    /// says and would have to be told anything else, so a rule that read some property of the walker
    /// would be one the client could not compute — and the symptom is a rubber-band, not an error.</summary>
    [Test]
    public void WhoIsWalkingDoesNotEnterIntoIt()
    {
        var (move, _, pm) = Setup(passThrough: true);
        pm[Mover].Char.Access = AdminLevel.Creator;
        pm[Blocker].Char.Access = AdminLevel.Player;

        Assert.That(StepDownAndReportY(move, pm), Is.EqualTo(6));
    }

    sealed class SilentDispatcher : IPacketDispatcher
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
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
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

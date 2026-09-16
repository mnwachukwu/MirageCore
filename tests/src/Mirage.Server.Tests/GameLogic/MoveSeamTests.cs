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
/// What a run costs, which is a game's to say.
///
/// <para>🔴 Core knows where a body may stand and how often a step may be taken, and nothing else about
/// moving: it has no stamina, no encumbrance, and no reason a body would slow down. So a game that wants
/// running to cost something needs a seam, and the seam has two halves that fail differently. A policy
/// that is asked but never charged gives a run that is free forever; one that is charged but never asked
/// gives a bar that empties and then goes negative while the body sprints on.</para>
///
/// <para>The refusal SLOWS rather than stops, which is the part worth pinning: a body out of breath in
/// the far corner of a map still gets home.</para>
/// </summary>
[TestFixture]
public class MoveSeamTests
{
    private const int Map = 1, Idx = 1;

    private sealed class Legs : IMovePolicy
    {
        public bool CanRun { get; set; } = true;

        public int Ran { get; private set; }

        public Refusal MayRun(EntityHandle who) => CanRun ? Refusal.Allow : Refusal.Deny("winded");

        public void OnRan(EntityHandle who) => Ran++;
    }

    private static (MovementSystem Move, PlayerManager Pm, GameWorld World) Walkable(params IMovePolicy[] policies)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var move = new MovementSystem(world, pm, new NoOpDispatcher(), moves: policies);

        world.Maps[Map] = new MapRecord(16, 12);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 12; y++)
                world.Maps[Map].Tile[x, y] = new TileRecord { Type = TileType.Walkable };

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 5;
        world.MapObservers[Map].Add(Idx);

        return (move, pm, world);
    }

    // A step at a time, each one paid for: the pace gate banks credit in real time, so a burst sent in
    // one instant is refused for arriving early rather than for anything this file is about.
    private static void Step(MovementSystem move, Direction dir, MovementType pace)
    {
        move.PlayerMove(Idx, dir, pace);
        Thread.Sleep((int)MovementFormulas.BaseWalkMsPerTile);
    }

    [Test]
    public void ARunningStepIsCharged()
    {
        var legs = new Legs();
        var (move, _, _) = Walkable(legs);

        Step(move, Direction.Right, MovementType.Running);

        Assert.That(legs.Ran, Is.EqualTo(1));
    }

    [Test]
    public void AWalkingStepIsNotCharged()
    {
        var legs = new Legs();
        var (move, _, _) = Walkable(legs);

        Step(move, Direction.Right, MovementType.Walking);

        Assert.That(legs.Ran, Is.Zero, "a walk is not a run, whatever the client claimed");
    }

    /// <summary>A refused run keeps moving. Anything else strands a player wherever they ran out.</summary>
    [Test]
    public void ARefusedRunWalksInsteadOfStopping()
    {
        var legs = new Legs { CanRun = false };
        var (move, pm, _) = Walkable(legs);

        Step(move, Direction.Right, MovementType.Running);

        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.X, Is.EqualTo(6), "the step still happened");
            Assert.That(legs.Ran, Is.Zero, "and was not billed as a run");
        });
    }

    /// <summary>A step refused by a WALL costs nothing: the policy is about the legs, and they went
    /// nowhere.</summary>
    [Test]
    public void AStepIntoAWallIsNotCharged()
    {
        var legs = new Legs();
        var (move, pm, world) = Walkable(legs);
        world.Maps[Map].Tile[6, 5] = new TileRecord { Type = TileType.Blocked };

        Step(move, Direction.Right, MovementType.Running);

        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.X, Is.EqualTo(5));
            Assert.That(legs.Ran, Is.Zero);
        });
    }

    /// <summary>God mode is out of every game's reach here. It exists so somebody can cross a broken
    /// map, and a rule that slowed it down or emptied a bar under it would defeat that.</summary>
    [Test]
    public void AnObserverRunsForFree()
    {
        var legs = new Legs { CanRun = false };
        var (move, pm, _) = Walkable(legs);
        pm[Idx].Char.GodMode = true;

        Step(move, Direction.Right, MovementType.Running);

        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.X, Is.EqualTo(6));
            Assert.That(legs.Ran, Is.Zero, "and is charged nothing either");
        });
    }

    /// <summary>With no policy declared a run is free, which is the engine by itself and a coherent
    /// game rather than a broken one.</summary>
    [Test]
    public void WithNoPolicy_RunningIsFree()
    {
        var (move, pm, _) = Walkable();

        Step(move, Direction.Right, MovementType.Running);

        Assert.That(pm[Idx].Char.X, Is.EqualTo(6));
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

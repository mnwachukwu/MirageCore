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
/// What a game can actually do.
///
/// <para><b>Every one of these was already implemented and unreachable.</b> The engine enforced a downed
/// body, coloured a marked name, drew an engaged border and kept a disconnected body in the world — and
/// a module holding only <c>ICoreBuilder</c> could enter none of those states. These pin the way in, and
/// they pin it through <see cref="IWorld"/> rather than through the systems behind it, because that
/// interface is the promise a game is written against.</para>
/// </summary>
[TestFixture]
public class WorldActionTests
{
    const int Idx = 1, Map = 1;

    private static (IWorld World, GameWorld Game, PlayerManager Pm) Build()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var attributes = new AttributeSystem(world, pm, dispatcher);
        var deaths = new DeathSystem(world, pm, dispatcher, movement);
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var decals = new DecalSystem(world, dispatcher);

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

        var actions = new ServerWorld(world, pm, attributes, deaths, movement, items,
                                      joinLeave: null!, decals);
        return (actions, world, pm);
    }

    private static EntityHandle Me => EntityHandle.ForPlayer(Idx);
    private static EntityHandle Nobody => EntityHandle.ForPlayer(Constants.MaxPlayers);

    // ── Who is here ───────────────────────────────────────────────────────────

    [Test]
    public void ABodyInTheWorld_IsFoundWhereItStands()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.IsInWorld(Me), Is.True);
            Assert.That(world.PlaceOf(Me), Is.EqualTo(new WorldPlace(Map, 5, 5)));
        });
    }

    /// <summary>A handle outlives what it names — a player logs out, an NPC despawns, a slot is reused.
    /// So every method answers for a body that is not there rather than throwing on it.</summary>
    [Test]
    public void AHandleNamingNobody_IsAnsweredRatherThanThrownAt()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.IsInWorld(Nobody), Is.False);
            Assert.That(world.PlaceOf(Nobody), Is.EqualTo(WorldPlace.Nowhere));
            Assert.That(world.AttributesOf(Nobody), Is.Null);
            Assert.That(world.SetAttribute(Nobody, "hp", 10), Is.False);
            Assert.That(world.Warp(Nobody, new WorldPlace(Map, 1, 1)), Is.False);
            Assert.DoesNotThrow(() => world.SetEngaged(Nobody, 10));
            Assert.DoesNotThrow(() => world.SetDowned(Nobody, 10));
            Assert.DoesNotThrow(() => world.Give(Nobody, 1));
            Assert.DoesNotThrow(() => world.Take(Nobody, 1));
        });
    }

    [Test]
    public void AHandleNamingNothingAtAll_IsAlsoSafe()
    {
        var (world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(world.IsInWorld(EntityHandle.None), Is.False);
            Assert.That(world.PlaceOf(EntityHandle.None), Is.EqualTo(WorldPlace.Nowhere));
            Assert.DoesNotThrow(() => world.SetActionCooldown(EntityHandle.None, 1));
        });
    }

    // ── The states the engine already acted on ────────────────────────────────

    [Test]
    public void EngagedState_CanBeEnteredAndCleared()
    {
        var (world, _, pm) = Build();

        world.SetEngaged(Me, 10);
        Assert.That(pm[Idx].IsInCombat(Environment.TickCount64), Is.True);

        world.SetEngaged(Me, 0);
        Assert.That(pm[Idx].IsInCombat(Environment.TickCount64), Is.False);
    }

    [Test]
    public void DownedState_StopsTheBodyAndCountsDownToItsReturn()
    {
        var (world, _, pm) = Build();

        world.SetDowned(Me, 30);

        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.Dead, Is.True);
            Assert.That(pm[Idx].Char.RespawnReadyUtc, Is.GreaterThan(0));
        });

        world.SetDowned(Me, 0);
        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.Dead, Is.False);
            Assert.That(pm[Idx].Char.RespawnReadyUtc, Is.Zero);
        });
    }

    [Test]
    public void MarkedState_IsWhatEveryPkRuleAlreadyReads()
    {
        var (world, _, pm) = Build();

        world.SetMarked(Me, 60);
        Assert.That(pm[Idx].Char.IsPk(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Is.True);

        world.SetMarked(Me, 0);
        Assert.That(pm[Idx].Char.IsPk(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Is.False);
    }

    [Test]
    public void AggressorState_CanBeEnteredAndCleared()
    {
        var (world, _, pm) = Build();

        world.SetAggressor(Me, 30);
        Assert.That(pm[Idx].IsAggressor(Environment.TickCount64), Is.True);

        world.SetAggressor(Me, 0);
        Assert.That(pm[Idx].IsAggressor(Environment.TickCount64), Is.False);
    }

    /// <summary>The cooldown is a START stamp the bar measures forward from, not an expiry — so a game
    /// setting one must land a stamp in the PAST-or-now, and clearing it must zero the stamp.</summary>
    [Test]
    public void ActionCooldown_StampsAStartAndClearsToZero()
    {
        var (world, _, pm) = Build();

        world.SetActionCooldown(Me, 1);
        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].AttackTimer, Is.GreaterThan(0));
            Assert.That(pm[Idx].AttackTimer, Is.LessThanOrEqualTo(Environment.TickCount64));
            Assert.That(pm[Idx].Char.AttackTimer, Is.EqualTo(pm[Idx].AttackTimer),
                        "the record's copy is what the client is told");
        });

        world.SetActionCooldown(Me, 0);
        Assert.That(pm[Idx].AttackTimer, Is.Zero);
    }

    // ── What can be done to a body ────────────────────────────────────────────

    [Test]
    public void Warp_PutsTheBodySomewhereElse()
    {
        var (world, _, pm) = Build();

        Assert.That(world.Warp(Me, new WorldPlace(Map, 2, 3)), Is.True);
        Assert.That(world.PlaceOf(Me), Is.EqualTo(new WorldPlace(Map, 2, 3)));
    }

    [Test]
    public void ATileThatIsNotThere_IsRefusedRatherThanTravelledTo()
    {
        var (world, _, _) = Build();

        Assert.That(world.Warp(Me, new WorldPlace(Map, 99, 99)), Is.False);
        Assert.That(world.PlaceOf(Me), Is.EqualTo(new WorldPlace(Map, 5, 5)));
    }

    [Test]
    public void GiveAndTake_MoveItemsThroughTheBag()
    {
        var (world, game, pm) = Build();
        game.Items[7].Type = ItemType.Consumable;

        world.Give(Me, 7);
        Assert.That(pm[Idx].Char.Inv[1].Num, Is.EqualTo(7));

        world.Take(Me, 7);
        Assert.That(pm[Idx].Char.Inv[1].Num, Is.Zero);
    }

    [Test]
    public void Attributes_AreWrittenAndReadBack()
    {
        var (world, _, _) = Build();

        Assert.That(world.SetAttribute(Me, "hp", 42), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(world.AttributesOf(Me)!["hp"].AsLong(), Is.EqualTo(42));
            Assert.That(world.RemoveAttribute(Me, "hp"), Is.True);
            Assert.That(world.AttributesOf(Me)!.Has("hp"), Is.False);
        });
    }

    // ── The world itself ──────────────────────────────────────────────────────

    [Test]
    public void Stain_LandsOnTheGroundWhereItWasAsked()
    {
        var (world, game, _) = Build();

        world.Stain(new WorldPlace(Map, 4, 4), size: 1, WorldLayer.Ground, amount: 1f);

        Assert.That(game.MapDecals.ContainsKey(Map), Is.True,
                    "a game asked for a stain and the map has none");
    }

    [Test]
    public void AGamesOwnRecords_AreReadableByFamilyAndSlot()
    {
        var (world, game, _) = Build();
        var species = new RecordFamily { Id = "Species", DefaultLimit = 3, LimitIsFixed = true };
        game.ModuleRecords.Declare(species, 3);
        game.ModuleRecords.Set("Species", 2, new AttributeBag().Set("name", "Vulpine"));

        Assert.Multiple(() =>
        {
            Assert.That(world.RecordsOf("Species"), Has.Count.EqualTo(3));
            Assert.That(world.RecordAt("Species", 2)!["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(world.RecordAt("Moves", 1), Is.Null, "a family this world does not hold");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────

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

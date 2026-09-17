using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// What a game can actually do.
///
/// <para><b>Every one of these was already implemented and unreachable.</b> The engine enforced a downed
/// body, colored a marked name, drew an engaged border and kept a disconnected body in the world — and
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
        var (world, game, pm, _) = BuildHeard();
        return (world, game, pm);
    }

    private static (IWorld World, GameWorld Game, PlayerManager Pm, NoOpDispatcher Sent) BuildHeard()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var attributes = new AttributeSystem(world, pm, dispatcher);
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

        var spawn = new SpawnSystem(world, pm, dispatcher, items);
        var deaths = new DeathSystem(world, pm, dispatcher, movement, spawn);
        var ai = new NpcAiSystem(world, pm, dispatcher, movement, spawn, items);
        var actions = new ServerWorld(world, pm, attributes, deaths, movement, items,
                                      joinLeave: null!, decals, ai, guilds: null!, dispatcher);
        return (actions, world, pm, dispatcher);
    }

    /// <summary>🔴 A guild is answered from who is IN THE WORLD, not from the roster on disk.
    ///
    /// <para>The two are different questions: the roster is accounts, some logged out for a week, and
    /// the only thing a game does with this answer is act on the bodies in it.</para></summary>
    [Test]
    public void AGuildIsAnsweredFromWhoIsInTheWorld()
    {
        var (world, game, pm) = Build();

        game.Guilds[7] = new GuildRecord { Index = 7, Name = "The Gathering" };

        pm[Idx].Guild = 7;
        Playing(pm, 2, guild: 7);
        Playing(pm, 3, guild: 9);

        Assert.Multiple(() =>
        {
            Assert.That(world.GuildOf(EntityHandle.ForPlayer(Idx)), Is.EqualTo("The Gathering"));

            Assert.That(world.GuildmatesOf(EntityHandle.ForPlayer(Idx)),
                Is.EqualTo(new[] { EntityHandle.ForPlayer(Idx), EntityHandle.ForPlayer(2) }).AsCollection,
                "the one in another guild is not in it");

            Assert.That(world.GuildOf(EntityHandle.ForPlayer(3)), Is.Empty,
                "a guild id nothing answers to is no guild");

            Assert.That(world.GuildmatesOf(EntityHandle.ForPlayer(4)), Is.Empty,
                "and somebody who is not in the world has none");
        });
    }

    /// <summary>A party is a pair, and it is answered the same way.</summary>
    [Test]
    public void APartyIsThePairItIs()
    {
        var (world, _, pm) = Build();

        Playing(pm, 2, guild: 0);
        pm[Idx].InParty = true;
        pm[Idx].PartyPlayer = 2;

        Assert.Multiple(() =>
        {
            Assert.That(world.PartyOf(EntityHandle.ForPlayer(Idx)),
                Is.EqualTo(new[] { EntityHandle.ForPlayer(Idx), EntityHandle.ForPlayer(2) }).AsCollection);

            Assert.That(world.PartyOf(EntityHandle.ForPlayer(2)), Is.Empty,
                "the partner has to say so too - a party is not inferred from the other side");
        });
    }

    /// <summary>Puts somebody in the world, for the group questions to find.</summary>
    private static void Playing(PlayerManager pm, int index, int guild)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Guild = guild;
        sp.Char.Map = Map;
    }

    // ── Creatures ───────────────────────────────────────────────────────

    /// <summary>🔴 The reverse of <c>PlaceOf</c>, which nothing offered.
    ///
    /// <para>Everything else a game holds was handed to it by the engine. A verb used on a square
    /// gives a game coordinates, so without this it can say what happened and not who it happened
    /// to.</para></summary>
    [Test]
    public void ABodyIsFoundByTheSquareItStandsOn()
    {
        var (world, game, pm) = Build();

        game.MapNpcs[Map, 4].Num = 1;
        game.MapNpcs[Map, 4].X = 9;
        game.MapNpcs[Map, 4].Y = 3;

        Assert.Multiple(() =>
        {
            Assert.That(world.At(new WorldPlace(Map, 5, 5)), Is.EqualTo(EntityHandle.ForPlayer(Idx)),
                "the player standing there");

            Assert.That(world.At(new WorldPlace(Map, 9, 3)), Is.EqualTo(EntityHandle.ForNpc(Map, 4)),
                "and the creature");

            Assert.That(world.At(new WorldPlace(Map, 1, 1)), Is.EqualTo(EntityHandle.None),
                "an empty square is nobody");

            Assert.That(world.At(WorldPlace.Nowhere), Is.EqualTo(EntityHandle.None),
                "and so is nowhere");
        });
    }

    /// <summary>🔴 A creature's timed states land on the body, where a client can be told about them.
    ///
    /// <para>The packet has carried a <c>combatMs</c> field the whole time and the server always sent
    /// "never", because nothing could set one — so a fight with a wolf ran with the wolf's overhead
    /// bars hidden.</para></summary>
    [Test]
    public void ACreaturesTimedStatesLandOnTheBody()
    {
        var (world, game, _) = Build();

        game.MapNpcs[Map, 2].Num = 1;
        game.MapNpcs[Map, 2].X = 4;
        game.MapNpcs[Map, 2].Y = 4;

        var wolf = EntityHandle.ForNpc(Map, 2);
        world.SetEngaged(wolf, 10);
        world.SetMarked(wolf, 60);
        world.SetAggressor(wolf, 5);

        var body = game.MapNpcs[Map, 2];

        Assert.Multiple(() =>
        {
            Assert.That(body.CombatExpiresAt, Is.GreaterThan(Environment.TickCount64));
            Assert.That(body.MarkedUntilUtc, Is.GreaterThan(0L));
            Assert.That(body.AggressorUntil, Is.GreaterThan(Environment.TickCount64));

            // What the client is actually told, which is the half that was always "never".
            Assert.That(
                JoinLeaveSystem.BuildMapNpcs(game, Map).Npcs.Single(n => n.Slot == 2).MsSinceCombat,
                Is.Not.EqualTo(int.MaxValue));
        });

        world.SetEngaged(wolf, 0);
        Assert.That(game.MapNpcs[Map, 2].CombatExpiresAt, Is.Zero, "zero seconds clears it");
    }

    /// <summary>⚠ Downed stays a player's state. A creature that runs out of health despawns and its
    /// slot counts down to a respawn, which the spawn clock owns — a second answer to "when does it
    /// come back" would be two clocks disagreeing.</summary>
    [Test]
    public void ACreatureIsNotDowned()
    {
        var (world, game, _) = Build();

        game.MapNpcs[Map, 3].Num = 1;
        world.SetDowned(EntityHandle.ForNpc(Map, 3), 30);

        Assert.That(game.MapNpcs[Map, 3].SpawnWait, Is.Zero, "nothing was touched");
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
            Assert.That(pm[Idx].Char.Downed, Is.True);
            Assert.That(pm[Idx].Char.RespawnReadyUtc, Is.GreaterThan(0));
        });

        world.SetDowned(Me, 0);
        Assert.Multiple(() =>
        {
            Assert.That(pm[Idx].Char.Downed, Is.False);
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
                        "the client is told the record's copy");
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

    /// <summary>
    /// 🔴 The one chat path that carries text rather than a key.
    ///
    /// <para>Every other line the server says is looked up per recipient, so the engine's own words
    /// arrive in each player's language. A game's words are not in that table and cannot be added to it,
    /// so they travel as written, and a rule can say anything at all.</para>
    /// </summary>
    [Test]
    public void AGameCanSaySomethingToOnePlayer()
    {
        var (world, _, _, sent) = BuildHeard();

        world.Tell(Me, "You are too tired to go on.");

        var said = sent.Sent.Select(s => s.Packet).OfType<ChatMsgPacket>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(said, Has.Count.EqualTo(1));
            Assert.That(said[0].Msg, Is.EqualTo("You are too tired to go on."));
            Assert.That(sent.Sent[0].Index, Is.EqualTo(Idx), "to that player and nobody else");
        });
    }

    [Test]
    public void AGameChoosesTheChannelAndTheColor()
    {
        var (world, _, _, sent) = BuildHeard();

        world.Tell(Me, "A storm is coming.", ChatChannels.System, GameColor.BrightCyan);

        var said = sent.Sent.Select(s => s.Packet).OfType<ChatMsgPacket>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(said.Channel, Is.EqualTo(ChatChannels.System));
            Assert.That(said.Color, Is.EqualTo(GameColor.BrightCyan));
        });
    }

    /// <summary>Like everything else here, saying something to a body that is not there does nothing.</summary>
    [Test]
    public void SayingSomethingToNobody_DoesNothing()
    {
        var (world, _, _, sent) = BuildHeard();

        world.Tell(Nobody, "Are you there?");
        world.Tell(Me, "");

        Assert.That(sent.Sent, Is.Empty);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class NoOpDispatcher : IPacketDispatcher
    {
        /// <summary>What was sent to whom, so a test can read back what a player was told.</summary>
        public List<(int Index, IPacket Packet)> Sent { get; } = [];

        public void SendTo(int index, IPacket packet) => Sent.Add((index, packet));
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

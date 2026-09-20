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
/// What a game may ask about the ground it is standing on.
///
/// <para>🔴 <b>A game is handed squares constantly</b> — a verb was used on one, a body is standing on
/// one — and every one of these is the engine answering a question it already answers for itself a
/// hundred times a tick. A rule about range, about sight, or about what is underfoot had nothing to
/// start from.</para>
///
/// <para>⚠ The two that matter most are <see cref="IWorld.Distance"/> and <see cref="IWorld.CanSee"/>,
/// because both are things a script would otherwise reimplement badly: subtraction calls a body one
/// tile over a border unreachable, and a hand-rolled sight trace disagrees with the arrow the player
/// was shown.</para>
/// </summary>
[TestFixture]
public class WorldQuestionTests
{
    const int Map = 1, Idx = 1;

    [Test]
    public void TheGroundSaysWhatKindOfTileItIs()
    {
        var (world, game, _) = Build();
        game.Maps[Map].Tile[3, 3] = new TileRecord { Type = TileType.Blocked };
        game.Maps[Map].Tile[4, 3] = new TileRecord { Type = TileType.Warp };

        Assert.Multiple(() =>
        {
            Assert.That(world.TileAt(new WorldPlace(Map, 3, 3)), Is.EqualTo("blocked"));
            Assert.That(world.TileAt(new WorldPlace(Map, 4, 3)), Is.EqualTo("warp"));
            Assert.That(world.TileAt(new WorldPlace(Map, 5, 5)), Is.EqualTo("walkable"));
            Assert.That(world.TileAt(new WorldPlace(Map, 99, 99)), Is.Empty, "that is not a square");

            // ⚠ A world holds a FIXED number of map slots and every one is a real map — an unauthored
            // one is blank rather than absent. So naming no map means naming a number the world does
            // not have, and nothing else does.
            Assert.That(world.TileAt(new WorldPlace(7, 1, 1)), Is.EqualTo("walkable"),
                "a blank map slot is an empty map, not a missing one");
            Assert.That(world.TileAt(new WorldPlace(game.Limits.Maps + 1, 1, 1)), Is.Empty,
                "and this world has no such map at all");
        });
    }

    /// <summary>⚠ A wall stops sight only if it was authored to. A railing or a window is blocked to walk
    /// through and clear to see through, and a rule that treated every wall as opaque would refuse casts
    /// the player can see landing.</summary>
    [Test]
    public void SightStopsAtAWallThatWasAuthoredToStopIt()
    {
        var (world, game, _) = Build();

        var from = new WorldPlace(Map, 2, 5);
        var to = new WorldPlace(Map, 8, 5);

        Assert.That(world.CanSee(from, to), Is.True, "nothing is in the way yet");

        game.Maps[Map].Tile[5, 5] = new TileRecord { Type = TileType.Blocked, BlocksSight = true };
        Assert.That(world.CanSee(from, to), Is.False);

        game.Maps[Map].Tile[5, 5] = new TileRecord { Type = TileType.Blocked, BlocksSight = false };
        Assert.That(world.CanSee(from, to), Is.True, "a railing is walked around and seen over");
    }

    /// <summary>🔴 <b>The world scrolls contiguously, so a body one tile over a border is one tile
    /// away.</b> Arithmetic on the coordinates alone says it is on another map and unreachable, which is
    /// the shape every range rule gets wrong.</summary>
    [Test]
    public void DistanceCountsAcrossAMapBorder()
    {
        var (world, game, _) = Build();

        // A second map hung off the first's right edge, which joins the two into one world.
        game.Maps[2] = OpenMap();
        game.Maps[Map].Right = 2;
        game.Maps[2].Left = Map;

        int across = world.Distance(new WorldPlace(Map, 15, 5), new WorldPlace(2, 0, 5));

        Assert.Multiple(() =>
        {
            Assert.That(world.Distance(new WorldPlace(Map, 2, 2), new WorldPlace(Map, 5, 6)),
                Is.EqualTo(7), "three across and four down");
            Assert.That(across, Is.EqualTo(1), "one tile over the seam is one tile away");
            Assert.That(world.Distance(new WorldPlace(Map, 1, 1), new WorldPlace(9, 1, 1)),
                Is.EqualTo(-1), "and a map that is not there cannot be compared");
        });
    }

    [Test]
    public void TheSkySaysWhatItIsDoing()
    {
        var (world, game, _) = Build();

        Assert.That(world.WeatherOn(Map), Is.EqualTo("clear"));

        game.Weather = WeatherType.Snow;

        Assert.Multiple(() =>
        {
            Assert.That(world.WeatherOn(Map), Is.EqualTo("snow"));

            // ⚠ One sky over the whole world. A game asks per map because this question will always
            // have that shape; today every map gets the same answer.
            Assert.That(world.WeatherOn(9), Is.EqualTo("snow"));
            Assert.That(world.WeatherOn(game.Limits.Maps + 1), Is.Empty, "there is no such map");
        });
    }

    /// <summary>Whether a body is running, and so whether running costs anything.</summary>
    [Test]
    public void ABodySaysWhetherItIsRunning()
    {
        var (world, _, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        Assert.That(world.IsRunning(who), Is.False);

        pm[Idx].Char.Moving = MovementType.Running;

        Assert.That(world.IsRunning(who), Is.True);
    }

    /// <summary>🔴 <b>A game that hands somebody a sword has no other way to put it in their hand.</b>
    /// The engine already knows how to wear things and which slot each item names; what it had no way to
    /// hear was a rule asking for it.</summary>
    [Test]
    public void ARuleCanPutSomethingOn()
    {
        var (world, game, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        game.Items[3].Type = ItemType.Equipment;
        game.Items[3].EquipSlot = "hand";
        pm[Idx].Char.Inv[4].Num = 3;
        pm[Idx].Char.Inv[4].Quantity = 1;

        Assert.That(world.Wear(who, 3), Is.True);
        Assert.That(pm[Idx].Char.EquippedIn("hand"), Is.EqualTo(4), "worn out of the bag slot it sits in");

        // ⚠ Asking for something to be ON must never be the thing that takes it off.
        Assert.That(world.Wear(who, 3), Is.True, "already worn is not a failure");
        Assert.That(pm[Idx].Char.EquippedIn("hand"), Is.EqualTo(4), "and it is still on");
    }

    /// <summary>And take it off again. It stays in the bag — a rule about ruined gear takes a piece
    /// out of use without destroying it.</summary>
    [Test]
    public void ARuleCanTakeSomethingOff()
    {
        var (world, game, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        game.Items[3].Type = ItemType.Equipment;
        game.Items[3].EquipSlot = "hand";
        pm[Idx].Char.Inv[4].Num = 3;
        pm[Idx].Char.Inv[4].Quantity = 1;
        world.Wear(who, 3);

        Assert.Multiple(() =>
        {
            Assert.That(world.Remove(who, 3), Is.True);
            Assert.That(pm[Idx].Char.EquippedIn("hand"), Is.Zero, "nothing is in the slot");
            Assert.That(pm[Idx].Char.Inv[4].Num, Is.EqualTo(3), "and it is still in the bag");

            Assert.That(world.Remove(who, 3), Is.False, "taking off what is not on is not a thing");
            Assert.That(world.Remove(who, 9), Is.False, "nor is one they never had");
        });
    }

    [Test]
    public void NothingIsWornThatIsNotThereToWear()
    {
        var (world, game, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        game.Items[3].Type = ItemType.Equipment;
        game.Items[3].EquipSlot = "hand";
        game.Items[5].Type = ItemType.Equipment;
        game.Items[5].EquipSlot = "saddle";       // a slot this world does not declare
        pm[Idx].Char.Inv[4].Num = 5;

        Assert.Multiple(() =>
        {
            Assert.That(world.Wear(who, 3), Is.False, "the bag does not hold one");
            Assert.That(world.Wear(who, 5), Is.False, "and this world has nowhere to put that");
        });
    }

    /// <summary>Core spends durability on its own — a swing wears a weapon, a shop restores it — and a
    /// game that puts a cost on dying could reach none of it.</summary>
    [Test]
    public void WornGearCanBeReadAndWornOut()
    {
        var (world, game, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        game.Items[3].Type = ItemType.Equipment;
        game.Items[3].EquipSlot = "hand";
        game.Items[3].Durability = 100;
        pm[Idx].Char.Inv[4].Num = 3;
        pm[Idx].Char.Inv[4].Dur = 80;
        pm[Idx].Char.SetEquipped("hand", 4);

        Assert.Multiple(() =>
        {
            Assert.That(world.WornBy(who), Is.EqualTo(new[] { 3 }));
            Assert.That(world.DurabilityOf(who, 3), Is.EqualTo((80, 100)));
            Assert.That(world.Wear(who, 3, 30), Is.EqualTo(30));
            Assert.That(world.DurabilityOf(who, 3).Left, Is.EqualTo(50));
        });

        // Never past nothing, and the answer is how much was actually taken.
        Assert.That(world.Wear(who, 3, 999), Is.EqualTo(50), "only what was left");
        Assert.That(world.DurabilityOf(who, 3).Left, Is.Zero);
    }

    /// <summary>A copy in the bag is not a copy being worn. Two of the same item are two different
    /// amounts of wear, and a rule about what a death cost means the one that was on them.</summary>
    [Test]
    public void OnlyTheWornCopyIsReachable()
    {
        var (world, game, pm) = Build();
        var who = EntityHandle.ForPlayer(Idx);

        game.Items[3].Type = ItemType.Equipment;
        game.Items[3].EquipSlot = "hand";
        game.Items[3].Durability = 100;
        pm[Idx].Char.Inv[4].Num = 3;
        pm[Idx].Char.Inv[4].Dur = 90;

        Assert.Multiple(() =>
        {
            Assert.That(world.WornBy(who), Is.Empty, "carried, not worn");
            Assert.That(world.DurabilityOf(who, 3), Is.EqualTo((0, 0)));
            Assert.That(world.Wear(who, 3, 10), Is.Zero);
            Assert.That(pm[Idx].Char.Inv[4].Dur, Is.EqualTo(90), "and the carried one is untouched");
        });
    }

    /// <summary>The engine's own repair rate, so a game pricing wear agrees with what the shop
    /// charges.</summary>
    [Test]
    public void RepairCostIsTheEnginesOwnRate()
    {
        var (world, game, _) = Build();

        game.Items[3].Durability = 100;
        game.Items[3].Price = 400;
        game.Prices = new GamePrices { RepairPercent = 20 };

        Assert.Multiple(() =>
        {
            Assert.That(world.RepairCost(3, 20),
                Is.EqualTo(game.Prices.RepairCost(20, game.Items[3])));
            Assert.That(world.RepairCost(3, 0), Is.Zero, "nothing to repair costs nothing");
            Assert.That(world.RepairCost(game.Limits.Items + 1, 20), Is.Zero,
                "and this world has no such item");
        });
    }

    /// <summary>A group is the engine's idea of a region. A game that owns regions turns where somebody
    /// is standing into which region it is.</summary>
    [Test]
    public void AMapSaysWhichRegionItIsIn()
    {
        var (world, game, _) = Build();
        game.Maps[Map].MapGroup = 4;

        Assert.Multiple(() =>
        {
            Assert.That(world.MapGroupOf(Map), Is.EqualTo(4));
            Assert.That(world.MapGroupOf(2), Is.Zero, "a map in no group");
            Assert.That(world.MapGroupOf(999), Is.Zero, "and one that is not there");
        });
    }

    /// <summary>What a game hangs on a region, read back the way every other record is.</summary>
    [Test]
    public void AGamesOwnFieldsOnARegionAreReadBack()
    {
        var (world, game, _) = Build();
        game.MapGroups[2] = new MapGroupRecord { Index = 2, Name = "The Marches" };
        game.MapGroups[2].Attributes.Set("holder", AttributeValue.From(7L));

        Assert.That(world.RecordAt("MapGroups", 2)!.TryGet("holder", out AttributeValue held) ? held.AsLong() : 0L,
            Is.EqualTo(7L));
    }

    /// <summary>🔴 <b>A game's fields on a MAP inherit from its region, the same way the engine's own
    /// map properties do.</b> A dungeon says once, on the group, that it is somewhere you can be
    /// attacked, and every map in it answers that — which is the whole reason a group exists.</summary>
    [Test]
    public void AGamesOwnFieldsOnAMapFallBackToItsRegion()
    {
        var (world, game, _) = Build();
        game.MapGroups[2] = new MapGroupRecord { Index = 2, Name = "The Deeps" };
        game.MapGroups[2].Attributes.Set("moral", AttributeValue.From(1L));
        game.Maps[Map].MapGroup = 2;
        game.Maps[3].MapGroup = 2;
        game.Maps[3].Attributes.Set("moral", AttributeValue.From(2L));

        Assert.Multiple(() =>
        {
            Assert.That(world.MapValue(Map, "moral")?.AsLong(), Is.EqualTo(1L),
                "a map that says nothing takes its region's answer");
            Assert.That(world.MapValue(3, "moral")?.AsLong(), Is.EqualTo(2L),
                "and a map that says something outranks it");
            Assert.That(world.MapValue(2, "moral"), Is.Null, "a map in no group carries nothing");
            Assert.That(world.MapValue(game.Limits.Maps + 1, "moral"), Is.Null,
                "and this world has no such map at all");
        });
    }

    /// <summary>⚠ An editor authors the map's OWN bag, with nothing inherited in it — or saving one
    /// map would quietly write its region's answers onto it.</summary>
    [Test]
    public void AuthoringAMapSeesItsOwnFieldsAndNotItsRegions()
    {
        var (world, game, _) = Build();
        game.MapGroups[2] = new MapGroupRecord { Index = 2, Name = "The Deeps" };
        game.MapGroups[2].Attributes.Set("moral", AttributeValue.From(1L));
        game.Maps[Map].MapGroup = 2;

        Assert.That(world.RecordAt("Maps", Map)!.TryGet("moral", out _), Is.False);
    }

    /// <summary>A game writing one of its own fields at run time, on a record the ENGINE owns. The
    /// engine's own properties stay out of reach; a key it has never heard of has nothing to
    /// normalize.</summary>
    [Test]
    public void AGameWritesItsOwnFieldsOnAnEnginesRecord()
    {
        var (world, game, _) = Build();
        game.MapGroups[2] = new MapGroupRecord { Index = 2, Name = "The Marches" };

        Assert.Multiple(() =>
        {
            Assert.That(world.SetRecordValue("MapGroups", 2, "holder", AttributeValue.From(7L)), Is.True);
            Assert.That(game.MapGroups[2].Attributes["holder"].AsLong(), Is.EqualTo(7L));

            Assert.That(world.SetRecordValue("Maps", Map, "season", AttributeValue.From(3L)), Is.True);
            Assert.That(game.Maps[Map].Attributes["season"].AsLong(), Is.EqualTo(3L));

            Assert.That(world.SetRecordValue("Maps", game.Limits.Maps + 1, "season", AttributeValue.From(3L)),
                Is.False, "a slot that is not there is refused");
        });
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    static MapRecord OpenMap()
    {
        var map = new MapRecord(16, 12);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 12; y++)
                map.Tile[x, y] = new TileRecord { Type = TileType.Walkable };

        return map;
    }

    /// <summary>One open map, one player standing on it, and one place to wear something.</summary>
    static (IWorld World, GameWorld Game, PlayerManager Pm) Build()
    {
        var game = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(game, pm, dispatcher);
        var attributes = new AttributeSystem(game, pm, dispatcher);
        var items = new ItemSystem(game, pm, dispatcher, persistence: null!, bg: null!);
        var decals = new DecalSystem(game, dispatcher);
        var spawn = new SpawnSystem(game, pm, dispatcher, items);
        var deaths = new DeathSystem(game, pm, dispatcher, movement, spawn);
        var ai = new NpcAiSystem(game, pm, dispatcher, movement, spawn, items);

        game.Maps[Map] = OpenMap();
        game.EquipSlots = new EquipSlotSet([new EquipSlot { Key = "hand", LabelKey = "Hand" }]);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 5;

        var world = new ServerWorld(game, pm, attributes, deaths, movement, items,
                                    joinLeave: null!, decals, ai, guilds: null!, dispatcher);
        return (world, game, pm);
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

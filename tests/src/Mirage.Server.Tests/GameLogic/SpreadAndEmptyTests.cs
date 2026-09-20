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
/// Spots spread across a region, and a map kept clear of creatures.
///
/// <para>🔴 <b>Neither is something a script could do.</b> A region is thousands of tiles read one at a
/// time; the seam step, the walking distance and the connected stretch are all things the engine knows
/// only because it moves bodies across them. And nothing reached the spawner at all — a game could ask
/// for a creature to die and had no way to ask for a map to stay clear.</para>
/// </summary>
[TestFixture]
public class SpreadAndEmptyTests
{
    private const int Group = 3, Left = 1, Right = 2, Lonely = 4, NpcKind = 1;

    /// <summary>Two 16x12 maps of the same group, joined left-to-right and open all over.</summary>
    private static (GameWorld World, SpreadSystem Spread, SpawnSystem Spawns) Build()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);

        foreach (int m in (int[])[Left, Right, Lonely])
        {
            world.Maps[m] = new MapRecord(16, 12) { MapGroup = Group };
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 12; y++)
                    world.Maps[m].Tile[x, y] = new TileRecord { Type = TileType.Walkable };
        }

        world.Maps[Left].Right = Right;
        world.Maps[Right].Left = Left;

        world.Npcs[NpcKind].Name = "Bandit";
        world.Npcs[NpcKind].SpawnSecs = 1;

        return (world, new SpreadSystem(world, dispatcher), new SpawnSystem(world, pm, dispatcher, items));
    }

    // ── Spreading ─────────────────────────────────────────────────────────────

    [Test]
    public void SpotsComeBackFromTheRegion()
    {
        var (_, spread, _) = Build();

        var spots = spread.SpreadOver(Group, 2);

        Assert.That(spots, Has.Count.EqualTo(2));
        Assert.That(spots.Select(s => s.Map), Is.Unique, "one to a map while a map is still free");
    }

    [Test]
    public void AskingForNothing_AnswersWithNothing()
    {
        var (_, spread, _) = Build();

        Assert.That(spread.SpreadOver(Group, 0), Is.Empty);
    }

    [Test]
    public void ARegionNobodyAuthored_AnswersWithNothing()
    {
        var (_, spread, _) = Build();

        Assert.That(spread.SpreadOver(99, 3), Is.Empty);
    }

    /// <summary>Fewer than asked for is an ordinary answer: the region's largest walkable stretch had
    /// nowhere else to put one.</summary>
    [Test]
    public void MoreThanThereIsRoomFor_AnswersWithWhatThereWas()
    {
        var (_, spread, _) = Build();

        Assert.That(spread.SpreadOver(Group, 99), Has.Count.EqualTo(2),
            "the two maps of the region's largest walkable stretch, and nowhere else to put one");
    }

    /// <summary>⚠ <c>onlyWhere</c> names one of the GAME's own truth fields on Maps, and a
    /// spot goes only on a map carrying it — which is how a game keeps them off its towns.</summary>
    [Test]
    public void AFieldOfTheGamesOwn_DecidesWhichMapsMayHoldOne()
    {
        var (world, spread, _) = Build();

        world.Maps[Right].Attributes.Set("contested", AttributeValue.From(true));

        var spots = spread.SpreadOver(Group, 3, "contested");

        Assert.That(spots, Has.Count.EqualTo(1));
        Assert.That(spots[0].Map, Is.EqualTo(Right));
    }

    /// <summary>🔴 Every spot is reachable on foot from every other. One stranded across unwalkable
    /// ground belongs to whoever happens to be nearest and is never contested for.</summary>
    [Test]
    public void AMapCutOffFromTheRest_HoldsNoSpots()
    {
        var (world, spread, _) = Build();

        // The lonely map is in the group and linked to nothing, so it is its own smaller stretch.
        var spots = spread.SpreadOver(Group, 3);

        Assert.That(spots.Select(s => s.Map), Does.Not.Contain(Lonely));
        Assert.That(spots, Has.Count.EqualTo(2), "the joined pair, and not the island");
        Assert.That(world.Maps[Lonely].Left, Is.Zero, "nothing joins it, so it is an island");
    }

    /// <summary>🔴 <b>A square is four things.</b> A bridge and the water under it are one tile on three
    /// counts and two different places, so a spot carries the plane it is on — and the walk reaches the
    /// deck, which is joined to the ground only at a ramp.</summary>
    [Test]
    public void ASpotCarriesThePlaneItIsOn()
    {
        var (_, spread, _) = Build();

        var spots = spread.SpreadOver(Group, 2);

        Assert.That(spots.Select(s => s.Layer), Is.All.EqualTo(WorldLayer.Ground),
            "a world with no decks in it puts everything on the ground");
    }

    // ── Emptying a map ────────────────────────────────────────────────────────

    private static void Standing(GameWorld world, int mapNum)
    {
        world.Maps[mapNum].Npcs.Add(new MapNpcEntry(NpcKind, PinX: 5, PinY: 5,
                                                    PinLayer: WorldLayer.Ground, PinDir: null));
        var mn = world.MapNpcs[mapNum, 1];
        mn.Num = NpcKind;
        mn.X = 5;
        mn.Y = 5;
    }

    [Test]
    public void EmptyingTakesEverythingOffTheMap()
    {
        var (world, _, spawns) = Build();
        Standing(world, Left);

        Assert.Multiple(() =>
        {
            Assert.That(spawns.Empty(Left), Is.True);
            Assert.That(world.MapNpcs[Left, 1].Num, Is.Zero);
            Assert.That(spawns.IsEmptied(Left), Is.True);
        });
    }

    /// <summary>🔴 Nothing comes back while a map is emptied. Without this the despawn is undone a minute
    /// later, one slot at a time, and the map a game emptied fills back up while it is still being fought
    /// over.</summary>
    [Test]
    public void NothingRespawnsOntoAnEmptiedMap()
    {
        var (world, _, spawns) = Build();
        Standing(world, Left);
        spawns.Empty(Left);

        spawns.CheckNpcRespawn(Left, Environment.TickCount64 + 10_000);
        spawns.SpawnMapNpcs(Left);
        spawns.SpawnNpc(1, Left);

        Assert.That(world.MapNpcs[Left, 1].Num, Is.Zero, "no route back on is open, not just the clock");
    }

    /// <summary>Refilling puts a map's own back at once rather than leaving it bare until each slot's clock
    /// comes round.</summary>
    [Test]
    public void RefillingPutsThemBackAtOnce()
    {
        var (world, _, spawns) = Build();
        Standing(world, Left);
        spawns.Empty(Left);

        Assert.Multiple(() =>
        {
            Assert.That(spawns.Refill(Left), Is.True);
            Assert.That(spawns.IsEmptied(Left), Is.False);
            Assert.That(world.MapNpcs[Left, 1].Num, Is.EqualTo(NpcKind));
        });
    }

    [Test]
    public void EmptyingWhatIsAlreadyEmpty_IsAnOrdinaryNo()
    {
        var (world, _, spawns) = Build();
        Standing(world, Left);

        Assert.Multiple(() =>
        {
            Assert.That(spawns.Empty(Left), Is.True);
            Assert.That(spawns.Empty(Left), Is.False);
            Assert.That(spawns.Refill(Right), Is.False, "and refilling one that was never emptied");
        });
    }

    [Test]
    public void AMapThatIsNotThere_CannotBeEmptied()
    {
        var (_, _, spawns) = Build();

        Assert.That(spawns.Empty(999_999), Is.False);
    }

    /// <summary>⚠ One map at a time. A game emptying a region says so map by map, because which maps make
    /// up a place is the game's own answer — the engine's map groups are one way to say it and not the
    /// only one.</summary>
    [Test]
    public void EmptyingOneMapLeavesItsNeighborAlone()
    {
        var (world, _, spawns) = Build();
        Standing(world, Left);
        Standing(world, Right);

        spawns.Empty(Left);

        Assert.That(world.MapNpcs[Right, 1].Num, Is.EqualTo(NpcKind));
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

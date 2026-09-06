using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// Where a RANDOM spawn is allowed to put a body on the upper plane.
///
/// <para>The rule is one question — is this tile a DECK, joined to a RAMP — asked of the whole world at
/// once (<see cref="GameWorld.IsFringeSpawnable"/>).</para>
///
/// <para>🔴 The fringe plane is walkable BY DEFAULT — <see cref="LayerLogic.AttrFor"/> reads it as Walkable
/// wherever no fringe attribute says otherwise — so "not blocked up top" is true of open sky over almost
/// every tile of every map. A search that asked only that would scatter mobs across the whole sky, outside
/// the railings that bound the deck and with no way down. The deck is the surface, and that is what keeps a
/// spawn inside the barriers without anybody painting NpcAvoid over every empty tile of the map.</para>
///
/// <para>🔴 And the joining cannot be asked one map at a time: the plane runs on through a seam exactly like
/// the ground does, so a deck can begin on one map and the ramp that reaches it stand on another.</para>
///
/// <para>A PINNED entry is exempt throughout — an author naming a tile and a plane has said what they
/// meant. See <see cref="NpcSpawnPlacementTests"/>.</para>
/// </summary>
[TestFixture]
public class NpcFringeSpawnTests
{
    const int Map = 1, NpcNum = 1, Deck = 303;

    /// <summary>Replays a fixed sequence, so a test can put the search exactly where it wants it.</summary>
    sealed class Rolls : IRandomSource
    {
        private readonly Queue<int> _q;
        public Rolls(params int[] rolls) => _q = new Queue<int>(rolls);
        private int Take() => _q.Count > 0 ? _q.Dequeue() : 0;
        public int Next(int maxExclusive) => Take() % Math.Max(1, maxExclusive);
        public int Next(int minInclusive, int maxExclusive) => minInclusive + Next(maxExclusive - minInclusive);
        public long NextInt64(long minInclusive, long maxExclusive) => minInclusive;
        public double NextDouble() => 0d;
    }

    static GameWorld NewWorld()
    {
        var world = new GameWorld();
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, PinX: null, PinY: null));
        return world;
    }

    /// <summary>Deck art on a run of tiles — the walkable top of a bridge.</summary>
    static void PaintDeck(GameWorld world, int y, int fromX, int toX) => PaintDeckOn(world, Map, y, fromX, toX);

    /// <summary>The one thing that joins the planes.</summary>
    static void PlaceRamp(GameWorld world, int mapNum, int x, int y)
    {
        var map = world.Maps[mapNum];
        map.Tile[x, y] = map.Tile[x, y] with
        {
            FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down },
        };
        world.InvalidateFringeReach();
    }

    static List<MapNpcRecord> SpawnMany(GameWorld world, int times)
    {
        var pm = new PlayerManager();
        var spawn = new SpawnSystem(world, pm, new NoOpDispatcher());
        var seen = new List<MapNpcRecord>();
        for (int i = 0; i < times; i++)
        {
            spawn.SpawnNpc(1, Map);
            var mn = world.MapNpcs[Map, 1];
            seen.Add(new MapNpcRecord { X = mn.X, Y = mn.Y, Layer = mn.Layer });
            mn.Num = 0;   // free the post so the next call re-rolls a placement
        }
        return seen;
    }

    [Test]
    public void ADeckWithARamp_CanTakeASpawn()
    {
        var world = NewWorld();
        PaintDeck(world, y: 4, fromX: 6, toX: 9);
        PlaceRamp(world, Map, 7, 5);   // directly under the deck row: you step off the ramp onto it

        // dir, then per attempt: layer (0 = the fringe), x, y.
        var pm = new PlayerManager();
        new SpawnSystem(world, pm, new NoOpDispatcher(), new Rolls(0, 0, 8, 4)).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.Multiple(() =>
        {
            Assert.That(mn.Layer, Is.EqualTo(WorldLayer.Fringe), "the deck is reachable, so it is a place to stand");
            Assert.That((mn.X, mn.Y), Is.EqualTo((8, 4)));
        });
    }

    [Test]
    public void EveryFringeSpawn_LandsOnAuthoredDeck()
    {
        var world = NewWorld();
        PaintDeck(world, y: 4, fromX: 6, toX: 9);
        PlaceRamp(world, Map, 7, 5);   // directly under the deck row: you step off the ramp onto it

        var placements = SpawnMany(world, 400);
        var upstairs = placements.Where(p => p.Layer == WorldLayer.Fringe).ToList();

        Assert.That(upstairs, Is.Not.Empty, "a reachable deck should take spawns at all");
        Assert.That(upstairs.All(p => p.Y == 4 && p.X >= 6 && p.X <= 9), Is.True,
            "a body on the upper plane stood somewhere with no deck under it: "
            + string.Join(" ", upstairs.Where(p => p.Y != 4 || p.X < 6 || p.X > 9).Select(p => $"({p.X},{p.Y})")));
    }

    [Test]
    public void ADeckWithNoRamp_TakesNoSpawns()
    {
        var world = NewWorld();
        PaintDeck(world, y: 4, fromX: 6, toX: 9);   // a surface, but nothing joins the planes

        Assert.That(SpawnMany(world, 400).Any(p => p.Layer == WorldLayer.Fringe), Is.False,
            "a mob up on a deck nobody can climb to is a mob nobody can fight");
    }

    [Test]
    public void ARampWithNoDeck_TakesNoSpawns()
    {
        var world = NewWorld();
        PlaceRamp(world, Map, 7, 5);   // the plane is walkable by default, but there is nothing to stand on

        Assert.That(SpawnMany(world, 400).Any(p => p.Layer == WorldLayer.Fringe), Is.False,
            "the open sky is walkable up top; without deck art it is not a surface");
    }

    [Test]
    public void AMapWithNoUpperPlaneAtAll_SpawnsOnTheGroundAsBefore()
    {
        var world = NewWorld();

        Assert.That(SpawnMany(world, 200).All(p => p.Layer == WorldLayer.Ground), Is.True,
            "a ground-only map must behave exactly as it did");
    }

    // ── The mount axis ───────────────────────────────────────────────────────────
    // 🔴 A ramp is a CORRIDOR, not a doorway: its sides are a wall on both planes (LayerLogic.CanEnter), so
    // the only way off the top of one is up-ramp. A deck merely TOUCHING a ramp's flank is not reachable by
    // it, and marking it so would put mobs somewhere a player cannot climb.

    [Test]
    public void ADeckBesideARampsFlank_IsNotReachedByIt()
    {
        var world = NewWorld();
        // The ramp's ground side is Down, so it is mounted from below and stepped off UPWARD. This deck sits
        // to its left — touching it, and no way onto it.
        PlaceRamp(world, Map, 7, 5);
        PaintDeck(world, y: 5, fromX: 4, toX: 6);

        Assert.That(world.IsFringeSpawnable(Map, 6, 5), Is.False,
            "stepping sideways off a ramp is blocked on both planes");
        Assert.That(SpawnMany(world, 400).Any(p => p.Layer == WorldLayer.Fringe), Is.False);
    }

    [Test]
    public void TheDeckOffARampsTop_IsReachedByIt()
    {
        var world = NewWorld();
        PlaceRamp(world, Map, 7, 5);
        PaintDeck(world, y: 4, fromX: 4, toX: 9);   // up-ramp from a Down-mounted ramp

        Assert.That(world.IsFringeSpawnable(Map, 7, 4), Is.True, "off the top is the way onto the deck");
        Assert.That(world.IsFringeSpawnable(Map, 4, 4), Is.True, "and along it from there");
    }

    [Test]
    public void TheDeckAtARampsFoot_IsNotOnTheFringe()
    {
        var world = NewWorld();
        PlaceRamp(world, Map, 7, 5);
        PaintDeck(world, y: 4, fromX: 6, toX: 8);   // the real deck, up-ramp
        PaintDeck(world, y: 6, fromX: 6, toX: 8);   // art at the FOOT, below the ramp

        Assert.That(world.IsFringeSpawnable(Map, 7, 6), Is.False,
            "stepping down the ramp toward its ground side leaves the fringe plane — that is the way DOWN");
    }

    // ── Across the seam ──────────────────────────────────────────────────────────
    // The fringe plane runs on through a seam exactly like the ground does, so a deck can begin on one map
    // and the ramp that reaches it stand on another, a series of decks along. Asking one map at a time gets
    // BOTH answers wrong: it refuses every map in such a chain but the one holding the ramp, and it accepts
    // a stranded deck on a map that happens to have a ramp somewhere else on it.

    /// <summary>Links Map (1) to its east neighbour (2), both ways, so the seam is real.</summary>
    static void LinkEast(GameWorld world, int west, int east)
    {
        world.Maps[west].Right = east;
        world.Maps[east].Left = west;
        world.InvalidateFringeReach();
    }

    static void PaintDeckOn(GameWorld world, int mapNum, int y, int fromX, int toX)
    {
        var map = world.Maps[mapNum];
        for (int x = fromX; x <= toX; x++)
            map.Tile[x, y] = map.Tile[x, y].WithArt(LayerType.Fringe, [Deck]);
        world.InvalidateFringeReach();
    }

    [Test]
    public void ADeckReachedByARampOnTheNextMap_TakesSpawns()
    {
        var world = NewWorld();
        // The deck runs the full width of map 1 and on across the seam into map 2, where the ramp stands.
        PaintDeckOn(world, 1, y: 4, fromX: 0, toX: 15);
        PaintDeckOn(world, 2, y: 4, fromX: 0, toX: 15);
        PlaceRamp(world, 2, 7, 5);
        LinkEast(world, 1, 2);

        Assert.That(world.HasSpawnableFringe(1), Is.True,
            "map 1's deck is walked onto from map 2; the seam is not the end of the plane");
        Assert.That(SpawnMany(world, 400).Any(p => p.Layer == WorldLayer.Fringe), Is.True);
    }

    [Test]
    public void ADeckChainWithNoRampAnywhere_TakesNoSpawns()
    {
        var world = NewWorld();
        PaintDeckOn(world, 1, y: 4, fromX: 0, toX: 15);
        PaintDeckOn(world, 2, y: 4, fromX: 0, toX: 15);
        LinkEast(world, 1, 2);

        Assert.That(SpawnMany(world, 400).Any(p => p.Layer == WorldLayer.Fringe), Is.False,
            "a chain of decks with no way up is still a chain of decks with no way up");
    }

    [Test]
    public void ADeckStrandedOnAMapThatHasARampElsewhere_TakesNoSpawns()
    {
        var world = NewWorld();
        PaintDeck(world, y: 2, fromX: 1, toX: 4);    // joined to the ramp below it
        PlaceRamp(world, Map, 2, 3);
        PaintDeck(world, y: 9, fromX: 10, toX: 13);  // a second deck, joined to nothing

        var upstairs = SpawnMany(world, 400).Where(p => p.Layer == WorldLayer.Fringe).ToList();

        Assert.That(upstairs, Is.Not.Empty, "the reachable deck still takes spawns");
        Assert.That(upstairs.All(p => p.Y == 2), Is.True,
            "the map has a ramp, but not one that reaches THIS deck: "
            + string.Join(" ", upstairs.Where(p => p.Y != 2).Select(p => $"({p.X},{p.Y})")));
    }

    [Test]
    public void UnlinkingTheMaps_TakesTheDeckBackOut()
    {
        var world = NewWorld();
        PaintDeckOn(world, 1, y: 4, fromX: 0, toX: 15);
        PaintDeckOn(world, 2, y: 4, fromX: 0, toX: 15);
        PlaceRamp(world, 2, 7, 5);
        LinkEast(world, 1, 2);
        Assume.That(world.HasSpawnableFringe(1), Is.True);

        world.Maps[1].Right = 0;
        world.Maps[2].Left = 0;
        world.InvalidateFringeReach();

        Assert.That(world.HasSpawnableFringe(1), Is.False,
            "the answer is cached, so a link change has to drop it");
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

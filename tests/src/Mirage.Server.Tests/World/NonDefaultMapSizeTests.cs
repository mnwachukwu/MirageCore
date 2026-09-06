using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A world whose maps are NOT 16x12.
///
/// <para>Every other test in the suite runs at the default size, where a stride read off a constant and a
/// stride read off the map agree on every answer. So the whole suite passing says nothing about whether
/// the engine measures in the map's own units — this fixture is the only thing that does. The maps here
/// are 24x20 precisely because both numbers differ from the default and from each other, so an axis
/// swapped for the other one shows up as a wrong answer rather than a coincidence.</para>
/// </summary>
[TestFixture]
public class NonDefaultMapSizeTests
{
    private const int W = 24, H = 20;
    private const int Center = 1, Right = 2, Down = 3;
    private const int Idx = 1;

    /// <summary>Three linked maps at 24x20: the center, its right neighbour, and the one below.</summary>
    private static GameWorld WideWorld()
    {
        var world = new GameWorld();
        foreach (int n in (int[])[Center, Right, Down])
            world.Maps[n] = new MapRecord(W, H);
        world.Maps[Center].Right = Right;
        world.Maps[Right].Left = Center;
        world.Maps[Center].Down = Down;
        world.Maps[Down].Up = Center;
        return world;
    }

    // ── The map knows its own size ────────────────────────────────────────────

    [Test]
    public void AMapReportsTheSizeItWasBuiltAt()
    {
        var map = new MapRecord(W, H);

        Assert.Multiple(() =>
        {
            Assert.That(map.Width, Is.EqualTo(W));
            Assert.That(map.Height, Is.EqualTo(H));
            Assert.That(map.Contains(W - 1, H - 1), Is.True, "the far corner is a tile");
            Assert.That(map.Contains(W, H - 1), Is.False, "one past the right edge is not");
            Assert.That(map.Contains(W - 1, H), Is.False, "one past the bottom edge is not");
        });
    }

    /// <summary>Every cell is addressable — the grid is filled, not just allocated.</summary>
    [Test]
    public void EveryTileOfAResizedMapExists()
    {
        var map = new MapRecord(W, H);

        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                Assert.That(map.Tile[x, y], Is.Not.Null, $"({x},{y})");
    }

    // ── World coordinates measure in the map's own units ──────────────────────

    [Test]
    public void TheGridTakesItsStrideFromTheCenterMap()
    {
        var grid = WorldCoordHelper.BuildMapGrid(WideWorld().Maps, Center);

        Assert.Multiple(() =>
        {
            Assert.That(grid.TilesX, Is.EqualTo(W));
            Assert.That(grid.TilesY, Is.EqualTo(H));
        });
    }

    /// <summary>The center map's (0,0) sits one whole map in on each axis, so neighbours fit on every side.
    /// At 24x20 that is (24,20) — the number a default-sized stride would get wrong on both axes.</summary>
    [Test]
    public void TheCenterMapsOriginIsOneMapInOnEachAxis()
    {
        var grid = WorldCoordHelper.BuildMapGrid(WideWorld().Maps, Center);

        Assert.Multiple(() =>
        {
            Assert.That(grid.CenterToWorld(0, 0), Is.EqualTo((W, H)));
            Assert.That(grid.ToWorld(1, 1, 0, 0), Is.EqualTo((W, H)), "the center cell and CenterToWorld agree");
            Assert.That(grid.ToWorld(2, 1, 0, 0), Is.EqualTo((2 * W, H)), "the right neighbour starts one map over");
            Assert.That(grid.ToWorld(1, 2, 0, 0), Is.EqualTo((W, 2 * H)), "the map below starts one map down");
        });
    }

    /// <summary>The tile just past the center map's right edge is the right neighbour's column 0 — the seam
    /// the whole scrolling world turns on.</summary>
    [Test]
    public void AWorldCoordinateResolvesAcrossTheSeamAtTheRightSize()
    {
        var grid = WorldCoordHelper.BuildMapGrid(WideWorld().Maps, Center);

        Assert.Multiple(() =>
        {
            Assert.That(grid.ResolveWorldTile(W + W - 1, H), Is.EqualTo((Center, W - 1, 0)), "the center's last column");
            Assert.That(grid.ResolveWorldTile(W + W, H), Is.EqualTo((Right, 0, 0)), "one further is the neighbour's first");
            Assert.That(grid.ResolveWorldTile(W, H + H), Is.EqualTo((Down, 0, 0)), "and downward likewise");
        });
    }

    [Test]
    public void ToWorldAndBackIsTheIdentityOnEveryCell()
    {
        var grid = WorldCoordHelper.BuildMapGrid(WideWorld().Maps, Center);

        Assert.Multiple(() =>
        {
            foreach (var (col, row, map) in ((int, int, int)[])[(1, 1, Center), (2, 1, Right), (1, 2, Down)])
            {
                foreach (var (x, y) in ((int, int)[])[(0, 0), (W - 1, H - 1), (7, 13)])
                {
                    var (wx, wy) = grid.ToWorld(col, row, x, y);
                    Assert.That(grid.ResolveWorldTile(wx, wy), Is.EqualTo((map, x, y)), $"cell ({col},{row}) tile ({x},{y})");
                }
            }
        });
    }

    // ── Gameplay reach does not grow with the map ─────────────────────────────

    /// <summary>The whole reason the viewport and the map size are separate constants. A 24x20 map is wider
    /// than the camera, and casting range must not notice.</summary>
    [Test]
    public void SpellRangeIsTheSameCircleOnALargerMap()
    {
        var grid = WorldCoordHelper.BuildMapGrid(WideWorld().Maps, Center);
        var (cx, cy) = grid.CenterToWorld(10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(WorldCoordHelper.IsInSpellRange(cx, cy, cx + Constants.SpellRangeTiles, cy), Is.True,
                "exactly r away is in range");
            Assert.That(WorldCoordHelper.IsInSpellRange(cx, cy, cx + Constants.SpellRangeTiles + 1, cy), Is.False,
                "one past r is out, on a map of any width");
            Assert.That(WorldCoordHelper.IsInSpellRange(cx, cy, cx, cy + Constants.SpellRangeTiles), Is.True);
            Assert.That(WorldCoordHelper.IsInSpellRange(cx, cy, cx, cy + Constants.SpellRangeTiles + 1), Is.False);
        });
    }

    // ── Movement uses the map's own edges ─────────────────────────────────────

    private static (GameWorld world, MovementSystem move, PlayerRecord p) Walker(GameWorld world, int x, int y)
    {
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var move = new MovementSystem(world, pm, dispatcher, new BloodSystem(world, dispatcher));
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Center;
        p.X = x;
        p.Y = y;
        p.MaxHp = 100;
        p.Hp = 100;
        world.MapObservers[Center].Add(Idx);
        return (world, move, p);
    }

    /// <summary>A step inside the map at a column the default size does not even have. At 16 wide this
    /// player would be standing off the map; at 24 they have room to walk.</summary>
    [Test]
    public void APlayerWalksPastWhereTheDefaultMapWouldHaveEnded()
    {
        var (_, move, p) = Walker(WideWorld(), Constants.MaxMapX, 5);

        move.PlayerMove(Idx, Direction.Right, MovementType.Walking);

        Assert.That((p.Map, p.X), Is.EqualTo((Center, Constants.MaxMapX + 1)), "a normal step, not an edge cross");
    }

    /// <summary>The seam is at the map's OWN last column, not the default's.</summary>
    [Test]
    public void TheEdgeCrossHappensAtTheMapsOwnLastColumn()
    {
        var (_, move, p) = Walker(WideWorld(), W - 1, 5);

        move.PlayerMove(Idx, Direction.Right, MovementType.Walking);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Right, 0, 5)), "crossed onto the neighbour's first column");
    }

    [Test]
    public void TheEdgeCrossDownwardHappensAtTheMapsOwnLastRow()
    {
        var (_, move, p) = Walker(WideWorld(), 5, H - 1);

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Down, 5, 0)));
    }

    /// <summary>Coming back the other way lands on the neighbour's own last column — which is what makes a
    /// seam crossing reversible at any size.</summary>
    [Test]
    public void CrossingBackLandsOnTheOtherMapsOwnLastColumn()
    {
        var world = WideWorld();
        var (_, move, p) = Walker(world, 0, 5);
        p.Map = Right;
        world.MapObservers[Right].Add(Idx);

        move.PlayerMove(Idx, Direction.Left, MovementType.Walking);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Center, W - 1, 5)));
    }

    // ── Warp bounds follow the map ────────────────────────────────────────────

    [Test]
    public void AWarpTargetIsJudgedAgainstTheMapsOwnSize()
    {
        var world = WideWorld();
        var (_, move, _) = Walker(world, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(move.IsWarpDestinationValid(Center, W - 1, H - 1), Is.True, "the far corner of a 24x20 map");
            Assert.That(move.IsWarpDestinationValid(Center, W, H - 1), Is.False, "one past its right edge");
            Assert.That(move.IsWarpDestinationValid(Center, W - 1, H), Is.False, "one past its bottom edge");
            // Map 4 was never resized, so it is still the default — the bound is per map, not per world.
            Assert.That(move.IsWarpDestinationValid(4, W - 1, H - 1), Is.False, "a default-sized map has no such tile");
            Assert.That(move.IsWarpDestinationValid(4, Constants.MaxMapX, Constants.MaxMapY), Is.True);
        });
    }

    /// <summary>A position off the end of a resized map comes back onto it, at ITS corner.</summary>
    [Test]
    public void ARememberedPositionIsRepairedToTheMapsOwnCorner()
    {
        var world = WideWorld();

        Assert.That(world.RepairPosition(Center, 999, 999, (Center, 0, 0)), Is.EqualTo((Center, W - 1, H - 1)));
    }

    // ── Harness ───────────────────────────────────────────────────────────────

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

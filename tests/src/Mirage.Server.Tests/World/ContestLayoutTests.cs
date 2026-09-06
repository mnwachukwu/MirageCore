using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>
/// The territory layout: every map of a contest placed on ONE tile grid.
///
/// <para>🔴 What it is for: a client holds only the nine maps around itself, so a capture point's map number
/// says nothing about where that point is. Without a layout an off-screen marker can only ever point at the
/// three or four flags already nearly in view, which is the opposite of what a marker is for.</para>
///
/// <para>Origins are in TILES, not map cells, because maps differ in size — a neighbour sits one map's WIDTH
/// away, not one cell.</para>
/// </summary>
[TestFixture]
public class ContestLayoutTests
{
    private static GuildTerritorySystem System(GameWorld world) =>
        new(world, new PlayerManager(), new SilentDispatcher(),
            guilds: null!, spawn: null!, persistence: null!, bg: null!,
            NullLogger<GuildTerritorySystem>.Instance);

    private static List<ContestMapView> Layout(GameWorld world, List<int> maps)
    {
        var m = typeof(GuildTerritorySystem).GetMethod("BuildTerritoryLayout",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<ContestMapView>)m.Invoke(System(world), new object?[] { maps })!;
    }

    private static (int X, int Y) OriginOf(List<ContestMapView> layout, int map)
    {
        var v = layout.Single(l => l.Map == map);
        return (v.OriginX, v.OriginY);
    }

    /// <summary>Maps 1..4 left to right, all default size.</summary>
    private static GameWorld Row()
    {
        var world = new GameWorld();
        for (int m = 1; m <= 4; m++)
        {
            if (m > 1) world.Maps[m].Left = m - 1;
            if (m < 4) world.Maps[m].Right = m + 1;
        }
        return world;
    }

    [Test]
    public void EachMapSitsOneMapWidthFromItsNeighbour()
    {
        var layout = Layout(Row(), [1, 2, 3, 4]);

        int w = Constants.DefaultMapWidth;
        Assert.Multiple(() =>
        {
            Assert.That(OriginOf(layout, 1), Is.EqualTo((0, 0)));
            Assert.That(OriginOf(layout, 2), Is.EqualTo((w, 0)));
            Assert.That(OriginOf(layout, 3), Is.EqualTo((w * 2, 0)));
            Assert.That(OriginOf(layout, 4), Is.EqualTo((w * 3, 0)));
        });
    }

    /// <summary>Up and Left go negative — the anchor map is not required to be the top-left one.</summary>
    [Test]
    public void WalkingUpAndLeftPlacesMapsAtNegativeOrigins()
    {
        var world = new GameWorld();
        world.Maps[1].Left = 2;
        world.Maps[2].Right = 1;
        world.Maps[1].Up = 3;
        world.Maps[3].Down = 1;

        var layout = Layout(world, [1, 2, 3]);

        Assert.Multiple(() =>
        {
            Assert.That(OriginOf(layout, 1), Is.EqualTo((0, 0)));
            Assert.That(OriginOf(layout, 2), Is.EqualTo((-Constants.DefaultMapWidth, 0)));
            Assert.That(OriginOf(layout, 3), Is.EqualTo((0, -Constants.DefaultMapHeight)));
        });
    }

    /// <summary>🔴 A neighbour is offset by the size of the map you STEP OFF, not by a fixed cell. A row of
    /// mixed-width maps is where a cell-based layout drifts, and the drift compounds along the row.</summary>
    [Test]
    public void AWiderMapPushesItsNeighbourFurther()
    {
        var world = Row();
        // Size IS the tile array's shape, so a wider map is a wider grid.
        world.Maps[1].Tile = TileGrid.Empty(40, Constants.DefaultMapHeight);

        var layout = Layout(world, [1, 2, 3]);

        Assert.Multiple(() =>
        {
            Assert.That(OriginOf(layout, 2).X, Is.EqualTo(40), "map 2 sits one map-1 width across");
            Assert.That(OriginOf(layout, 3).X, Is.EqualTo(40 + Constants.DefaultMapWidth),
                "and map 3 one map-2 width beyond that");
        });
    }

    /// <summary>A map the links never reach still gets placed, so a point on it is never simply lost.</summary>
    [Test]
    public void AnUnlinkedMapIsStillPlaced()
    {
        var world = Row();

        var layout = Layout(world, [1, 2, 3, 4, 9]);   // 9 links to nothing

        Assert.That(layout.Select(l => l.Map), Does.Contain(9));
    }

    /// <summary>Links leaving the territory are not followed: a layout describes the contested ground and
    /// nothing else, and a link outward would drag an unrelated map onto the grid.</summary>
    [Test]
    public void ALinkOutOfTheTerritoryIsNotFollowed()
    {
        var world = Row();

        var layout = Layout(world, [1, 2]);   // map 2 still links Right to 3, which is not in the territory

        Assert.That(layout, Has.Count.EqualTo(2));
        Assert.That(layout.Select(l => l.Map), Has.None.EqualTo(3));
    }
}

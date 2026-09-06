using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// The world's second set of connections.
///
/// <para>A warp is authored entirely on the departing map: the receiving map stores nothing at all about it.
/// That asymmetry is what these cover — reading the graph backwards so a destination can show what opens onto
/// it, and forwards so a map can say how many places it reaches that its grid neighbors do not.</para>
/// </summary>
[TestFixture]
public class WarpLinksTests
{
    private static MapRecord Blank(int up = 0, int down = 0, int left = 0, int right = 0) =>
        new() { Up = up, Down = down, Left = left, Right = right };

    // Puts a warp on the ground plane at (x,y) sending the player to (destX,destY) of destMap.
    private static MapRecord WithWarp(MapRecord map, int x, int y, int destMap, int destX, int destY)
    {
        map.Tile[x, y] = map.Tile[x, y].WithGroundAttr(new TileAttr
        {
            Type = TileType.Warp,
            WarpMap = (short)destMap,
            WarpX = (ushort)destX,
            WarpY = (ushort)destY,
            WarpLayer = WorldLayer.Ground,
        });
        return map;
    }

    private static MapRecord WithFringeWarp(MapRecord map, int x, int y, int destMap, int destX, int destY)
    {
        map.Tile[x, y] = map.Tile[x, y] with
        {
            FringeAttr = new FringeAttr
            {
                Type = TileType.Warp,
                WarpMap = (short)destMap,
                WarpX = (ushort)destX,
                WarpY = (ushort)destY,
                WarpLayer = WorldLayer.Fringe,
            },
        };
        return map;
    }

    // ── Outbound ──────────────────────────────────────────────────────────────

    /// <summary>Counted per destination MAP, not per warp tile. A doorway three tiles wide is one place, and
    /// counting tiles would report a building's threshold as three connections.</summary>
    [Test]
    public void ThreeWarpTilesToOneMapCountAsOneDestination()
    {
        var map = Blank();
        WithWarp(map, 1, 1, 40, 0, 0);
        WithWarp(map, 2, 1, 40, 1, 0);
        WithWarp(map, 3, 1, 40, 2, 0);

        Assert.That(WarpLinks.WarpOnlyDestinations(7, map), Is.EqualTo(new[] { 40 }));
    }

    /// <summary>A grid neighbor is excluded: it is drawn in the cell next door, so counting it would report
    /// as hidden a connection the reader can already see.</summary>
    [Test]
    public void AWarpToAGridNeighborIsNotCounted()
    {
        var map = Blank(right: 8);
        WithWarp(map, 1, 1, 8, 0, 0);
        WithWarp(map, 2, 1, 40, 0, 0);

        Assert.That(WarpLinks.WarpOnlyDestinations(7, map), Is.EqualTo(new[] { 40 }),
            "the neighbor drops out and the distant map stays");
    }

    /// <summary>A warp back into the same map goes nowhere new, so it is not a connection to anywhere.</summary>
    [Test]
    public void AWarpToItselfIsNotCounted()
    {
        var map = Blank();
        WithWarp(map, 1, 1, 7, 5, 5);

        Assert.That(WarpLinks.WarpOnlyDestinations(7, map), Is.Empty);
    }

    /// <summary>Warps on the fringe deck count as much as ground ones — both planes are real, and reading only
    /// the ground would undercount any map whose doorways are up top.</summary>
    [Test]
    public void FringeWarpsAreCountedToo()
    {
        var map = Blank();
        WithFringeWarp(map, 4, 4, 51, 1, 1);

        Assert.That(WarpLinks.WarpOnlyDestinations(7, map), Is.EqualTo(new[] { 51 }));
    }

    /// <summary>A map with no warps reports none, which is what keeps the badge off a clean map.</summary>
    [Test]
    public void AMapWithNoWarpsHasNoDestinations()
    {
        Assert.That(WarpLinks.WarpOnlyDestinations(7, Blank(up: 2, right: 3)), Is.Empty);
    }

    // ── Inbound ───────────────────────────────────────────────────────────────

    /// <summary>Several maps landing on one tile compound into a single arrival naming all of them. The tile
    /// is one doorway however many doors open onto it, and the marker has room for one badge.</summary>
    [Test]
    public void MapsLandingOnOneTileCompoundIntoOneArrival()
    {
        var a = WithWarp(Blank(), 0, 0, 7, 5, 5);
        var b = WithWarp(Blank(), 1, 1, 7, 5, 5);
        var c = WithWarp(Blank(), 2, 2, 7, 5, 5);

        var inbound = WarpLinks.InboundTo(7, [(3, a), (4, b), (5, c)]);

        Assert.That(inbound.Count, Is.EqualTo(1));
        Assert.That((inbound[0].X, inbound[0].Y), Is.EqualTo((5, 5)));
        Assert.That(inbound[0].SourceMaps, Is.EqualTo(new[] { 3, 4, 5 }));
        Assert.That(inbound[0].WarpCount, Is.EqualTo(3));
    }

    /// <summary>Two warps from the SAME map onto one tile are one source but two warps, so a marker can say
    /// "one map" while the read-out still accounts for both doors.</summary>
    [Test]
    public void TwoWarpsFromOneMapAreOneSourceButTwoWarps()
    {
        var a = Blank();
        WithWarp(a, 0, 0, 7, 5, 5);
        WithWarp(a, 1, 0, 7, 5, 5);

        var inbound = WarpLinks.InboundTo(7, [(3, a)]);

        Assert.That(inbound.Count, Is.EqualTo(1));
        Assert.That(inbound[0].SourceMaps, Is.EqualTo(new[] { 3 }));
        Assert.That(inbound[0].WarpCount, Is.EqualTo(2));
    }

    /// <summary>Arrivals on different tiles stay separate — they are different doorways, and merging them
    /// would put a marker where nobody lands.</summary>
    [Test]
    public void ArrivalsOnDifferentTilesStaySeparate()
    {
        var a = Blank();
        WithWarp(a, 0, 0, 7, 5, 5);
        WithWarp(a, 1, 0, 7, 2, 8);

        var inbound = WarpLinks.InboundTo(7, [(3, a)]);

        Assert.That(inbound.Count, Is.EqualTo(2));
        Assert.That(inbound.Select(w => (w.X, w.Y)), Is.EquivalentTo(new[] { (5, 5), (2, 8) }));
    }

    /// <summary>The destination PLANE separates arrivals too: a warp landing on the fringe deck and one
    /// landing on the ground at the same column are different places to stand.</summary>
    [Test]
    public void TheSameColumnOnTwoPlanesIsTwoArrivals()
    {
        var a = Blank();
        WithWarp(a, 0, 0, 7, 5, 5);
        a.Tile[1, 0] = a.Tile[1, 0] with
        {
            FringeAttr = new FringeAttr
            {
                Type = TileType.Warp, WarpMap = 7, WarpX = 5, WarpY = 5, WarpLayer = WorldLayer.Fringe,
            },
        };

        var inbound = WarpLinks.InboundTo(7, [(3, a)]);

        Assert.That(inbound.Count, Is.EqualTo(2));
        Assert.That(inbound.Select(w => w.Layer),
            Is.EquivalentTo(new[] { WorldLayer.Ground, WorldLayer.Fringe }));
    }

    /// <summary>Warps aimed at other maps are ignored, so a map's markers are its own arrivals and not the
    /// world's warp table.</summary>
    [Test]
    public void WarpsToOtherMapsAreNotReported()
    {
        var a = WithWarp(Blank(), 0, 0, 99, 1, 1);

        Assert.That(WarpLinks.InboundTo(7, [(3, a)]), Is.Empty);
    }

    /// <summary>A map's own warp back into itself still arrives, because somebody does land there.</summary>
    [Test]
    public void AMapsWarpIntoItselfIsAnArrival()
    {
        var self = WithWarp(Blank(), 0, 0, 7, 9, 9);

        var inbound = WarpLinks.InboundTo(7, [(7, self)]);

        Assert.That(inbound.Count, Is.EqualTo(1));
        Assert.That(inbound[0].SourceMaps, Is.EqualTo(new[] { 7 }));
    }

    /// <summary>Only the maps handed over are read. Online the editor holds a fraction of the world, and the
    /// answer has to be "what is knowable" rather than a claim that nothing arrives.</summary>
    [Test]
    public void OnlyTheMapsSuppliedAreScanned()
    {
        var seen = WithWarp(Blank(), 0, 0, 7, 5, 5);
        var unseen = WithWarp(Blank(), 0, 0, 7, 5, 5);

        var inbound = WarpLinks.InboundTo(7, [(3, seen)]);

        Assert.That(inbound.Single().SourceMaps, Is.EqualTo(new[] { 3 }));
        Assert.That(unseen, Is.Not.Null, "the unread map exists but was never offered");
    }

    /// <summary>Map 0 is the "no warp" sentinel on the destination field, so a blank warp is not an arrival
    /// anywhere and never marks map 0.</summary>
    [Test]
    public void ADestinationOfZeroIsNotAWarp()
    {
        var a = Blank();
        a.Tile[0, 0] = a.Tile[0, 0].WithGroundAttr(new TileAttr { Type = TileType.Warp, WarpMap = 0 });

        Assert.That(WarpLinks.Exits(a), Is.Empty);
        Assert.That(WarpLinks.InboundTo(0, [(3, a)]), Is.Empty);
    }
}

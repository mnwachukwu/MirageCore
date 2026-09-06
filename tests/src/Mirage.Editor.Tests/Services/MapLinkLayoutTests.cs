using Mirage.Editor.Services;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// Where each map lands on the cell grid.
///
/// <para>Maps carry no coordinates, so position exists only as a consequence of walking the Up/Down/Left/Right
/// links. That makes the walk the single point of failure for both callers that draw a world: the PNG export
/// and the World Preview window. These pin the three properties that keep it total on a world nobody promised
/// was well-formed — a cycle terminates, two maps never share a cell, and a missing map is a hole rather than
/// an exception — plus the radius bound the preview relies on to stay affordable.</para>
/// </summary>
[TestFixture]
public class MapLinkLayoutTests
{
    private static MapRecord Map(int up = 0, int down = 0, int left = 0, int right = 0) =>
        new() { Up = up, Down = down, Left = left, Right = right };

    // Fetch over a fixed table; an id with no entry reads as a map that does not exist.
    private static Func<int, ValueTask<MapRecord?>> From(Dictionary<int, MapRecord> world) =>
        id => new ValueTask<MapRecord?>(world.GetValueOrDefault(id));

    private static async Task<MapLinkLayoutResult> Flood(
        Dictionary<int, MapRecord> world, int origin = 1, int radius = 0) =>
        await MapLinkLayout.FloodAsync(origin, radius, From(world));

    private static (int X, int Y)? CellOf(MapLinkLayoutResult r, int mapNum) =>
        r.Placements.Where(p => p.MapNum == mapNum)
            .Select(p => ((int, int)?)(p.CellX, p.CellY)).FirstOrDefault();

    /// <summary>The origin anchors the grid at (0,0) even with nothing linked to it, because a one-map world
    /// is the state every new world starts in and the export must still produce an image.</summary>
    [Test]
    public async Task TheOriginIsPlacedEvenWithNoLinks()
    {
        var r = await Flood(new Dictionary<int, MapRecord> { [1] = Map() });

        Assert.That(r.Placements.Select(p => p.MapNum), Is.EqualTo(new[] { 1 }));
        Assert.That(CellOf(r, 1), Is.EqualTo((0, 0)));
        Assert.That(r.CellsWide, Is.EqualTo(1));
        Assert.That(r.CellsHigh, Is.EqualTo(1));
    }

    /// <summary>Each of the four links steps one cell in its own direction. Up is negative Y: the grid is
    /// screen-oriented, and an inverted axis would mirror every exported world vertically.</summary>
    [Test]
    public async Task EachLinkStepsOneCellInItsOwnDirection()
    {
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(up: 2, down: 3, left: 4, right: 5),
            [2] = Map(down: 1),
            [3] = Map(up: 1),
            [4] = Map(right: 1),
            [5] = Map(left: 1),
        });

        Assert.That(CellOf(r, 1), Is.EqualTo((0, 0)));
        Assert.That(CellOf(r, 2), Is.EqualTo((0, -1)));
        Assert.That(CellOf(r, 3), Is.EqualTo((0, 1)));
        Assert.That(CellOf(r, 4), Is.EqualTo((-1, 0)));
        Assert.That(CellOf(r, 5), Is.EqualTo((1, 0)));
    }

    /// <summary>A ring of maps linking back to the start terminates. Without the placed-once guard the walk
    /// would circle the cycle forever and hang the editor rather than fail.</summary>
    [Test]
    public async Task ACycleIsPlacedOnceAndTerminates()
    {
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(right: 2, down: 4),
            [2] = Map(left: 1, down: 3),
            [3] = Map(up: 2, left: 4),
            [4] = Map(up: 1, right: 3),
        });

        Assert.That(r.Placements.Count, Is.EqualTo(4));
        Assert.That(r.Placements.Select(p => p.MapNum).Distinct().Count(), Is.EqualTo(4));
        Assert.That(CellOf(r, 3), Is.EqualTo((1, 1)));
    }

    /// <summary>Two maps whose links aim them at the same cell resolve first-come. Overwriting instead would
    /// silently drop a map from an export; refusing to place either would punch a hole through a world whose
    /// only fault is one bad reciprocal link.</summary>
    [Test]
    public async Task TwoMapsClaimingOneCell_TheFirstKeepsIt()
    {
        // 2 sits right of 1. Both 2's Down and 3's Right aim at cell (1,1).
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(right: 2, down: 3),
            [2] = Map(left: 1, down: 4),
            [3] = Map(up: 1, right: 5),
            [4] = Map(up: 2),
            [5] = Map(left: 3),
        });

        // Which of the two arrives first is a consequence of the walk order and not a promise; that one
        // of them is turned away, and is not quietly relocated, is.
        var atCell = r.Placements.Where(p => (p.CellX, p.CellY) == (1, 1)).ToList();
        Assert.That(atCell.Count, Is.EqualTo(1), "a cell must hold at most one map");
        Assert.That(atCell[0].MapNum, Is.AnyOf(4, 5));
        int loser = atCell[0].MapNum == 4 ? 5 : 4;
        Assert.That(r.Placements.Any(p => p.MapNum == loser), Is.False, "the loser is not placed elsewhere");
    }

    /// <summary>A link pointing at a map that does not exist leaves a gap and keeps walking. The export draws
    /// that gap black; throwing instead would make one dangling link unexportable.</summary>
    [Test]
    public async Task AMissingMapLeavesAGapAndTheWalkContinues()
    {
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(right: 99, down: 3),
            [3] = Map(up: 1),
        });

        Assert.That(r.Placements.Select(p => p.MapNum), Is.EquivalentTo(new[] { 1, 3 }));
        Assert.That(CellOf(r, 3), Is.EqualTo((0, 1)));
    }

    // A straight west-to-east corridor: map 1 at the origin, each subsequent map one cell further right.
    private static Dictionary<int, MapRecord> Corridor(int length)
    {
        var world = new Dictionary<int, MapRecord>();
        for (int i = 1; i <= length; i++)
            world[i] = Map(left: i > 1 ? i - 1 : 0, right: i < length ? i + 1 : 0);
        return world;
    }

    /// <summary>The radius cuts the walk at exactly the requested distance. The control half matters as much
    /// as the bound: a flood that returned nothing at all would satisfy "the far map is absent" on its own.</summary>
    [Test]
    public async Task TheRadiusCutsAtExactlyItsOwnDistance()
    {
        var r = await Flood(Corridor(10), radius: 3);

        Assert.That(r.Placements.Any(p => p.MapNum == 4), Is.True, "the map ON the radius is drawn");
        Assert.That(r.Placements.Any(p => p.MapNum == 5), Is.False, "the map one step past it is not");
        Assert.That(r.Placements.Count, Is.EqualTo(4));
        Assert.That(r.TruncatedByRadius, Is.True);
    }

    /// <summary>The radius is Chebyshev, so the drawn region is a square box: a corner map at (r, r) is inside
    /// it. Under a Manhattan metric that corner sits at distance 2r and would vanish, turning the box into a
    /// diamond and making the map count unpredictable from the radius.</summary>
    [Test]
    public async Task TheRadiusIsASquareBoxNotADiamond()
    {
        // 1 -> 2 (right) -> 3 (down): 3 lands on the diagonal corner at (1, 1).
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(right: 2),
            [2] = Map(left: 1, down: 3),
            [3] = Map(up: 2),
        }, radius: 1);

        Assert.That(CellOf(r, 3), Is.EqualTo((1, 1)), "the corner of the box is inside a radius of 1");
        Assert.That(r.TruncatedByRadius, Is.False);
    }

    /// <summary>Truncation reports whether anything was actually cut, so the window can distinguish "this is
    /// the whole region" from "there is more you are not seeing".</summary>
    [Test]
    public async Task TruncatedIsSetOnlyWhenALinkWasActuallyCut()
    {
        Assert.That((await Flood(Corridor(4), radius: 10)).TruncatedByRadius, Is.False);
        Assert.That((await Flood(Corridor(4), radius: 2)).TruncatedByRadius, Is.True);
    }

    /// <summary>Radius 0 means unbounded, which is what the PNG export passes. Reading it as "a box of zero"
    /// would silently reduce every world export to its origin map.</summary>
    [Test]
    public async Task RadiusZeroFloodsTheWholeGraph()
    {
        var r = await Flood(Corridor(12), radius: 0);

        Assert.That(r.Placements.Count, Is.EqualTo(12));
        Assert.That(r.TruncatedByRadius, Is.False);
    }

    /// <summary>The bounding box spans the placed maps, and is what the export turns into an image size.
    /// A box measured from the origin instead would clip everything reached by walking up or left.</summary>
    [Test]
    public async Task TheBoxSpansEveryPlacedMap()
    {
        var r = await Flood(new Dictionary<int, MapRecord>
        {
            [1] = Map(left: 2, down: 3),
            [2] = Map(right: 1),
            [3] = Map(up: 1),
        });

        Assert.That((r.MinX, r.MaxX), Is.EqualTo((-1, 0)));
        Assert.That((r.MinY, r.MaxY), Is.EqualTo((0, 1)));
        Assert.That((r.CellsWide, r.CellsHigh), Is.EqualTo((2, 2)));
    }

    /// <summary>An origin that does not exist yields an empty layout rather than a one-cell box, so a caller
    /// sizing an image off it does not allocate a surface for a map it never read.</summary>
    [Test]
    public async Task AnUnreadableOriginPlacesNothing()
    {
        var r = await Flood(new Dictionary<int, MapRecord> { [2] = Map() }, origin: 1);

        Assert.That(r.Placements, Is.Empty);
        Assert.That((r.CellsWide, r.CellsHigh), Is.EqualTo((0, 0)));
    }

    /// <summary>Map number 0 is the "no map that way" sentinel on every link field, so it is never a map to
    /// walk to. Treating it as one would place a phantom map at the origin of every world.</summary>
    [Test]
    public async Task MapNumberZeroIsNeverPlaced()
    {
        var r = await Flood(new Dictionary<int, MapRecord> { [1] = Map(right: 0) }, origin: 0);

        Assert.That(r.Placements, Is.Empty);
    }

    /// <summary>Progress counts maps as they are read, so the online path can report a flood that is still
    /// fetching. It reports placed maps, not queued ones, so it never overstates what has arrived.</summary>
    [Test]
    public async Task ProgressCountsEachMapAsItIsRead()
    {
        var seen = new List<int>();
        await MapLinkLayout.FloodAsync(1, 0, From(Corridor(4)), seen.Add);

        Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    // A world of randomly-linked maps, reciprocal where it can be and contradictory where two maps happen
    // to claim the same neighbor. Deliberately not a well-formed world: the walk has to survive one.
    private static Dictionary<int, MapRecord> ScrambledWorld(int seed, int count)
    {
        var rng = new System.Random(seed);
        var world = new Dictionary<int, MapRecord>();
        for (int i = 1; i <= count; i++) world[i] = Map();
        foreach (var (num, map) in world)
        {
            if (rng.Next(4) > 0) map.Right = rng.Next(1, count + 1);
            if (rng.Next(4) > 0) map.Down = rng.Next(1, count + 1);
            if (map.Right > 0 && world.TryGetValue(map.Right, out var r)) r.Left = num;
            if (map.Down > 0 && world.TryGetValue(map.Down, out var d)) d.Up = num;
        }
        return world;
    }

    /// <summary>
    /// The properties the PNG export turns into an image, held over worlds nobody hand-checked.
    ///
    /// <para>The export sizes its surface from the bounding box and blits each map at its cell offset, so a
    /// duplicate cell would overwrite one map with another and a box that did not span every placement would
    /// clip whatever fell outside it. Both would show up only as a wrong picture, which is why they are
    /// asserted here rather than left to somebody comparing a ten-thousand-pixel PNG by eye.</para>
    /// </summary>
    [Test]
    public async Task EveryPlacementIsUniqueAndInsideTheBox([Range(1, 12)] int seed)
    {
        var r = await Flood(ScrambledWorld(seed, 25));

        Assert.That(r.Placements, Is.Not.Empty, "the origin at least is always placed");
        Assert.That(r.Placements.Select(p => (p.CellX, p.CellY)).Distinct().Count(),
            Is.EqualTo(r.Placements.Count), "no two maps share a cell");
        Assert.That(r.Placements.Select(p => p.MapNum).Distinct().Count(),
            Is.EqualTo(r.Placements.Count), "no map is placed twice");
        Assert.That(r.Placements.All(p =>
                p.CellX >= r.MinX && p.CellX <= r.MaxX && p.CellY >= r.MinY && p.CellY <= r.MaxY),
            "the box contains every placement");
        Assert.That(r.Placements.Any(p => p.CellX == r.MinX), "the box is tight on the left");
        Assert.That(r.Placements.Any(p => p.CellY == r.MinY), "the box is tight on the top");
    }
}

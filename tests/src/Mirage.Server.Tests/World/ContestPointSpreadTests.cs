using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Capture points spread by WALKING distance across the whole territory.
///
/// <para>🔴 The bug these exist for: map selection was <c>maps[i % maps.Count]</c> over a list built by
/// scanning map 1 upward, so the flags always took the territory's lowest-numbered maps. Map numbers run in
/// authoring order and authoring order is spatially clustered, so every war put the flags a map apart, in the
/// same places, with no randomness anywhere in the selection.</para>
///
/// <para>The territory here is a straight chain of maps, which is the shape that makes the failure legible:
/// picking by map number lands adjacent, picking by distance lands at opposite ends.</para>
/// </summary>
[TestFixture]
public class ContestPointSpreadTests
{
    private const int Chain = 5;   // maps 1..5, left-to-right

    /// <summary>Maps 1..5 in a row, all walkable, each linked to its neighbors. Territory index 1.</summary>
    private static GameWorld ChainWorld()
    {
        var world = new GameWorld();
        for (int m = 1; m <= Chain; m++)
        {
            var map = world.Maps[m];
            map.MapGroup = 1;
            for (int x = 0; x <= Constants.MaxMapX; x++)
                for (int y = 0; y <= Constants.MaxMapY; y++)
                    map.EditTile(x, y, t => t with { Type = TileType.Walkable });
            if (m > 1) map.Left = m - 1;
            if (m < Chain) map.Right = m + 1;
        }
        world.MapGroups[1] = new MapGroupRecord { Index = 1 };
        return world;
    }

    private static List<(int Map, int X, int Y)> Choose(GameWorld world, List<int> all, List<int> eligible, int count)
    {
        var sys = new GuildTerritorySystem(world, new PlayerManager(), new SilentDispatcher(),
            guilds: null!, spawn: null!, persistence: null!, bg: null!,
            NullLogger<GuildTerritorySystem>.Instance);
        var m = typeof(GuildTerritorySystem).GetMethod("ChooseCapturePoints",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<(int Map, int X, int Y)>)m.Invoke(sys, new object?[] { all, eligible, count })!;
    }

    private static List<int> All => Enumerable.Range(1, Chain).ToList();

    /// <summary>Run it repeatedly: the first pick is random by design, so a property that only holds for some
    /// seeds is not a property.</summary>
    [Test]
    public void TwoPointsNeverLandOnNeighboringMaps()
    {
        for (int trial = 0; trial < 25; trial++)
        {
            var picks = Choose(ChainWorld(), All, All, 2);

            Assert.That(picks, Has.Count.EqualTo(2));
            int a = picks[0].Map, b = picks[1].Map;
            Assert.That(a, Is.Not.EqualTo(b), "one point to a map");
            Assert.That(System.Math.Abs(a - b), Is.GreaterThan(1),
                $"picked maps {a} and {b} — adjacent maps are the clustering this replaced");
        }
    }

    /// <summary>Every point on its own map, for the full five-point case.</summary>
    [Test]
    public void EveryPointTakesADifferentMap()
    {
        var picks = Choose(ChainWorld(), All, All, Chain);

        Assert.That(picks.Select(p => p.Map).Distinct().Count(), Is.EqualTo(picks.Count));
    }

    /// <summary>🔴 A Safe map in the MIDDLE of a territory must not disconnect it. The graph spans every map
    /// so the halves stay one walkable region; only the candidate set excludes safe ground. Building the
    /// graph from eligible maps alone would strand maps 1-2 from maps 4-5 and put both points on one side.</summary>
    [Test]
    public void ATownInTheMiddleIsWalkedThroughNotAroundTheTerritory()
    {
        for (int trial = 0; trial < 25; trial++)
        {
            var world = ChainWorld();
            world.Maps[3].Moral = MapMoral.Safe;
            var eligible = new List<int> { 1, 2, 4, 5 };

            var picks = Choose(world, All, eligible, 2);

            Assert.That(picks, Has.Count.EqualTo(2));
            Assert.That(picks.Select(p => p.Map), Has.None.EqualTo(3), "no flag on safe ground");
            bool spansTheTown = picks.Any(p => p.Map < 3) && picks.Any(p => p.Map > 3);
            Assert.That(spansTheTown, Is.True,
                $"both points landed on one side of the town ({picks[0].Map}, {picks[1].Map})");
        }
    }

    /// <summary>A point is only ever placed where the others can be walked to. An island reachable by nobody
    /// would be held by whoever spawned nearest and never contested.</summary>
    [Test]
    public void PointsNeverLandOnAnIslandCutOffFromTheRest()
    {
        var world = ChainWorld();
        world.Maps[5].Left = 0;        // map 5 is severed from the chain
        world.Maps[4].Right = 0;

        for (int trial = 0; trial < 25; trial++)
        {
            var picks = Choose(world, All, All, 2);

            Assert.That(picks.Select(p => p.Map), Has.None.EqualTo(5),
                "the four-map region is larger, so the severed map is not a candidate");
        }
    }

    /// <summary>The control: with only one eligible map there is nowhere to spread to, and the run stops at
    /// what it can place rather than doubling up or looping.</summary>
    [Test]
    public void FewerEligibleMapsThanPointsPlacesWhatItCan()
    {
        var picks = Choose(ChainWorld(), All, [2], 3);

        Assert.That(picks, Has.Count.EqualTo(1));
        Assert.That(picks[0].Map, Is.EqualTo(2));
    }
}

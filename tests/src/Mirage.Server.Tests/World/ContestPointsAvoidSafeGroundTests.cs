using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
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
/// A capture point on a Safe map can never change hands — no PvP resolves there — so a war would hand the
/// defender a point nobody can contest. Safe ground is not eligible.
///
/// <para>🔴 Moral is INHERITABLE: a map with no Moral of its own takes its group's. The town case that
/// matters most in practice is exactly that shape — a group marked Safe with plain maps inside it — so this
/// asserts the inherited case, not just a map with Moral stamped on it.</para>
/// </summary>
[TestFixture]
public class ContestPointsAvoidSafeGroundTests
{
    private const int OpenMap = 1, SafeMap = 2, SafeGroup = 4;

    private static GuildTerritorySystem System(GameWorld world) =>
        new(world, new PlayerManager(), new SilentDispatcher(),
            guilds: null!, spawn: null!, persistence: null!, bg: null!,
            NullLogger<GuildTerritorySystem>.Instance);

    /// <summary>Two all-walkable maps: one open, one whose Safe moral is INHERITED from its group.</summary>
    private static GameWorld TwoMaps()
    {
        var world = new GameWorld();
        foreach (int m in new[] { OpenMap, SafeMap })
        {
            var map = world.Maps[m];
            for (int x = 0; x <= Constants.MaxMapX; x++)
                for (int y = 0; y <= Constants.MaxMapY; y++)
                    map.EditTile(x, y, t => t with { Type = TileType.Walkable });
        }

        world.MapGroups[SafeGroup] = new MapGroupRecord { Index = SafeGroup, Moral = MapMoral.Safe };
        world.Maps[SafeMap].MapGroup = SafeGroup;
        world.Maps[SafeMap].Moral = null;   // inherits Safe from the group
        return world;
    }

    private static List<ContestPoint> Generate(GameWorld world, List<int> maps)
    {
        var sys = System(world);
        var m = typeof(GuildTerritorySystem).GetMethod("GenerateContestPoints",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<ContestPoint>)m.Invoke(sys, new object?[] { maps, 0 })!;
    }

    [Test]
    public void NoPointLandsOnASafeMap()
    {
        var world = TwoMaps();

        var points = Generate(world, [OpenMap, SafeMap]);

        Assert.Multiple(() =>
        {
            Assert.That(points, Is.Not.Empty, "a territory with open ground still gets its points");
            Assert.That(points.Select(p => p.Map), Has.None.EqualTo(SafeMap),
                "a point on safe ground can never be taken");
        });
    }

    /// <summary>Count is a function of the TERRITORY's size, not of how much of it is eligible — a territory
    /// does not get fewer points for containing a town.</summary>
    [Test]
    public void TheSafeMapStillCountsTowardHowManyPointsThereAre()
    {
        var world = TwoMaps();

        int withTown = Generate(world, [OpenMap, SafeMap]).Count;
        int openOnly = Generate(world, [OpenMap]).Count;

        Assert.That(withTown, Is.EqualTo(openOnly),
            "both territories are under the maps-per-point threshold, so both take the minimum");
    }

    /// <summary>The fallback: an all-safe territory places anyway rather than running a contest with no
    /// points, which nobody could ever score and the defender would win by default.</summary>
    [Test]
    public void AnAllSafeTerritoryStillPlaces()
    {
        var world = TwoMaps();
        world.Maps[OpenMap].Moral = MapMoral.Safe;

        var points = Generate(world, [OpenMap, SafeMap]);

        Assert.That(points, Is.Not.Empty);
    }
}

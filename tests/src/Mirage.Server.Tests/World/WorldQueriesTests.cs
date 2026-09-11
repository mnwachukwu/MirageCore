using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// The spatial primitives answer about POSITION and EXISTENCE, never about condition. That line is the
/// thing worth pinning: a resolver that also asked whether a body was in some particular state would
/// hand back null for a body that is plainly there, and every caller wanting that body for some other
/// reason would quietly stop finding it.
/// </summary>
[TestFixture]
public class WorldQueriesTests
{
    private const int Map = 1;

    private static (WorldQueries queries, GameWorld world) New()
    {
        var world = new GameWorld();
        return (new WorldQueries(world, new PlayerManager()), world);
    }

    private static void Spawn(GameWorld world, int slot, int x, int y)
    {
        var mn = world.MapNpcs[Map, slot];
        mn.Num = 1;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 100;
    }

    // ── Resolving an identity ─────────────────────────────────────────────────

    [Test]
    public void AHandleThatNamesNoNpcResolvesToNothing()
    {
        var (queries, _) = New();

        Assert.Multiple(() =>
        {
            Assert.That(queries.ResolveNpc(EntityHandle.None), Is.Null);
            Assert.That(queries.ResolveNpc(EntityHandle.ForPlayer(1)), Is.Null,
                        "a player handle names no NPC");
        });
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 1)]
    [TestCase(999_999, 1)]
    public void AnIdentityOutsideTheWorldResolvesToNothing(int spawnMap, int spawnSlot)
    {
        var (queries, _) = New();

        Assert.That(queries.ResolveNpc(spawnMap, spawnSlot), Is.Null);
    }

    [Test]
    public void AnIdentityNothingHasSpawnedIntoResolvesToNothing()
    {
        var (queries, _) = New();

        Assert.That(queries.ResolveNpc(Map, 1), Is.Null);
    }

    [Test]
    public void ASpawnedNpcResolvesToItsHomeSlot()
    {
        var (queries, world) = New();
        Spawn(world, slot: 1, x: 5, y: 5);

        var found = queries.ResolveNpc(Map, 1);

        Assert.That(found, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(found!.Value.CurrentMap, Is.EqualTo(Map));
            Assert.That(found.Value.CurrentSlot, Is.EqualTo(1));
        });
    }

    /// <summary>A handle IS the stable identity, so the two ways of naming one body must agree.</summary>
    [Test]
    public void ResolvingByHandleAgreesWithResolvingByIdentity()
    {
        var (queries, world) = New();
        Spawn(world, slot: 2, x: 4, y: 4);

        Assert.That(queries.ResolveNpc(EntityHandle.ForNpc(Map, 2)), Is.EqualTo(queries.ResolveNpc(Map, 2)));
    }

    /// <summary>Condition is the caller's question. A resolver that filtered on it would stop finding
    /// bodies that are still standing in the world.</summary>
    [Test]
    public void ABodyInAnyConditionStillResolves()
    {
        var (queries, world) = New();
        Spawn(world, slot: 3, x: 6, y: 6);
        world.MapNpcs[Map, 3].Hp = 0;

        Assert.That(queries.ResolveNpc(Map, 3), Is.Not.Null);
    }

    // ── Viewport ──────────────────────────────────────────────────────────────

    [Test]
    public void AnEmptyMapShowsNobody()
    {
        var (queries, _) = New();

        Assert.That(queries.NpcsInViewport(Map, 5, 5), Is.Empty);
    }

    [Test]
    public void ABodyStandingNearbyIsSeen()
    {
        var (queries, world) = New();
        Spawn(world, slot: 1, x: 6, y: 5);

        Assert.That(queries.NpcsInViewport(Map, 5, 5).Select(n => n.CurrentSlot), Does.Contain(1));
    }

    [Test]
    public void ABodyInAnyConditionIsStillSeen()
    {
        var (queries, world) = New();
        Spawn(world, slot: 1, x: 6, y: 5);
        world.MapNpcs[Map, 1].Hp = 0;

        Assert.That(queries.NpcsInViewport(Map, 5, 5), Is.Not.Empty);
    }

    // ── Sweep ─────────────────────────────────────────────────────────────────

    [Test]
    public void ASweepRefusesToWriteIntoNothing()
    {
        var (queries, world) = New();
        var grid = WorldCoordHelper.BuildMapGrid(world.Maps, Map);
        var (wx, wy) = grid.CenterToWorld(5, 5);
        var run = WorldCoordHelper.LeadingEdgeTiles(wx, wy, 1, Direction.Down);

        Assert.That(() => queries.SweepTiles(in grid, in run, WorldLayer.Ground, 0, 1, null!),
                    Throws.ArgumentNullException);
    }

    [Test]
    public void ASweepFindsWhatIsStandingOnTheRun()
    {
        var (queries, world) = New();
        Spawn(world, slot: 1, x: 5, y: 6);

        var grid = WorldCoordHelper.BuildMapGrid(world.Maps, Map);
        var (wx, wy) = grid.CenterToWorld(5, 5);
        var run = WorldCoordHelper.LeadingEdgeTiles(wx, wy, 1, Direction.Down);
        var found = new List<WorldQueries.SweptBody>();

        queries.SweepTiles(in grid, in run, WorldLayer.Ground, 0, 1, found);

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].NpcSlot, Is.EqualTo(1));
        });
    }

    /// <summary>The caller owns the list, so one caller's sweep cannot disturb another's results —
    /// which is what lets two of them sweep in the same tick.</summary>
    [Test]
    public void TwoCallersSweepingKeepTheirOwnResults()
    {
        var (queries, world) = New();
        Spawn(world, slot: 1, x: 5, y: 6);

        var grid = WorldCoordHelper.BuildMapGrid(world.Maps, Map);
        var (wx, wy) = grid.CenterToWorld(5, 5);
        var onto = WorldCoordHelper.LeadingEdgeTiles(wx, wy, 1, Direction.Down);
        var away = WorldCoordHelper.LeadingEdgeTiles(wx, wy, 1, Direction.Up);

        var first = new List<WorldQueries.SweptBody>();
        var second = new List<WorldQueries.SweptBody>();

        queries.SweepTiles(in grid, in onto, WorldLayer.Ground, 0, 1, first);
        queries.SweepTiles(in grid, in away, WorldLayer.Ground, 0, -1, second);

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.EqualTo(1), "the second sweep left the first list alone");
            Assert.That(second, Is.Empty, "nothing standing the other way");
        });
    }

    [Test]
    public void ASweepClearsWhatIsHandedToIt()
    {
        var (queries, world) = New();
        var grid = WorldCoordHelper.BuildMapGrid(world.Maps, Map);
        var (wx, wy) = grid.CenterToWorld(5, 5);
        var run = WorldCoordHelper.LeadingEdgeTiles(wx, wy, 1, Direction.Down);

        var reused = new List<WorldQueries.SweptBody> { default, default };
        queries.SweepTiles(in grid, in run, WorldLayer.Ground, 0, 1, reused);

        Assert.That(reused, Is.Empty);
    }

    // ── Reach ─────────────────────────────────────────────────────────────────

    [Test]
    public void AdjacencyIsMeasuredInTheDirectionFaced()
    {
        var (queries, _) = New();

        Assert.Multiple(() =>
        {
            Assert.That(queries.IsFacingAcrossMaps(Map, Direction.Down, 5, 5, Map, 5, 6), Is.True);
            Assert.That(queries.IsFacingAcrossMaps(Map, Direction.Up, 5, 5, Map, 5, 6), Is.False,
                        "the body is there, but not the way this one is facing");
            Assert.That(queries.IsFacingAcrossMaps(Map, Direction.Down, 5, 5, Map, 5, 8), Is.False,
                        "two tiles away is not adjacent");
        });
    }

    [Test]
    public void AMapNotOnTheGridIsOutOfReachRatherThanAnError()
    {
        var (queries, _) = New();

        Assert.That(queries.IsFacingAcrossMaps(Map, Direction.Down, 5, 5, targetMap: 40, 5, 6), Is.False);
    }
}

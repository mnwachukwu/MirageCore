using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// <see cref="GameWorld.RepairPosition"/> — the half of the warp bound that must never say no.
///
/// <para>A coordinate reaching a warp is one of two things. An authored destination (a Warp tile, a map's
/// boot point) is CONTENT, and one that names no tile is refused and reported for its author to correct.
/// A remembered position (a saved character's tile, a purchased respawn point) is STATE, and there is
/// nobody to correct it — refusing would lock a player out of the game or out of their own body. Those
/// come through here instead, and always come out somewhere real.</para>
/// </summary>
[TestFixture]
public class RepairPositionTests
{
    private const int Map = 1;
    private static readonly (int Map, int X, int Y) Spawn = (2, 4, 4);

    private static GameWorld World() => new();

    [Test]
    public void APositionThatAlreadyExists_IsLeftAlone()
    {
        Assert.That(World().RepairPosition(Map, 3, 4, Spawn), Is.EqualTo((Map, 3, 4)));
    }

    /// <summary>A map that shrank under a player pulls them to its edge, not to spawn: the nearest real
    /// tile to where they were standing is far less surprising than the other side of the world.</summary>
    [TestCase(Constants.MaxMapX + 5, 4, Constants.MaxMapX, 4, TestName = "past the right edge")]
    [TestCase(3, Constants.MaxMapY + 5, 3, Constants.MaxMapY, TestName = "past the bottom edge")]
    [TestCase(-3, 4, 0, 4, TestName = "left of column zero")]
    [TestCase(3, -3, 3, 0, TestName = "above row zero")]
    [TestCase(999, 999, Constants.MaxMapX, Constants.MaxMapY, TestName = "far outside on both axes")]
    public void APositionOffTheGrid_ClampsOntoTheSameMap(int x, int y, int wantX, int wantY)
    {
        Assert.That(World().RepairPosition(Map, x, y, Spawn), Is.EqualTo((Map, wantX, wantY)));
    }

    [Test]
    public void APositionOnAMapThatIsGone_FallsBackToTheGivenSpawn()
    {
        var world = World();

        Assert.Multiple(() =>
        {
            Assert.That(world.RepairPosition(world.Limits.Maps + 1, 3, 4, Spawn), Is.EqualTo(Spawn));
            Assert.That(world.RepairPosition(0, 3, 4, Spawn), Is.EqualTo(Spawn), "map 0 is not a map");
            Assert.That(world.RepairPosition(-1, 3, 4, Spawn), Is.EqualTo(Spawn));
        });
    }

    /// <summary>The fallback is clamped on the same terms, so a misconfigured spawn point cannot strand
    /// anyone either — which is the whole point of a path that is not allowed to fail.</summary>
    [Test]
    public void AFallbackThatIsAlsoOffTheGrid_IsClampedToo()
    {
        Assert.That(World().RepairPosition(0, 3, 4, (2, 999, 999)),
                    Is.EqualTo((2, Constants.MaxMapX, Constants.MaxMapY)));
    }

    [Test]
    public void AFallbackOnAMapThatIsAlsoGone_LandsOnTheFirstMap()
    {
        var world = World();
        int gone = world.Limits.Maps + 1;

        Assert.That(world.RepairPosition(gone, 3, 4, (gone, 3, 4)), Is.EqualTo((1, 0, 0)));
    }

    /// <summary>Whatever it is handed, the answer is always a tile that can be indexed.</summary>
    [Test]
    public void TheAnswerIsAlwaysATileThatExists()
    {
        var world = World();
        int gone = world.Limits.Maps + 1;
        (int Map, int X, int Y)[] asked =
        [
            (Map, 3, 4), (Map, -50, -50), (Map, 5000, 5000), (0, 0, 0), (gone, 7, 7), (-4, 2, 2),
        ];

        Assert.Multiple(() =>
        {
            foreach (var (m, x, y) in asked)
            {
                var got = world.RepairPosition(m, x, y, (gone, 999, 999));
                Assert.That(world.IsRealMap(got.Map), Is.True, $"({m},{x},{y}) landed on map {got.Map}");
                Assert.That(world.Maps[got.Map].Contains(got.X, got.Y), Is.True,
                            $"({m},{x},{y}) landed on ({got.X},{got.Y}), which is not a tile");
            }
        });
    }
}

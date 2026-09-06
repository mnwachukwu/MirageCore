using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// The bounds on a map's size, and which of them are real.
///
/// <para><see cref="MapSize.HardMax"/> is a format limit: a warp names its destination tile as a 16-bit
/// coordinate, so a map past that could hold tiles no door could point at. <see cref="MapSize.SoftCap"/> is
/// only advice about cost and is never enforced — these hold the two apart, because a cap that quietly
/// becomes a limit is worse than either.</para>
/// </summary>
[TestFixture]
public class MapSizeTests
{
    /// <summary>The ceiling is the width of a warp's destination coordinate, wherever that is stored.
    ///
    /// <para>UNSIGNED, and that is the point: a tile coordinate is never negative, so signing one throws away
    /// half the range to represent positions that cannot exist. Every type that stores one is held to it
    /// here — a single signed field among them would quietly halve how large a map can be.</para></summary>
    [Test]
    public void TheHardMaxIsAWarpDestinationsWidth()
    {
        (Type Owner, string[] Fields)[] storesACoordinate =
        [
            (typeof(TileRecord), ["WarpX", "WarpY", "DoorX", "DoorY"]),
            (typeof(FringeAttr), ["WarpX", "WarpY", "DoorX", "DoorY"]),
            (typeof(TileAttr), ["WarpX", "WarpY", "DoorX", "DoorY"]),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(MapSize.HardMax, Is.EqualTo(ushort.MaxValue));
            foreach (var (owner, fields) in storesACoordinate)
            {
                foreach (string field in fields)
                {
                    Assert.That(owner.GetProperty(field)?.PropertyType, Is.EqualTo(typeof(ushort)),
                        $"{owner.Name}.{field} stores a tile coordinate, so it bounds how large a map can be.");
                }
            }
        });
    }

    [Test]
    public void ClampingPullsBothAxesOntoTheLegalRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new MapSize(0, 0).Clamped(), Is.EqualTo(new MapSize(1, 1)), "one tile is the floor");
            Assert.That(new MapSize(-5, -5).Clamped(), Is.EqualTo(new MapSize(1, 1)));
            Assert.That(new MapSize(int.MaxValue, 4).Clamped(), Is.EqualTo(new MapSize(MapSize.HardMax, 4)));
            Assert.That(new MapSize(4, int.MaxValue).Clamped(), Is.EqualTo(new MapSize(4, MapSize.HardMax)));
            Assert.That(new MapSize(24, 20).Clamped(), Is.EqualTo(new MapSize(24, 20)), "a legal size is untouched");
        });
    }

    /// <summary>The soft cap is advice. Nothing clamps to it, and a size past it is a perfectly good map.</summary>
    [Test]
    public void TheSoftCapIsNeverEnforced()
    {
        var big = new MapSize(MapSize.SoftCap * 4, MapSize.SoftCap * 4);

        Assert.Multiple(() =>
        {
            Assert.That(big.IsPastSoftCap, Is.True, "it is worth saying");
            Assert.That(big.Clamped(), Is.EqualTo(big), "and never acted on");
        });
    }

    /// <summary>Each axis is judged on its own, so a map that is wide but short draws the warning for its
    /// width alone.</summary>
    [TestCase(MapSize.SoftCap, MapSize.SoftCap, false, TestName = "exactly at the cap on both axes")]
    [TestCase(MapSize.SoftCap + 1, 200, true, TestName = "one past it on width")]
    [TestCase(200, MapSize.SoftCap + 1, true, TestName = "one past it on height")]
    [TestCase(16, 12, false, TestName = "the default")]
    public void TheSoftCapIsJudgedPerAxis(int w, int h, bool warns)
    {
        Assert.That(new MapSize(w, h).IsPastSoftCap, Is.EqualTo(warns));
    }

    /// <summary>A new map fills the camera exactly, so one created without a thought scrolls nowhere.</summary>
    [Test]
    public void TheDefaultIsTheCameraWindow()
    {
        Assert.That(MapSize.Default, Is.EqualTo(new MapSize(Constants.ViewportTilesX, Constants.ViewportTilesY)));
    }

    /// <summary>A map may be built at any size the format allows, and reports the size it was built at.</summary>
    [TestCase(1, 1)]
    [TestCase(16, 12)]
    [TestCase(MapSize.SoftCap, MapSize.SoftCap)]
    [TestCase(MapSize.SoftCap + 1, 300)]
    public void AMapCanBeBuiltAtAnyLegalSize(int w, int h)
    {
        var map = new MapRecord(w, h);

        Assert.Multiple(() =>
        {
            Assert.That((map.Width, map.Height), Is.EqualTo((w, h)));
            Assert.That(map.Contains(w - 1, h - 1), Is.True, "the far corner is a tile");
            Assert.That(map.Contains(w, h - 1), Is.False);
        });
    }
}

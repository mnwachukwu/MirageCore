using Mirage.Editor.Controls;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Authoring;

// The editor overlay's ramp-block color-coding (amber = mixed-direction block; red = invalid/dead block).
// Pure MapRecord analysis, so no Avalonia render context is needed.  A fresh MapRecord is open walkable ground
// everywhere (default tiles), so tests only place the ramps/walls that matter.
[TestFixture]
public class RampOverlayTests
{
    private static MapRecord Map() => new() { Name = "Ramps" };

    // A ramp whose ARROW points `lift` (up-ramp) — stored ground side is the opposite.
    private static void Ramp(MapRecord m, int x, int y, Direction groundSide) =>
        m.EditTile(x, y, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = groundSide } });

    // ── Mixed-direction detection (amber) ───────────────────────────────────────
    [Test]
    public void IsMixedBlock_TrueForDifferentAdjacentDirections()
    {
        var m = Map();
        Ramp(m, 5, 5, Direction.Left);   // '>'
        Ramp(m, 6, 5, Direction.Right);  // '<'

        Assert.Multiple(() =>
        {
            Assert.That(RampOverlay.IsMixedBlock(m, 5, 5), Is.True);
            Assert.That(RampOverlay.IsMixedBlock(m, 6, 5), Is.True);
        });
    }

    [Test]
    public void IsMixedBlock_FalseForASingleOrientationBlock()
    {
        var m = Map();
        Ramp(m, 8, 8, Direction.Down);
        Ramp(m, 8, 9, Direction.Down);
        Ramp(m, 9, 8, Direction.Down);

        Assert.Multiple(() =>
        {
            Assert.That(RampOverlay.IsMixedBlock(m, 8, 8), Is.False);
            Assert.That(RampOverlay.IsMixedBlock(m, 8, 9), Is.False);
        });
    }

    // ── Invalid-block detection (red) ───────────────────────────────────────────
    [Test]
    public void IsInvalidBlock_FeetCollide_IsInvalid()
    {
        // '<' at (5,5): ground side Right → foot is (6,5), a ramp.
        // '>' at (6,5): ground side Left  → foot is (5,5), a ramp.
        // Neither ground foot touches real ground → the block connects nothing.
        var m = Map();
        Ramp(m, 5, 5, Direction.Right);
        Ramp(m, 6, 5, Direction.Left);

        Assert.Multiple(() =>
        {
            Assert.That(RampOverlay.IsInvalidBlock(m, 5, 5), Is.True);
            Assert.That(RampOverlay.IsInvalidBlock(m, 6, 5), Is.True, "invalidity is a property of the whole block");
        });
    }

    [Test]
    public void IsInvalidBlock_SingleRampOnOpenGround_IsValid()
    {
        var m = Map();
        Ramp(m, 5, 5, Direction.Down);   // foot (5,6) is open walkable ground → a mount point

        Assert.That(RampOverlay.IsInvalidBlock(m, 5, 5), Is.False);
    }

    [Test]
    public void IsInvalidBlock_MixedButHasAMountPoint_IsValid()
    {
        // A mixed block that IS reachable: (5,5)='^' mounts from the ground below, (6,5)='>' rides off it.
        var m = Map();
        Ramp(m, 5, 5, Direction.Down);   // foot (5,6) open ground → mount point
        Ramp(m, 6, 5, Direction.Left);   // foot (5,5) is a ramp — but the block still has the mount above

        Assert.Multiple(() =>
        {
            Assert.That(RampOverlay.IsMixedBlock(m, 5, 5), Is.True, "different directions → amber");
            Assert.That(RampOverlay.IsInvalidBlock(m, 5, 5), Is.False, "but it can be mounted → not red");
            Assert.That(RampOverlay.IsInvalidBlock(m, 6, 5), Is.False);
        });
    }

    [Test]
    public void IsInvalidBlock_GroundFootIsAWall_IsInvalid()
    {
        var m = Map();
        Ramp(m, 5, 5, Direction.Down);          // foot (5,6)...
        m.EditTile(5, 6, t => t with { Type = TileType.Blocked });   // ...is a ground wall → you can't stand there to mount

        Assert.That(RampOverlay.IsInvalidBlock(m, 5, 5), Is.True);
    }

    [Test]
    public void IsInvalidBlock_GroundFootOffMapEdge_NotFlagged()
    {
        // A ramp whose ground side runs off the map edge might mount across a seam — give it the benefit of the
        // doubt rather than a false "broken" flag.
        var m = Map();
        Ramp(m, 0, 5, Direction.Left);   // foot (-1,5) is off the map

        Assert.That(RampOverlay.IsInvalidBlock(m, 0, 5), Is.False);
    }
}

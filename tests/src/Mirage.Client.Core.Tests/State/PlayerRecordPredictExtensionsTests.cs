using Mirage.Client.Core.Logic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>Client-side step prediction: the 6-field mutation that starts the local walk/run animation BEFORE
/// the server echo. Locks the collapsed prediction contract so the call sites can't drift.</summary>
[TestFixture]
public class PlayerRecordPredictExtensionsTests
{
    // Stepping right: the destination is set, and the interpolation offset is kicked back to the origin tile
    // (negative X, since we slide in FROM the left) so the sprite animates across.
    [Test]
    public void PredictMove_Right_SetsDestinationAndBackfillsOffset()
    {
        var me = new PlayerRecord { X = 5, Y = 5, Dir = Direction.Down };
        me.PredictMove(Direction.Right, nx: 6, ny: 5, MovementType.Walking, WorldLayer.Fringe);
        Assert.Multiple(() =>
        {
            Assert.That(me.X, Is.EqualTo(6));
            Assert.That(me.Y, Is.EqualTo(5));
            Assert.That(me.Dir, Is.EqualTo(Direction.Right));
            Assert.That(me.Moving, Is.EqualTo(MovementType.Walking));
            Assert.That(me.XOffset, Is.EqualTo(-Constants.PicX), "slide in from the tile we left");
            Assert.That(me.YOffset, Is.EqualTo(0f));
            Assert.That(me.Layer, Is.EqualTo(WorldLayer.Fringe), "PredictMove commits the predicted layer");
        });
    }

    // Stepping up: Y decreases and the Y offset is positive (slide down into place from above).
    [Test]
    public void PredictMove_Up_PositiveYOffset()
    {
        var me = new PlayerRecord { X = 5, Y = 5 };
        me.PredictMove(Direction.Up, nx: 5, ny: 4, MovementType.Running, WorldLayer.Ground);
        Assert.Multiple(() =>
        {
            Assert.That(me.Y, Is.EqualTo(4));
            Assert.That(me.YOffset, Is.EqualTo(Constants.PicY));
            Assert.That(me.XOffset, Is.EqualTo(0f));
            Assert.That(me.Moving, Is.EqualTo(MovementType.Running));
        });
    }
}

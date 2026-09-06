using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>The seamless-crossing grid re-frame. Crossing an edge slides the whole 3x3 one cell opposite the
/// travel direction so the cell entered becomes the new center, preserving every loaded map + its entities,
/// dropping the row/column that scrolls off, and pruning guests that fell out of the region.</summary>
[TestFixture]
public class ClientStateShiftGridTests
{
    // Lay out map numbers 1..9 across the 3x3 grid (center [1,1] = 5):
    //   [1][2][3]
    //   [4][5][6]
    //   [7][8][9]
    static ClientState GridState()
    {
        var s = new ClientState { CenterMapNum = 5 };
        int n = 1;
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                s.NeighborMapNums[c, r] = n;
                if (!(c == 1 && r == 1)) s.NeighborMaps[c, r] = new MapRecord();
                n++;
            }
        }

        return s;
    }

    // Crossing UP: content slides down a row, so the map that was directly above (cell [1,0] = 2) becomes the
    // new center, carrying its NPC slots; the newly revealed top row is emptied for the server to fill.
    [Test]
    public void ShiftGrid_Up_UpNeighborBecomesCenter_WithItsNpcs()
    {
        var s = GridState();
        s.NeighborNpcs[1, 0][1].Num = 42;   // an NPC on the map directly above

        s.ShiftGrid(Direction.Up);

        Assert.Multiple(() =>
        {
            Assert.That(s.CenterMapNum, Is.EqualTo(2), "the up-neighbor is the new center");
            Assert.That(s.NeighborMapNums[1, 1], Is.EqualTo(2));
            Assert.That(s.NeighborMapNums[0, 1], Is.EqualTo(1), "old top row slides to the middle");
            Assert.That(s.NeighborMapNums[2, 1], Is.EqualTo(3));
            Assert.That(s.NeighborMapNums[1, 2], Is.EqualTo(5), "old center slides down");
            Assert.That(s.NeighborMapNums[1, 0], Is.EqualTo(0), "the revealed top row is empty");
            Assert.That(s.MapNpcs[1].Num, Is.EqualTo(42), "the up-neighbor's NPCs are now the center's");
        });
    }

    // Crossing LEFT: content slides right, so the left neighbor ([0,1] = 4) becomes the new center.
    [Test]
    public void ShiftGrid_Left_LeftNeighborBecomesCenter()
    {
        var s = GridState();
        s.ShiftGrid(Direction.Left);
        Assert.Multiple(() =>
        {
            Assert.That(s.CenterMapNum, Is.EqualTo(4));
            Assert.That(s.NeighborMapNums[2, 1], Is.EqualTo(5), "old center slides right");
            Assert.That(s.NeighborMapNums[0, 1], Is.EqualTo(0), "the revealed left column is empty");
        });
    }

    // The GridShifted event reports the world-pixel offset the data slid, so world-pixel subscribers re-anchor.
    [Test]
    public void ShiftGrid_RaisesGridShifted_WithPixelOffset()
    {
        var s = GridState();
        (int dx, int dy) got = (-1, -1);
        s.GridShifted += (x, y) => got = (x, y);
        s.ShiftGrid(Direction.Up);   // content slides DOWN one map-height, X unchanged
        Assert.That(got, Is.EqualTo((0, WorldCoordHelper.MapTilesY * Constants.PicY)));
    }

    // A visiting guest whose map scrolled out of the 3x3 is dropped; one still in the region is kept.
    [Test]
    public void ShiftGrid_PrunesGuestsThatFellOutOfGrid()
    {
        var s = GridState();
        s.TraversalNpcs[(8, 1)] = new ClientTraversalNpc { Num = 1, CurrentMapNum = 8, SpawnMapNum = 8, SpawnSlot = 1 };
        s.TraversalNpcs[(5, 1)] = new ClientTraversalNpc { Num = 1, CurrentMapNum = 5, SpawnMapNum = 5, SpawnSlot = 1 };

        s.ShiftGrid(Direction.Up);   // map 8 (the Down cell) leaves the region; map 5 (old center) survives below

        Assert.Multiple(() =>
        {
            Assert.That(s.TraversalNpcs.ContainsKey((8, 1)), Is.False, "a guest on a map that scrolled out is dropped");
            Assert.That(s.TraversalNpcs.ContainsKey((5, 1)), Is.True, "a guest still in the region is kept");
        });
    }
}

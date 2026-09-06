using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Rendering;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Rendering;

/// <summary>Sprite-sheet source-rect math. ItemAtlas is a vertical 32px strip (pic 0 = top). TileAtlas is a
/// grid whose column count is derived from each sheet's pixel width, with 1-based tile numbers wrapping to the
/// next row past the last column — a wrap/off-by-one here misrenders every tile after the first row.
/// Marked non-parallelizable because TileAtlas.Init sets shared static column state.</summary>
[TestFixture]
[NonParallelizable]
public class AtlasTests
{
    static Rectangle Cell(int col, int row) =>
        new(col * Constants.PicX, row * Constants.PicY, Constants.PicX, Constants.PicY);

    // ── ItemAtlas: vertical strip ─────────────────────────────────────────────────

    [Test]
    public void ItemAtlas_MapsPicToVerticalStripCell()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ItemAtlas.GetSourceRect(0), Is.EqualTo(Cell(0, 0)), "pic 0 = top of the strip");
            Assert.That(ItemAtlas.GetSourceRect(3), Is.EqualTo(Cell(0, 3)), "pic N = N cells down, same column");
        });
    }

    [Test]
    public void ItemAtlas_NegativePic_Empty()
        => Assert.That(ItemAtlas.GetSourceRect(-1), Is.EqualTo(Rectangle.Empty));

    // ── TileAtlas: grid ───────────────────────────────────────────────────────────

    [Test]
    public void TileAtlas_OneBasedTile_FirstIsTopLeft_LastOfRowIsRightmost()
    {
        TileAtlas.Init([Constants.PicX * 7]);   // a 7-wide sheet
        Assert.Multiple(() =>
        {
            Assert.That(TileAtlas.GetSourceRect(0, 1), Is.EqualTo(Cell(0, 0)), "tile 1 = top-left");
            Assert.That(TileAtlas.GetSourceRect(0, 7), Is.EqualTo(Cell(6, 0)), "tile 7 = last cell of row 0");
        });
    }

    [Test]
    public void TileAtlas_WrapsToNextRowPastLastColumn()
    {
        TileAtlas.Init([Constants.PicX * 7, Constants.PicX * 10]);   // sheet 0: 7 cols, sheet 1: 10 cols
        Assert.Multiple(() =>
        {
            Assert.That(TileAtlas.GetSourceRect(0, 8), Is.EqualTo(Cell(0, 1)), "7-wide: tile 8 wraps to row 1");
            Assert.That(TileAtlas.GetSourceRect(1, 11), Is.EqualTo(Cell(0, 1)), "10-wide: tile 11 wraps to row 1");
        });
    }

    [Test]
    public void TileAtlas_Init_DerivesColumnsFromWidth_ZeroWidthFallsBackToOne()
    {
        TileAtlas.Init([0]);   // a 0-width sheet → 1-column fallback
        Assert.That(TileAtlas.GetSourceRect(0, 3), Is.EqualTo(Cell(0, 2)), "single column: tile 3 sits at row 2");
    }

    [Test]
    public void TileAtlas_OutOfRange_Empty()
    {
        TileAtlas.Init([Constants.PicX * 7]);
        Assert.Multiple(() =>
        {
            Assert.That(TileAtlas.GetSourceRect(0, 0), Is.EqualTo(Rectangle.Empty), "tile 0 = none");
            Assert.That(TileAtlas.GetSourceRect(-1, 5), Is.EqualTo(Rectangle.Empty), "negative sheet");
            Assert.That(TileAtlas.GetSourceRect(99, 5), Is.EqualTo(Rectangle.Empty), "sheet out of range");
        });
    }
}

using Avalonia;
using Mirage.Editor.Controls;
using NUnit.Framework;
using Side = Mirage.Editor.Controls.AttributeBorderGeometry.Side;

namespace Mirage.Editor.Tests.Controls;

/// <summary>Where an attribute outline lands relative to the grid line it marks. Two cells carrying different
/// attributes each draw the edge they share, so the two bands have to land on opposite sides of that line —
/// drawn on the line they occupy the same coordinates and only the cell painted last survives.</summary>
[TestFixture]
public class AttributeBorderGeometryTests
{
    const double W = 32, H = 32, T = 1.5;
    const double CellX = 64, CellY = 96;

    [Test]
    public void EveryBand_LiesWhollyInsideItsOwnCell()
    {
        var cell = new Rect(CellX, CellY, W, H);

        Assert.Multiple(() =>
        {
            foreach (Side side in Enum.GetValues<Side>())
            {
                var band = AttributeBorderGeometry.Band(CellX, CellY, W, H, side, T);
                Assert.That(cell.Contains(band), Is.True, $"the {side} band escapes its own cell");
            }
        });
    }

    /// <summary>The reported case, horizontally: this cell's right edge and its neighbor's left edge are the
    /// same grid line at x=96.</summary>
    [Test]
    public void FacingBandsOfHorizontalNeighbors_AreFlushAndDisjoint()
    {
        var left = AttributeBorderGeometry.Band(CellX, CellY, W, H, Side.Right, T);
        var right = AttributeBorderGeometry.Band(CellX + W, CellY, W, H, Side.Left, T);

        Assert.Multiple(() =>
        {
            Assert.That(left.Right, Is.EqualTo(CellX + W), "the left cell's band stops at the shared line");
            Assert.That(right.X, Is.EqualTo(CellX + W), "and the right cell's band starts there");
            Assert.That(left.Intersects(right), Is.False, "so neither can paint over the other");
        });
    }

    [Test]
    public void FacingBandsOfVerticalNeighbors_AreFlushAndDisjoint()
    {
        var above = AttributeBorderGeometry.Band(CellX, CellY, W, H, Side.Bottom, T);
        var below = AttributeBorderGeometry.Band(CellX, CellY + H, W, H, Side.Top, T);

        Assert.Multiple(() =>
        {
            Assert.That(above.Bottom, Is.EqualTo(CellY + H), "the upper cell's band stops at the shared line");
            Assert.That(below.Y, Is.EqualTo(CellY + H), "and the lower cell's band starts there");
            Assert.That(above.Intersects(below), Is.False, "so neither can paint over the other");
        });
    }

    /// <summary>Each band is inset by its OWN width, so the two sides stay separated even if the attributes
    /// are ever given different outline weights.</summary>
    [Test]
    public void FacingBands_StayDisjoint_WhenTheTwoThicknessesDiffer()
    {
        var left = AttributeBorderGeometry.Band(CellX, CellY, W, H, Side.Right, 4.0);
        var right = AttributeBorderGeometry.Band(CellX + W, CellY, W, H, Side.Left, 1.0);

        Assert.That(left.Intersects(right), Is.False);
    }

    /// <summary>A band runs the cell's full span, so the two sides meeting at a corner overlap there rather
    /// than leaving an unpainted notch.</summary>
    [Test]
    public void AdjacentSidesOfOneCell_MeetAtTheCorner()
    {
        var top = AttributeBorderGeometry.Band(CellX, CellY, W, H, Side.Top, T);
        var leftSide = AttributeBorderGeometry.Band(CellX, CellY, W, H, Side.Left, T);

        Assert.That(top.Intersects(leftSide), Is.True, "the top and left bands cover the shared corner");
    }
}

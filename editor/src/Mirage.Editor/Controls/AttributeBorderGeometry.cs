using Avalonia;

namespace Mirage.Editor.Controls;

/// <summary>
/// Where an attribute outline sits on the cell it marks.  Each band lies wholly INSIDE its own cell, against
/// the shared grid line rather than centered on it, so two cells carrying different attributes each keep their
/// own side of the edge they share instead of drawing over the same coordinates.
/// </summary>
internal static class AttributeBorderGeometry
{
    internal enum Side
    {
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>The rectangle to fill for one <paramref name="side"/> of the cell whose top-left corner is
    /// (<paramref name="px"/>, <paramref name="py"/>).  Runs the cell's full width or height, so the sides of a
    /// contiguous region meet at its corners with no notch.</summary>
    internal static Rect Band(double px, double py, double w, double h, Side side, double thickness) => side switch
    {
        Side.Top => new Rect(px, py, w, thickness),
        Side.Bottom => new Rect(px, py + h - thickness, w, thickness),
        Side.Left => new Rect(px, py, thickness, h),
        _ => new Rect(px + w - thickness, py, thickness, h),
    };
}

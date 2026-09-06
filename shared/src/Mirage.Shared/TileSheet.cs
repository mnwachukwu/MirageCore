namespace Mirage.Shared;

/// <summary>
/// Where a tile sits inside its sheet. A sheet is a grid of <see cref="Constants.PicX"/> x
/// <see cref="Constants.PicY"/> cells, filled left to right and top to bottom, and tile numbers are
/// 1-based within it.
///
/// <para>Sheets may differ in width, so the column count is a property of the sheet rather than a constant.
/// This lives here because two very different consumers need the same answer — the renderer's source
/// rectangle and the light occluder's alpha lookup — and a second copy of the arithmetic would be a place
/// for them to disagree about where a tile is.</para>
/// </summary>
public static class TileSheet
{
    /// <summary>How many tiles fit across a sheet of this pixel width. At least one, so a sheet narrower
    /// than a tile still resolves rather than dividing by zero.</summary>
    public static int Columns(int sheetPixelWidth) => Math.Max(1, sheetPixelWidth / Constants.PicX);

    /// <summary>The top-left pixel of a 1-based tile number within a sheet of this width.</summary>
    public static (int X, int Y) Origin(int sheetPixelWidth, int tileNum)
    {
        int index = tileNum - 1;
        int cols = Columns(sheetPixelWidth);
        return (index % cols * Constants.PicX, index / cols * Constants.PicY);
    }
}

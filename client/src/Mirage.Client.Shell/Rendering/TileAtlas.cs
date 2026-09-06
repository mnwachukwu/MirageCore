using Microsoft.Xna.Framework;
using Mirage.Shared;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Maps a (sheet, 1-based tile number) pair to a source rectangle in that tileset's texture.
/// Each sheet's column count is derived from its own texture width, so sheets may differ in size.
/// </summary>
public static class TileAtlas
{
    // Pixel width per sheet, indexed by sheet number. Defaults to the original 7-wide layout.
    private static int[] _widths = [7 * Constants.PicX];

    /// <summary>Call once after the tileset textures are loaded, passing each sheet's pixel width
    /// (index = sheet number; a 0 width yields a 1-column fallback for that slot).</summary>
    public static void Init(int[] sheetWidths)
    {
        _widths = sheetWidths is null || sheetWidths.Length == 0 ? [7 * Constants.PicX] : sheetWidths;
    }

    /// <summary>Where a tile lives in its sheet. Shares <see cref="TileSheet"/> with the light occluder, so
    /// the shadow a tile casts is sampled from the same pixels the renderer draws.</summary>
    public static Rectangle GetSourceRect(int sheet, int tileNum)
    {
        if (tileNum <= 0) return Rectangle.Empty;
        if (sheet < 0 || sheet >= _widths.Length) return Rectangle.Empty;
        var (x, y) = TileSheet.Origin(_widths[sheet], tileNum);
        return new Rectangle(x, y, Constants.PicX, Constants.PicY);
    }
}

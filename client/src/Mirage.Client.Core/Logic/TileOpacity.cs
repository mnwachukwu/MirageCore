using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// What each tile graphic covers, at <see cref="SubCells"/> x <see cref="SubCells"/> per tile — the shape a
/// tile casts a shadow in.
///
/// <para>The ART decides that shape: a tile's alpha is sampled into a coverage grid, and a light trace stops
/// only on covered cells. So a mountain whose lower third is transparent pixels lets the ground show lit
/// beneath it.</para>
///
/// <para>🔴 <c>BlocksLight</c> decides WHETHER a tile casts a shadow. The art decides only its SHAPE. A tile
/// the author never flagged is not consulted here at all, however solid its graphic looks.</para>
///
/// <para>Coverage comes from the TOPMOST art cell of the stack on the layer being lit, because that is the
/// thing standing there. Nearly every blocked tile is a floor with an obstruction laid over it, and a floor
/// is opaque wall to wall — union the stack and every silhouette fills back in to a full square.</para>
///
/// <para>Filled once at content load and read from the light trace, so it is a static store like the
/// renderer's tile atlas rather than world state: it describes the ART, which no map can change.</para>
/// </summary>
public static class TileOpacity
{
    /// <summary>Coverage samples across a tile, each way. Sixty-four cells is one <see cref="ulong"/> per
    /// tile, and at <see cref="Constants.PicX"/> that is a four-pixel cell — fine enough to read a
    /// silhouette, coarse enough that a whole sheet's coverage is a few kilobytes.</summary>
    public const int SubCells = 8;

    /// <summary>Pixels per coverage cell, each way.</summary>
    public const int PixelsPerCell = Constants.PicX / SubCells;

    /// <summary>A pixel counts as art at or above this alpha. The .bmp art is color-keyed to a hard 0 or 255,
    /// so this matters only for .png sheets with real edges.</summary>
    private const byte OpaqueAlpha = 128;

    /// <summary>How many of a cell's pixels must be art for the cell to stop light. Half: a cell is a SAMPLE
    /// of coverage, and counting any single pixel would let a one-pixel outline shade four pixels of ground
    /// and fatten every shadow by a cell all round.</summary>
    private const int CoveredPixels = PixelsPerCell * PixelsPerCell / 2;

    /// <summary>Every cell covered — what a tile shades when its art cannot be read.</summary>
    public const ulong Solid = ulong.MaxValue;

    /// <summary>No cell covered.</summary>
    public const ulong Open = 0UL;

    // [sheet][tileNum - 1] -> the tile's 8x8 coverage bits, row-major from the top-left.
    private static ulong[]?[] _sheets = [];

    /// <summary>Drops every sheet's coverage. Content reload only.</summary>
    public static void Reset() => _sheets = [];

    /// <summary>
    /// Reads one sheet's coverage out of its alpha channel, one byte a pixel, row-major.
    ///
    /// <para>Takes the pixels the loader already holds, so no sheet is decoded twice, and keeps none of them:
    /// a sheet of any size collapses to eight bytes a tile.</para>
    /// </summary>
    public static void SetSheet(int sheet, ReadOnlySpan<byte> alpha, int width, int height)
    {
        if (sheet < 0 || sheet >= Constants.MaxTilesets || width <= 0 || height <= 0) return;
        if (alpha.Length < width * height) return;

        int cols = TileSheet.Columns(width);
        int rows = height / Constants.PicY;
        int tiles = cols * rows;
        if (tiles <= 0) return;

        if (sheet >= _sheets.Length)
        {
            var grown = new ulong[]?[sheet + 1];
            _sheets.CopyTo(grown, 0);
            _sheets = grown;
        }

        var coverage = new ulong[tiles];
        for (int t = 0; t < tiles; t++)
        {
            var (ox, oy) = TileSheet.Origin(width, t + 1);
            ulong bits = 0;
            for (int cy = 0; cy < SubCells; cy++)
            {
                for (int cx = 0; cx < SubCells; cx++)
                {
                    int opaque = 0;
                    for (int py = 0; py < PixelsPerCell; py++)
                    {
                        int y = oy + cy * PixelsPerCell + py;
                        if (y >= height) break;
                        int rowStart = y * width + ox + cx * PixelsPerCell;
                        for (int px = 0; px < PixelsPerCell; px++)
                        {
                            if (ox + cx * PixelsPerCell + px >= width) break;
                            if (alpha[rowStart + px] >= OpaqueAlpha) opaque++;
                        }
                    }

                    if (opaque >= CoveredPixels) bits |= 1UL << (cy * SubCells + cx);
                }
            }

            coverage[t] = bits;
        }

        _sheets[sheet] = coverage;
    }

    /// <summary>The coverage of one packed layer cell. An empty cell covers nothing; a cell whose sheet is
    /// not loaded covers everything, so an unreadable sheet shades a whole square rather than none of it.</summary>
    public static ulong Of(int packedCell)
    {
        int tile = LayerCell.Tile(packedCell);
        if (tile <= 0) return Open;
        int sheet = LayerCell.Sheet(packedCell);
        if (sheet < 0 || sheet >= _sheets.Length) return Solid;
        var coverage = _sheets[sheet];
        if (coverage is null) return Solid;
        return tile - 1 < coverage.Length ? coverage[tile - 1] : Solid;
    }

    /// <summary>
    /// The shadow a tile casts on one layer: the coverage of the TOPMOST art cell of that layer's stack.
    ///
    /// <para>A tile with no art there has no silhouette to offer and shades its whole square — an invisible
    /// barrier is still a barrier.</para>
    /// </summary>
    public static ulong ShadowOf(in TileRecord tile, WorldLayer layer)
    {
        int top = layer == WorldLayer.Fringe
            ? LayerCell.TopmostNonEmptyIndex(tile.Fringe)
            : LayerCell.TopmostNonEmptyIndex(tile.Ground);
        if (top < 0) return Solid;
        return Of(layer == WorldLayer.Fringe ? tile.Fringe[top] : tile.Ground[top]);
    }

    /// <summary>True when a coverage grid stops light at cell (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public static bool Covers(ulong coverage, int cx, int cy)
        => (uint)cx < SubCells && (uint)cy < SubCells && (coverage & (1UL << (cy * SubCells + cx))) != 0;
}

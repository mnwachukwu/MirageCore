using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Which parts of the world a light actually reaches.
///
/// <para>Light is stopped by ART, not by tiles. A tile the author flagged <c>BlocksLight</c> casts a shadow
/// in the shape of its own graphic (see <see cref="TileOpacity"/>), so a mountain whose lower third is
/// transparent pixels lets the ground show lit beneath it, and a narrow trunk casts a narrow shadow.</para>
///
/// <para>Obstacles are read on the light's OWN layer, and each layer has its own light map: a fringe wall
/// shades the deck it stands on, not the ground beneath.</para>
/// </summary>
public static class LightOcclusion
{
    /// <summary>The 3x3 neighbourhood in tiles, measured in the center map's size. A light is culled long
    /// before it could reach past it.</summary>
    public static int GridW(ClientState state) => state.MapTilesX * 3;

    /// <inheritdoc cref="GridW"/>
    public static int GridH(ClientState state) => state.MapTilesY * 3;

    /// <summary>
    /// Mask texels per tile — the same grid the art's coverage is sampled into, so a shadow's edge lands
    /// where the graphic's edge is.
    ///
    /// <para>At <see cref="Constants.PicX"/> that is a four-pixel texel. A linear sampler ramps between texel
    /// CENTRES, so a shadow's boundary is four pixels wide: enough to read as an edge rather than a stair,
    /// narrow enough to sit on the silhouette it came from.</para>
    /// </summary>
    public const int SubSamples = TileOpacity.SubCells;

    /// <summary>The side of the square a light of this radius reaches over, in tiles. This is what the mask
    /// COVERS, and so what <see cref="MaskUv"/> maps onto — independent of how finely it is sampled.</summary>
    public static int MaskSide(int radiusTiles) => Math.Max(0, radiusTiles) * 2 + 1;

    /// <summary>The mask texture's side, in texels.</summary>
    public static int MaskTexels(int radiusTiles) => MaskSide(radiusTiles) * SubSamples;

    /// <summary>How many cells <see cref="Fill"/> needs for a light of this radius.</summary>
    public static int MaskCells(int radiusTiles) => MaskTexels(radiusTiles) * MaskTexels(radiusTiles);

    /// <summary>
    /// Where a halo's own 0..1 quad coordinates land in its reach mask: <c>maskUv = uv * Scale + Offset</c>.
    ///
    /// <para>The mask spans <see cref="MaskSide"/> tiles around <paramref name="tileScreenX"/>,<paramref
    /// name="tileScreenY"/> — the tile the occlusion was traced from — while the halo spans its own radius
    /// around wherever it is being drawn. Both are axis-aligned rectangles in the same space, so the mapping
    /// between them is one scale and one offset, and a halo sliding sub-tile slides across a mask that stays
    /// put.</para>
    ///
    /// <para>Texel <c>i</c> of the mask covers the whole of tile <c>i - r</c>, so mapping the mask's rectangle
    /// onto 0..1 puts each texel's CENTRE on its tile's centre — which is what makes a linear sample between
    /// two texels a ramp across the boundary between their tiles.</para>
    /// </summary>
    public static (float ScaleX, float ScaleY, float OffsetX, float OffsetY) MaskUv(
        float destLeft, float destTop, float destW, float destH,
        float tileScreenX, float tileScreenY, int reachRadius)
    {
        int r = Math.Max(0, reachRadius);
        int side = MaskSide(r);
        float spanX = side * (float)Constants.PicX, spanY = side * (float)Constants.PicY;
        float maskX = tileScreenX - r * Constants.PicX;
        float maskY = tileScreenY - r * Constants.PicY;
        return (destW / spanX, destH / spanY, (destLeft - maskX) / spanX, (destTop - maskY) / spanY);
    }

    /// <summary>
    /// The shadow the tile at a world coordinate casts on <paramref name="layer"/>, as a
    /// <see cref="TileOpacity"/> coverage grid. <see cref="TileOpacity.Open"/> when it casts none.
    ///
    /// <para>Off the loaded neighbourhood reads as solid: a light does not spill into a map nobody has.</para>
    /// </summary>
    public static ulong ShadowAt(ClientState state, int worldX, int worldY, WorldLayer layer)
    {
        if (worldX < 0 || worldY < 0 || worldX >= GridW(state) || worldY >= GridH(state)) return TileOpacity.Solid;
        int col = worldX / state.MapTilesX, row = worldY / state.MapTilesY;
        var map = state.NeighborMaps[col, row];
        if (map is null) return TileOpacity.Solid;

        int lx = worldX - col * state.MapTilesX, ly = worldY - row * state.MapTilesY;
        var tile = map.Tile[lx, ly];
        var attr = LayerLogic.AttrFor(tile, layer);

        // A closed door stops light exactly as a wall does; a ramp is a slope, not a wall, so it casts nothing.
        if (attr.Type == TileType.Key)
        {
            var doors = col == 1 && row == 1 ? state.TempTile : state.NeighborTempTiles[col, row];
            return doors[lx, ly, (int)layer] ? TileOpacity.Open : TileOpacity.ShadowOf(tile, layer);
        }

        if (attr.Type != TileType.Blocked || !attr.BlocksLight) return TileOpacity.Open;
        return TileOpacity.ShadowOf(tile, layer);
    }

    /// <summary>
    /// Whether light from the tile at <paramref name="lightWX"/>,<paramref name="lightWY"/> reaches the
    /// CENTRE of the tile at <paramref name="tileWX"/>,<paramref name="tileWY"/> — the tile-level read of the
    /// same per-texel trace <see cref="Fill"/> runs, and the value <see cref="Fill"/> writes at that tile's
    /// middle texel.
    ///
    /// <para>A tile is now lit in PARTS, so this answers for one point in it rather than for the whole
    /// square: a tile half in a mountain's shadow reaches at its centre and not at its edge.</para>
    /// </summary>
    public static bool Reaches(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                               int tileWX, int tileWY, bool mounted = true)
    {
        int dx = tileWX - lightWX, dy = tileWY - lightWY;
        if (dx == 0 && dy == 0) return true;

        int r = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int side = MaskSide(r);
        Span<ulong> shadow = side * side <= 1024 ? stackalloc ulong[1024] : new ulong[side * side];
        for (int j = -r; j <= r; j++)
        {
            for (int i = -r; i <= r; i++)
            {
                shadow[(j + r) * side + (i + r)] = mounted && i == 0 && j == 0
                    ? TileOpacity.Open                                       // a mounted light is not in its own shadow
                    : ShadowAt(state, lightWX + i, lightWY + j, layer);
            }
        }

        int half = SubSamples / 2;
        return Lit(shadow, side, r * SubSamples + half, r * SubSamples + half,
                   (dx + r) * SubSamples + half, (dy + r) * SubSamples + half);
    }

    /// <summary>
    /// Fills <paramref name="mask"/> with one light's reach, at <see cref="SubSamples"/> texels per tile,
    /// row-major over <see cref="MaskTexels"/> — texel <c>(tx, ty)</c> sits in the tile at offset
    /// <c>(tx / SubSamples - r, ty / SubSamples - r)</c> from the light's own.
    ///
    /// <para>Every texel is traced separately, from the light's own centre, against the coverage the tiles
    /// around it coloured in. A texel standing on art is dark, and so is any texel whose line to the light
    /// crosses art — which is what puts a shadow's edge on the silhouette rather than on the tile border.</para>
    ///
    /// <para>Open ground costs nothing: with no shadow anywhere in the light's square there is nothing to
    /// trace against, and the mask is filled in one pass.</para>
    ///
    /// <para>What lands in <paramref name="mask"/> is a SIGNED DISTANCE FIELD, not the lit/dark bits the
    /// trace produced — see <see cref="Encode"/> for why.</para>
    /// </summary>
    /// <param name="mounted">
    /// True when the light BELONGS to the tile it is on — a sconce on the wall it hangs from, a torch carried
    /// by whoever occupies it. Such a light is never in its own shadow, or it would leave one lit texel and
    /// darkness everywhere else.
    ///
    /// <para>🔴 False for a light merely PASSING OVER a tile, which is every spell effect: a burst scattering
    /// against a wall lands particles on the wall's own tile, and exempting those lights it up as though it
    /// stopped nothing. Most visible firing at something backed right up against it.</para>
    /// </param>
    public static void Fill(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                            int radiusTiles, byte[] mask, bool mounted)
    {
        int r = Math.Max(0, radiusTiles);
        int side = MaskSide(r);
        int texels = side * SubSamples;
        var traced = Traced(texels * texels);

        Span<ulong> shadow = side * side <= 256 ? stackalloc ulong[256] : new ulong[side * side];
        bool anyShadow = false;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                // A MOUNTED light does not stand in its own shadow; one passing over a tile very much does.
                ulong cover = mounted && dx == 0 && dy == 0
                    ? TileOpacity.Open
                    : ShadowAt(state, lightWX + dx, lightWY + dy, layer);
                shadow[(dy + r) * side + (dx + r)] = cover;
                anyShadow |= cover != TileOpacity.Open;
            }
        }

        if (!anyShadow)
        {
            Array.Fill(mask, byte.MaxValue, 0, texels * texels);   // nothing to cast an edge, so no field to build
            return;
        }

        // How many occluder tiles lie in any rectangle of the light's square, as an inclusive 2D prefix sum.
        // A ray never leaves the box between the light's tile and the tile it is aimed at, so an empty box
        // means every texel of that tile is lit and none of its sixty-four rays need walking at all.
        int stride = side + 1;
        Span<int> counted = stride * stride <= 1024 ? stackalloc int[1024] : new int[stride * stride];
        counted[..(stride * stride)].Clear();
        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                int here = shadow[j * side + i] != TileOpacity.Open ? 1 : 0;
                counted[(j + 1) * stride + i + 1] =
                    here + counted[j * stride + i + 1] + counted[(j + 1) * stride + i] - counted[j * stride + i];
            }
        }

        // The light stands at the centre tile; its ray starts at that tile's middle texel.
        int lightTX = r * SubSamples + SubSamples / 2;
        int lightTY = r * SubSamples + SubSamples / 2;

        for (int ty = 0; ty < side; ty++)
        {
            for (int tx = 0; tx < side; tx++)
            {
                ulong cover = shadow[ty * side + tx];
                // A tile whose art fills it is dark to its last texel, whatever the geometry says.
                if (cover == TileOpacity.Solid) { FillBlock(traced, texels, tx, ty, false); continue; }
                if (cover == TileOpacity.Open && Boxed(counted, stride, r, r, tx, ty) == 0)
                {
                    FillBlock(traced, texels, tx, ty, true);
                    continue;
                }

                for (int sy = 0; sy < SubSamples; sy++)
                {
                    int row = (ty * SubSamples + sy) * texels + tx * SubSamples;
                    for (int sx = 0; sx < SubSamples; sx++)
                    {
                        traced[row + sx] = Lit(shadow, side, lightTX, lightTY,
                                             tx * SubSamples + sx, ty * SubSamples + sy);
                    }
                }
            }
        }

        EncodeDistanceField(traced, texels, mask);
    }

    // ── The mask is a distance field ──────────────────────────────────────────

    /// <summary>How many texels of distance the byte range spans, each way.</summary>
    private const float SdfRangeTexels = 2f;

    /// <summary>
    /// One texel's signed distance to the shadow's edge, in texels, packed into a byte: 128 is exactly on
    /// the edge, above it is lit, below it is dark.
    ///
    /// <para>🔴 This is what makes a sharp shadow possible at all. A mask of 0s and 1s sampled with LINEAR
    /// filtering ramps from lit to dark across the space between two texel CENTRES — four world pixels wide
    /// and centred on the boundary, so half of it falls on the art itself and every silhouette wears a
    /// hairline of light. Interpolating a DISTANCE is different: the blend of two distances is still very
    /// nearly the distance, so the shader can threshold it and land the edge on the boundary to a fraction
    /// of a texel. The same trick reads crisp glyphs out of a small font atlas.</para>
    ///
    /// <para>Only the range near the edge is worth resolving — the shader never looks past a fraction of a
    /// texel — so the byte spends its 256 steps on <see cref="SdfRangeTexels"/> each way and saturates
    /// beyond. That is a sixteenth of a texel per step, a quarter of a world pixel.</para>
    /// </summary>
    public static byte Encode(float texelsFromEdge)
        => (byte)Math.Clamp(MathF.Round(128f + texelsFromEdge * (127f / SdfRangeTexels)), 0f, 255f);

    /// <summary>Which side of the edge an encoded texel is on. What the mask meant when it held bits.</summary>
    public static bool IsLit(byte encoded) => encoded > 128;

    /// <summary>
    /// Turns the traced lit/dark bits into the signed distance field the shader reads.
    ///
    /// <para>Distances are measured to the nearest texel of the OTHER kind and then pulled in by half a
    /// texel, which puts zero on the boundary BETWEEN two texel centres — where the art's edge actually is,
    /// and where the old binary mask's halfway point already sat. The edge does not move; it only stops
    /// being smeared across four pixels.</para>
    /// </summary>
    public static void EncodeDistanceField(ReadOnlySpan<bool> traced, int texels, byte[] into)
    {
        int cells = texels * texels;
        var nearest = Nearest(cells);
        Array.Fill(nearest, float.MaxValue, 0, cells);

        // The boundary runs BETWEEN texels that disagree, half a texel from each — which is where the art's
        // edge is, and where the old mask of bits already crossed halfway. Every piece of it is pushed out to
        // the handful of texels near enough to care, so the work follows the LENGTH of the shadow's outline
        // rather than the area of the square: a mask with one wall in the corner pays for one wall.
        for (int y = 0; y < texels; y++)
        {
            int row = y * texels;
            for (int x = 0; x < texels; x++)
            {
                bool here = traced[row + x];
                if (x + 1 < texels && traced[row + x + 1] != here) Splat(nearest, texels, x + 0.5f, y);
                if (y + 1 < texels && traced[row + texels + x] != here) Splat(nearest, texels, x, y + 0.5f);
            }
        }

        for (int i = 0; i < cells; i++)
        {
            // Untouched means no boundary within reach, so the shader has saturated either way.
            float away = nearest[i] == float.MaxValue ? SdfRangeTexels + 1f : MathF.Sqrt(nearest[i]);
            into[i] = Encode(traced[i] ? away : -away);
        }
    }

    /// <summary>How far from the boundary the field is worth resolving. One past the range the byte can hold,
    /// so the saturating texels are the only ones left out.</summary>
    private const int SplatReach = (int)SdfRangeTexels + 1;

    /// <summary>Records one piece of boundary as the nearest-so-far (squared) for every texel around it.</summary>
    private static void Splat(float[] nearest, int texels, float mx, float my)
    {
        int x0 = Math.Max(0, (int)mx - SplatReach), x1 = Math.Min(texels - 1, (int)mx + SplatReach + 1);
        int y0 = Math.Max(0, (int)my - SplatReach), y1 = Math.Min(texels - 1, (int)my + SplatReach + 1);
        for (int y = y0; y <= y1; y++)
        {
            float dy = y - my;
            float dySq = dy * dy;
            int row = y * texels;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - mx;
                float dSq = dx * dx + dySq;
                if (dSq < nearest[row + x]) nearest[row + x] = dSq;
            }
        }
    }

    // Scratch reused across every light and frame: the render path traces one mask at a time, and a mask at
    // the largest radius is 200 texels a side, which is too much to keep taking from the stack.
    private static bool[] _traced = [];
    private static float[] _nearest = [];

    private static bool[] Traced(int cells)
    {
        if (_traced.Length < cells) _traced = new bool[cells];
        return _traced;
    }

    private static float[] Nearest(int cells)
    {
        if (_nearest.Length < cells) _nearest = new float[cells];
        return _nearest;
    }

    /// <summary>Occluder tiles in the rectangle spanned by two tiles of the light's square, corners included.</summary>
    private static int Boxed(ReadOnlySpan<int> counted, int stride, int ax, int ay, int bx, int by)
    {
        int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx) + 1;
        int y0 = Math.Min(ay, by), y1 = Math.Max(ay, by) + 1;
        return counted[y1 * stride + x1] - counted[y0 * stride + x1]
             - counted[y1 * stride + x0] + counted[y0 * stride + x0];
    }

    /// <summary>Writes one tile's whole block of texels.</summary>
    private static void FillBlock(bool[] mask, int texels, int tileX, int tileY, bool lit)
    {
        for (int sy = 0; sy < SubSamples; sy++)
            Array.Fill(mask, lit, (tileY * SubSamples + sy) * texels + tileX * SubSamples, SubSamples);
    }

    /// <summary>
    /// Whether the light's ray reaches one texel: Bresenham across the coverage grid, stopping on the first
    /// covered texel it meets.
    ///
    /// <para>The TARGET texel is tested too, so a texel standing on art is dark — a wall's own face is not
    /// lit. Two covered texels meeting at a corner close it, so light cannot squeeze diagonally between them;
    /// that is the same rule the spell line-of-sight walks, one resolution finer.</para>
    /// </summary>
    private static bool Lit(ReadOnlySpan<ulong> shadow, int side, int fromTX, int fromTY, int toTX, int toTY)
    {
        int dx = Math.Abs(toTX - fromTX), dy = Math.Abs(toTY - fromTY);
        int sx = fromTX < toTX ? 1 : -1, sy = fromTY < toTY ? 1 : -1;
        int err = dx - dy;
        int cx = fromTX, cy = fromTY;

        while (cx != toTX || cy != toTY)
        {
            int e2 = err << 1;
            int stepX = 0, stepY = 0;
            if (e2 > -dy)
            {
                err -= dy;
                stepX = sx;
            }
            if (e2 < dx)
            {
                err += dx;
                stepY = sy;
            }
            if (stepX != 0 && stepY != 0
                && Covered(shadow, side, cx + stepX, cy) && Covered(shadow, side, cx, cy + stepY))
            {
                return false;
            }

            cx += stepX;
            cy += stepY;
            if (Covered(shadow, side, cx, cy)) return false;
        }

        return true;
    }

    /// <summary>Whether one texel of the light's square stands on art.
    ///
    /// <para>Every coordinate here comes off a line between two texels of the square, and a Bresenham line
    /// stays inside the box its endpoints span, so the index is in range by construction — which is why this
    /// does not check, in the one loop that runs tens of thousands of times a mask.</para></summary>
    private static bool Covered(ReadOnlySpan<ulong> shadow, int side, int tx, int ty)
    {
        ulong cover = System.Runtime.CompilerServices.Unsafe.Add(
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(shadow),
            ty / SubSamples * side + tx / SubSamples);
        return (cover & 1UL << ((ty % SubSamples) * TileOpacity.SubCells + tx % SubSamples)) != 0;
    }
}

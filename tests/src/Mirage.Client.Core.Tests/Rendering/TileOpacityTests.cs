using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The shape a tile casts a shadow in comes from its ART, so a mountain whose lower third is transparent
/// pixels lets the ground show lit beneath it.
///
/// <para>🔴 The shape is read off the TOPMOST art cell of the stack on the layer being lit. Nearly every
/// blocked tile in a real map is a floor with an obstruction laid over it, and a floor is opaque wall to
/// wall — union the stack and every silhouette fills back in to a full square, which is the whole thing this
/// is here to avoid.</para>
///
/// <para>A sheet is built here rather than loaded, so a tile's coverage is whatever these tests drew.</para>
/// </summary>
[TestFixture]
public class TileOpacityTests
{
    private const int W = 24, H = 20;
    private const int Sheet = 3;
    private const int Cols = 4;

    // Tile numbers in the fabricated sheet, 1-based.
    private const int Solid = 1;        // every pixel art
    private const int Empty = 2;        // every pixel transparent
    private const int TopHalf = 3;      // art above the middle, nothing below
    private const int Pillar = 4;       // a four-pixel column down the middle

    [SetUp]
    public void BuildSheet()
    {
        int width = Cols * Constants.PicX, height = Constants.PicY;
        var alpha = new byte[width * height];
        for (int t = 1; t <= Cols; t++)
        {
            var (ox, oy) = TileSheet.Origin(width, t);
            for (int y = 0; y < Constants.PicY; y++)
            {
                for (int x = 0; x < Constants.PicX; x++)
                {
                    bool art = t switch
                    {
                        Solid => true,
                        Empty => false,
                        TopHalf => y < Constants.PicY / 2,
                        _ => x >= Constants.PicX / 2 - 2 && x < Constants.PicX / 2 + 2,
                    };
                    if (art) alpha[(oy + y) * width + ox + x] = 255;
                }
            }
        }

        TileOpacity.Reset();
        TileOpacity.SetSheet(Sheet, alpha, width, height);
    }

    [TearDown]
    public void DropSheet() => TileOpacity.Reset();

    private static int Cell(int tile) => LayerCell.Pack(tile, Sheet, anim: false);

    [Test]
    public void ArtDecidesCoverage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TileOpacity.Of(Cell(Solid)), Is.EqualTo(TileOpacity.Solid), "opaque art covers everything");
            Assert.That(TileOpacity.Of(Cell(Empty)), Is.EqualTo(TileOpacity.Open), "transparent art covers nothing");
            Assert.That(TileOpacity.Of(LayerCell.Empty), Is.EqualTo(TileOpacity.Open), "an empty cell is not art");

            ulong half = TileOpacity.Of(Cell(TopHalf));
            for (int cx = 0; cx < TileOpacity.SubCells; cx++)
            {
                Assert.That(TileOpacity.Covers(half, cx, 0), Is.True, $"top row, column {cx}");
                Assert.That(TileOpacity.Covers(half, cx, TileOpacity.SubCells - 1), Is.False, $"bottom row, column {cx}");
            }
        });
    }

    /// <summary>A shadow is as wide as the thing casting it. The pillar is four pixels across the middle of
    /// its tile, so it covers the two cells those pixels fall in and leaves the rest of the square open.</summary>
    [Test]
    public void ANarrowPillar_CoversOnlyWhatItStandsOn()
    {
        ulong pillar = TileOpacity.Of(Cell(Pillar));
        int mid = TileOpacity.SubCells / 2;

        Assert.Multiple(() =>
        {
            Assert.That(TileOpacity.Covers(pillar, mid - 1, 0), Is.True, "the cell left of centre");
            Assert.That(TileOpacity.Covers(pillar, mid, 0), Is.True, "the cell right of centre");
            Assert.That(TileOpacity.Covers(pillar, 0, 0), Is.False, "the tile's own left edge is open");
            Assert.That(TileOpacity.Covers(pillar, TileOpacity.SubCells - 1, 0), Is.False, "and so is its right");
        });
    }

    [Test]
    public void AnUnloadedSheet_CoversEverything()
    {
        // Nothing to read a silhouette from, so the tile shades its whole square rather than none of it —
        // missing art must never turn a wall into a window.
        Assert.That(TileOpacity.Of(LayerCell.Pack(1, Sheet + 9, anim: false)), Is.EqualTo(TileOpacity.Solid));
    }

    [Test]
    public void TheTopmostCellIsTheSilhouette_NotTheFloorUnderIt()
    {
        var tile = new TileRecord()
            .WithArt(LayerType.Ground, [Cell(Solid), Cell(TopHalf)]);   // an opaque floor with an obstruction laid over it

        ulong shadow = TileOpacity.ShadowOf(tile, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(shadow, Is.EqualTo(TileOpacity.Of(Cell(TopHalf))), "the obstruction is the shape");
            Assert.That(shadow, Is.Not.EqualTo(TileOpacity.Solid), "the floor beneath it is not");
        });
    }

    [Test]
    public void ATileWithNoArtOnThatLayer_ShadesItsWholeSquare()
    {
        Assert.That(TileOpacity.ShadowOf(new TileRecord(), WorldLayer.Ground), Is.EqualTo(TileOpacity.Solid),
            "an invisible barrier is still a barrier");
    }

    [Test]
    public void EachLayerReadsItsOwnStack()
    {
        var tile = new TileRecord()
            .WithArt(LayerType.Ground, [Cell(Solid)])
            .WithArt(LayerType.Fringe, [Cell(TopHalf)]);

        Assert.Multiple(() =>
        {
            Assert.That(TileOpacity.ShadowOf(tile, WorldLayer.Ground), Is.EqualTo(TileOpacity.Solid));
            Assert.That(TileOpacity.ShadowOf(tile, WorldLayer.Fringe), Is.EqualTo(TileOpacity.Of(Cell(TopHalf))));
        });
    }

    // ── What it looks like in a lit scene ─────────────────────────────────────

    private static ClientState SceneWithOccluderAt(int localX, int localY, int topCell)
    {
        var state = new ClientState();
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++) state.NeighborMaps[col, row] = new MapRecord(W, H);
        }

        state.NeighborMaps[1, 1]!.EditTile(localX, localY, t => t
            .WithArt(LayerType.Ground, [Cell(Solid), topCell])
            with { Type = TileType.Blocked });
        return state;
    }

    /// <summary>The headline: an occluder transparent along its lower half stops light through its top and
    /// lets it past underneath, in ONE tile. A tile is lit in parts now.</summary>
    [Test]
    public void AnOccluderTransparentBelow_LetsLightPastItsLowerHalf()
    {
        const int r = 3;
        int lx = W + W / 2, ly = H + H / 2;
        var state = SceneWithOccluderAt(W / 2 + 2, H / 2, Cell(TopHalf));
        var mask = new byte[LightOcclusion.MaskCells(r)];

        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted: true);

        int texels = LightOcclusion.MaskTexels(r);
        int sub = LightOcclusion.SubSamples;
        int col = (2 + r) * sub + sub / 2;              // a column through the occluder, two tiles east
        int tileTop = r * sub;                          // the light's own row of tiles

        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.IsLit(mask[(tileTop + 1) * texels + col]), Is.False, "its solid top stops the light");
            Assert.That(LightOcclusion.IsLit(mask[(tileTop + sub - 1) * texels + col]), Is.True, "and its clear bottom does not");
        });
    }

    /// <summary>An occluder whose art fills its square shades the square, exactly as one with no art does.
    /// Reading shape from the graphic must not turn ordinary walls into windows.</summary>
    [Test]
    public void AnOpaqueOccluder_StillShadesItsWholeSquare()
    {
        const int r = 3;
        int lx = W + W / 2, ly = H + H / 2;
        var state = SceneWithOccluderAt(W / 2 + 2, H / 2, Cell(Solid));
        var mask = new byte[LightOcclusion.MaskCells(r)];

        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted: true);

        int texels = LightOcclusion.MaskTexels(r);
        int sub = LightOcclusion.SubSamples;
        for (int ty = r * sub; ty < (r + 1) * sub; ty++)
        {
            for (int tx = (2 + r) * sub; tx < (3 + r) * sub; tx++)
                Assert.That(LightOcclusion.IsLit(mask[ty * texels + tx]), Is.False, $"texel ({tx},{ty}) of a solid wall is lit");
        }
    }
}

using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// A wall stops light.
///
/// <para>Without this a torch inside a building lights the street through its own wall. The rule is the
/// same straight tile-line the server uses for spell line-of-sight, so a wall that stops a fireball stops
/// the light beside it — and these pin that the two agree.</para>
/// </summary>
[TestFixture]
public class LightOcclusionTests
{
    private const int W = WorldCoordHelper.MapTilesX;   // one map's width, so the centre cell starts here

    private static ClientState StateWithWalls(params (int X, int Y)[] walls)
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = new MapRecord();
        foreach (var (x, y) in walls)
            state.NeighborMaps[1, 1]!.EditTile(x, y, t => t with { Type = TileType.Blocked });
        return state;
    }

    // World coordinates of a tile in the centre map.
    private static (int X, int Y) At(int x, int y) => (W + x, WorldCoordHelper.MapTilesY + y);

    [Test]
    public void OpenGround_IsReached()
    {
        var state = StateWithWalls();
        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.True);
    }

    /// <summary>The case that started this: a light with a wall between it and a tile does not reach it.</summary>
    [Test]
    public void ATileBehindAWall_IsNotReached()
    {
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.False);
    }

    /// <summary>Light stops AT a tile that blocks it, so the tile is not lit. A tile that should catch the
    /// light and be stood on — open water, ground cover — carries <c>BlocksLight</c> false; nothing is
    /// exempted here on its behalf, or the flag would not mean what it says.</summary>
    [Test]
    public void ATileThatBlocksLight_IsNotItselfLit()
    {
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var (wx, wy) = At(7, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, wx, wy), Is.False);
    }

    [Test]
    public void AWallBehindAWall_IsNotLit()
    {
        var state = StateWithWalls((7, 5), (8, 5));
        var (lx, ly) = At(5, 5);
        var (farX, farY) = At(8, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, farX, farY), Is.False);
    }

    [Test]
    public void ALightStandsOnItsOwnTile()
    {
        var state = StateWithWalls((5, 5));
        var (lx, ly) = At(5, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, lx, ly), Is.True);
    }

    /// <summary>Obstacles are read on the light's own layer: a fringe wall shades the deck it stands on,
    /// not the ground beneath it.</summary>
    [Test]
    public void AFringeWall_DoesNotShadeTheGroundBeneath()
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = new MapRecord();
        state.NeighborMaps[1, 1]!.EditTile(7, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.Blocked } });

        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.True,
                "a fringe wall is not on the ground plane");
            Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Fringe, tx, ty), Is.False,
                "on the fringe plane it is a wall like any other");
        });
    }

    private const int Sub = LightOcclusion.SubSamples;

    // The mask is finer than a tile, so a tile is only "reached" if EVERY texel in it is — which is the
    // question that matters: a tile with a dark half has the light stopping inside it.
    private static bool WhollyLit(byte[] mask, int r, int dx, int dy)
    {
        int texels = LightOcclusion.MaskTexels(r);
        for (int sy = 0; sy < Sub; sy++)
        {
            for (int sx = 0; sx < Sub; sx++)
                if (!LightOcclusion.IsLit(mask[((dy + r) * Sub + sy) * texels + (dx + r) * Sub + sx])) return false;
        }
        return true;
    }

    private static bool WhollyDark(byte[] mask, int r, int dx, int dy)
    {
        int texels = LightOcclusion.MaskTexels(r);
        for (int sy = 0; sy < Sub; sy++)
        {
            for (int sx = 0; sx < Sub; sx++)
                if (LightOcclusion.IsLit(mask[((dy + r) * Sub + sy) * texels + (dx + r) * Sub + sx])) return false;
        }
        return true;
    }

    /// <summary>Fill answers over the light's own square, indexed from its tile.</summary>
    [Test]
    public void Fill_MarksTheReachableTilesOverItsOwnSquare()
    {
        const int r = 3;
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var mask = new byte[LightOcclusion.MaskCells(r)];

        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted: true);

        Assert.Multiple(() =>
        {
            Assert.That(WhollyLit(mask, r, 0, 0), Is.True, "its own tile");
            Assert.That(WhollyLit(mask, r, 0, -2), Is.True, "open ground clear of the wall");
            Assert.That(WhollyDark(mask, r, 3, 0), Is.True, "behind the wall");
        });
    }

    /// <summary>
    /// A shadow begins at the edge of the thing casting it, and the ground keeps every texel right up to it.
    ///
    /// <para>Nothing is trimmed back from art. The linear sampler's ramp therefore straddles the boundary and
    /// a hairline of light rides the leading edge of a silhouette — the price of a shadow that lands exactly
    /// on the graphic rather than a texel short of it.</para>
    /// </summary>
    [Test]
    public void TheShadowBeginsAtTheOccludersOwnEdge()
    {
        const int r = 3;
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var mask = new byte[LightOcclusion.MaskCells(r)];

        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask, mounted: true);

        Assert.Multiple(() =>
        {
            Assert.That(WhollyDark(mask, r, 2, 0), Is.True, "the wall takes nothing, to its last texel");
            Assert.That(WhollyLit(mask, r, 1, 0), Is.True,
                "the ground against it keeps every texel — the boundary is the wall's edge, not a texel short");
            Assert.That(WhollyLit(mask, r, 1, 1), Is.True,
                "and the tile diagonal to the wall keeps every one — nothing stands between it and the light");
        });
    }

    /// <summary>The mask covers the light's square and nothing more, so its cost follows the radius
    /// rather than the size of the world around it.</summary>
    [TestCase(0, 64)]
    [TestCase(1, 576)]
    [TestCase(3, 3136)]
    [TestCase(8, 18496)]
    public void TheMask_IsSizedByTheRadiusAlone(int radius, int cells)
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.MaskSide(radius), Is.EqualTo(radius * 2 + 1));
            Assert.That(LightOcclusion.MaskTexels(radius), Is.EqualTo((radius * 2 + 1) * Sub));
            Assert.That(LightOcclusion.MaskCells(radius), Is.EqualTo(cells));
        });
    }

    /// <summary>A map that has not arrived is opaque: light does not spill into a cell nothing is known
    /// about, which is the same answer spell line-of-sight gives.</summary>
    [Test]
    public void AnUnloadedNeighbour_StopsLight()
    {
        var state = StateWithWalls();
        state.NeighborMaps[0, 1] = null;
        var (lx, ly) = At(1, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, W - 3, ly), Is.False);
    }
}

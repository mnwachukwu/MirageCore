using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The mapping that puts a light's reach mask under its halo.
///
/// <para>The mask holds one texel per tile and is sampled with linear filtering, so a wall's edge arrives as
/// a ramp across a tile instead of the 32px stair a per-tile draw leaves behind. That only works if a texel's
/// CENTRE sits on its tile's centre — half a texel out and every shadow is offset by half a tile, which
/// looks like the light is lying about where the wall is.</para>
///
/// <para>The other half of it is that the mask is anchored to the tile the occlusion was TRACED from, not to
/// the halo. A walking player's halo slides sub-tile between tiles while the trace stays on a tile, and the
/// shadows have to stay with the trace — that mismatch is what used to make the shadow pattern jump a whole
/// tile sideways mid-step.</para>
/// </summary>
[TestFixture]
public class LightMaskUvTests
{
    private const int Tile = Constants.PicX;

    // Where a point at `screen` lands in mask UV, given the mapping for that halo.
    private static (float U, float V) Sample(float screenX, float screenY,
        float destLeft, float destTop, float destW, float destH,
        float tileScreenX, float tileScreenY, int r)
    {
        var (sx, sy, ox, oy) = LightOcclusion.MaskUv(destLeft, destTop, destW, destH,
                                                     tileScreenX, tileScreenY, r);
        // The quad's own 0..1 coordinates at that screen point, which is what the pixel shader is handed.
        float u = (screenX - destLeft) / destW, v = (screenY - destTop) / destH;
        return (u * sx + ox, v * sy + oy);
    }

    [Test]
    public void TheEmittersOwnTileCentre_LandsOnTheCentreTexel()
    {
        const int r = 3;
        int side = LightOcclusion.MaskSide(r);            // 7 texels across
        float tileX = 200f, tileY = 120f;                 // the traced tile's top-left on screen
        float radiusPx = r * Tile;
        var (u, v) = Sample(tileX + Tile / 2f, tileY + Tile / 2f,
                            tileX + Tile / 2f - radiusPx, tileY + Tile / 2f - radiusPx,
                            radiusPx * 2f, radiusPx * 2f, tileX, tileY, r);

        // Texel r's centre — the middle of a 7-wide mask — is at (r + 0.5) / 7.
        Assert.That(u, Is.EqualTo((r + 0.5f) / side).Within(1e-5f));
        Assert.That(v, Is.EqualTo((r + 0.5f) / side).Within(1e-5f));
    }

    [Test]
    public void EachTilesCentre_LandsOnItsOwnTexelsCentre()
    {
        const int r = 2;
        int side = LightOcclusion.MaskSide(r);
        float tileX = 64f, tileY = 96f;
        float radiusPx = r * Tile;
        float left = tileX + Tile / 2f - radiusPx, top = tileY + Tile / 2f - radiusPx;

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                var (u, v) = Sample(tileX + dx * Tile + Tile / 2f, tileY + dy * Tile + Tile / 2f,
                                    left, top, radiusPx * 2f, radiusPx * 2f, tileX, tileY, r);
                Assert.That(u, Is.EqualTo((dx + r + 0.5f) / side).Within(1e-5f), $"u at dx={dx}");
                Assert.That(v, Is.EqualTo((dy + r + 0.5f) / side).Within(1e-5f), $"v at dy={dy}");
            }
        }
    }

    [Test]
    public void AHaloThatSlidesSubTile_SlidesAcrossAMaskThatDoesNot()
    {
        const int r = 3;
        float tileX = 200f, tileY = 120f;
        float radiusPx = r * Tile;
        // The emitter has walked 11px east of the tile its occlusion was traced from.
        const float step = 11f;
        float centreX = tileX + Tile / 2f + step, centreY = tileY + Tile / 2f;

        // A fixed point in the WORLD keeps the same place in the mask however far the halo has slid.
        float probeX = tileX + Tile * 2 + Tile / 2f, probeY = tileY + Tile / 2f;
        var still = Sample(probeX, probeY, tileX + Tile / 2f - radiusPx, tileY + Tile / 2f - radiusPx,
                           radiusPx * 2f, radiusPx * 2f, tileX, tileY, r);
        var moved = Sample(probeX, probeY, centreX - radiusPx, centreY - radiusPx,
                           radiusPx * 2f, radiusPx * 2f, tileX, tileY, r);

        Assert.That(moved.U, Is.EqualTo(still.U).Within(1e-5f),
            "The wall is where the wall is; only the halo moved.");
        Assert.That(moved.V, Is.EqualTo(still.V).Within(1e-5f));
    }

    [Test]
    public void TheHaloNeverReachesTheMasksEdge()
    {
        // MaskSide is 2r+1 tiles for a halo 2r tiles wide, so there is half a tile of margin on every side
        // and the linear sampler never has to clamp. A halo that ran to the edge would smear its last texel.
        for (int r = 1; r <= 8; r++)
        {
            float tileX = 0f, tileY = 0f;
            float radiusPx = r * Tile;
            var (sx, _, ox, _) = LightOcclusion.MaskUv(tileX + Tile / 2f - radiusPx, tileY + Tile / 2f - radiusPx,
                                                       radiusPx * 2f, radiusPx * 2f, tileX, tileY, r);
            Assert.That(ox, Is.GreaterThan(0f), $"left margin at r={r}");
            Assert.That(ox + sx, Is.LessThan(1f), $"right margin at r={r}");
        }
    }
}

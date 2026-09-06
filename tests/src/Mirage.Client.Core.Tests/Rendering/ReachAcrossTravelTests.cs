using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// A freely-moving light — a spell bolt — cross-fades between the tile it came from and the one it is
/// crossing into, the same way a walking body does.
///
/// <para>Reach is answered per TILE, so a mask can only change in a jump at a border. A bolt crosses more
/// borders, faster, than anything on legs, so without the blend its shadows snap square as it flies.</para>
///
/// <para>The blend runs 0 at the moment of entry (all of the tile behind) to 1 at the far edge (all of the
/// tile it is in) — the same sense the shader lerps in, and the easiest thing in this to get backwards.</para>
/// </summary>
[TestFixture]
public class ReachAcrossTravelTests
{
    private const int W = 24, H = 20;
    private const int Tile = Constants.PicX;

    private static (ClientState State, Camera Camera) Scene()
    {
        var state = new ClientState { MyIndex = 1 };
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                state.NeighborMaps[col, row] = new MapRecord(W, H);
                state.NeighborMapNums[col, row] = col * 3 + row + 1;
            }
        }

        state.CenterMapNum = state.NeighborMapNums[1, 1];
        var camera = new Camera();
        camera.Update(W / 2, H / 2, 0f, 0f, state.NeighborMapNums, W, H);
        return (state, camera);
    }

    // The tile a screen position belongs to, so a test can say which tile a mask was traced from.
    private static int TileOfScreenX(Camera camera, float screenX)
    {
        for (int t = 0; t < W * 3; t++)
        {
            var (sx, _) = camera.WorldTileToScreen(t, 0, 0f, 0f);
            if (MathF.Abs(sx - screenX) < 0.01f) return t;
        }

        return -1;
    }

    [Test]
    public void MovingRight_ComesFromTheTileBehindAndBlendsForward()
    {
        var (state, camera) = Scene();
        const int into = 20;

        // A quarter of the way across tile 20, heading east.
        var reach = RenderCommandBuilder.ReachAcrossTravel(
            state, camera, (into + 0.25f) * Tile, 10 * Tile + 5, vx: 90f, vy: 0f, WorldLayer.Ground, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(reach.Into, Is.Not.Null, "a moving light traces both tiles");
            Assert.That(TileOfScreenX(camera, reach.FromScreenX), Is.EqualTo(into - 1), "from the tile behind");
            Assert.That(TileOfScreenX(camera, reach.IntoScreenX), Is.EqualTo(into), "into the one it is crossing");
            Assert.That(reach.Blend, Is.EqualTo(0.25f).Within(0.001f), "a quarter across is a quarter blended");
        });
    }

    [Test]
    public void MovingLeft_ComesFromTheOtherSide()
    {
        var (state, camera) = Scene();
        const int into = 20;

        var reach = RenderCommandBuilder.ReachAcrossTravel(
            state, camera, (into + 0.25f) * Tile, 10 * Tile + 5, vx: -90f, vy: 0f, WorldLayer.Ground, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(TileOfScreenX(camera, reach.FromScreenX), Is.EqualTo(into + 1), "from the tile to its east");
            // Entering from the east, a quarter past the west edge is three quarters of the way across.
            Assert.That(reach.Blend, Is.EqualTo(0.75f).Within(0.001f));
        });
    }

    [Test]
    public void TheBlendRunsFromEntryToExit()
    {
        var (state, camera) = Scene();
        float At(float frac) => RenderCommandBuilder.ReachAcrossTravel(
            state, camera, (20 + frac) * Tile, 10 * Tile, 90f, 0f, WorldLayer.Ground, 2f).Blend;

        Assert.Multiple(() =>
        {
            Assert.That(At(0f), Is.EqualTo(0f).Within(0.001f), "at the border it is still all the tile behind");
            Assert.That(At(0.5f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(At(0.99f), Is.EqualTo(0.99f).Within(0.001f), "and at the far edge it is all this one");
        });
    }

    [Test]
    public void TheDominantAxisWins()
    {
        var (state, camera) = Scene();

        // Mostly southward: the pair is vertical, so the from-tile shares this one's column.
        var reach = RenderCommandBuilder.ReachAcrossTravel(
            state, camera, 20 * Tile + 8, (10 + 0.5f) * Tile, vx: 10f, vy: 200f, WorldLayer.Ground, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(reach.FromScreenX, Is.EqualTo(reach.IntoScreenX), "a vertical pair keeps the column");
            Assert.That(reach.FromScreenY, Is.LessThan(reach.IntoScreenY), "and comes from the tile above");
            Assert.That(reach.Blend, Is.EqualTo(0.5f).Within(0.001f));
        });
    }

    /// <summary>A burst that is going nowhere has no border to blend across, and pays for one trace — the same
    /// rule that keeps a standing emitter from tracing twice.</summary>
    [Test]
    public void AStationaryBurst_BlendsNothing()
    {
        var (state, camera) = Scene();

        var reach = RenderCommandBuilder.ReachAcrossTravel(
            state, camera, 20 * Tile + 8, 10 * Tile + 8, vx: 0f, vy: 0f, WorldLayer.Ground, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(reach.Into, Is.Null);
            Assert.That(reach.Blend, Is.EqualTo(0f));
            Assert.That(reach.From, Is.Not.Null);
        });
    }
}

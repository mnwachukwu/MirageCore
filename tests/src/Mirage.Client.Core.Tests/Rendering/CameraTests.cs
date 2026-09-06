using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>The seamless-world camera follows the local player on the center map but clamps to the known
/// 3x3 grid's extent, and its tile↔screen transforms round-trip. With no neighbors it locks to the
/// center map exactly, reproducing the original single-map view (screenX == localX * PicX).</summary>
[TestFixture]
public class CameraTests
{
    // A 3x3 grid with only the center cell populated (the "no neighbors loaded" case).
    static int[,] CenterOnly()
    {
        var g = new int[3, 3];
        g[1, 1] = 1;
        return g;
    }

    static int[,] FullGrid()
    {
        var g = new int[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = c * 3 + r + 1;
        }

        return g;
    }

    // With no neighbors the camera locks to the center map regardless of player position: it pins to the
    // center map's top-left world pixel (512, 384) so the view never scrolls into the black void.
    [Test]
    public void Update_NoNeighbors_LocksToCenterMap()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(512f));
            Assert.That(cam.CameraY, Is.EqualTo(384f));
        });

        // Player at the far corner of the center map: still locked (no neighbor to scroll toward).
        cam.Update(Constants.MaxMapX, Constants.MaxMapY, 0f, 0f, CenterOnly(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(512f));
            Assert.That(cam.CameraY, Is.EqualTo(384f));
        });
    }

    // The documented invariant: locked to center, a center-map local tile lands at screenX == localX*PicX.
    [Test]
    public void WorldTileToScreen_NoNeighbors_MatchesSingleMapView()
    {
        var cam = new Camera();
        cam.Update(8, 6, 0f, 0f, CenterOnly(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        for (int lx = 0; lx <= Constants.MaxMapX; lx++)
        {
            int worldX = WorldCoordHelper.MapTilesX + lx;
            var (sx, _) = cam.WorldTileToScreen(worldX, WorldCoordHelper.MapTilesY, 0f, 0f);
            Assert.That(sx, Is.EqualTo(lx * Constants.PicX), $"local x {lx}");
        }
    }

    // With every neighbor present the camera follows the player (centers them) and does not clamp mid-map.
    [Test]
    public void Update_FullGrid_FollowsPlayer()
    {
        var cam = new Camera();
        cam.Update(8, 6, 0f, 0f, FullGrid(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        float pwx = (WorldCoordHelper.MapTilesX + 8) * Constants.PicX;
        float pwy = (WorldCoordHelper.MapTilesY + 6) * Constants.PicY;
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(pwx - Camera.ViewW / 2f));
            Assert.That(cam.CameraY, Is.EqualTo(pwy - Camera.ViewH / 2f));
        });
    }

    // Scrolling clamps to the grid edge: with only the center map, panning is locked; with a left column
    // present the camera can scroll one map-width further left than the center-only clamp.
    [Test]
    public void Update_LeftColumnPresent_AllowsScrollingLeft()
    {
        var withLeft = new int[3, 3];
        withLeft[1, 1] = 1;
        withLeft[0, 1] = 2;   // a map exists to the left

        var cam = new Camera();
        cam.Update(0, 6, 0f, 0f, withLeft, Constants.DefaultMapWidth, Constants.DefaultMapHeight);   // player at the left edge of the center map
        // minCamX is now 0 (left column present), so the camera scrolls left of the center-only lock (512).
        Assert.That(cam.CameraX, Is.LessThan(512f));
    }

    // ScreenToGridTile is the inverse of the tile→screen map: the center-map origin round-trips to (1,1,0,0).
    [Test]
    public void ScreenToGridTile_InvertsWorldTileToScreen()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);   // locked: CameraX=512, CameraY=384
        // Center map local (0,0) is world (16,12); its screen pos with the locked camera is (0,0).
        Assert.That(cam.ScreenToGridTile(0, 0), Is.EqualTo(new GridTileHit(Col: 1, Row: 1, LocalX: 0, LocalY: 0)));
    }

    // Pixels off the top/left of the world (negative world tile) are on no cell.
    [Test]
    public void ScreenToGridTile_OffWorld_ReturnsNull()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, FullGrid(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        Assert.That(cam.ScreenToGridTile(-300, -300), Is.Null);
    }

    [Test]
    public void IsWorldTileVisible_CenterTileVisible_FarTileNot()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly(), Constants.DefaultMapWidth, Constants.DefaultMapHeight);   // CameraX=512, CameraY=384
        Assert.Multiple(() =>
        {
            Assert.That(cam.IsWorldTileVisible(16, 12), Is.True, "center-map origin is on-screen");
            Assert.That(cam.IsWorldTileVisible(40, 12), Is.False, "a tile far to the right is culled");
        });
    }

    // ── Arriving somewhere new ───────────────────────────────────────────────

    /// <summary>
    /// A neighbour that EXISTS but has not finished loading still lets the camera scroll toward it.
    ///
    /// <para>This is what stops a warp from lurching. The eight neighbour numbers land together the moment
    /// the server describes the new surroundings, while each map's data resolves separately over however
    /// many frames the cache or the wire takes. If the reach were computed from what had finished loading,
    /// it would widen one arrival at a time and snap the view with each — most visibly where the destination
    /// clamps differently from the origin, which is a one-room interior opening onto a town.</para>
    ///
    /// <para>Stated as "the camera does not sit at the locked position", because the locked value IS the
    /// bug: it is where a camera that believed itself surrounded by nothing would park.</para>
    /// </summary>
    [Test]
    public void Update_NeighborKnownButNotYetLoaded_AlreadyScrolls()
    {
        const float LockedToCenter = 512f;
        var justArrived = new int[3, 3];
        justArrived[1, 1] = 1;
        justArrived[0, 1] = 2;   // named by the server; its tiles are still resolving

        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, justArrived, Constants.DefaultMapWidth, Constants.DefaultMapHeight);

        Assert.That(cam.CameraX, Is.LessThan(LockedToCenter),
            "the camera reaches toward a map it knows is there, rather than waiting for it to download");
    }

    /// <summary>The eight numbers arrive in one batch, so the bounds settle ONCE. Reading the same grid
    /// again — as every later frame does while the maps stream in — must not move the camera again.</summary>
    [Test]
    public void Update_IsStableWhileTheMapsBehindItStillLoad()
    {
        var known = FullGrid();
        var cam = new Camera();

        cam.Update(8, 6, 0f, 0f, known, Constants.DefaultMapWidth, Constants.DefaultMapHeight);
        float settledX = cam.CameraX, settledY = cam.CameraY;
        for (int frame = 0; frame < 5; frame++) cam.Update(8, 6, 0f, 0f, known, Constants.DefaultMapWidth, Constants.DefaultMapHeight);

        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(settledX));
            Assert.That(cam.CameraY, Is.EqualTo(settledY));
        });
    }
}

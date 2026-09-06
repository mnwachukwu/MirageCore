using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Centers the viewport on the local player within the seamless 3×3 map world.
/// The camera follows the player but clamps to the extent of the world that EXISTS
/// around them: it stops scrolling in a direction only when the entire row/column on
/// that side holds no map.  So a column that holds a map only diagonally (e.g. the
/// left column when just an up-left map exists) is still reachable — empty cells in
/// it simply render black.  Where nothing surrounds the center map at all the camera
/// locks to it, reproducing the original single-map view exactly
/// (screenX = localX * PicX) — which is what a one-room interior looks like.
/// </summary>
public sealed class Camera
{
    // The window, in pixels. A property of the render target, fixed however large the maps are.
    public const int ViewW = Constants.ViewportTilesX * Constants.PicX; // 512
    public const int ViewH = Constants.ViewportTilesY * Constants.PicY; // 384

    /// <summary>One map's size in tiles, taken from the center map on each <see cref="Update"/>. The scroll
    /// bounds and the screen-to-tile inverse are both measured in maps, so they move when the world does.
    /// Starts at the default so a camera that has not seen a map yet still answers.</summary>
    public int MapTilesX { get; private set; } = Constants.DefaultMapWidth;

    /// <inheritdoc cref="MapTilesX"/>
    public int MapTilesY { get; private set; } = Constants.DefaultMapHeight;

    private int MapPxW => MapTilesX * Constants.PicX;
    private int MapPxH => MapTilesY * Constants.PicY;

    /// <summary>World-pixel coordinate of the viewport's top-left corner (true, sub-pixel value).</summary>
    public float CameraX { get; private set; }
    public float CameraY { get; private set; }

    /// <summary>
    /// Recompute the camera from the local player's position on the center map.
    /// <paramref name="neighborMapNums"/> is the 3×3 cell grid of map NUMBERS ([col,row], center [1,1]);
    /// a cell holds 0 where the world has no map. Scrolling in a direction is allowed as long as that
    /// side's row/column names any map at all; it clamps only when the whole row/column is empty.
    ///
    /// <para><b>Map NUMBERS, not loaded map records, and that distinction is the whole point.</b> The
    /// numbers for all eight neighbours arrive together in one batch the moment the server describes the
    /// new surroundings; each map's DATA then resolves separately, from disk cache or over the wire, over
    /// however many frames that takes. Clamping on what has finished loading makes the camera's reach grow
    /// one arrival at a time, so a warp into a town wide enough to scroll snaps the view repeatedly as its
    /// neighbours land — worst where the destination's clamping differs from the origin's, which is exactly
    /// a single-room interior opening onto open ground. Clamping on what EXISTS settles the bounds once.</para>
    ///
    /// <para>A cell that is named but not yet loaded renders black, which is the same thing that already
    /// happens for a diagonal-only neighbour — an accepted, momentary state rather than a new one.</para>
    /// </summary>
    public void Update(int playerLocalX, int playerLocalY, float xOffset, float yOffset, int[,] neighborMapNums,
                       int mapTilesX, int mapTilesY)
    {
        MapTilesX = mapTilesX;
        MapTilesY = mapTilesY;

        // Center map sits at grid (1,1) → world tile origin (MapTilesX, MapTilesY).
        float pwx = (MapTilesX + playerLocalX) * Constants.PicX + xOffset;
        float pwy = (MapTilesY + playerLocalY) * Constants.PicY + yOffset;

        float camX = pwx - ViewW / 2f;
        float camY = pwy - ViewH / 2f;

        bool hasLeftCol = neighborMapNums[0, 0] > 0 || neighborMapNums[0, 1] > 0 || neighborMapNums[0, 2] > 0;
        bool hasRightCol = neighborMapNums[2, 0] > 0 || neighborMapNums[2, 1] > 0 || neighborMapNums[2, 2] > 0;
        bool hasTopRow = neighborMapNums[0, 0] > 0 || neighborMapNums[1, 0] > 0 || neighborMapNums[2, 0] > 0;
        bool hasBotRow = neighborMapNums[0, 2] > 0 || neighborMapNums[1, 2] > 0 || neighborMapNums[2, 2] > 0;

        // Scroll bounds reach the grid edge when that side has any map, else the center edge.
        float minCamX = hasLeftCol ? 0f : MapPxW;
        float maxCamX = (hasRightCol ? 3 * MapPxW : 2 * MapPxW) - ViewW;
        float minCamY = hasTopRow ? 0f : MapPxH;
        float maxCamY = (hasBotRow ? 3 * MapPxH : 2 * MapPxH) - ViewH;

        CameraX = Math.Clamp(camX, minCamX, maxCamX);
        CameraY = Math.Clamp(camY, minCamY, maxCamY);
    }

    /// <summary>Screen position (FLOAT, sub-pixel) of a world tile's top-left, including sub-tile
    /// movement offset.  Kept sub-pixel so the supersampled world target scrolls smoothly AND the
    /// camera-centered player lands on an exact pixel (no wobble); the composite is a plain downscale
    /// with no fractional slide.</summary>
    public (float sx, float sy) WorldTileToScreen(int worldTileX, int worldTileY, float xOff, float yOff)
        => (worldTileX * Constants.PicX + xOff - CameraX,
            worldTileY * Constants.PicY + yOff - CameraY);

    /// <summary>True if a world tile is at least partly inside the 512×384 viewport.</summary>
    public bool IsWorldTileVisible(int worldTileX, int worldTileY)
    {
        float sx = worldTileX * Constants.PicX - CameraX;
        float sy = worldTileY * Constants.PicY - CameraY;
        return sx > -Constants.PicX && sx < ViewW && sy > -Constants.PicY && sy < ViewH;
    }

    /// <summary>
    /// Inverse of the tile→screen mapping: which grid cell + local tile a screen
    /// pixel falls on.  Returns null for pixels outside the loaded 3×3 area or off
    /// the top/left of the world.
    /// </summary>
    public GridTileHit? ScreenToGridTile(int screenX, int screenY)
    {
        int worldX = (int)Math.Floor((screenX + CameraX) / Constants.PicX);
        int worldY = (int)Math.Floor((screenY + CameraY) / Constants.PicY);
        if (worldX < 0 || worldY < 0) return null;
        int col = worldX / MapTilesX;
        int row = worldY / MapTilesY;
        if (col < 0 || col > 2 || row < 0 || row > 2) return null;
        int localX = worldX % MapTilesX;
        int localY = worldY % MapTilesY;
        return new GridTileHit(col, row, localX, localY);
    }
}

/// <summary>Where a screen pixel landed: which cell of the loaded 3×3 map grid
/// (<see cref="Col"/>, <see cref="Row"/>) and which tile within that map
/// (<see cref="LocalX"/>, <see cref="LocalY"/>). Named rather than a four-<c>int</c> tuple — the two
/// pairs are in different coordinate spaces, and swapping one for the other reads fine and points at
/// the wrong map.</summary>
public readonly record struct GridTileHit(int Col, int Row, int LocalX, int LocalY);

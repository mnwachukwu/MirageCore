using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Shared math for the seamless 3×3 scrolling-map world.  Used by both the client
/// (rendering / camera) and the server (cross-map combat, targeting, AI).
///
/// A 3×3 virtual grid is anchored to the player's current ("center") map at grid
/// position [col=1, row=1].  World-tile coordinates place the center map's local
/// tile (0,0) at world tile (MapTilesX, MapTilesY) so neighbors fit on every side:
///
///   [UL 0,0] [U 1,0] [UR 2,0]
///   [L  0,1] [C 1,1] [R  2,1]
///   [DL 0,2] [D 1,2] [DR 2,2]
///
/// All math is integer.  Diagonal neighbor resolution mirrors the editor exactly
/// (try cardinal→cardinal both ways, first non-zero wins).
/// </summary>
public static class WorldCoordHelper
{
    /// <summary>The default map size in tiles — the stride this file's world-coordinate math assumes.
    /// A map's own size is <c>MapRecord.Width</c> / <c>Height</c>.</summary>
    public const int MapTilesX = Constants.DefaultMapWidth;
    public const int MapTilesY = Constants.DefaultMapHeight;

    /// <summary>The camera's window in tiles. Equal to the default map size today and independent of it
    /// by construction — see <see cref="Constants.ViewportTilesX"/>.</summary>
    public const int ViewportTilesX = Constants.ViewportTilesX;
    public const int ViewportTilesY = Constants.ViewportTilesY;

    // World coordinates are computed through MapGrid — see MapGrid.ToWorld / ResolveWorldTile. There is no
    // constant-strided version on purpose: one would read the DEFAULT map size and silently give the wrong
    // answer for every neighbourhood that is not that size.

    private static MapRecord? MapAt(MapRecord?[] maps, int num)
        => num > 0 && num < maps.Length ? maps[num] : null;

    private static int FirstNonZero(int a, int b) => a != 0 ? a : b;

    /// <summary>
    /// Builds the 3×3 grid of map numbers around <paramref name="centerMapNum"/>.
    /// Center is [1,1]; 0 means "no map linked in that cell".  Diagonals are derived the same way the
    /// editor does (see MapEditorViewModel NeighborMapUpLeft etc.): Up→Left first, then Left→Up fallback.
    ///
    /// Returns a <see cref="MapGrid"/> value (struct) rather than a heap <c>int[3,3]</c>: this is called
    /// per NPC per tick by the AI target scans and per chasing NPC per tick by <see cref="ToWorldRelative"/>,
    /// so allocating an array each time would churn the GC for nothing.  A stack value costs nothing.
    /// </summary>
    public static MapGrid BuildMapGrid(MapRecord?[] allMaps, int centerMapNum)
    {
        var c = MapAt(allMaps, centerMapNum);
        // No center map to measure, so the grid takes the default size. Every cell is 0 anyway.
        if (c is null) return new MapGrid(0, 0, 0, 0, centerMapNum, 0, 0, 0, 0, MapTilesX, MapTilesY);

        var up = MapAt(allMaps, c.Up);
        var down = MapAt(allMaps, c.Down);
        var left = MapAt(allMaps, c.Left);
        var right = MapAt(allMaps, c.Right);

        return new MapGrid(
            FirstNonZero(up?.Left ?? 0, left?.Up ?? 0),     // [0,0] UpLeft
            c.Up,                                            // [1,0] Up
            FirstNonZero(up?.Right ?? 0, right?.Up ?? 0),    // [2,0] UpRight
            c.Left,                                          // [0,1] Left
            centerMapNum,                                    // [1,1] Center
            c.Right,                                         // [2,1] Right
            FirstNonZero(down?.Left ?? 0, left?.Down ?? 0),  // [0,2] DownLeft
            c.Down,                                          // [1,2] Down
            FirstNonZero(down?.Right ?? 0, right?.Down ?? 0),// [2,2] DownRight
            // The center's size is the whole grid's: a map only links to maps its own size.
            c.Width, c.Height
        );
    }

    /// <summary>
    /// Grid (col,row) of <paramref name="queryMapNum"/> relative to the center map,
    /// or null if it is not one of the 9 observable maps.  Center is preferred, then
    /// cardinals, then diagonals (handles small worlds where a map repeats in cells).
    /// </summary>
    public static (int col, int row)? GridPosition(MapRecord?[] allMaps, int centerMapNum, int queryMapNum)
    {
        if (queryMapNum <= 0) return null;
        if (queryMapNum == centerMapNum) return (1, 1);
        return GridPosition(BuildMapGrid(allMaps, centerMapNum), queryMapNum);
    }

    /// <summary>
    /// Grid (col,row) of <paramref name="queryMapNum"/> within a pre-built grid, or null if it isn't one
    /// of the 9 cells.  Lets a caller that scans many entities build the grid once and reuse it (e.g. an
    /// NPC's 9-map target search) instead of rebuilding per query.  Reads the struct fields directly
    /// (passed by <c>in</c> to avoid copying); center first, then cardinals, then diagonals so a map that
    /// repeats in a tiny world resolves to its nearest cell.
    /// </summary>
    public static (int col, int row)? GridPosition(in MapGrid g, int queryMapNum) => g.PositionOf(queryMapNum);

    /// <summary>
    /// World-tile coordinate of an entity, expressed relative to <paramref name="centerMapNum"/>.
    /// Returns null when the entity's map is not observable from the center.
    /// </summary>
    public static (int worldX, int worldY)? ToWorldRelative(
        MapRecord?[] allMaps, int centerMapNum, int entityMapNum, int localX, int localY)
    {
        if (entityMapNum == centerMapNum)
        {
            var self = MapAt(allMaps, centerMapNum);
            return self is null ? null : (self.Width + localX, self.Height + localY);
        }
        return BuildMapGrid(allMaps, centerMapNum).ToWorldRelative(entityMapNum, localX, localY);
    }

    public static int WorldManhattan(int ax, int ay, int bx, int by)
        => Math.Abs(ax - bx) + Math.Abs(ay - by);

    public static bool IsWorldAdjacent(int ax, int ay, int bx, int by)
        => WorldManhattan(ax, ay, bx, by) == 1;

    /// <summary>(dx,dy) unit step for a facing direction.</summary>
    public static (int dx, int dy) DirDelta(Direction dir) => dir switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        _ => (0, 0),
    };

    /// <summary>True if (bx,by) is exactly one tile from (ax,ay) in the facing direction.</summary>
    public static bool IsAdjacentInDir(int ax, int ay, Direction dir, int bx, int by)
    {
        var (dx, dy) = DirDelta(dir);
        return bx == ax + dx && by == ay + dy;
    }

    // ── Variable-size NPC footprints (world-tile space) ──────────────────────
    // A size-S NPC occupies an SxS block of tiles anchored at its TOP-LEFT tile, so the body extends
    // toward +x/+y.  All footprint math lives in world-tile coords (ToWorld/ResolveWorldTile) so a
    // footprint that spills across a seamless-map seam is handled uniformly.  For S=1 these reduce to
    // the classic single-tile behavior.

    /// <summary>True if world tile (wx,wy) lies within the SxS footprint whose top-left anchor is
    /// (anchorWX,anchorWY).  For size 1 this is just the anchor tile.</summary>
    public static bool FootprintContains(int anchorWX, int anchorWY, int size, int wx, int wy)
        => wx >= anchorWX && wx < anchorWX + size && wy >= anchorWY && wy < anchorWY + size;

    /// <summary>True if two SxS footprints (top-left anchors A and B, sizes in tiles) share ANY tile — their
    /// per-axis spans overlap on both axes. For size 1 on both this is just A == B. Used by the editor's
    /// size-aware placement collision checks and any runtime footprint-overlap test.</summary>
    public static bool FootprintsOverlap(int aX, int aY, int aSize, int bX, int bY, int bSize)
        => RectAxisGap(aX, aSize, bX, bSize) == 0 && RectAxisGap(aY, aSize, bY, bSize) == 0;

    /// <summary>True when two SxS footprints (top-left anchors A and B) touch orthogonally — one tile of gap on
    /// one axis while the spans overlap on the other.  This is melee reach measured EDGE to EDGE: two size-3
    /// bodies standing face to face sit 3 tiles apart anchor to anchor, so an anchor-distance test reads them as
    /// far apart and neither can ever swing.  Symmetric in A and B, so reach is the same read from either body.
    /// Size 1 on both sides is exactly <see cref="IsWorldAdjacent"/> — diagonals excluded.</summary>
    public static bool AreFootprintsAdjacent(int aX, int aY, int aSize, int bX, int bY, int bSize)
    {
        int dx = RectAxisGap(aX, aSize, bX, bSize);
        int dy = RectAxisGap(aY, aSize, bY, bSize);
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }

    /// <summary>The run of <paramref name="size"/> world tiles immediately beyond a footprint's leading
    /// edge in <paramref name="dir"/> - the tiles a size-S body would step INTO (movement validation) or
    /// STRIKE (melee) when facing/moving that way.  Anchor is the footprint's top-left tile.  For size 1
    /// this is the single tile in front.  The run steps along the edge (positive unit step), so both the
    /// indexer and <see cref="TileRun.Contains"/> are valid.</summary>
    public static TileRun LeadingEdgeTiles(int anchorWX, int anchorWY, int size, Direction dir) => dir switch
    {
        Direction.Right => new TileRun(anchorWX + size, anchorWY, 0, 1, size),
        Direction.Left => new TileRun(anchorWX - 1, anchorWY, 0, 1, size),
        Direction.Down => new TileRun(anchorWX, anchorWY + size, 1, 0, size),
        Direction.Up => new TileRun(anchorWX, anchorWY - 1, 1, 0, size),
        _ => new TileRun(anchorWX, anchorWY, 0, 1, 0),
    };

    /// <summary>
    /// Which side of an SxS body the tile (wx,wy) lies beyond — the direction that body must FACE for
    /// <see cref="LeadingEdgeTiles"/> to cover that tile.
    ///
    /// <para>🔴 Read the direction off the ANCHOR instead and a big NPC swings at empty air. A player standing
    /// above the right-hand column of a size-3 body is one tile from it, but the anchor is two columns to the
    /// left, so anchor math calls that "to the right" and puts the leading edge somewhere the player is not.
    /// Measured from the body, only the axis the tile actually overshoots can win, and it comes out Up.</para>
    ///
    /// <para>Ties go to the horizontal, matching <see cref="WorldDirectionFrom"/> — which this reduces to
    /// exactly at size 1, where the body IS the anchor. A tile INSIDE the body has no side to be beyond, so it
    /// defers to the anchor reading; nothing legal can stand there.</para>
    /// </summary>
    public static Direction FootprintFacingToward(int anchorWX, int anchorWY, int size, int wx, int wy)
    {
        int gx = AxisOvershoot(anchorWX, size, wx);
        int gy = AxisOvershoot(anchorWY, size, wy);
        if (gx == 0 && gy == 0) return WorldDirectionFrom(anchorWX, anchorWY, wx, wy);
        if (Math.Abs(gx) >= Math.Abs(gy)) return gx >= 0 ? Direction.Right : Direction.Left;
        return gy >= 0 ? Direction.Down : Direction.Up;
    }

    /// <summary>How far past a footprint's span on one axis a coordinate sits, signed away from the body;
    /// 0 while it is level with the body on that axis.</summary>
    private static int AxisOvershoot(int anchor, int size, int v)
        => v < anchor ? v - anchor
         : v >= anchor + size ? v - (anchor + size - 1)
         : 0;

    /// <summary>The signed per-axis offset from a BODY to a tile: how far it would have to travel on each axis
    /// to touch, and which way. Zero on an axis the body is already level with. At size 1 this is the plain
    /// coordinate difference.</summary>
    public static (int X, int Y) FootprintOffsetTo(int anchorWX, int anchorWY, int size, int wx, int wy)
        => (AxisOvershoot(anchorWX, size, wx), AxisOvershoot(anchorWY, size, wy));

    /// <summary>
    /// Manhattan distance measured EDGE to EDGE between two bodies — the tiles that actually separate them,
    /// which is 0 when they overlap and 1 exactly when <see cref="AreFootprintsAdjacent"/> holds.
    ///
    /// <para>Anchor-to-anchor overstates it by up to <c>size - 1</c> per axis, and only on the +x/+y sides,
    /// so an anchor reading is not merely wrong but LOPSIDED: a big NPC reads as further from something at its
    /// bottom-right than from the same thing at its top-left. Every distance an NPC compares against a
    /// threshold — chase pace, whether a step closed ground, how near the nearest candidate is — has to be
    /// this one, or the threshold means a different thing on each side of the body.</para>
    ///
    /// <para>Symmetric, and identical to <see cref="WorldManhattan"/> when both sides are size 1.</para>
    /// </summary>
    public static int FootprintManhattan(int aX, int aY, int aSize, int bX, int bY, int bSize)
        => RectAxisGap(aX, aSize, bX, bSize) + RectAxisGap(aY, aSize, bY, bSize);

    /// <summary>True when two bodies are within <paramref name="range"/> tiles of each other on BOTH axes,
    /// measured edge to edge — the square band an NPC notices things in. At size 1 on both sides this is the
    /// classic anchor box.</summary>
    public static bool AreFootprintsWithin(int aX, int aY, int aSize, int bX, int bY, int bSize, int range)
        => RectAxisGap(aX, aSize, bX, bSize) <= range && RectAxisGap(aY, aSize, bY, bSize) <= range;

    /// <summary>
    /// The single authority for "is this target in casting/targeting range."  Range is the
    /// 16×12 viewport a player sees <i>when centered</i> — a fixed band of -8..+7 tiles in X
    /// and -6..+5 in Y around the actor, regardless of the actual camera.
    ///
    /// This is deliberately camera-independent: when the client camera clamps at an
    /// unpopulated border the player can <i>render</i> more tiles to one side, but that must
    /// never grant extra range.  The server enforces this authoritatively.  The client may
    /// let the player select any visible target; it uses this same check only to signal
    /// castability visually (target-arrow color), never to widen range.  The camera's own
    /// visibility test is for render culling only.
    /// </summary>
    public static bool IsWithinViewport(int playerWorldX, int playerWorldY, int targetWorldX, int targetWorldY)
    {
        int dx = targetWorldX - playerWorldX;
        int dy = targetWorldY - playerWorldY;
        return dx >= -(ViewportTilesX / 2) && dx <= (ViewportTilesX / 2) - 1
            && dy >= -(ViewportTilesY / 2) && dy <= (ViewportTilesY / 2) - 1;
    }

    /// <summary>
    /// Spell-cast range: a pure Pythagorean circle of radius 5 around the caster, with the same reach
    /// cardinally and diagonally.
    ///
    /// <para>A wider-than-tall rectangle would let mages hit farther on X than on Y, and the meta that
    /// falls out of that is mages keeping prey on the long axis while melee closes on the short one. The
    /// circle removes the directional advantage entirely. R=5 is the largest symmetric circle fitting the
    /// 16×12 viewport, limited by the short half-extent of 5 in Y; larger means reach beyond what is
    /// rendered, or asymmetry.</para>
    ///
    /// <para>The viewport's four corner wings stay visible: you can SEE entities out there and not cast on
    /// them. Visibility and earshot still use <see cref="IsWithinViewport"/>'s asymmetric rectangle.</para>
    ///
    /// Inherently two-way: if A is within B's circle, B is within A's circle (distance is
    /// symmetric in (dx,dy)), so no separate "mutual range" check is needed for PvP fairness.
    /// </summary>
    public static bool IsInSpellRange(int playerWorldX, int playerWorldY, int targetWorldX, int targetWorldY) =>
        IsInSpellRange(playerWorldX, playerWorldY, 1, targetWorldX, targetWorldY, 1);

    /// <summary>Footprint-aware spell-circle test: true when the NEAREST tiles of two SxS footprints (top-left
    /// anchors A and B, sizes in tiles) fall within the r=5 circle.  So an oversize NPC is targetable when ANY
    /// tile of its body is inside the caster's circle (not just its anchor), and an oversize NPC caster reaches
    /// from its body edge.  <see cref="RectAxisGap"/> is symmetric, so this stays inherently two-way (PvP-fair)
    /// at any sizes; size 1 on both sides is exactly the plain point check above.</summary>
    public static bool IsInSpellRange(int aWorldX, int aWorldY, int aSize, int bWorldX, int bWorldY, int bSize)
    {
        int dx = RectAxisGap(aWorldX, aSize, bWorldX, bSize);
        int dy = RectAxisGap(aWorldY, aSize, bWorldY, bSize);
        const int r = Constants.SpellRangeTiles;
        return dx * dx + dy * dy <= r * r;
    }

    // Minimum tile gap between two 1-D spans [aMin, aMin+aSize-1] and [bMin, bMin+bSize-1]; 0 if they overlap/touch.
    private static int RectAxisGap(int aMin, int aSize, int bMin, int bSize)
    {
        int aMax = aMin + aSize - 1, bMax = bMin + bSize - 1;
        if (aMax < bMin) return bMin - aMax;   // A entirely below/left of B
        if (bMax < aMin) return aMin - bMax;   // B entirely below/left of A
        return 0;                              // spans overlap on this axis
    }

    /// <summary>
    /// Straight-line tile traversal from (from*) to (to*) in world-tile coords; false the moment
    /// the line crosses a tile the caller's <paramref name="isBlockedAt"/> reports as blocking.
    /// Both endpoints are skipped — the caster sits on one, the target on the other; neither is
    /// its own obstacle. Integer Bresenham, so the trace is symmetric (caster→target == target→caster).
    ///
    /// Movement is cardinal-only, so a diagonal step also fails when BOTH perpendicular corner
    /// tiles are blocked: that pair forms an impassable wall even though the diagonal line itself
    /// slips between them — a spell shouldn't fly through a corner a chasing NPC can't walk through.
    ///
    /// Used by the server's authoritative cast gate and the client's target-arrow color so the
    /// gray arrow never lies about whether the cast will land.
    /// </summary>
    public static bool HasClearSpellLineOfSight<TPredicate>(
        int fromWorldX, int fromWorldY,
        int toWorldX, int toWorldY,
        TPredicate isBlockedAt) where TPredicate : struct, ISpellLosPredicate
    {
        // Direction-independence: standard Bresenham picks alternate minor-axis tiles based on
        // which endpoint the trace starts at, so without this normalization A→B could clear LoS
        // while B→A fails it on the same wall placement. Anchor the trace at the lexically smaller
        // endpoint so caster→target and target→caster always walk the same tiles.
        if (fromWorldX > toWorldX || (fromWorldX == toWorldX && fromWorldY > toWorldY))
        {
            (fromWorldX, toWorldX) = (toWorldX, fromWorldX);
            (fromWorldY, toWorldY) = (toWorldY, fromWorldY);
        }
        int dx = Math.Abs(toWorldX - fromWorldX);
        int dy = Math.Abs(toWorldY - fromWorldY);
        int sx = fromWorldX < toWorldX ? 1 : -1;
        int sy = fromWorldY < toWorldY ? 1 : -1;
        int err = dx - dy;
        int cx = fromWorldX, cy = fromWorldY;
        while (cx != toWorldX || cy != toWorldY)
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
                && isBlockedAt.IsBlocked(cx + stepX, cy) && isBlockedAt.IsBlocked(cx, cy + stepY))
            {
                return false;
            }

            cx += stepX;
            cy += stepY;
            if (cx == toWorldX && cy == toWorldY) return true;
            if (isBlockedAt.IsBlocked(cx, cy)) return false;
        }
        return true;
    }

    /// <summary>Single-step direction from (ax,ay) toward (bx,by); moves along the longer axis first.</summary>
    public static Direction WorldDirectionFrom(int ax, int ay, int bx, int by)
    {
        int dx = bx - ax;
        int dy = by - ay;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? Direction.Right : Direction.Left;
        return dy >= 0 ? Direction.Down : Direction.Up;
    }
}

/// <summary>
/// Tile-blocking predicate for <see cref="WorldCoordHelper.HasClearSpellLineOfSight"/>.  Always
/// implemented as a <c>readonly struct</c> so the generic call site is JIT-specialized: no boxing,
/// no virtual dispatch on <see cref="IsBlocked"/>, no per-call closure allocation.  Each consumer
/// supplies its own struct that captures whatever world state it needs (server: GameWorld + a
/// pre-built MapGrid; client: the ClientState).
/// </summary>
public interface ISpellLosPredicate
{
    bool IsBlocked(int worldX, int worldY);
}

/// <summary>
/// The 3×3 block of map numbers around a center map, held as nine discrete fields so the whole grid
/// lives on the stack with no backing array object.  Field names are <c>C{col}{row}</c> (center is
/// <see cref="C11"/>); a value of 0 means "no map linked in that cell".  Built by
/// <see cref="WorldCoordHelper.BuildMapGrid"/>; pass by <c>in</c> when handing it to a reader to avoid
/// copying it.
///
/// <para>It also carries the neighbourhood's tile size, which is what makes it — rather than any
/// constant — the thing world coordinates are computed against. See <see cref="TilesX"/>.</para>
/// </summary>
public readonly struct MapGrid(int c00, int c10, int c20, int c01, int c11, int c21, int c02, int c12, int c22,
                               int tilesX, int tilesY)
{
    public readonly int C00 = c00, C10 = c10, C20 = c20; // row 0 (top):    UpLeft,   Up,     UpRight
    public readonly int C01 = c01, C11 = c11, C21 = c21; // row 1 (middle): Left,     Center, Right
    public readonly int C02 = c02, C12 = c12, C22 = c22; // row 2 (bottom): DownLeft, Down,   DownRight

    /// <summary>Every cell's size in tiles — ONE size for the whole neighbourhood.
    ///
    /// <para>A map may only link to maps of its own size, so a 3×3 is uniform by construction and a world
    /// coordinate has a single stride wherever in the grid it lands. That is why the stride lives here and
    /// not in a constant: the size belongs to the neighbourhood being measured, and a grid built around a
    /// 256×256 map measures in 256s.</para></summary>
    public readonly int TilesX = tilesX;

    /// <inheritdoc cref="TilesX"/>
    public readonly int TilesY = tilesY;

    /// <summary>Map number at grid cell (col,row); 0 if unlinked or out of range. For the 9-cell sweeps
    /// (observer sets, neighbor sends) — the hot lookup path reads the fields directly instead.</summary>
    public int this[int col, int row] => (col, row) switch
    {
        (0, 0) => C00,
        (1, 0) => C10,
        (2, 0) => C20,
        (0, 1) => C01,
        (1, 1) => C11,
        (2, 1) => C21,
        (0, 2) => C02,
        (1, 2) => C12,
        (2, 2) => C22,
        _ => 0,
    };

    /// <summary>World-tile coordinate of a local tile within a given grid cell. The center map's (0,0) is
    /// world (<see cref="TilesX"/>, <see cref="TilesY"/>), so neighbors fit on every side.</summary>
    public (int worldX, int worldY) ToWorld(int col, int row, int localX, int localY)
        => (col * TilesX + localX, row * TilesY + localY);

    /// <summary>World-tile coordinate of a local tile on the CENTER map — the common case.</summary>
    public (int worldX, int worldY) CenterToWorld(int localX, int localY)
        => (TilesX + localX, TilesY + localY);

    /// <summary>Inverse of <see cref="ToWorld"/>: resolves a world-tile coordinate back to the
    /// (mapNum, localX, localY) it lands on. <c>mapNum</c> is 0 when the coordinate falls on an unlinked
    /// or out-of-range cell, so callers must validate it before indexing the map.</summary>
    public (int mapNum, int localX, int localY) ResolveWorldTile(int worldX, int worldY)
    {
        int col = worldX / TilesX;
        int row = worldY / TilesY;
        return (this[col, row], worldX - col * TilesX, worldY - row * TilesY);
    }

    /// <summary>Grid (col,row) of <paramref name="queryMapNum"/>, or null if it isn't one of the 9 cells.
    /// Center first, then cardinals, then diagonals, so a map that repeats in a tiny world resolves to its
    /// nearest cell.</summary>
    public (int col, int row)? PositionOf(int queryMapNum)
    {
        if (queryMapNum <= 0) return null;
        if (C11 == queryMapNum) return (1, 1);
        if (C10 == queryMapNum) return (1, 0);
        if (C12 == queryMapNum) return (1, 2);
        if (C01 == queryMapNum) return (0, 1);
        if (C21 == queryMapNum) return (2, 1);
        if (C00 == queryMapNum) return (0, 0);
        if (C20 == queryMapNum) return (2, 0);
        if (C02 == queryMapNum) return (0, 2);
        if (C22 == queryMapNum) return (2, 2);
        return null;
    }

    /// <summary>World-tile coordinate of an entity on any of the 9 maps, or null when its map is not one
    /// of them.</summary>
    public (int worldX, int worldY)? ToWorldRelative(int entityMapNum, int localX, int localY)
    {
        var gp = PositionOf(entityMapNum);
        return gp is null ? null : ToWorld(gp.Value.col, gp.Value.row, localX, localY);
    }
}

/// <summary>
/// A short run of consecutive world tiles along one axis - the S tiles just beyond a footprint's leading
/// edge (see <see cref="WorldCoordHelper.LeadingEdgeTiles"/>).  A stack-only value with no backing array
/// (S is at most <see cref="Constants.MaxNpcSize"/>).  The step is a positive unit vector on exactly one
/// axis, so the tiles are <c>Origin + i*Step</c> for <c>i</c> in [0, Count).
/// </summary>
public readonly struct TileRun(int originWX, int originWY, int stepX, int stepY, int count)
{
    public readonly int OriginWX = originWX, OriginWY = originWY, StepX = stepX, StepY = stepY, Count = count;

    /// <summary>The i-th tile in the run (0-based; caller keeps i in [0, <see cref="Count"/>)).</summary>
    public (int worldX, int worldY) this[int i] => (OriginWX + StepX * i, OriginWY + StepY * i);

    /// <summary>True if world tile (wx,wy) is one of the run's tiles.</summary>
    public bool Contains(int wx, int wy)
    {
        int along = StepX != 0 ? wx - OriginWX : wy - OriginWY;
        int perp = StepX != 0 ? wy - OriginWY : wx - OriginWX;
        return perp == 0 && along >= 0 && along < Count;
    }
}

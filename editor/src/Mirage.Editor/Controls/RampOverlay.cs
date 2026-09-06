using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.Generic;

namespace Mirage.Editor.Controls;

/// <summary>
/// Pure analysis of LayerRamp blocks for the map-editor overlay color-coding, split out of
/// <see cref="TileGridControl"/> so it can be unit-tested without an Avalonia render context.  Operates on a
/// <see cref="MapRecord"/> in 0-based map coordinates; same-map only (a block continuing across a seam is not
/// analyzed here).  Movement itself is governed by <see cref="LayerLogic"/> — this is only the visual signal:
///  * <see cref="IsMixedBlock"/> → amber: the ramp sits in a multi-direction block (an intentional hump/
///    staircase, or a misplacement) — "make sure you meant this";
///  * <see cref="IsInvalidBlock"/> → red frame: the whole contiguous block has no ground mount point, so it can
///    never be climbed onto and connects nothing (e.g. two ramps whose ground feet face each other, [<][>]).
/// </summary>
public static class RampOverlay
{
    private static readonly (int dx, int dy)[] Adj = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public static bool IsRamp(TileRecord t) => t.FringeAttr is { Type: TileType.LayerRamp };

    private static (int dx, int dy) DirDelta(Direction d) => d switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        _ => (1, 0),
    };

    /// <summary>True when a ramp at (x,y) has an orthogonally-adjacent ramp facing a DIFFERENT ground-side
    /// direction — i.e. it belongs to a multi-direction block.  Assumes (x,y) is a ramp.</summary>
    public static bool IsMixedBlock(MapRecord map, int x, int y)
    {
        if (map.Tile[x, y].FringeAttr is not { Type: TileType.LayerRamp } self) return false;
        foreach (var (dx, dy) in Adj)
        {
            int nx = x + dx, ny = y + dy;
            if (map.Contains(nx, ny) && map.Tile[nx, ny].FringeAttr is { Type: TileType.LayerRamp } fa
                && fa.RampGroundSide != self.RampGroundSide)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when the contiguous ramp block containing (x,y) has NO ground mount point: no ramp in the
    /// block has a non-ramp, ground-walkable tile on its ground side, so it can't be climbed onto from the ground
    /// and connects the two planes nowhere.  A ground foot running off the map edge is given the benefit of the
    /// doubt (it may mount across a seam), so only fully-enclosed dead blocks report true.  Assumes (x,y) is a
    /// ramp.</summary>
    public static bool IsInvalidBlock(MapRecord map, int x, int y)
    {
        var block = new HashSet<(int, int)>();
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (!block.Add((cx, cy))) continue;
            if (map.Tile[cx, cy].FringeAttr is not { Type: TileType.LayerRamp } fa) continue;

            var (gx, gy) = DirDelta(fa.RampGroundSide);
            int fx = cx + gx, fy = cy + gy;
            if (!map.Contains(fx, fy))
                return false;   // foot off the map edge → assume a cross-seam mount; not flagged
            var foot = map.Tile[fx, fy];
            if (foot.FringeAttr is not { Type: TileType.LayerRamp } && foot.Type != TileType.Blocked)
                return false;   // a real ground mount point exists → the block is fine

            foreach (var (dx, dy) in Adj)
            {
                int nx = cx + dx, ny = cy + dy;
                if (map.Contains(nx, ny) && map.Tile[nx, ny].FringeAttr is { Type: TileType.LayerRamp })
                    stack.Push((nx, ny));
            }
        }
        return true;   // no mount point anywhere in the block
    }
}

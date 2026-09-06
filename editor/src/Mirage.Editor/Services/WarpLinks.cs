using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.Services;

/// <summary>One warp tile: where it sits, and where it sends you.</summary>
public readonly record struct WarpExit(
    int X, int Y, WorldLayer Layer, int DestMap, int DestX, int DestY, WorldLayer DestLayer);

/// <summary>A tile that warps arrive on, and every map that sends somebody to it.</summary>
/// <param name="X">Destination tile column.</param>
/// <param name="Y">Destination tile row.</param>
/// <param name="Layer">Plane the arrival lands on.</param>
/// <param name="SourceMaps">Maps with at least one warp landing here, ascending. Never empty.</param>
/// <param name="WarpCount">Warp tiles landing here, which can exceed the source count.</param>
public sealed record InboundWarp(
    int X, int Y, WorldLayer Layer, IReadOnlyList<int> SourceMaps, int WarpCount);

/// <summary>
/// Who warps where.
///
/// <para>Warps are the world's second set of connections and the invisible one: a map's Up/Down/Left/Right
/// links put it on the grid, but a warp tile can send a player anywhere and nothing on either map says so.
/// This reads that graph back off the tiles, in both directions.</para>
///
/// <para>Both planes are scanned through <see cref="LayerLogic.AttrFor"/>, so a warp authored on the fringe
/// deck counts the same as one on the ground.</para>
/// </summary>
public static class WarpLinks
{
    /// <summary>Every warp tile on a map, both planes, in row order.</summary>
    public static IEnumerable<WarpExit> Exits(MapRecord map)
    {
        ArgumentNullException.ThrowIfNull(map);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var tile = map.Tile[x, y];
                foreach (var layer in Planes)
                {
                    var attr = LayerLogic.AttrFor(tile, layer);
                    if (attr.Type != TileType.Warp || attr.WarpMap <= 0) continue;
                    yield return new WarpExit(x, y, layer, attr.WarpMap, attr.WarpX, attr.WarpY, attr.WarpLayer);
                }
            }
        }
    }

    private static readonly WorldLayer[] Planes = [WorldLayer.Ground, WorldLayer.Fringe];

    /// <summary>
    /// The maps this one reaches by warp and by warp only, ascending.
    ///
    /// <para>Its own four grid links are excluded, and so is itself: the point of the number is what a reader
    /// cannot already see. A neighbor is drawn in the cell next door, and a warp home goes nowhere new.
    /// Counted per destination MAP rather than per warp tile, so a doorway with three tiles of threshold is
    /// one connection.</para>
    /// </summary>
    public static IReadOnlyList<int> WarpOnlyDestinations(int mapNum, MapRecord map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var found = new SortedSet<int>();
        foreach (var exit in Exits(map))
        {
            if (exit.DestMap == mapNum) continue;
            if (exit.DestMap == map.Up || exit.DestMap == map.Down ||
                exit.DestMap == map.Left || exit.DestMap == map.Right) continue;
            found.Add(exit.DestMap);
        }
        return [.. found];
    }

    /// <summary>Warps arriving on <paramref name="destMap"/>, one entry per destination tile.
    ///
    /// <para>Several warps landing on one tile compound into a single arrival, because that is what the tile
    /// is: one doorway, however many doors open onto it. <paramref name="world"/> supplies whatever maps are
    /// readable, so a caller that cannot see the whole world gets the part it can.</para></summary>
    public static IReadOnlyList<InboundWarp> InboundTo(
        int destMap, IEnumerable<(int Num, MapRecord Map)> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (destMap <= 0) return [];

        var bySite = new Dictionary<(int X, int Y, WorldLayer Layer), (SortedSet<int> Maps, int Count)>();
        foreach (var (num, map) in world)
        {
            if (map is null) continue;
            foreach (var exit in Exits(map))
            {
                if (exit.DestMap != destMap) continue;
                var key = (exit.DestX, exit.DestY, exit.DestLayer);
                if (!bySite.TryGetValue(key, out var slot)) slot = ([], 0);
                slot.Maps.Add(num);
                bySite[key] = (slot.Maps, slot.Count + 1);
            }
        }

        return [.. bySite
            .OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Layer)
            .Select(kv => new InboundWarp(kv.Key.X, kv.Key.Y, kv.Key.Layer, [.. kv.Value.Maps], kv.Value.Count))];
    }
}

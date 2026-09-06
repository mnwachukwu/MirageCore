using Mirage.Shared.Records;

namespace Mirage.Editor.Services;

/// <summary>One map placed on the integer cell grid, with the record it was placed from.</summary>
public readonly record struct MapPlacement(int MapNum, int CellX, int CellY, MapRecord Record);

/// <summary>The result of a flood: every placed map plus the bounding box they occupy.</summary>
/// <param name="Placements">Placed maps, in discovery order. Empty when the origin could not be read.</param>
/// <param name="MinX">Left edge of the occupied box, in cells.</param>
/// <param name="MinY">Top edge of the occupied box, in cells.</param>
/// <param name="MaxX">Right edge of the occupied box, in cells.</param>
/// <param name="MaxY">Bottom edge of the occupied box, in cells.</param>
/// <param name="TruncatedByRadius">True when at least one link led outside the radius.</param>
public sealed record MapLinkLayoutResult(
    IReadOnlyList<MapPlacement> Placements,
    int MinX, int MinY, int MaxX, int MaxY,
    bool TruncatedByRadius)
{
    /// <summary>Width of the occupied box in cells; 0 when nothing was placed.</summary>
    public int CellsWide => Placements.Count == 0 ? 0 : MaxX - MinX + 1;

    /// <summary>Height of the occupied box in cells; 0 when nothing was placed.</summary>
    public int CellsHigh => Placements.Count == 0 ? 0 : MaxY - MinY + 1;

    /// <summary>An empty layout: no maps and a zero-size box.</summary>
    public static readonly MapLinkLayoutResult Empty = new([], 0, 0, 0, 0, false);
}

/// <summary>
/// Lays the map-link graph out on an integer cell grid.
///
/// <para>Maps carry no coordinates, only <see cref="MapRecord.Up"/>/<see cref="MapRecord.Down"/>/
/// <see cref="MapRecord.Left"/>/<see cref="MapRecord.Right"/> links, so position is discovered by
/// walking them outward from an origin placed at (0, 0). Two guards make the walk total on any world,
/// however badly linked: a map id is placed at most once, and a cell is filled at most once. A link
/// cycle therefore terminates, and two maps claiming the same cell resolve first-come rather than
/// overwriting each other.</para>
///
/// <para>Shared by the world PNG export (unbounded) and the World Preview window (radius-bounded), so
/// the two can never disagree about where a map sits.</para>
/// </summary>
public static class MapLinkLayout
{
    /// <summary>
    /// Floods outward from <paramref name="originMapNum"/>, placing each reachable map on a cell.
    /// </summary>
    /// <param name="originMapNum">Map placed at the center. Nothing is placed if it cannot be read.</param>
    /// <param name="radius">Chebyshev cell radius around the origin; 0 or less floods the whole graph.</param>
    /// <param name="fetch">Reads one map by number, or null when it does not exist. Must not mutate editor state.</param>
    /// <param name="onDiscovered">Called with the running placed-map count after each map is read.</param>
    /// <param name="ct">Cancels the walk between maps.</param>
    public static async ValueTask<MapLinkLayoutResult> FloodAsync(
        int originMapNum, int radius, Func<int, ValueTask<MapRecord?>> fetch,
        Action<int>? onDiscovered = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (originMapNum <= 0) return MapLinkLayoutResult.Empty;

        var coordOf = new Dictionary<int, (int X, int Y)>();
        var cellUsed = new HashSet<(int, int)>();
        var placed = new List<MapPlacement>();
        var queue = new Queue<int>();
        bool truncated = false;

        bool InRadius(int x, int y) =>
            radius <= 0 || (Math.Abs(x) <= radius && Math.Abs(y) <= radius);

        void Place(int id, int x, int y)
        {
            if (id <= 0) return;
            if (!InRadius(x, y)) { truncated = true; return; }
            if (coordOf.ContainsKey(id) || !cellUsed.Add((x, y))) return;
            coordOf[id] = (x, y);
            queue.Enqueue(id);
        }

        Place(originMapNum, 0, 0);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            int id = queue.Dequeue();
            var rec = await fetch(id).ConfigureAwait(true);
            if (rec is null) continue;

            var (x, y) = coordOf[id];
            placed.Add(new MapPlacement(id, x, y, rec));
            onDiscovered?.Invoke(placed.Count);

            Place(rec.Up, x, y - 1);
            Place(rec.Down, x, y + 1);
            Place(rec.Left, x - 1, y);
            Place(rec.Right, x + 1, y);
        }

        if (placed.Count == 0) return MapLinkLayoutResult.Empty;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in placed)
        {
            minX = Math.Min(minX, p.CellX);
            maxX = Math.Max(maxX, p.CellX);
            minY = Math.Min(minY, p.CellY);
            maxY = Math.Max(maxY, p.CellY);
        }
        return new MapLinkLayoutResult(placed, minX, minY, maxX, maxY, truncated);
    }
}

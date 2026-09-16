using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.World;

/// <summary>
/// Every door tile that is part of the same door.
///
/// <para><b>A gate is wider than a tile.</b> A plate names one square and a key is used on the one
/// square somebody is facing, but the barrier those open is usually two or three tiles of wall with a
/// gap in it — so opening the named square alone leaves a gate somebody still cannot walk through, and
/// authoring one plate per square of it is not something a single plate can do at all.</para>
///
/// <para>Connected orthogonally, on ONE layer, within ONE map: a run of door tiles touching each other
/// is one door. Diagonal neighbours are not, because two gates meeting at a corner are two gates, and a
/// plate that opened both would be opening one the author never pointed at.</para>
/// </summary>
public static class DoorSpan
{
    /// <summary>⚠ A ceiling on the fill. A map authored as a wall of doors would otherwise make one step
    /// onto a plate walk the whole map, and a number this far past any real gate can only be reached by
    /// an authoring mistake — which opens what it opens rather than hanging the server.</summary>
    public const int Most = 64;

    /// <summary>The door tiles making up the door at <paramref name="x"/>, <paramref name="y"/>, that one
    /// included. Empty when the tile named is not a door on that layer.</summary>
    public static List<(int X, int Y)> From(MapRecord map, int x, int y, WorldLayer layer)
    {
        ArgumentNullException.ThrowIfNull(map);

        var span = new List<(int X, int Y)>();
        if (!IsDoor(map, x, y, layer)) return span;

        var seen = new HashSet<(int, int)> { (x, y) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((x, y));

        while (queue.Count > 0 && span.Count < Most)
        {
            var (cx, cy) = queue.Dequeue();
            span.Add((cx, cy));

            Consider(map, cx + 1, cy, layer, seen, queue);
            Consider(map, cx - 1, cy, layer, seen, queue);
            Consider(map, cx, cy + 1, layer, seen, queue);
            Consider(map, cx, cy - 1, layer, seen, queue);
        }

        return span;
    }

    private static void Consider(MapRecord map, int x, int y, WorldLayer layer,
                                 HashSet<(int, int)> seen, Queue<(int X, int Y)> queue)
    {
        if (!IsDoor(map, x, y, layer) || !seen.Add((x, y))) return;

        queue.Enqueue((x, y));
    }

    private static bool IsDoor(MapRecord map, int x, int y, WorldLayer layer) =>
        map.Contains(x, y) && LayerLogic.AttrFor(map.Tile[x, y], layer).Type == TileType.Door;
}

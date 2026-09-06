using Microsoft.Extensions.Logging;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Where a contest's capture points go: the territory read as ONE walkable graph across its seams,
/// and a greedy farthest-point walk over it.</summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    /// <summary>One walkable tile of a territory, as a node in <see cref="TerritoryGraph"/>.</summary>
    private readonly record struct TerritoryNode(int Map, int X, int Y);

    /// <summary>Every walkable tile in a territory, joined across map seams, with the map grids kept so a
    /// neighbor lookup is an array index rather than a dictionary probe.
    ///
    /// <para>Edges are 4-connected within a map, plus the seam step, which preserves the coordinate running
    /// ALONG the seam and lands on the far edge of the neighbor — the same rule
    /// <see cref="MovementSystem"/> walks a player across. A seam edge exists only where the tiles on both
    /// sides are walkable, so a wall against a border is not a crossing.</para></summary>
    private sealed class TerritoryGraph
    {
        public readonly List<TerritoryNode> Nodes = [];
        public readonly Dictionary<int, int[,]> IdOf = [];   // map -> [x,y] node id, -1 for unwalkable

        public int At(int map, int x, int y)
        {
            if (!IdOf.TryGetValue(map, out var grid)) return -1;
            if (x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1)) return -1;
            return grid[x, y];
        }
    }

    /// <summary>Builds the territory's walkable graph.
    ///
    /// <para>🔴 Built over EVERY map in the territory, safe ones included, even though no point may be placed
    /// on safe ground. The graph answers "can a player walk from here to there", and a town in the middle of a
    /// territory is walked THROUGH. Leaving safe maps out would cut the territory in half at its towns and
    /// strand the halves in separate components, which is the opposite of what the safe-ground rule is
    /// for.</para></summary>
    private TerritoryGraph BuildTerritoryGraph(List<int> maps)
    {
        var g = new TerritoryGraph();
        foreach (int m in maps)
        {
            var map = _world.Maps[m];
            var grid = new int[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (map.Tile[x, y].Type != TileType.Walkable) { grid[x, y] = -1; continue; }
                    grid[x, y] = g.Nodes.Count;
                    g.Nodes.Add(new TerritoryNode(m, x, y));
                }
            }
            g.IdOf[m] = grid;
        }
        return g;
    }

    /// <summary>Lays the territory's maps out on ONE tile grid by walking their edge links, so a point on
    /// any of them can be placed relative to a player standing on any other.
    ///
    /// <para>Origins are in tiles rather than map cells because the maps are not all the same size: a
    /// neighbour's origin is this map's origin plus THIS map's width or height, which is exactly the step a
    /// player takes across that seam.</para>
    ///
    /// <para>🔴 A territory whose links disagree with themselves — a loop that returns to a map at a
    /// different offset — keeps the FIRST placement. Breadth-first from the lowest map number makes that
    /// choice stable rather than dependent on enumeration order, and a marker a few tiles out on a
    /// malformed layout is better than one that moves every tick.</para></summary>
    private List<ContestMapView> BuildTerritoryLayout(List<int> maps)
    {
        var origin = new Dictionary<int, (int X, int Y)>();
        var queue = new Queue<int>();
        var inTerritory = new HashSet<int>(maps);

        foreach (int start in maps.OrderBy(m => m))
        {
            if (origin.ContainsKey(start)) continue;   // a second component: place it at its own origin
            origin[start] = (0, 0);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int m = queue.Dequeue();
                var map = _world.Maps[m];
                var (ox, oy) = origin[m];
                Step(map.Up, ox, oy - _world.Maps[map.Up > 0 ? map.Up : m].Height);
                Step(map.Down, ox, oy + map.Height);
                Step(map.Left, ox - _world.Maps[map.Left > 0 ? map.Left : m].Width, oy);
                Step(map.Right, ox + map.Width, oy);

                void Step(int next, int nx, int ny)
                {
                    if (next <= 0 || !inTerritory.Contains(next) || origin.ContainsKey(next)) return;
                    origin[next] = (nx, ny);
                    queue.Enqueue(next);
                }
            }
        }

        return [.. origin.Select(kv => new ContestMapView { Map = kv.Key, OriginX = kv.Value.X, OriginY = kv.Value.Y })];
    }

    /// <summary>The node ids reachable in one step from <paramref name="node"/>, appended to
    /// <paramref name="into"/>. A step off a map edge follows that edge's declared link.</summary>
    private void StepsFrom(TerritoryGraph g, in TerritoryNode node, List<int> into)
    {
        into.Clear();
        var map = _world.Maps[node.Map];
        int lastX = map.Width - 1, lastY = map.Height - 1;

        Add(g.At(node.Map, node.X - 1, node.Y));
        Add(g.At(node.Map, node.X + 1, node.Y));
        Add(g.At(node.Map, node.X, node.Y - 1));
        Add(g.At(node.Map, node.X, node.Y + 1));

        // Seam steps. The link is followed only onto a map inside this territory (At returns -1 otherwise),
        // so a border with the wider world is a wall for these purposes.
        if (node.Y == 0 && map.Up > 0) Add(g.At(map.Up, node.X, _world.Maps[map.Up].Height - 1));
        if (node.Y == lastY && map.Down > 0) Add(g.At(map.Down, node.X, 0));
        if (node.X == 0 && map.Left > 0) Add(g.At(map.Left, _world.Maps[map.Left].Width - 1, node.Y));
        if (node.X == lastX && map.Right > 0) Add(g.At(map.Right, 0, node.Y));

        void Add(int id) { if (id >= 0) into.Add(id); }
    }

    /// <summary>Walking distance in tiles from <paramref name="from"/> to every node, unreachable ones left
    /// at <see cref="int.MaxValue"/>. Breadth-first, because every step costs one tile.</summary>
    private int[] WalkDistances(TerritoryGraph g, int from)
    {
        var dist = new int[g.Nodes.Count];
        Array.Fill(dist, int.MaxValue);
        dist[from] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        var steps = new List<int>();
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            StepsFrom(g, g.Nodes[id], steps);
            foreach (int next in steps)
            {
                if (dist[next] != int.MaxValue) continue;
                dist[next] = dist[id] + 1;
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    /// <summary>Chooses where a contest's capture points go: up to <paramref name="count"/> tiles, each on a
    /// map of its own, every one reachable on foot from every other, spread by WALKING distance.
    ///
    /// <para>The walk is greedy farthest-point (k-center): a random start, then repeatedly the tile whose
    /// nearest already-chosen point is furthest away. Random start because a deterministic one puts the same
    /// flags in the same places every war; greedy after that because it is what turns one arbitrary tile into
    /// a spread set.</para>
    ///
    /// <para>🔴 Distance is measured by WALKING, not by map index and not in a straight line. Map numbers run
    /// in authoring order, so ordering by them clusters the flags into whichever corner of the territory was
    /// drawn first. A straight line is a lie wherever a wall, a cliff or water stands between two tiles that
    /// are close together on paper and a long way apart on foot.</para>
    ///
    /// <para>Candidates are confined to the graph's LARGEST connected component, so every point is walkable
    /// from every other. A point stranded across unwalkable ground could be held by whoever happened to be
    /// nearest and never contested.</para></summary>
    private List<(int Map, int X, int Y)> ChooseCapturePoints(List<int> allMaps, List<int> pointMaps, int count)
    {
        var chosen = new List<(int Map, int X, int Y)>();
        if (count <= 0 || allMaps.Count == 0) return chosen;

        var g = BuildTerritoryGraph(allMaps);
        if (g.Nodes.Count == 0) return chosen;

        var eligible = new HashSet<int>(pointMaps);
        var candidates = LargestComponentCandidates(g, eligible);
        if (candidates.Count == 0) return chosen;

        // Distance from each candidate to its nearest chosen point, seeded by the random first pick.
        int seed = candidates[Rng.Next(candidates.Count)];
        var nearest = WalkDistances(g, seed);
        Take(seed);

        var usedMaps = new HashSet<int> { g.Nodes[seed].Map };
        while (chosen.Count < count)
        {
            int best = -1, bestDist = -1;
            foreach (int id in candidates)
            {
                // One point to a map while any eligible map is still free — the maps ARE the spread at the
                // coarse scale, and two flags on one map read as one contested area rather than two.
                if (usedMaps.Contains(g.Nodes[id].Map)) continue;
                if (nearest[id] > bestDist) { bestDist = nearest[id]; best = id; }
            }
            if (best < 0) break;   // every eligible map in the component already holds a point

            var d = WalkDistances(g, best);
            for (int i = 0; i < nearest.Length; i++) nearest[i] = Math.Min(nearest[i], d[i]);
            usedMaps.Add(g.Nodes[best].Map);
            Take(best);
        }

        if (chosen.Count < count)
        {
            _logger.LogWarning(
                "Territory contest: wanted {Want} capture points but the territory's largest walkable region " +
                "spans only {Got} eligible map(s).", count, chosen.Count);
        }
        return chosen;

        void Take(int id)
        {
            var n = g.Nodes[id];
            chosen.Add((n.Map, n.X, n.Y));
        }
    }

    /// <summary>Candidate tiles: those in the graph's largest connected component that sit on a map a point
    /// is allowed on. The component is measured over ALL its tiles, not just the eligible ones, so a region
    /// joined only through a town still counts as one region.</summary>
    private List<int> LargestComponentCandidates(TerritoryGraph g, HashSet<int> eligibleMaps)
    {
        var seen = new bool[g.Nodes.Count];
        var best = new List<int>();
        var component = new List<int>();
        var queue = new Queue<int>();
        var steps = new List<int>();
        int bestSize = 0;

        for (int start = 0; start < g.Nodes.Count; start++)
        {
            if (seen[start]) continue;
            component.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            int size = 0;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                size++;
                if (eligibleMaps.Contains(g.Nodes[id].Map)) component.Add(id);
                StepsFrom(g, g.Nodes[id], steps);
                foreach (int next in steps)
                {
                    if (seen[next]) continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (size > bestSize) { bestSize = size; best = [.. component]; }
        }
        return best;
    }
}

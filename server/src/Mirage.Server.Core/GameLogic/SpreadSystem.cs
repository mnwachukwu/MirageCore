using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Spots spread across a region, every one walkable from every other.
///
/// <para><b>Core performs this; it does not decide what the spots are for.</b> Reading a region as one
/// walkable graph across its map seams, measuring distance along it, and choosing a spread set is real
/// machinery, and it is the same machinery whether the answer becomes capture points, chests, patrol
/// posts, or somewhere to hide a key. What a game supplies is the region, how many, and which of its
/// maps a spot is allowed on.</para>
///
/// <para><b>A script could not do this.</b> It reads one tile at a time, and a region is thousands of
/// them; and the seam step, the walking distance and the connected component are all things the engine
/// already knows because it moves bodies across them.</para>
/// </summary>
public sealed class SpreadSystem : GameSystem
{
    private readonly GameWorld _world;

    public SpreadSystem(GameWorld world, IPacketDispatcher dispatcher, IRandomSource? rng = null)
        : base(dispatcher, rng: rng) => _world = world;

    /// <summary>One walkable tile of one plane, as a node in <see cref="Region"/>.</summary>
    private readonly record struct Node(int Map, int X, int Y, WorldLayer Layer);

    /// <summary>Every walkable tile of a region, joined across its map seams, with the grids kept so a
    /// neighbor lookup is an array index rather than a dictionary probe.
    ///
    /// <para>Edges are the four steps within a map plus the seam step, which keeps the coordinate running
    /// ALONG the seam and lands on the far edge of the neighbor — the same step
    /// <see cref="MovementSystem"/> walks a body across. A seam edge exists only where both sides are
    /// walkable, so a wall against a border is not a crossing.</para></summary>
    private sealed class Region
    {
        public readonly List<Node> Nodes = [];

        /// <summary>map -> [plane, x, y] node id, -1 where nothing stands. Two planes, because a bridge
        /// and the water under it are one square and two places.</summary>
        public readonly Dictionary<int, int[,,]> IdOf = [];

        public int At(int map, int x, int y, WorldLayer layer)
        {
            if (!IdOf.TryGetValue(map, out var grid)) return -1;
            if (x < 0 || y < 0 || x >= grid.GetLength(1) || y >= grid.GetLength(2)) return -1;

            return grid[(int)layer, x, y];
        }
    }

    /// <summary>
    /// Up to <paramref name="count"/> spots spread across a map group, nothing twice, every one reachable
    /// on foot from every other.
    ///
    /// <para>🔴 <b>Distance is measured by WALKING</b>, not by map number and not in a straight line. Map
    /// numbers run in authoring order, so spreading by them piles everything into whichever corner was
    /// drawn first. A straight line is a lie wherever a wall, a cliff or water stands between two tiles
    /// that are near on paper and a long way apart on foot.</para>
    ///
    /// <para><paramref name="onlyWhere"/> names one of the GAME's own truth fields on Maps: a spot is
    /// placed only on a map carrying it. Blank puts one anywhere in the group. Either way the walk covers
    /// the whole group — a town in the middle of a region is walked THROUGH, and leaving it out of the
    /// graph would cut the region in half at its towns and strand the halves apart.</para>
    ///
    /// <para>Fewer than asked for is an ordinary answer, and means the region's largest walkable stretch
    /// had nowhere else to put one.</para>
    /// </summary>
    public IReadOnlyList<WorldPlace> SpreadOver(int group, int count, string onlyWhere = "")
    {
        if (count <= 0) return [];

        var maps = MapsIn(group);
        if (maps.Count == 0) return [];

        var region = Build(maps);
        if (region.Nodes.Count == 0) return [];

        var candidates = Largest(region, Eligible(maps, onlyWhere));
        if (candidates.Count == 0) return [];

        // The walk: a random start, then repeatedly the tile whose nearest already-chosen spot is
        // furthest away. Random, because a fixed start puts the same spots in the same places forever;
        // greedy after that, because it is what turns one arbitrary tile into a spread set.
        var chosen = new List<WorldPlace>(count);
        int seed = candidates[Rng.Next(candidates.Count)];
        int[] nearest = Distances(region, seed);

        Take(seed);
        var taken = new HashSet<int> { region.Nodes[seed].Map };

        while (chosen.Count < count)
        {
            int best = -1, furthest = -1;

            foreach (int id in candidates)
            {
                // ⚠ One to a map while any eligible map is still free. In a world made of maps the maps
                // ARE the spread at the coarse scale, and two spots on one map read as one place rather
                // than two.
                if (taken.Contains(region.Nodes[id].Map)) continue;
                if (nearest[id] <= furthest) continue;

                furthest = nearest[id];
                best = id;
            }

            if (best < 0) break;   // every eligible map in the stretch already holds one

            int[] from = Distances(region, best);
            for (int i = 0; i < nearest.Length; i++) nearest[i] = Math.Min(nearest[i], from[i]);

            taken.Add(region.Nodes[best].Map);
            Take(best);
        }

        return chosen;

        void Take(int id)
        {
            var node = region.Nodes[id];
            chosen.Add(new WorldPlace(node.Map, node.X, node.Y, node.Layer));
        }
    }

    /// <summary>The maps of a group, in number order.</summary>
    private List<int> MapsIn(int group)
    {
        var maps = new List<int>();
        if (group <= 0) return maps;

        for (int m = 1; m <= _world.Limits.Maps; m++)
            if (_world.Maps[m].MapGroup == group) maps.Add(m);

        return maps;
    }

    /// <summary>The maps a spot may be placed on: those carrying the game's field, or all of them when no
    /// field is named.</summary>
    private HashSet<int> Eligible(List<int> maps, string onlyWhere)
    {
        if (string.IsNullOrWhiteSpace(onlyWhere)) return [.. maps];

        var allowed = new HashSet<int>();

        foreach (int m in maps)
        {
            if (MapGroupResolve.Value(_world.Maps[m], Group(m), onlyWhere) is { } held && held.AsBool())
                allowed.Add(m);
        }

        return allowed;
    }

    private Shared.Records.MapGroupRecord? Group(int mapNum) =>
        _world.MapGroups.GetValueOrDefault(_world.Maps[mapNum].MapGroup);

    private Region Build(List<int> maps)
    {
        var region = new Region();

        foreach (int m in maps)
        {
            var map = _world.Maps[m];
            var grid = new int[2, map.Width, map.Height];

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    grid[(int)WorldLayer.Ground, x, y] = -1;
                    grid[(int)WorldLayer.Fringe, x, y] = -1;

                    if (map.Tile[x, y].Type == TileType.Walkable)
                    {
                        grid[(int)WorldLayer.Ground, x, y] = region.Nodes.Count;
                        region.Nodes.Add(new Node(m, x, y, WorldLayer.Ground));
                    }

                    // A deck is a surface only where it is joined to a ramp: the fringe plane reads as
                    // open sky over most of every map, and a spot in the air is a spot nobody can walk to.
                    if (_world.IsFringeSpawnable(m, x, y))
                    {
                        grid[(int)WorldLayer.Fringe, x, y] = region.Nodes.Count;
                        region.Nodes.Add(new Node(m, x, y, WorldLayer.Fringe));
                    }
                }
            }

            region.IdOf[m] = grid;
        }

        return region;
    }

    /// <summary>The nodes one step from this one. A step off an edge follows that edge's declared link,
    /// and only onto a map inside the region — so a border with the wider world is a wall here.</summary>
    private void StepsFrom(Region region, in Node node, List<int> into)
    {
        into.Clear();

        var map = _world.Maps[node.Map];
        int lastX = map.Width - 1, lastY = map.Height - 1;

        Add(region.At(node.Map, node.X - 1, node.Y, node.Layer));
        Add(region.At(node.Map, node.X + 1, node.Y, node.Layer));
        Add(region.At(node.Map, node.X, node.Y - 1, node.Layer));
        Add(region.At(node.Map, node.X, node.Y + 1, node.Layer));

        if (node.Y == 0 && map.Up > 0) Add(region.At(map.Up, node.X, _world.Maps[map.Up].Height - 1, node.Layer));
        if (node.Y == lastY && map.Down > 0) Add(region.At(map.Down, node.X, 0, node.Layer));
        if (node.X == 0 && map.Left > 0) Add(region.At(map.Left, _world.Maps[map.Left].Width - 1, node.Y, node.Layer));
        if (node.X == lastX && map.Right > 0) Add(region.At(map.Right, 0, node.Y, node.Layer));

        // ⚠ A ramp is the ONE step between the planes. Without it the deck is an island: every spot on a
        // bridge would look unreachable from the ground and the whole upper level would be left out.
        if (map.Tile[node.X, node.Y].FringeAttr is { Type: TileType.LayerRamp })
        {
            Add(region.At(node.Map, node.X, node.Y,
                          node.Layer == WorldLayer.Ground ? WorldLayer.Fringe : WorldLayer.Ground));
        }

        void Add(int id) { if (id >= 0) into.Add(id); }
    }

    /// <summary>Walking distance in tiles from one node to every node, unreachable ones left at the
    /// ceiling. Breadth-first, because every step costs one tile.</summary>
    private int[] Distances(Region region, int from)
    {
        var far = new int[region.Nodes.Count];
        Array.Fill(far, int.MaxValue);
        far[from] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(from);
        var steps = new List<int>();

        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            StepsFrom(region, region.Nodes[id], steps);

            foreach (int next in steps)
            {
                if (far[next] != int.MaxValue) continue;

                far[next] = far[id] + 1;
                queue.Enqueue(next);
            }
        }

        return far;
    }

    /// <summary>Candidate tiles: the eligible ones in the region's largest connected stretch.
    ///
    /// <para>🔴 Confined to one stretch so every spot is walkable from every other. One stranded across
    /// unwalkable ground belongs to whoever happens to be nearest and is never contested for.</para>
    ///
    /// <para>The stretch is measured over ALL its tiles rather than only the eligible ones, so a region
    /// joined through a town still counts as one.</para></summary>
    private List<int> Largest(Region region, HashSet<int> eligible)
    {
        var seen = new bool[region.Nodes.Count];
        var best = new List<int>();
        var here = new List<int>();
        var queue = new Queue<int>();
        var steps = new List<int>();
        int biggest = 0;

        for (int start = 0; start < region.Nodes.Count; start++)
        {
            if (seen[start]) continue;

            here.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            int size = 0;

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                size++;

                if (eligible.Contains(region.Nodes[id].Map)) here.Add(id);

                StepsFrom(region, region.Nodes[id], steps);

                foreach (int next in steps)
                {
                    if (seen[next]) continue;

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            if (size > biggest) { biggest = size; best = [.. here]; }
        }

        return best;
    }
}

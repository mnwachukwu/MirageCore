using Mirage.Shared;

namespace Mirage.Client.Core.State;

/// <summary>
/// Which Key doors stand open on one map, for the client's collision prediction and its door art.
///
/// <para>Sparse: an entry exists only for a door that is actually open, so a map costs what its open
/// doors cost and nothing for its size. The server's <c>TempTileState</c> keeps its half the same way, and
/// the two stay in step because both are told about a door by the same MapKey packet.</para>
///
/// <para>Indexed rather than enumerated at every read site — the hot paths (rendering a tile, predicting
/// a step) ask about one tile at a time.</para>
/// </summary>
public sealed class OpenDoors
{
    private readonly HashSet<(int X, int Y, int Layer)> _open = [];

    /// <summary>True when the door on this (tile, layer) stands open.</summary>
    public bool this[int x, int y, int layer] => _open.Contains((x, y, layer));

    /// <inheritdoc cref="this[int, int, int]"/>
    public bool this[int x, int y, WorldLayer layer] => _open.Contains((x, y, (int)layer));

    /// <summary>How many doors are open — the whole cost of a sweep over this map.</summary>
    public int Count => _open.Count;

    /// <summary>Bumped whenever a door actually moves. A closed door stops light, so anything that caches
    /// what a light reaches has to know this changed; comparing versions is cheaper than re-deriving it.</summary>
    public int Version { get; private set; }

    public void Set(int x, int y, int layer, bool open)
    {
        bool moved = open ? _open.Add((x, y, layer)) : _open.Remove((x, y, layer));
        if (moved) Version++;
    }

    /// <summary>Every door shut — what a map load or a grid reframe leaves behind.</summary>
    public void Clear()
    {
        if (_open.Count == 0) return;
        _open.Clear();
        Version++;
    }
}

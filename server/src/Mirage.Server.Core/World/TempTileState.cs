using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>Per-map RUNTIME tile state that is never persisted: which doors stand open and when a
/// picked-up tile item is due back. Rebuilt empty on every map load.
///
/// <para>Both are held SPARSELY, keyed by (x, y, layer): a map carries an entry only while something is
/// actually running on that tile, and the entry's presence IS the "active" flag. The two sweeps that age
/// them out read the entries alone, so their cost follows the number of open doors and picked-up items on
/// the map and never its area.</para>
///
/// <para>Both are per-(tile, LAYER): a Key door on the fringe deck of a bridge is independent of the
/// ground door beneath it, and a Ground tile-item and a Fringe tile-item sharing one (x, y) respawn on
/// separate clocks.</para></summary>
public sealed class TempTileState
{
    private readonly Dictionary<(int X, int Y, WorldLayer Layer), long> _doorOpenedAt = [];
    private readonly Dictionary<(int X, int Y, WorldLayer Layer), long> _itemRemovedAt = [];

    /// <summary>Every door standing open on this map, against the TickCount64 it opened at. The
    /// auto-close sweep's whole work list.</summary>
    public IReadOnlyDictionary<(int X, int Y, WorldLayer Layer), long> OpenDoors => _doorOpenedAt;

    /// <summary>Every tile-defined item that has been picked up and is waiting to come back, against the
    /// TickCount64 it was taken at. The respawn sweep's whole work list.</summary>
    public IReadOnlyDictionary<(int X, int Y, WorldLayer Layer), long> TakenTileItems => _itemRemovedAt;

    public bool IsDoorOpen(int x, int y, WorldLayer layer) => _doorOpenedAt.ContainsKey((x, y, layer));

    /// <summary>Stamps a door open at <paramref name="now"/> (TickCount64) so the auto-close sweep ages it
    /// out on its own clock. Callers gate on <see cref="IsDoorOpen"/> first — re-stamping an already-open
    /// door would extend its window.</summary>
    public void OpenDoor(int x, int y, WorldLayer layer, long now) => _doorOpenedAt[(x, y, layer)] = now;

    public void CloseDoor(int x, int y, WorldLayer layer) => _doorOpenedAt.Remove((x, y, layer));

    /// <summary>When the tile item on this (tile, layer) was taken, or 0 if one is standing there now.</summary>
    public long TileItemTakenAt(int x, int y, WorldLayer layer) =>
        _itemRemovedAt.TryGetValue((x, y, layer), out long at) ? at : 0;

    /// <summary>Starts this (tile, layer)'s respawn clock at <paramref name="now"/> (TickCount64).</summary>
    public void TakeTileItem(int x, int y, WorldLayer layer, long now) => _itemRemovedAt[(x, y, layer)] = now;

    /// <summary>Clears the respawn clock — an item is standing on the tile again.</summary>
    public void RestoreTileItem(int x, int y, WorldLayer layer) => _itemRemovedAt.Remove((x, y, layer));
}

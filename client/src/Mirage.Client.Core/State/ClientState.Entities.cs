using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>Map entities across the seamless grid: NPCs (native and traversal guests), dropped
/// items, and the footprint/occupancy queries over them.</summary>
public sealed partial class ClientState
{
    // ── Map entities (1-based for NPCs; items keyed by server slot id) ────────

    // Settable so a seamless crossing can swap the center container with a neighbor cell's (see ShiftGrid).
    // MapItems is a dict keyed by the server's stable per-map slot id — there's no fixed cap, the server
    // sends Num=0 to signal removal.
    public Dictionary<int, MapItemRecord> MapItems { get; private set; } = InitMapItems();
    public ClientMapNpc[] MapNpcs { get; private set; } = InitMapNpcs();

    /// <summary>
    /// NPCs on the 8 neighbor maps ([col,row]; [1,1] unused — center uses <see cref="MapNpcs"/>).
    /// Each cell is a full 1-based slot array, always allocated.  Routed by map number via
    /// <see cref="NpcsForMap"/>.
    /// </summary>
    public ClientMapNpc[,][] NeighborNpcs { get; } = InitNeighborNpcs();

    private static ClientMapNpc[,][] InitNeighborNpcs()
    {
        var g = new ClientMapNpc[3, 3][];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = InitMapNpcs();
        }

        return g;
    }

    /// <summary>
    /// The NPC slot array for a given map number — the center array, the matching neighbor
    /// cell, or null if that map isn't currently observed.
    /// </summary>
    public ClientMapNpc[]? NpcsForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        if (mapNum == CenterMapNum) return MapNpcs;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1) && NeighborMapNums[c, r] == mapNum)
                    return NeighborNpcs[c, r];
            }
        }

        return null;
    }

    /// <summary>Grid cell (col,row) of a loaded map within the current 3×3, or null if it isn't one of
    /// the nine.  Center resolves to (1,1).  Used to convert a map-local tile to a world tile (e.g. to
    /// measure a guest's step across a seam in world space).</summary>
    public (int col, int row)? CellForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        if (mapNum == CenterMapNum) return (1, 1);
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1) && NeighborMapNums[c, r] == mapNum)
                    return (c, r);
            }
        }

        return null;
    }

    /// <summary>True if a visiting (chasing) guest NPC currently stands on the given map tile.  Guests
    /// live outside the slot arrays, so client collision must check them separately to be as solid as
    /// native NPCs — otherwise a predicted move onto a guest is rejected by the server (rubber-band).</summary>
    public bool IsGuestOnTile(int mapNum, int x, int y)
    {
        if (mapNum <= 0) return false;
        foreach (var t in TraversalNpcs.Values)
        {
            if (t.Num > 0 && t.CurrentMapNum == mapNum && t.X == x && t.Y == y)
                return true;
        }

        return false;
    }

    /// <summary>Footprint- and seam-aware NPC collision for the local move predictor: true if a native or
    /// guest NPC's body (its SxS footprint from the def Size) covers tile (x,y) on <paramref name="mapNum"/>,
    /// INCLUDING a large NPC on the LEFT / UP / UP-LEFT neighbor whose body spills across the seam onto it.
    /// Mirrors the server's <c>GameWorld.IsTileOccupiedByNpc</c> so a predicted step onto any tile of a big
    /// NPC's body is blocked locally instead of being accepted and then rubber-banded by the server.</summary>
    public bool IsTileNpcBlocked(int mapNum, int x, int y, WorldLayer layer)
    {
        if (mapNum <= 0) return false;
        if (TileHasNpcFootprint(mapNum, x, y, layer)) return true;

        // Footprints extend +x/+y, so only the LEFT / UP / UP-LEFT neighbors can spill onto this map, and
        // only onto its top/left border tiles. Fast-bail off the border (the common case).
        if (x >= Constants.MaxNpcSize - 1 && y >= Constants.MaxNpcSize - 1) return false;
        var cell = CellForMap(mapNum);
        if (cell is null) return false;
        int col = cell.Value.col, row = cell.Value.row;
        int qwx = col * MapTilesX + x;
        int qwy = row * MapTilesY + y;
        return BigNpcOnCellCovers(col - 1, row, qwx, qwy, layer)          // left neighbor
            || BigNpcOnCellCovers(col, row - 1, qwx, qwy, layer)          // up neighbor
            || BigNpcOnCellCovers(col - 1, row - 1, qwx, qwy, layer);     // up-left neighbor
    }

    private bool TileHasNpcFootprint(int mapNum, int x, int y, WorldLayer layer)
    {
        var npcs = NpcsForMap(mapNum);
        if (npcs is not null)
        {
            for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            {
                var n = npcs[i];
                if (n.Num > 0 && n.Layer == layer && WorldCoordHelper.FootprintContains(n.X, n.Y, NpcSizeFor(n.Num), x, y)) return true;
            }
        }

        foreach (var t in TraversalNpcs.Values)
            if (t.Num > 0 && t.Layer == layer && t.CurrentMapNum == mapNum && WorldCoordHelper.FootprintContains(t.X, t.Y, NpcSizeFor(t.Num), x, y)) return true;
        return false;
    }

    // Large NPCs (size > 1) anchored on grid cell (col,row) whose world-space footprint covers the query
    // world tile. Size-1 NPCs never spill across a seam, so they're skipped.
    private bool BigNpcOnCellCovers(int col, int row, int qwx, int qwy, WorldLayer layer)
    {
        if ((uint)col > 2 || (uint)row > 2) return false;
        int m = (col == 1 && row == 1) ? CenterMapNum : NeighborMapNums[col, row];
        if (m <= 0) return false;
        var npcs = (col == 1 && row == 1) ? MapNpcs : NeighborNpcs[col, row];
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var n = npcs[i];
            if (n.Num <= 0 || n.Layer != layer) continue;
            int size = NpcSizeFor(n.Num);
            if (size <= 1) continue;
            var (awx, awy) = ToWorld(col, row, n.X, n.Y);
            if (WorldCoordHelper.FootprintContains(awx, awy, size, qwx, qwy)) return true;
        }
        foreach (var t in TraversalNpcs.Values)
        {
            if (t.Num <= 0 || t.Layer != layer || t.CurrentMapNum != m) continue;
            int size = NpcSizeFor(t.Num);
            if (size <= 1) continue;
            var (awx, awy) = ToWorld(col, row, t.X, t.Y);
            if (WorldCoordHelper.FootprintContains(awx, awy, size, qwx, qwy)) return true;
        }
        return false;
    }

    private int NpcSizeFor(int num)
        => num > 0 && num <= Limits.Npcs && NpcDefs[num] is not null ? NpcDefs[num].EffectiveSize : 1;

    /// <summary>
    /// Ground items on the 8 neighbor maps ([col,row]; [1,1] unused — center uses
    /// <see cref="MapItems"/>).  Each cell is a dict keyed by server slot id, always allocated.
    /// </summary>
    public Dictionary<int, MapItemRecord>[,] NeighborItems { get; } = InitNeighborItems();

    private static Dictionary<int, MapItemRecord>[,] InitNeighborItems()
    {
        var g = new Dictionary<int, MapItemRecord>[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = InitMapItems();
        }

        return g;
    }

    /// <summary>
    /// Visiting (chasing) NPCs that crossed in from a neighbor map, keyed by their permanent
    /// <c>(SpawnMapNum, SpawnSlot)</c> identity.  Each carries its own CurrentMapNum and is drawn
    /// on whichever grid cell holds that map — independent of the fixed per-cell slot arrays.
    /// </summary>
    public Dictionary<(int SpawnMapNum, int SpawnSlot), ClientTraversalNpc> TraversalNpcs { get; } = new();

    /// <summary>The item dictionary (keyed by server slot id) for a given map number, or null if not currently observed.</summary>
    public Dictionary<int, MapItemRecord>? ItemsForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        if (mapNum == CenterMapNum) return MapItems;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1) && NeighborMapNums[c, r] == mapNum)
                    return NeighborItems[c, r];
            }
        }

        return null;
    }

    private static Dictionary<int, MapItemRecord> InitMapItems() => new();

    private static ClientMapNpc[] InitMapNpcs()
    {
        var arr = new ClientMapNpc[Constants.MaxMapNpcs + 1];
        for (int i = 0; i <= Constants.MaxMapNpcs; i++) arr[i] = new ClientMapNpc();
        return arr;
    }

    /// <summary>Set by <c>InputProcessor</c> when the melee key aimed at an interactable NPC on the OTHER plane —
    /// refused rather than sent. One-shot: the Shell drains it into a chat refusal and clears it (same hand-off as
    /// <see cref="BankOpen"/>), because Core owns the decision but has no chat of its own.</summary>
    public bool NpcInteractWrongLayer { get; set; }
}

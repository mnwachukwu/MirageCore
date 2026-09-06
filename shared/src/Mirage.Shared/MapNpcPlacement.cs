using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>Why a fixed NPC-spawn pin can't sit at a tile. <see cref="None"/> = the
/// footprint fits.</summary>
public enum NpcPlacementError { None, OffMap, OnBlocked, Overlap }

/// <summary>Size-aware fixed-spawn placement validation, shared so the editor (place + render) and the server
/// (save backstop) agree on what "fits". A size-S NPC occupies an SxS block anchored at its TOP-LEFT pin tile,
/// matching <c>SpawnSystem.IsFootprintOnWalkableGround</c> + the <see cref="WorldCoordHelper"/> footprint math.</summary>
public static class MapNpcPlacement
{
    /// <summary>Validate the pin at (x,y) on <paramref name="layer"/> for the entry at <paramref name="entryIndex"/>:
    /// its NPC's footprint must be fully on-map, all Walkable on that layer, and not overlap any OTHER pinned entry
    /// ON THE SAME LAYER. Different-layer pins never conflict, so a Ground pin and a Fringe pin may stack on one
    /// tile. Returns the first failure. <paramref name="npcSize"/> maps an NPC number to its EffectiveSize (>= 1).
    /// <paramref name="overlapBelowIndex"/> limits the overlap check to entries with a LOWER index — the server's
    /// first-wins sanitize passes the current index so an overlap drops the later pin; the editor leaves it at the
    /// default (check every other pin).</summary>
    public static NpcPlacementError ValidatePin(MapRecord map, int entryIndex, int x, int y, WorldLayer layer,
        Func<int, int> npcSize, int overlapBelowIndex = int.MaxValue)
    {
        int npcNum = entryIndex >= 0 && entryIndex < map.Npcs.Count ? map.Npcs[entryIndex].Npc : 0;
        int size = Math.Max(1, npcSize(npcNum));

        if (x < 0 || y < 0 || x + size > map.Width || y + size > map.Height)
            return NpcPlacementError.OffMap;

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                if (LayerLogic.AttrFor(map.Tile[x + i, y + j], layer).Type != TileType.Walkable) return NpcPlacementError.OnBlocked;
        }

        for (int k = 0; k < map.Npcs.Count && k < overlapBelowIndex; k++)
        {
            if (k == entryIndex) continue;
            var e = map.Npcs[k];
            if (!e.HasPin || e.PinLayer != layer) continue;   // unpinned, or a different plane → can stack
            if (WorldCoordHelper.FootprintsOverlap(x, y, size, e.PinX!.Value, e.PinY!.Value, Math.Max(1, npcSize(e.Npc))))
                return NpcPlacementError.Overlap;
        }

        return NpcPlacementError.None;
    }
}

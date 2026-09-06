namespace Mirage.Shared.Records;

/// <summary>What resizing a map to a given size would cost. Every count is of something that EXISTS and
/// would stop existing — a blank tile falling outside the new bounds costs nothing and is not counted, so
/// a zero across the board means the resize takes nothing with it.</summary>
public readonly record struct MapResizeCost(int AuthoredTiles, int Lights, int NpcPins, int InboundWarps)
{
    /// <summary>True when the resize would discard something.</summary>
    public bool IsLossy => AuthoredTiles > 0 || Lights > 0 || NpcPins > 0 || InboundWarps > 0;
}

/// <summary>
/// Changing a map's size.
///
/// <para>Tiles inside both the old and new bounds are carried over; everything outside the new bounds is
/// gone. <b>There is no undo</b> — not in the editor, and not by any other means, because the discarded
/// tiles are not written anywhere first. <see cref="CostOf"/> exists so an author can be told exactly what
/// they are about to lose before they agree to lose it.</para>
///
/// <para>Anything that names a tile by coordinate goes with the tile: a placed light or an NPC spawn pin
/// outside the new bounds is dropped rather than left pointing at nothing.</para>
/// </summary>
public static class MapResize
{
    /// <summary>Every map joined to <paramref name="mapNum"/> — the ones it names as neighbours, and the
    /// ones naming it. Ascending, each listed once.
    ///
    /// <para>A linked map cannot be resized. World coordinates run continuously across a seam, so every map
    /// in a neighbourhood has to be the same size for a step across one to land where it looks like it
    /// should; resizing one alone would make the seam lie. Unlinking first is what makes it possible, and
    /// the size to settle on is a decision to take before a zone is joined up rather than after.</para></summary>
    public static IReadOnlyList<int> LinkedMaps(IReadOnlyList<MapRecord?> allMaps, int mapNum)
    {
        var linked = new SortedSet<int>();
        if (mapNum > 0 && mapNum < allMaps.Count && allMaps[mapNum] is { } self)
        {
            foreach (int n in (int[])[self.Up, self.Down, self.Left, self.Right])
                if (n > 0 && n != mapNum) linked.Add(n);
        }
        for (int m = 0; m < allMaps.Count; m++)
        {
            if (m == mapNum || allMaps[m] is not { } other) continue;
            if (other.Up == mapNum || other.Down == mapNum || other.Left == mapNum || other.Right == mapNum)
                linked.Add(m);
        }
        return [.. linked];
    }

    /// <summary>What <paramref name="map"/> would lose at <paramref name="size"/>.
    /// <paramref name="allMaps"/> is the world, used to count warps on OTHER maps that land on a tile this
    /// resize removes; pass null to skip that count (it is the only part needing the whole world).</summary>
    public static MapResizeCost CostOf(MapRecord map, MapSize size, IReadOnlyList<MapRecord?>? allMaps = null,
                                       int thisMapNum = 0)
    {
        size = size.Clamped();
        int tiles = 0;
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                if (x < size.Width && y < size.Height) continue;
                if (!IsBlank(map.Tile[x, y])) tiles++;
            }
        }

        int lights = map.Lights.Count(l => l.X >= size.Width || l.Y >= size.Height);
        int pins = map.Npcs.Count(n => n.HasPin && (n.PinX!.Value >= size.Width || n.PinY!.Value >= size.Height));
        int warps = allMaps is null ? 0 : CountInboundWarps(allMaps, thisMapNum, size);

        return new MapResizeCost(tiles, lights, pins, warps);
    }

    /// <summary>Resizes <paramref name="map"/> in place. Tiles common to both sizes are carried over as they
    /// are; new ground is blank; anything outside the new bounds is discarded.</summary>
    public static void Apply(MapRecord map, MapSize size)
    {
        size = size.Clamped();
        var grid = TileGrid.Empty(size.Width, size.Height);
        int keepW = Math.Min(map.Width, size.Width);
        int keepH = Math.Min(map.Height, size.Height);
        for (int x = 0; x < keepW; x++)
        {
            for (int y = 0; y < keepH; y++)
                grid[x, y] = map.Tile[x, y];
        }
        map.Tile = grid;

        // A light or a spawn pin names a tile. When the tile goes, so does it.
        map.Lights.RemoveAll(l => !map.Contains(l.X, l.Y));
        for (int i = 0; i < map.Npcs.Count; i++)
        {
            var e = map.Npcs[i];
            if (e.HasPin && !map.Contains(e.PinX!.Value, e.PinY!.Value))
                map.Npcs[i] = e with { PinX = null, PinY = null };
        }
    }

    /// <summary>Warps on any OTHER map whose destination falls outside <paramref name="size"/> on
    /// <paramref name="thisMapNum"/>. They are counted, never repaired: a warp pointing at nothing is
    /// refused at run time and reported, and correcting it is the author's job.</summary>
    private static int CountInboundWarps(IReadOnlyList<MapRecord?> allMaps, int thisMapNum, MapSize size)
    {
        if (thisMapNum <= 0) return 0;
        int count = 0;
        for (int m = 0; m < allMaps.Count; m++)
        {
            var other = allMaps[m];
            if (other is null) continue;
            for (int x = 0; x < other.Width; x++)
            {
                for (int y = 0; y < other.Height; y++)
                {
                    var t = other.Tile[x, y];
                    if (LandsOutside(t.Type, t.WarpMap, t.WarpX, t.WarpY)) count++;
                    if (t.FringeAttr is { } fa && LandsOutside(fa.Type, fa.WarpMap, fa.WarpX, fa.WarpY)) count++;
                }
            }
        }
        return count;

        bool LandsOutside(TileType type, short warpMap, ushort wx, ushort wy) =>
            type == TileType.Warp && warpMap == thisMapNum && (wx >= size.Width || wy >= size.Height);
    }

    // A tile nobody authored: no art on any stack, no attribute, no fringe plane.
    private static bool IsBlank(TileRecord t)
    {
        if (t.Type != TileType.Walkable || t.FringeAttr is not null) return false;
        foreach (int cell in t.Ground) if (!LayerCell.IsEmpty(cell)) return false;
        foreach (int cell in t.Fringe) if (!LayerCell.IsEmpty(cell)) return false;
        foreach (int cell in t.Canopy) if (!LayerCell.IsEmpty(cell)) return false;
        return true;
    }
}

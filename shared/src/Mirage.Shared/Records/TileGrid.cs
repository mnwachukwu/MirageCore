namespace Mirage.Shared.Records;

/// <summary>Builds a map's tile grid. The array's own dimensions ARE the map's size — no field records it
/// and nothing else has to agree with it — so every grid in the world is allocated through here.</summary>
public static class TileGrid
{
    /// <summary>A grid of <paramref name="width"/> x <paramref name="height"/> empty tiles, every cell
    /// addressable without a null check. A map is at least one tile on each axis.</summary>
    public static TileRecord[,] Empty(int width, int height)
    {
        var tiles = new TileRecord[Math.Max(1, width), Math.Max(1, height)];
        for (int x = 0; x < tiles.GetLength(0); x++)
        {
            for (int y = 0; y < tiles.GetLength(1); y++)
                tiles[x, y] = new TileRecord();
        }
        return tiles;
    }
}

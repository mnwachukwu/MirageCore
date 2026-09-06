using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.World;

/// <summary>
/// Server-side <see cref="LayerLogic.IWorldTileView"/>: resolves a world-tile coordinate to its
/// <see cref="TileRecord"/> across the 3x3 map grid, so a bridge footprint / ramp that spans a seamless
/// seam is read uniformly.  A coordinate that resolves off the grid returns null (LayerLogic treats that
/// as "no fringe" / not walkable).  Mirrors <see cref="WorldLosPredicate"/>; a readonly struct so it never
/// allocates.
/// </summary>
internal readonly struct ServerTileView(GameWorld world, MapGrid grid) : LayerLogic.IWorldTileView
{
    private readonly GameWorld _world = world;
    private readonly MapGrid _grid = grid;

    public TileRecord? At(int worldX, int worldY)
    {
        var (mapNum, lx, ly) = _grid.ResolveWorldTile(worldX, worldY);
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return null;
        return _world.Maps[mapNum]?.Tile[lx, ly];
    }
}

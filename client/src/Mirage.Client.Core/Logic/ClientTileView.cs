using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Client-side <see cref="LayerLogic.IWorldTileView"/>: resolves a world-tile coordinate (over the local
/// player's 3x3 <see cref="ClientState.NeighborMaps"/> grid, center = cell 1,1) to its <see cref="TileRecord"/>,
/// so the movement-prediction bridge gate reads the same tiles across a seam that the server's
/// <c>ServerTileView</c> does.  A coordinate off the loaded grid returns null (LayerLogic treats that as
/// "no fringe" / not walkable).  Mirrors the <c>SpellLosPredicate</c> cell math; a readonly struct so the
/// per-step prediction gate never allocates.
/// </summary>
internal readonly struct ClientTileView(ClientState state) : LayerLogic.IWorldTileView
{
    private readonly ClientState _state = state;

    public TileRecord? At(int worldX, int worldY)
    {
        int col = worldX / _state.MapTilesX;
        int row = worldY / _state.MapTilesY;
        if ((uint)col > 2 || (uint)row > 2) return null;
        var map = _state.NeighborMaps[col, row];
        if (map is null) return null;
        int lx = worldX - col * _state.MapTilesX;
        int ly = worldY - row * _state.MapTilesY;
        return map.Tile[lx, ly];
    }
}

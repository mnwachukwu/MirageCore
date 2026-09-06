using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Client-side spell line-of-sight queries. Mirrors the server's authoritative HasLineOfSight
/// (same WorldCoordHelper algorithm) so the target-arrow color, tab-target filter, and any other
/// "can I cast on this?" UI agree with what the server would actually permit.
///
/// Tiles outside the loaded observable area, or sitting under a Blocked / closed-Key tile, count
/// as blockers. Door state is taken from <see cref="ClientState.TempTile"/> for the center cell
/// (NeighborTempTiles[1,1] is reset to empty on every seam shift and would falsely report doors
/// closed) and from <see cref="ClientState.NeighborTempTiles"/> for the eight neighbor cells.
/// </summary>
public static class ClientLineOfSight
{
    /// <summary>True when the straight tile-line from the local player to (targetWX, targetWY) is clear of
    /// Blocked tiles and closed Key doors — obstacles read on the LOCAL PLAYER'S layer (a fringe wall blocks a
    /// fringe shot, not a ground shot beneath).  This form has NO endpoint layer gate, so the tab/click target
    /// filter still lets you cycle onto a cross-layer target (the arrow grays out; the server rejects the cast).</summary>
    public static bool HasClearFromLocalPlayer(ClientState state, int targetWorldX, int targetWorldY)
    {
        var me = state.Me;
        int myWX = state.MapTilesX + me.X;
        int myWY = state.MapTilesY + me.Y;
        return WorldCoordHelper.HasClearSpellLineOfSight(myWX, myWY, targetWorldX, targetWorldY,
            new SpellLosPredicate(state, me.Layer));
    }

    /// <summary>Arrow-feedback form: a full mirror of the server's HasLineOfSight.  The caster and target must
    /// first CONNECT across layers (same layer always; across layers only when one of them is on a ramp — a person
    /// on a ramp can shoot both the ground and the deck); then the obstacle line-of-sight above applies.  So a
    /// gray arrow means the server would actually refuse the cast.</summary>
    public static bool HasClearFromLocalPlayer(ClientState state, int targetWorldX, int targetWorldY, WorldLayer targetLayer)
    {
        var me = state.Me;
        int myWX = state.MapTilesX + me.X;
        int myWY = state.MapTilesY + me.Y;
        if (!LayerLogic.LayerConnects(new ClientTileView(state), myWX, myWY, me.Layer, targetWorldX, targetWorldY, targetLayer))
            return false;
        // Cross-layer cast: ramp tiles on the line block (mirrors the server) — can't cast through a ramp to a
        // target behind/under it; only a clean shot at the ramp foot (an excluded endpoint) lands.
        return WorldCoordHelper.HasClearSpellLineOfSight(myWX, myWY, targetWorldX, targetWorldY,
            new SpellLosPredicate(state, me.Layer, blockRamps: me.Layer != targetLayer));
    }

    /// <summary>Just the cross-layer CONNECT half of the rule above, with no obstacle line — what NPC INTERACTION
    /// needs. The server's interact gate (GameWorld.IsNpcInInteractRange) is r=5 with no line-of-sight, so reaching
    /// a keeper only asks that the two planes connect: same layer always, or across them from a ramp's mount side.
    /// Keeps the right-click menu and the melee key from offering an interaction the server would refuse.</summary>
    public static bool LayerConnectsFromLocalPlayer(ClientState state, int targetWorldX, int targetWorldY, WorldLayer targetLayer)
    {
        var me = state.Me;
        return LayerLogic.LayerConnects(new ClientTileView(state),
            state.MapTilesX + me.X, state.MapTilesY + me.Y, me.Layer,
            targetWorldX, targetWorldY, targetLayer);
    }

    // readonly struct so the generic LoS helper specializes per call site: no boxing on the
    // interface, no closure alloc — zero GC per frame on the arrow color check or per tab cycle.
    // Obstacles are read on the shooter's layer (via LayerLogic.AttrFor), mirroring the server predicate.
    private readonly struct SpellLosPredicate(ClientState state, WorldLayer layer, bool blockRamps = false) : ISpellLosPredicate
    {
        private readonly ClientState _state = state;
        private readonly WorldLayer _layer = layer;
        private readonly bool _blockRamps = blockRamps;

        public bool IsBlocked(int worldX, int worldY)
        {
            int col = worldX / _state.MapTilesX;
            int row = worldY / _state.MapTilesY;
            if (col < 0 || col > 2 || row < 0 || row > 2) return true;
            var map = _state.NeighborMaps[col, row];
            if (map is null) return true;
            int lx = worldX - col * _state.MapTilesX;
            int ly = worldY - row * _state.MapTilesY;
            var tile = map.Tile[lx, ly];
            if (_blockRamps && tile.FringeAttr is { Type: TileType.LayerRamp }) return true;   // ramp = wall on a cross-layer line
            var attr = LayerLogic.AttrFor(tile, _layer);
            var type = attr.Type;
            // A wall stops sight only if it is authored to. A railing or a window is Blocked to walk
            // through and clear to see through.
            if (type == TileType.Blocked) return attr.BlocksSight;
            // Center cell's authoritative door state is in state.TempTile; NeighborTempTiles[1,1]
            // is reset to empty on every seam shift and would falsely report doors closed.
            var doors = (col == 1 && row == 1) ? _state.TempTile : _state.NeighborTempTiles[col, row];
            if (type == TileType.Key && !doors[lx, ly, (int)_layer]) return true;   // door read on the shooter's layer
            return false;
        }
    }
}

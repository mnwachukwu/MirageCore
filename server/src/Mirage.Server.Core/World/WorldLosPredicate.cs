using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.World;

/// <summary>
/// World-tile blocking predicate for <see cref="WorldCoordHelper.HasClearSpellLineOfSight"/>.
/// Walls always block; a Key tile blocks only while its door is closed (open Key = you can see
/// through the doorway, matching movement).  Tiles outside the observer's 3×3 observable area
/// count as walls — LoS can't reach where the server hasn't loaded.  Shared by every world-LoS
/// consumer on the server (spell cast gate, NPC aggro acquisition).
/// <para>Two-layer world: obstacles are read on the SHOOTER'S layer (via LayerLogic.AttrFor), so a closed
/// fringe door / railing blocks a fringe-to-fringe shot but NOT a ground shot passing beneath it — and a
/// ramp reads solid on the ground (blocks a ground shot) while being clear on the fringe (the deck the shot
/// travels).</para>
/// <para><c>blockRamps</c>: a ramp is a PHYSICAL block on a CROSS-LAYER spell line — you can't cast through
/// a ramp to a target behind or under it. The LoS trace excludes both endpoints, so a clean cross-layer shot
/// at a ramp FOOT still connects (the ramp is the target's own tile), while a ramp mid-line blocks. Enabled
/// only for cross-layer casts; a same-layer shot uses the plain AttrFor read (a ground ramp already reads
/// Blocked there anyway).</para>
/// <para>Held as a readonly struct so the generic LoS helper specializes per call site: no boxing on the
/// interface, no closure alloc on the predicate — zero GC per check.</para>
/// </summary>
internal readonly struct WorldLosPredicate(GameWorld world, MapGrid grid, WorldLayer layer, bool blockRamps = false) : ISpellLosPredicate
{
    private readonly GameWorld _world = world;
    private readonly MapGrid _grid = grid;
    private readonly WorldLayer _layer = layer;
    private readonly bool _blockRamps = blockRamps;

    /// <inheritdoc/>
    public bool IsBlocked(int worldX, int worldY)
    {
        var (mapNum, lx, ly) = _grid.ResolveWorldTile(worldX, worldY);
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return true;
        var map = _world.Maps[mapNum];
        if (map is null) return true;
        var tile = map.Tile[lx, ly];
        if (_blockRamps && tile.FringeAttr is { Type: TileType.LayerRamp }) return true;   // ramp = wall on a cross-layer line
        var attr = LayerLogic.AttrFor(tile, _layer);
        var type = attr.Type;
        // A wall stops sight only if it is authored to. A railing or a window is Blocked to walk through
        // and clear to see through.
        if (type == TileType.Blocked) return attr.BlocksSight;
        if (type == TileType.Key && !_world.TempTiles[mapNum].IsDoorOpen(lx, ly, _layer)) return true;
        return false;
    }
}

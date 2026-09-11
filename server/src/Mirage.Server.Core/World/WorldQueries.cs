using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.World;

/// <summary>
/// Spatial questions about the live world: what a body can reach, what is standing on a run of tiles,
/// what is near enough to see, and which record a stable identity currently names.
///
/// <para>Every answer here is geometry and bookkeeping. Nothing decides whether an interaction is
/// permitted, so a game layer asks these the same way the engine does and applies its own rules to the
/// answers.</para>
///
/// <para><b>Seam-aware throughout.</b> Each query builds the 3x3 map grid around a center map and works
/// in world-relative coordinates, so a body one tile away across a map border is one tile away. A query
/// whose subject is not reachable from the center map answers "no" rather than throwing.</para>
/// </summary>
public sealed class WorldQueries(GameWorld world, PlayerManager players)
{
    private readonly GameWorld _world = world;
    private readonly PlayerManager _players = players;

    /// <summary>Where a stable identity currently is: the map it stands on, the native slot it occupies
    /// (0 for a visitor, which has no slot on the map it is visiting), and its live record.</summary>
    public readonly record struct NpcLocation(int CurrentMap, int CurrentSlot, MapNpcRecord Record);

    // ── Reach ─────────────────────────────────────────────────────────────────

    /// <summary>True when a body on <paramref name="actorMap"/> facing <paramref name="dir"/> from
    /// (ax, ay) is one tile — in world space — from (tx, ty) on <paramref name="targetMap"/>.
    ///
    /// <para>False when the target's map is not part of the grid around the actor's, which is what
    /// stops a query reaching somewhere unreachable rather than answering about the wrong tile.</para></summary>
    public bool IsFacingAcrossMaps(int actorMap, Direction dir, int ax, int ay, int targetMap, int tx, int ty)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var tw = grid.ToWorldRelative(targetMap, tx, ty);
        if (tw is null) return false;
        var (awx, awy) = grid.CenterToWorld(ax, ay);
        return WorldCoordHelper.IsAdjacentInDir(awx, awy, dir, tw.Value.worldX, tw.Value.worldY);
    }

    /// <summary>Footprint-aware facing test: true when the tile one step in <paramref name="dir"/> from
    /// (ax, ay) lands on any tile of the NPC's <paramref name="size"/>x<paramref name="size"/> footprint.
    /// For a size-1 NPC this is <see cref="IsFacingAcrossMaps"/> against its single tile.</summary>
    public bool IsFacingNpcAcrossMaps(int actorMap, Direction dir, int ax, int ay, int npcMap,
                                      MapNpcRecord mapNpc, int size)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var nw = grid.ToWorldRelative(npcMap, mapNpc.X, mapNpc.Y);
        if (nw is null) return false;
        var (awx, awy) = grid.CenterToWorld(ax, ay);
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        return WorldCoordHelper.FootprintContains(nw.Value.worldX, nw.Value.worldY, size, awx + dx, awy + dy);
    }

    /// <summary>Whether two adjacent tiles connect ACROSS the two gameplay layers: same layer always,
    /// and between layers only where one side stands on a ramp. Seam-aware.
    ///
    /// <para>Adjacency alone is not reach. A body on the ground and a body on the walkable top of a
    /// bridge can occupy neighboring tiles and still have no way to touch each other.</para></summary>
    public bool LayerConnectsInDir(int actorMap, int ax, int ay, WorldLayer actorLayer, Direction dir,
                                   WorldLayer targetLayer)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(ax, ay);
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        return LayerLogic.LayerConnects(view, aWX, aWY, actorLayer, aWX + dx, aWY + dy, targetLayer);
    }

    // ── Area sweep ────────────────────────────────────────────────────────────

    /// <summary>One body found by a sweep: an NPC, with the map and native slot to address it by, or a
    /// player index. Exactly one of the two is set.</summary>
    public readonly record struct SweptBody(MapNpcRecord? Npc, int NpcMap, int NpcSlot, int PlayerIndex);

    /// <summary>Everything standing on <paramref name="run"/>, deduped, in tile order.
    ///
    /// <para><b>Asked tile by tile, never by walking rosters.</b> A sweep that iterates native NPC
    /// slots, then the guest list, then the player set has to be taught about every kind of body that
    /// can stand on a map, and silently misses the ones it was never taught. Every tile is asked
    /// "who is here", which is the question that cannot go out of date.</para>
    ///
    /// <para>Bodies are DEDUPED: a body covering two tiles of the run is returned once. A gap in the
    /// run costs the tiles past it nothing — each tile is asked independently.</para>
    ///
    /// <para><paramref name="srcDx"/>/<paramref name="srcDy"/> step from a run tile BACK toward the
    /// origin, so each tile is layer-connect tested from where the reach actually comes from. Pass
    /// (0, 0) for a sweep with no direction to step back along, and the tile's own layer is used.</para>
    ///
    /// <para><b>The caller owns <paramref name="into"/>.</b> It is cleared and filled, so a caller that
    /// keeps one list and passes it every time allocates nothing — and two callers sweeping in the same
    /// tick cannot overwrite each other's results.</para></summary>
    public void SweepTiles(in MapGrid grid, in TileRun run, WorldLayer fromLayer,
                           int srcDx, int srcDy, List<SweptBody> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        // Built here rather than taken: it reads tiles through the same grid the run is expressed in,
        // so a caller passing a view built from a different grid would resolve every layer test against
        // the wrong maps. A non-allocating readonly struct, so owning it costs nothing.
        var view = new ServerTileView(_world, grid);

        for (int i = 0; i < run.Count; i++)
        {
            var (wx, wy) = run[i];
            var (tMap, tx, ty) = grid.ResolveWorldTile(wx, wy);
            if (tMap <= 0) continue;

            if (_world.NpcCoveringLocal(tMap, tx, ty) is { } hit)
            {
                if (Connects(view, wx, wy, fromLayer, srcDx, srcDy, hit.Npc.Layer) && !AlreadySwept(into, hit.Npc))
                {
                    into.Add(new SweptBody(hit.Npc, tMap, hit.Slot, 0));
                }
            }

            // Players are one tile each, so the map's observer set is the cheapest way to find whoever
            // is standing here — everyone on a map observes it.
            foreach (int p in _world.MapObservers[tMap])
            {
                if (!_players[p].IsPlaying) continue;
                var pc = _players[p].Char;
                if (pc.Map != tMap || pc.X != tx || pc.Y != ty) continue;
                if (!Connects(view, wx, wy, fromLayer, srcDx, srcDy, pc.Layer)) continue;
                into.Add(new SweptBody(null, 0, 0, p));
            }
        }
    }

    private static bool Connects(ServerTileView view, int wx, int wy, WorldLayer fromLayer,
                                 int srcDx, int srcDy, WorldLayer bodyLayer)
        => (srcDx == 0 && srcDy == 0)
            ? fromLayer == bodyLayer
            : LayerLogic.LayerConnects(view, wx - srcDx, wy - srcDy, fromLayer, wx, wy, bodyLayer);

    private static bool AlreadySwept(List<SweptBody> found, MapNpcRecord npc)
    {
        for (int i = 0; i < found.Count; i++)
        {
            if (ReferenceEquals(found[i].Npc, npc)) return true;
        }

        return false;
    }

    // ── Resolving an identity ─────────────────────────────────────────────────

    /// <summary>Where the NPC named by <paramref name="handle"/> currently is, or null when nothing in
    /// the world answers to that identity.</summary>
    public NpcLocation? ResolveNpc(EntityHandle handle)
        => handle.IsNpc ? ResolveNpc(handle.SpawnMap, handle.SpawnSlot) : null;

    /// <summary>Where the NPC that spawns in <paramref name="spawnSlot"/> on
    /// <paramref name="spawnMap"/> currently is, or null when it is not in the world.
    ///
    /// <para>A spawn identity is stable across map borders, so this is the only way to follow one body
    /// as it moves: its native slot is reserved while it is away, and it stands on another map with no
    /// slot of its own. The home slot is tried first, then the guest lists — a game-wide scan, which is
    /// cheap because the number of bodies away from home at once is small.</para>
    ///
    /// <para>Answers about EXISTENCE, not condition. Whether the body it found is in any state worth
    /// interacting with is the caller's question.</para></summary>
    public NpcLocation? ResolveNpc(int spawnMap, int spawnSlot)
    {
        if (spawnMap <= 0 || spawnSlot <= 0 || spawnMap > _world.Limits.Maps || spawnSlot > Constants.MaxMapNpcs)
        {
            return null;
        }

        var native = _world.MapNpcs[spawnMap, spawnSlot];
        if (native.Num > 0 && !native.IsReservedSlot) return new NpcLocation(spawnMap, spawnSlot, native);
        if (!native.IsReservedSlot) return null;

        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            var guests = _world.MapTraversalNpcs[m];
            for (int g = 0; g < guests.Count; g++)
            {
                var t = guests[g];
                // Slot 0: a visitor has no native slot on the map it is standing on, so a caller
                // addresses it through the record rather than by slot.
                if (t.SpawnMapNum == spawnMap && t.SpawnSlot == spawnSlot && t.Num > 0)
                {
                    return new NpcLocation(m, 0, t);
                }
            }
        }

        return null;
    }

    // ── Viewport ──────────────────────────────────────────────────────────────

    /// <summary>Every NPC within viewport range of (<paramref name="centerX"/>, <paramref name="centerY"/>)
    /// on <paramref name="centerMap"/>, natives and visitors alike, across the 3x3 grid of maps around it.
    ///
    /// <para>Walks the grid rather than one map, because a body near a border can see and be seen across
    /// it. Enumerates whatever exists; filtering to the ones a caller cares about is the caller's
    /// job.</para></summary>
    public IEnumerable<NpcLocation> NpcsInViewport(int centerMap, int centerX, int centerY)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, centerMap);
        var (cwx, cwy) = grid.CenterToWorld(centerX, centerY);

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;

                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var other = _world.MapNpcs[m, s];
                    if (other.Num <= 0) continue;
                    var (owx, owy) = grid.ToWorld(col, row, other.X, other.Y);
                    if (WorldCoordHelper.IsWithinViewport(cwx, cwy, owx, owy))
                    {
                        yield return new NpcLocation(m, s, other);
                    }
                }

                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0) continue;
                    var (owx, owy) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (WorldCoordHelper.IsWithinViewport(cwx, cwy, owx, owy))
                    {
                        yield return new NpcLocation(m, 0, gt);
                    }
                }
            }
        }
    }
}

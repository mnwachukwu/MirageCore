using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Buffers;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The breadth-first chase solver. <c>FindStepTowardObservableArea</c> answers it for one
/// source tile; <c>FillPathField</c> answers it for EVERY source tile at once and
/// <c>CachedStepTowardObservableArea</c> shares that one flood across a whole gang for a pass.
/// Also the occupancy bitmaps and footprint tests the flood treats as walls.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Direction deltas for BFS neighbor expansion (Up/Down/Left/Right) and the corresponding
    // direction the NPC steps to go FROM that neighbor back to the current tile.  Static so they
    // never allocate per call — the BFS hot path runs once per chasing NPC per tick.
    private static readonly int[] _bfsDx = { 0, 0, -1, 1 };
    private static readonly int[] _bfsDy = { -1, 1, 0, 0 };
    private static readonly Direction[] _bfsStepFromNeighbor = { Direction.Down, Direction.Up, Direction.Right, Direction.Left };

    // BFS for the shortest walkable path from (fromX,fromY) on `mapNum` to (toX,toY) on `targetMap`,
    // across the WHOLE 3×3 observable area, measured in the neighbourhood's own tile size.  Returns the first step direction
    // the NPC should take — the actual move for this tick — letting it route around walls, U-shapes,
    // and any solid map geometry AND follow linked borders into neighbor maps when that's shorter.
    // Cell crossings run the same entry gate as the live cross step, so a planned route can never reach
    // somewhere the NPC could not actually step.  Tiles currently held by other players
    // and NPCs are pinned as walls via a one-shot occupancy snapshot — so the planned route matches
    // what the NPC can actually step to THIS tick, and the plan stays consistent across ticks while
    // a blocker persists (no oscillation against a stationary mob standing between NPC and target).
    // Source and target tiles are special-cased: source is the BFS termination so its own occupancy
    // is never checked; target is the BFS root so the same is true.  Returns null when no walkable
    // path connects source and target in the observable area.
    //
    // Two-layer world: the BFS state is (cell, WorldLayer), 2*N states.  It roots at the target's layer
    // (targetLayer) and terminates on the chaser's layer (fromLayer); a ramp is the only place a step
    // crosses between layers (LayerLogic.CanEnter decides, matching live movement).  So an NPC under a
    // bridge routes to a ramp to reach a target on top, and one that can't reach the target's layer at all
    // returns null (the acquire gate then skips it / the give-up timer drops the lock).
    private Direction? FindStepTowardObservableArea(int mapNum, int fromX, int fromY, WorldLayer fromLayer,
                                             int targetMap, int toX, int toY, WorldLayer targetLayer,
                                             NpcRecord npc,
                                             bool planAroundActors = false, int selfSpawnMap = 0, int selfSpawnSlot = 0,
                                             int targetSize = 1)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        // The BFS runs over the whole observable area in the neighbourhood's own tile size.
        int W = grid.TilesX, H = grid.TilesY;
        int RW = 3 * W, RH = 3 * H;
        int N = RW * RH;                            // tiles per layer; the state space is 2*N (ground+fringe)
        var gp = grid.PositionOf(targetMap);
        if (gp is null) return null;  // target outside observable area — caller falls back to warp follow
        var view = new ServerTileView(_world, grid);

        var (srcWX, srcWY) = grid.CenterToWorld(fromX, fromY);
        var (tgtWX, tgtWY) = grid.ToWorld(gp.Value.col, gp.Value.row, toX, toY);
        if (srcWX == tgtWX && srcWY == tgtWY && fromLayer == targetLayer) return null;  // already on the target state
        int srcLayerI = (int)fromLayer;

        // Occupancy source for the BFS wall-mask (empty => blind, path on static geometry alone).  Indexed by
        // STATE (layer*N + cell), so an actor standing on the fringe surface never walls a ground walker beneath:
        //  * planAroundActors — a STALLED chaser re-planning: live actors become walls so it routes AROUND
        //    a mid-path blocker instead of parking behind it, EXCEPT the chaser's own pursuers, which stay
        //    walkable so a guard walling off its target isn't danced around.  Built fresh (rare path) from
        //    current positions, not the brain-tick cache.
        //  * NpcChasePlansAroundLiveActors (global, default off) — the shared full-occupancy snapshot,
        //    reused per (map, tick): a few microseconds warm, near-zero thereafter.
        // Scratch for this BFS. Rented, never stackalloc'd: 2*N states over a nine-map area is a few
        // kilobytes at the default size and several megabytes on a large map, and the game thread's stack
        // would not survive the second case.
        byte[] stalledBuf = [], visitedBuf = ArrayPool<byte>.Shared.Rent(2 * N);
        int[] queueBuf = ArrayPool<int>.Shared.Rent(2 * N);
        try
        {
        scoped Span<byte> occupied;   // scoped: never escapes this method
        if (planAroundActors)
        {
            stalledBuf = ArrayPool<byte>.Shared.Rent(2 * N);
            var stalledOcc = stalledBuf.AsSpan(0, 2 * N);
            stalledOcc.Clear();
            FillOccupancyExcludingPursuers(stalledOcc, mapNum, in grid, selfSpawnMap, selfSpawnSlot);
            occupied = stalledOcc;
        }
        else if (NpcChasePlansAroundLiveActors)
        {
            occupied = GetOccupancyBitmap(mapNum, in grid);
        }
        else
        {
            occupied = default;
        }
        Span<byte> visited = visitedBuf.AsSpan(0, 2 * N);
        Span<int> queue = queueBuf.AsSpan(0, 2 * N);
        visited.Clear();

        // Guards path across NpcAvoid ("npc block") tiles as if walkable; every other behavior
        // still treats them as walls, so the planned route only cuts through for guards.
        bool ignoreNpcAvoid = MovementSystem.NpcIgnoresNpcAvoid(npc.Behavior);
        int footprintSize = npc.EffectiveSize;   // >1 => the BFS must fit the whole SxS body at each cell

        int head = 0, tail = 0;

        // BFS expands outward from the TARGET STATE (its cell on its layer) so each state's recorded direction
        // is the step toward target; reaching the source state (cell AND layer) yields the NPC's first move.
        int startIdx = (int)targetLayer * N + tgtWY * RW + tgtWX;
        visited[startIdx] = 1;
        queue[tail++] = startIdx;

        while (head < tail)
        {
            int cur = queue[head++];
            int curLayerI = cur >= N ? 1 : 0;               // decode the state index → (layer, cell)
            var curLayer = (WorldLayer)curLayerI;
            int cell = cur - curLayerI * N;
            int cwx = cell % RW;
            int cwy = cell / RW;
            int curMap = grid[cwx / W, cwy / H];
            for (int d = 0; d < 4; d++)
            {
                int nwx = cwx + _bfsDx[d];
                int nwy = cwy + _bfsDy[d];
                if ((uint)nwx >= RW || (uint)nwy >= RH) continue;
                var stepDir = _bfsStepFromNeighbor[d];      // the step an NPC at the neighbor takes to reach cur

                int nCol = nwx / W;
                int nRow = nwy / H;
                int nMap = grid[nCol, nRow];

                // Each cell has two candidate states (ground / fringe).  The neighbor→cur step is a real graph
                // edge only when LayerLogic says stepping `stepDir` from the neighbor on `nLayer` actually lands
                // on (cur, curLayer): sticky within a layer, flipping only across a ramp.  This is the SAME gate
                // CanNpcMoveFrom applies live, so a planned step is always executable.  On a bridge-free map the
                // fringe candidate never survives (CanEnter's deck-edge guard fails), collapsing this to the
                // classic single-layer flood.
                for (int nl = 0; nl < 2; nl++)
                {
                    var nLayer = (WorldLayer)nl;
                    int sIdx = nl * N + nwy * RW + nwx;
                    // CanEnter is the expensive call in this loop, and in a flood most candidate edges lead
                    // to a state already reached, so the visited test comes first. The source state is never
                    // marked visited — reaching it returns — so this cannot swallow the answer.
                    if (visited[sIdx] != 0) continue;

                    if (!LayerLogic.CanEnter(view, cwx, cwy, footprintSize, nLayer, stepDir, out var landed)
                        || landed != curLayer)
                    {
                        continue;
                    }

                    if (nwx == srcWX && nwy == srcWY && nl == srcLayerI) return stepDir;
                    // Live blocker — treated as a wall. The WHOLE body has to fit: a big NPC that checks only
                    // its anchor plans straight through an actor standing two tiles to its right.
                    if (!occupied.IsEmpty && FootprintOccupied(occupied, nwx, nwy, footprintSize, nl, N, RW, RH)) continue;

                    if (nMap <= 0) continue;  // unlinked cell — treat as wall

                    // Occupied attack-slot on this layer: a live actor standing where this chaser would have to
                    // stand to swing walls that slot off, so a lined-up trailer routes to an OPEN one.  A slot is
                    // any cell whose BODY would touch the target's — anchor-to-anchor Manhattan 1 names four
                    // tiles, two of which sit INSIDE an oversize target, and misses every real slot around it.
                    // Layer-scoped — an actor on the ground slot does not wall the fringe slot above it.  Mark
                    // visited so it stays walled this BFS.
                    if (WorldCoordHelper.AreFootprintsAdjacent(nwx, nwy, footprintSize, tgtWX, tgtWY, targetSize)
                        && AttackSlotBlockedForBody(nwx, nwy, footprintSize, nLayer, in grid, RW, RH))
                    {
                        visited[sIdx] = 1;
                        continue;
                    }

                    // Walkability of the neighbor STATE at its layer.  AttrFor reads the fringe attribute on the
                    // fringe layer, so a tile with no FringeAttr reads Blocked — the fringe-fit gate falls out
                    // for free (a footprint not wholly on a fringe surface has a Blocked tile and is rejected).
                    if (footprintSize > 1)
                    {
                        // A big NPC may only plan onto a cell where its whole SxS body fits on walkable, linked
                        // tiles at nLayer; the live per-step CanNpcMoveFrom is the backstop for actor occupancy.
                        if (!FootprintBlockWalkable(nwx, nwy, footprintSize, nLayer, in grid, ignoreNpcAvoid)) continue;
                    }
                    else
                    {
                        var tt = LayerLogic.AttrFor(_world.Maps[nMap].Tile[nwx - nCol * W, nwy - nRow * H], nLayer).Type;
                        if (!MovementSystem.IsNpcWalkableTileType(tt, ignoreNpcAvoid)) continue;
                    }

                    visited[sIdx] = 1;
                    queue[tail++] = sIdx;
                }
            }
        }
        return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(visitedBuf);
            ArrayPool<int>.Shared.Return(queueBuf);
            if (stalledBuf.Length > 0) ArrayPool<byte>.Shared.Return(stalledBuf);
        }
    }

    // Blind (non-stalled) counterpart to FindStepTowardObservableArea for the legs pass, backed by the
    // per-pass _pathFieldCache: builds the whole source-agnostic direction field ONCE per (target, footprint,
    // behavior) key (FillPathField) and returns THIS chaser's from-tile step in O(1).  A gang on one target thus
    // shares a single expansion per pass instead of one flood each.  By construction dirField[src] equals
    // exactly what FindStepTowardObservableArea(..., fromX/Y = src, ..., planAroundActors:false) returns for
    // every src (locked by NpcPathCacheTests) — callers see identical steps, just deduplicated.  Valid only
    // while the BFS runs blind (!NpcChasePlansAroundLiveActors); the steppers gate on that before calling.
    private Direction? CachedStepTowardObservableArea(int mapNum, int fromX, int fromY, WorldLayer fromLayer,
                                                      int targetMap, int toX, int toY, WorldLayer targetLayer, NpcRecord npc, int targetSize = 1)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        int W = grid.TilesX, H = grid.TilesY;
        int RW = 3 * W;
        int N = RW * 3 * H;                   // tiles per layer (the field holds 2*N: ground then fringe)

        if (_pathFieldStamp != _pathNow)            // new pass — drop last pass's fields, reuse the buffers
        {
            _pathFieldCache.Clear();
            _pathFieldBuffersUsed = 0;
            _pathFieldStamp = _pathNow;
        }

        var key = new PathFieldKey(mapNum, targetMap, toX, toY, (int)targetLayer, npc.EffectiveSize, (int)npc.Behavior, targetSize);
        if (!_pathFieldCache.TryGetValue(key, out byte[]? field))
        {
            var gp = grid.PositionOf(targetMap);
            if (gp is null)
            {
                field = null;                       // target outside observable area — matches FindStep's early null
            }
            else
            {
                var (tgtWX, tgtWY) = grid.ToWorld(gp.Value.col, gp.Value.row, toX, toY);
                field = RentPathFieldBuffer(2 * N);
                FillPathField(field, mapNum, tgtWX, tgtWY, targetLayer, in grid, npc, targetSize);
            }
            _pathFieldCache[key] = field;
        }
        if (field is null) return null;             // target not in the observable area (caller warp-follows)

        // The chaser stands on the center cell (1,1) at its own layer; ToWorld(1,1,fromX,fromY) = (W+fromX, H+fromY)
        // and its state index is fromLayer*N + that cell.
        int v = field[(int)fromLayer * N + (H + fromY) * RW + (W + fromX)];
        return v == 0 ? null : (Direction)(v - 1);
    }

    // Source-agnostic form of FindStepTowardObservableArea: floods the WHOLE observable area from the target
    // STATE and records, for every (cell, layer) state, the first step FROM that state toward the target
    // (encoded dir+1; 0 = none), so any chaser reads its own (from-cell, from-layer) in O(1).  MUST mirror
    // FindStepTowardObservableArea's per-state checks EXACTLY (same order, incl. the layer-transition gate and
    // the attack-slot visited side-effect) so dirField[src] == that method's return for every src — locked by
    // NpcPathCacheTests.  Blind only: `occupied` stays empty here (the !occupied.IsEmpty branch is kept solely
    // for structural parity with the original, so drift is visible).  Load-bearing invariants:
    //  (1) record the inbound dir right after the layer-transition gate but BEFORE the other validity checks —
    //      mirrors FindStep's source-return (which also sits just after that gate), so a chaser standing on an
    //      occupied attack-slot or non-walkable SOURCE state still gets its step;
    //  (2) guard the record with sIdx != rootIdx so the root state keeps dir 0 (the goal has no step); the same
    //      cell on the OTHER layer is a DISTINCT state and may still record a dir;
    //  (3) mark the root state visited at init so it is never re-enqueued;
    //  (4) the caller Array.Clear'd the 2*N buffer on rent, so no stale directions leak from a prior target.
    private void FillPathField(Span<byte> dirField, int mapNum, int tgtWX, int tgtWY, WorldLayer targetLayer, in MapGrid grid,
                               NpcRecord npc, int targetSize)
    {
        int W = grid.TilesX, H = grid.TilesY;
        int RW = 3 * W, RH = 3 * H;
        int N = RW * RH;                      // tiles per layer; states are layer*N + cell (2*N total)

        var view = new ServerTileView(_world, grid);
        Span<byte> occupied = default;              // blind: static geometry + attack-slot ring only
        // Rented for the same reason as the BFS above: this space follows the observable area, not a constant.
        byte[] visitedBuf = ArrayPool<byte>.Shared.Rent(2 * N);
        int[] queueBuf = ArrayPool<int>.Shared.Rent(2 * N);
        try
        {
        Span<byte> visited = visitedBuf.AsSpan(0, 2 * N);
        Span<int> queue = queueBuf.AsSpan(0, 2 * N);
        visited.Clear();

        bool ignoreNpcAvoid = MovementSystem.NpcIgnoresNpcAvoid(npc.Behavior);
        int footprintSize = npc.EffectiveSize;

        int head = 0, tail = 0;
        int rootIdx = (int)targetLayer * N + tgtWY * RW + tgtWX;
        visited[rootIdx] = 1;                       // invariant (3): root never re-enqueued
        queue[tail++] = rootIdx;

        while (head < tail)
        {
            int cur = queue[head++];
            int curLayerI = cur >= N ? 1 : 0;               // decode the state index → (layer, cell)
            var curLayer = (WorldLayer)curLayerI;
            int cell = cur - curLayerI * N;
            int cwx = cell % RW;
            int cwy = cell / RW;
            int curMap = grid[cwx / W, cwy / H];
            for (int d = 0; d < 4; d++)
            {
                int nwx = cwx + _bfsDx[d];
                int nwy = cwy + _bfsDy[d];
                if ((uint)nwx >= RW || (uint)nwy >= RH) continue;
                var stepDir = _bfsStepFromNeighbor[d];      // the step an NPC at the neighbor takes to reach cur

                int nCol = nwx / W;
                int nRow = nwy / H;
                int nMap = grid[nCol, nRow];

                for (int nl = 0; nl < 2; nl++)
                {
                    var nLayer = (WorldLayer)nl;
                    int sIdx = nl * N + nwy * RW + nwx;
                    // CanEnter is the expensive call in this loop, and in a flood most candidate edges lead to
                    // a state already reached. A visited state has had its dirField written already — the write
                    // precedes every visited=1 — so skipping ahead of both leaves the field identical.
                    if (visited[sIdx] != 0) continue;

                    // Layer-transition gate (mirrors FindStep): neighbor→cur is a real edge only when stepping
                    // stepDir from the neighbor on nLayer lands on (cur, curLayer).  Ramp = the only cross-layer.
                    if (!LayerLogic.CanEnter(view, cwx, cwy, footprintSize, nLayer, stepDir, out var landed)
                        || landed != curLayer)
                    {
                        continue;
                    }

                    if (sIdx == rootIdx) continue;  // invariant (2): the goal state keeps dir 0

                    // Invariant (1): record the step toward the target on FIRST reach (dirField==0 freezes the
                    // shortest), BEFORE the checks below — so a chaser standing on an occupied attack-slot state
                    // or a non-walkable state still gets the same step FindStep returns for it.
                    if (dirField[sIdx] == 0) dirField[sIdx] = (byte)((int)stepDir + 1);
                    if (!occupied.IsEmpty && FootprintOccupied(occupied, nwx, nwy, footprintSize, nl, N, RW, RH)) continue;  // blind here — always empty (see doc)

                    if (nMap <= 0) continue;  // unlinked cell — treat as wall

                    // Occupied attack-slot ring state acts as a wall (mark visited, don't expand) — identical to
                    // FindStep; per-layer, shared per-pass via _attackSlotMemo, so it is chaser-independent.
                    if (WorldCoordHelper.AreFootprintsAdjacent(nwx, nwy, footprintSize, tgtWX, tgtWY, targetSize)
                        && AttackSlotBlockedForBody(nwx, nwy, footprintSize, nLayer, in grid, RW, RH))
                    {
                        visited[sIdx] = 1;
                        continue;
                    }

                    if (footprintSize > 1)
                    {
                        if (!FootprintBlockWalkable(nwx, nwy, footprintSize, nLayer, in grid, ignoreNpcAvoid)) continue;
                    }
                    else
                    {
                        var tt = LayerLogic.AttrFor(_world.Maps[nMap].Tile[nwx - nCol * W, nwy - nRow * H], nLayer).Type;
                        if (!MovementSystem.IsNpcWalkableTileType(tt, ignoreNpcAvoid)) continue;
                    }

                    visited[sIdx] = 1;
                    queue[tail++] = sIdx;
                }
            }
        }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(visitedBuf);
            ArrayPool<int>.Shared.Return(queueBuf);
        }
    }

    // Hands out a cleared direction-field buffer from the per-pass pool, growing
    // it only at the high-water mark of distinct fields in a pass (the used-count is reset in
    // CachedStepTowardObservableArea on a pass change) — so field builds are allocation-neutral once warm, like
    // _occupancyCache.
    private byte[] RentPathFieldBuffer(int states)
    {
        byte[] buf;
        if (_pathFieldBuffersUsed < _pathFieldBuffers.Count)
        {
            buf = _pathFieldBuffers[_pathFieldBuffersUsed];
            // A pooled buffer is sized for whatever neighbourhood it was first built for, and a player can
            // walk from a small zone into a large one mid-session — so it grows to fit before it is reused.
            if (buf.Length < states)
            {
                buf = new byte[states];
                _pathFieldBuffers[_pathFieldBuffersUsed] = buf;
            }
            Array.Clear(buf, 0, states);            // invariant (4): no stale directions from a prior target
        }
        else
        {
            buf = new byte[states];
            _pathFieldBuffers.Add(buf);
        }
        _pathFieldBuffersUsed++;
        return buf;
    }

    // True if a size-S body anchored (top-left) at observable-area world tile (aWX,aWY) fits entirely on
    // walkable, linked, in-area tiles AT THE GIVEN LAYER.  Used by the chase BFS so a big NPC only plans onto
    // cells its whole footprint can occupy; the live per-step CanNpcMoveFrom still guards actor occupancy, so
    // this is a static-geometry test only (nothing to self-exclude).  Reading walkability through
    // LayerLogic.AttrFor at `layer` means a fringe query on a tile with no FringeAttr reads Blocked, so the
    // fringe-footprint-fit invariant (whole body on a fringe surface) is enforced here for free.
    private bool FootprintBlockWalkable(int aWX, int aWY, int size, WorldLayer layer, in MapGrid grid, bool ignoreNpcAvoid)
    {
        int W = grid.TilesX, H = grid.TilesY;
        int RW = 3 * W;
        int RH = 3 * H;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int wx = aWX + i, wy = aWY + j;
                if ((uint)wx >= RW || (uint)wy >= RH) return false;
                int col = wx / W, row = wy / H;
                int m = grid[col, row];
                if (m <= 0) return false;
                var tt = LayerLogic.AttrFor(_world.Maps[m].Tile[wx - col * W, wy - row * H], layer).Type;
                if (!MovementSystem.IsNpcWalkableTileType(tt, ignoreNpcAvoid)) return false;
            }
        }

        return true;
    }

    // Attack-slot occupancy test for the chase BFS ring-mask, memoized per pathing pass.  A gang all
    // hunting one target re-queries the same ≤4 ring tiles every chaser; the memo computes each absolute
    // (tile, layer) once per pass and shares it.  Keyed on the absolute (map, x, y, layer) so it is
    // chaser-independent and seam-correct — an actor on the ground slot doesn't wall the fringe slot above it.
    // Snapshot at pass start — the same mild within-pass staleness as GetOccupancyBitmap; any real overlap is
    // still refused by the live per-step CanNpcMove.
    /// <summary>True when a chaser of this size could not stand at this cell to swing, because a live actor
    /// holds any tile its body would cover. Falls through to the memoized per-tile probe, so a gang hunting
    /// one target still shares the work.</summary>
    private bool AttackSlotBlockedForBody(int aWX, int aWY, int size, WorldLayer layer, in MapGrid grid, int RW, int RH)
    {
        for (int j = 0; j < size; j++)
        {
            int wy = aWY + j;
            if ((uint)wy >= RH) return true;
            for (int i = 0; i < size; i++)
            {
                int wx = aWX + i;
                if ((uint)wx >= RW) return true;
                var (m, lx, ly) = grid.ResolveWorldTile(wx, wy);
                if (m <= 0 || IsAttackSlotBlocked(m, lx, ly, layer)) return true;
            }
        }
        return false;
    }

    private bool IsAttackSlotBlocked(int mapNum, int x, int y, WorldLayer layer)
    {
        if (_attackSlotMemoStamp != _pathNow)
        {
            _attackSlotMemo.Clear();
            _attackSlotMemoStamp = _pathNow;
        }
        // 16 bits per axis — a whole tile coordinate (see MapSize.HardMax), so no two tiles collide.
        long key = ((long)(int)layer << 52) | ((long)mapNum << 32) | ((long)x << 16) | (long)y;
        if (_attackSlotMemo.TryGetValue(key, out bool blocked)) return blocked;
        blocked = IsTileOccupiedByActor(mapNum, x, y, layer);
        _attackSlotMemo[key] = blocked;
        return blocked;
    }

    // True if a live player or NPC currently stands on (mapNum, x, y) ON THE GIVEN LAYER.  This is the
    // occupancy half of MovementSystem.IsNpcDestFree WITHOUT the walkability test (the BFS runs its own
    // walkability check), so it is chaser-independent (no NpcAvoid/behavior dependence) and safe to share via
    // the memo above.
    private bool IsTileOccupiedByActor(int mapNum, int x, int y, WorldLayer layer)
    {
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            var pc = _pm[i].Char;
            if (pc.Map == mapNum && pc.X == x && pc.Y == y && pc.Layer == layer) return true;
        }
        return _world.IsTileOccupiedByNpc(mapNum, x, y, null, layer);
    }

    // Returns the live-occupancy bitmap for the 3×3 observable area centered on <paramref
    // name="mapNum"/>, rebuilding it from the player roster + every cell's NPCs when the cached
    // copy is stale (i.e. from a prior AI tick) and otherwise handing back the existing byte
    // array.  Bitmap arrays are allocated lazily per map; an unused map costs zero memory.  Indexed by
    // STATE (layer*N + cell), so an actor is a wall only on ITS OWN layer.  The bitmap may include
    // sources/targets, but BFS never reads occupancy for either (source check fires before the bitmap
    // check; target is the BFS root and so never expanded into).
    private Span<byte> GetOccupancyBitmap(int mapNum, in MapGrid grid)
    {
        int W = grid.TilesX, H = grid.TilesY;
        int RW = 3 * W, RH = 3 * H;
        int N = RW * RH;                      // tiles per layer; the bitmap holds 2*N (ground then fringe)

        var bitmap = _occupancyCache[mapNum];
        if (bitmap is null || bitmap.Length < 2 * N)
        {
            bitmap = new byte[2 * N];
            _occupancyCache[mapNum] = bitmap;
        }
        if (_occupancyCacheTicks[mapNum] == _aiNow) return bitmap;

        Array.Clear(bitmap, 0, 2 * N);
        // Players in this observable area are EXACTLY the players observing this center map (a player
        // observes map M iff M is one of the 9 cells they're standing on or adjacent to).  We iterate
        // that pre-maintained set instead of the whole 1,000-slot roster — typically 5–50 entries.
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            var pc = _pm[i].Char;
            var pgp = grid.PositionOf(pc.Map);
            if (pgp is null) continue;  // defensive: observer that left the area mid-tick
            var (pwx, pwy) = grid.ToWorld(pgp.Value.col, pgp.Value.row, pc.X, pc.Y);
            StampActor(bitmap, pwx, pwy, 1, (int)pc.Layer, N, RW, RH);
        }
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var n = _world.MapNpcs[m, s];
                    if (n.Num <= 0) continue;
                    var (wx, wy) = grid.ToWorld(col, row, n.X, n.Y);
                    StampActor(bitmap, wx, wy, _world.Npcs[n.Num].EffectiveSize, (int)n.Layer, N, RW, RH);
                }
                var list = _world.MapTraversalNpcs[m];
                for (int k = 0; k < list.Count; k++)
                {
                    var t = list[k];
                    if (t.Num <= 0) continue;
                    var (wx, wy) = grid.ToWorld(col, row, t.X, t.Y);
                    StampActor(bitmap, wx, wy, _world.Npcs[t.Num].EffectiveSize, (int)t.Layer, N, RW, RH);
                }
            }
        }

        _occupancyCacheTicks[mapNum] = _aiNow;
        return bitmap;
    }

    // Fills <paramref name="bmp"/> (a 3×3 observable-area bitmap) with live-actor occupancy for a STALLED
    // chaser's re-plan, OMITTING any NPC that is chasing the chaser itself (its pursuers, identified by
    // NpcTarget == the chaser's (selfSpawnMap, selfSpawnSlot)).  A pursuer left walkable means the chaser's
    // plan runs straight into it and holds (the per-step CanNpcMove still refuses the overlap), so a guard
    // walling off its target makes the target SETTLE rather than route around its own hunter — the one case
    // that separates "route around a mid-path blocker" (good) from the guard↔AoS dance (bad).  Built from
    // current positions each call; only invoked while a chaser is stalled, so the rebuild cost is off the
    // hot path.  The chaser's own tile may be marked (harmless: the BFS returns at the source before the
    // occupancy check) and so may the target root (never expanded into).
    private void FillOccupancyExcludingPursuers(Span<byte> bmp, int mapNum, in MapGrid grid, int selfSpawnMap, int selfSpawnSlot)
    {
        // 🔴 Strides come from the GRID, never from the default map size. The coordinates being written are
        // grid.ToWorld's, so a compile-time width silently addresses the wrong cells on any map that is not
        // the default — no crash, just a stalled chaser re-planning against a bitmap describing nowhere.
        int RW = 3 * grid.TilesX, RH = 3 * grid.TilesY;
        int N = RW * RH;                              // tiles per layer; bmp is 2*N, indexed by state (layer*N + cell)
        bmp.Clear();
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            var pc = _pm[i].Char;
            var pgp = grid.PositionOf(pc.Map);
            if (pgp is null) continue;
            var (pwx, pwy) = grid.ToWorld(pgp.Value.col, pgp.Value.row, pc.X, pc.Y);
            StampActor(bmp, pwx, pwy, 1, (int)pc.Layer, N, RW, RH);
        }
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var n = _world.MapNpcs[m, s];
                    if (n.Num <= 0) continue;
                    if (n.NpcTargetSpawnSlot == selfSpawnSlot && n.NpcTargetSpawnMap == selfSpawnMap) continue;  // my pursuer — leave walkable
                    var (wx, wy) = grid.ToWorld(col, row, n.X, n.Y);
                    StampActor(bmp, wx, wy, _world.Npcs[n.Num].EffectiveSize, (int)n.Layer, N, RW, RH);
                }
                var list = _world.MapTraversalNpcs[m];
                for (int k = 0; k < list.Count; k++)
                {
                    var t = list[k];
                    if (t.Num <= 0) continue;
                    if (t.NpcTargetSpawnSlot == selfSpawnSlot && t.NpcTargetSpawnMap == selfSpawnMap) continue;  // pursuer guest
                    var (wx, wy) = grid.ToWorld(col, row, t.X, t.Y);
                    StampActor(bmp, wx, wy, _world.Npcs[t.Num].EffectiveSize, (int)t.Layer, N, RW, RH);
                }
            }
        }
    }

    /// <summary>Marks an actor's WHOLE body in an occupancy bitmap, on its own layer.
    ///
    /// <para>🔴 One tile per actor makes a three-ton monster a one-tile obstacle: a chaser plans a route
    /// confidently through the other eight ninths of it, the live step gate refuses every attempt, and the
    /// chase stalls against something the plan says is not there.</para></summary>
    private static void StampActor(Span<byte> bmp, int aWX, int aWY, int size, int layerI, int N, int RW, int RH)
    {
        for (int j = 0; j < size; j++)
        {
            int wy = aWY + j;
            if ((uint)wy >= RH) continue;
            for (int i = 0; i < size; i++)
            {
                int wx = aWX + i;
                if ((uint)wx >= RW) continue;
                bmp[layerI * N + wy * RW + wx] = 1;
            }
        }
    }

    /// <summary>True when any tile a size-S body would stand on is held by a live actor.
    ///
    /// <para>The read side of the same rule: a plan that puts any part of the body on an occupied tile is not
    /// a plan. Testing the anchor alone lets a big NPC route straight through an actor two tiles to its
    /// right. Out of the observable area counts as blocked; the walkability test that follows rejects it
    /// anyway. At size 1 this is exactly the single-cell test it replaces.</para></summary>
    private static bool FootprintOccupied(ReadOnlySpan<byte> occupied, int aWX, int aWY, int size, int layerI,
                                          int N, int RW, int RH)
    {
        for (int j = 0; j < size; j++)
        {
            int wy = aWY + j;
            if ((uint)wy >= RH) return true;
            for (int i = 0; i < size; i++)
            {
                int wx = aWX + i;
                if ((uint)wx >= RW) return true;
                if (occupied[layerI * N + wy * RW + wx] != 0) return true;
            }
        }
        return false;
    }
}

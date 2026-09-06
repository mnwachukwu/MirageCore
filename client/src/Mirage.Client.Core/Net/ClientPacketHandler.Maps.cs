using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Diagnostics;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>Map delivery and the seamless 3x3 grid: cache checks, neighbor fetches, the crossing
/// handshake, and the grid shift that follows it.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Map loading ────────────────────────────────────────────────────────────

    private void HandleCheckForMap(CheckForMapPacket p)
    {
        // Center map change: blocks input and clears state until the new map loads.
        if (p.Col == 1 && p.Row == 1)
        {
            _state.ClearPendingCross();  // a true warp/teleport supersedes any predicted edge cross
            _state.GettingMap = true;
            _state.ClearMapState();
            _state.CenterMapNum = p.MapNum;
            _state.NeighborMapNums[1, 1] = p.MapNum;
            ClientLog.Debug("Loading map {Map} rev {Revision}.", p.MapNum, p.Revision);
            _ = ResolveMapAsync(p);
        }
        // Neighbor pre-load: cache-aware, non-blocking — never touches GettingMap.
        else if (p.Col is >= 0 and <= 2 && p.Row is >= 0 and <= 2)
        {
            // Record the cell→map mapping synchronously so any entity packets that
            // follow in this same batch (e.g. the neighbor's MapNpcs snapshot) route here.
            _state.NeighborMapNums[p.Col, p.Row] = p.MapNum;
            _ = ResolveNeighborMapAsync(p);
        }
    }

    // Seamless border crossing: the player walked into an already-loaded neighbor.  Re-frame the grid
    // (no GettingMap, no ClearMapState, no reload), re-center, place the player, and ask the server to
    // fill in the newly-revealed edge.  The preserved overlap renders continuously — no flicker.
    private void HandleSeamlessCross(SeamlessCrossPacket p)
    {
        // Confirmation of the cross we already predicted client-side: our grid is shifted and the
        // step is animating, so don't touch position or re-shift — just settle the prediction and
        // pull in the newly-revealed edge maps/entities.
        if (_state.PendingCrossToMap == p.MapNum)
        {
            _state.ClearPendingCross();
            // Cached center revision may be stale (e.g. this map was edited via the editor while we
            // were on a neighbor and our observer-refresh hadn't landed yet).  Tile changes wouldn't
            // show up and connections would block traversal until relog, so force a full reload.
            if (_state.Map is null || _state.Map.Revision != p.Revision)
            {
                _state.GettingMap = true;
                _state.ClearMapState();
                _state.CenterMapNum = p.MapNum;
                _state.NeighborMapNums[1, 1] = p.MapNum;
                _ = ResolveMapAsync(new CheckForMapPacket { MapNum = p.MapNum, Revision = p.Revision });
                return;
            }
            // Reconcile the layer to the server's authority (the prediction mirrors it, so this is normally a
            // no-op — but the server is the source of truth on the two-layer position).
            _state.Me.Layer = p.Layer;
            MapReady?.Invoke();
            _sender.SendRequestRegionSync();
            return;
        }
        // No matching prediction (server-initiated, or a rare misprediction) — do the shift here.
        _state.ClearPendingCross();

        _state.ApplySeamlessCross(p.MapNum, p.X, p.Y, p.Layer);

        // Guard: the crossed-into map should already be loaded as the neighbor in the crossing
        // direction at the server's current revision.  If it's missing OR cached stale, fall back to
        // a normal blocking reload rather than shifting null/stale data into the center.
        (int dc, int dr) = ClientState.CellToward(p.Dir);
        var neighbor = _state.NeighborMaps[dc, dr];
        if (neighbor is null || _state.NeighborMapNums[dc, dr] != p.MapNum || neighbor.Revision != p.Revision)
        {
            _state.GettingMap = true;
            _state.ClearMapState();
            _state.CenterMapNum = p.MapNum;
            _state.NeighborMapNums[1, 1] = p.MapNum;
            _ = ResolveMapAsync(new CheckForMapPacket { MapNum = p.MapNum, Revision = p.Revision });
            return;
        }

        // Re-frame the grid so the neighbor becomes the center — no GettingMap, no clear, no reload.
        _state.ShiftGrid(p.Dir);
        _state.CenterMapNum = p.MapNum;
        _state.NeighborMapNums[1, 1] = p.MapNum;

        // Refresh map-change side effects (music switches only if the new map's track differs).
        MapReady?.Invoke();

        _sender.SendRequestRegionSync();
    }

    // The server rejected a cross we already predicted (rare — the dest tile became blocked within
    // the prediction's sub-RTT window).  Reload the map we came from, blocking like a warp, which
    // re-syncs our authoritative position there.  Can't cheaply un-shift since the old edge row was
    // dropped, so a clean reload is the safe revert.
    private void RevertPendingCross()
    {
        int fromMap = _state.PendingCrossFromMap;
        int fromRev = _state.PendingCrossFromRevision;
        _state.ClearPendingCross();
        _state.GettingMap = true;
        _state.ClearMapState();
        _state.CenterMapNum = fromMap;
        _state.NeighborMapNums[1, 1] = fromMap;
        _ = ResolveMapAsync(new CheckForMapPacket { MapNum = fromMap, Revision = fromRev });
    }

    private async Task ResolveNeighborMapAsync(CheckForMapPacket p)
    {
        try
        {
            // Already holding this exact revision in the grid cell — nothing to fetch.
            var existing = _state.NeighborMaps[p.Col, p.Row];
            if (existing is not null && existing.Revision == p.Revision) return;

            int cachedRev = _mapCache.GetCachedRevision(p.MapNum);
            if (cachedRev == p.Revision)
            {
                var cached = await _mapCache.LoadAsync(p.MapNum);
                if (cached is not null)
                {
                    _state.NeighborMaps[p.Col, p.Row] = cached;
                    return;
                }
            }
            // Cache miss/stale: ask the server for this specific neighbor's full data.
            _sender.SendNeedNeighborMap(p.MapNum, p.Col, p.Row);
        }
        catch (Exception ex)
        {
            ClientLog.Warn(ex, "Neighbor map {Map} for cell {Col},{Row} could not be resolved.", p.MapNum, p.Col, p.Row);
        }
    }

    private async Task ResolveMapAsync(CheckForMapPacket p)
    {
        try
        {
            int cachedRev = _mapCache.GetCachedRevision(p.MapNum);
            if (cachedRev == p.Revision)
            {
                var cached = await _mapCache.LoadAsync(p.MapNum);
                if (cached is not null)
                {
                    _state.Map = cached;
                    _sender.SendMapData(p.MapNum);
                    return;
                }
            }
            _sender.SendNeedMap(p.MapNum, cachedRev < 0 ? 0 : cachedRev);
        }
        catch (Exception ex)
        {
            ClientLog.Error(ex, "Center map {Map} rev {Revision} could not be resolved.", p.MapNum, p.Revision);
        }
    }

    private void HandleSendMap(SendMapPacket p)
    {
        var map = BuildMapRecord(p);

        // Center map (col=row=1): drives the normal join handshake (cache + confirm,
        // which unblocks GettingMap).  Neighbor pre-loads just populate their grid
        // cell and cache silently — no confirm, no input block.
        if (p.Col == 1 && p.Row == 1)
        {
            _state.Map = map;
            _ = SaveAndConfirmAsync(p.MapNum, map);
        }
        else if (p.Col is >= 0 and <= 2 && p.Row is >= 0 and <= 2)
        {
            _state.NeighborMaps[p.Col, p.Row] = map;
            _ = SaveNeighborAsync(p.MapNum, map);
        }
    }

    private static MapRecord BuildMapRecord(SendMapPacket p)
    {
        // Sized from the packet before the tiles land: they travel sparsely, so the grid's extent is
        // stated rather than inferred.
        var map = new MapRecord(p.Width, p.Height)
        {
            Name = p.Name,
            DisplayName = p.DisplayName,
            Revision = p.Revision,
            Moral = p.Moral,
            Up = p.Up,
            Down = p.Down,
            Left = p.Left,
            Right = p.Right,
            Music = p.Music,
            BootMap = p.BootMap,
            BootX = p.BootX,
            BootY = p.BootY,
            Indoors = p.Indoors,
            AlwaysLit = p.AlwaysLit,
            AlwaysDark = p.AlwaysDark,
            MapGroup = p.MapGroup,
        };

        foreach (var t in p.Tiles)
            if (map.Contains(t.X, t.Y)) map.Tile[t.X, t.Y] = t.ToTile();

        // Dense NPC entry list: stored as-is — the game client renders NPCs from live spawn
        // packets and never reads these. Capped defensively at MaxMapNpcs runtime posts.
        for (int i = 0; i < p.Npcs.Length && i < Constants.MaxMapNpcs; i++)
            map.Npcs.Add(p.Npcs[i]);

        map.Lights.AddRange(p.Lights);

        return map;
    }

    private async Task SaveAndConfirmAsync(int mapNum, MapRecord map)
    {
        try
        {
            await _mapCache.SaveAsync(mapNum, map);
            _sender.SendMapData(mapNum);
        }
        catch (Exception ex)
        {
            ClientLog.Warn(ex, "Map {Map} arrived but could not be cached.", mapNum);
        }
    }

    private async Task SaveNeighborAsync(int mapNum, MapRecord map)
    {
        try { await _mapCache.SaveAsync(mapNum, map); }
        catch (Exception ex) { ClientLog.Warn(ex, "Neighbor map {Map} could not be cached.", mapNum); }
    }

    private void HandleMapItems(MapItemsPacket p)
    {
        var items = _state.ItemsForMap(p.MapNum);
        if (items is null) return;
        foreach (var item in p.Items)
        {
            if (item.Slot <= 0) continue;
            // Num == 0 is the server's "remove this slot" sentinel.
            if (item.Num <= 0)
            {
                items.Remove(item.Slot);
                continue;
            }
            if (!items.TryGetValue(item.Slot, out var mi))
            {
                mi = new MapItemRecord { Slot = item.Slot };
                items[item.Slot] = mi;
            }
            mi.Num = item.Num;
            mi.Quantity = item.Quantity;
            mi.Dur = item.Dur;
            mi.X = item.X;
            mi.Y = item.Y;
            mi.Layer = item.Layer;   // two-layer world: for the render pass-split (drops on/under a bridge)
            mi.Source = item.Source;
            // The loot claim, translated onto THIS machine's clock: the wire carries how long the tag
            // has left, because a server TickCount64 means nothing here. Used by the tile menu to tell
            // your loot from somebody else's; the server still re-checks every pick-up, so this is a
            // display fact rather than a permission.
            mi.TaggedToPlayer = item.TaggedTo;
            mi.TagExpiresAt = item.TagMsLeft > 0 ? Environment.TickCount64 + item.TagMsLeft : 0;
        }
        if (IsCenter(p.MapNum)) MapItemChanged?.Invoke(0); // 0 = full refresh (center inventory UI)
    }

    private void HandleMapNpcs(MapNpcsPacket p)
    {
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is not null)
        {
            long nowMs = Environment.TickCount64;
            foreach (var n in p.Npcs)
            {
                if (!SlotValidation.IsValidNpcSlot(n.Slot)) continue;
                npcs[n.Slot].ApplySnapshot(n.Num, n.Hp, n.MaxHp, n.Mp, n.MaxMp, n.Sp, n.MaxSp,
                                            n.X, n.Y, n.Dir, n.Layer, n.MsSinceCombat, n.HasTarget, nowMs);
            }
        }
        // The center map's snapshot is the final packet of the join sequence — it ends the
        // input-blocking load.  Neighbor snapshots are pre-loads and must not unblock.
        if (IsCenter(p.MapNum))
        {
            // 🔴 Only a BLOCKING load owes the arrival beat, which is why this reads the flag before
            // clearing it. A seamless crossing arrives here too — it asks for a region re-sync, and that
            // carries the new centre's NPC snapshot — but a crossing is one continuous walk, and charging
            // it the beat stalls the player at every seam. The stall is invisible at a 400 ms walk and
            // plainly visible at a sprint, where a step is shorter than the beat.
            bool wasBlockingLoad = _state.GettingMap;
            _state.GettingMap = false;
            // Landing is a step, and it owes the beat before the next one — see ClientState.ArrivedAtMs.
            // Stamped here rather than at the announcement, so the beat runs from the moment the player can
            // see where they are rather than overlapping a load they were watching nothing during.
            if (wasBlockingLoad) _state.ArrivedAtMs = Environment.TickCount64;
            MapReady?.Invoke();
        }
    }
}

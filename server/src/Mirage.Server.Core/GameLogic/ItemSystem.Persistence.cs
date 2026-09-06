using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Dropped-item save and load, with the per-map write coalescing that keeps a busy map
/// from queueing a save per drop.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Persistence ──────────────────────────────────────────────────────────

    /// <summary>Synchronous flush used by shutdown: queues a save for the latest snapshot and
    /// awaits the coalescing worker so the file is settled before the host exits.</summary>
    public Task SaveDroppedItemsForMapAsync(int mapNum) => EnqueueSaveDroppedItemsCore(mapNum);

    private Task EnqueueSaveDroppedItemsCore(int mapNum)
    {
        // Snapshot synchronously on the caller (game thread during play, shutdown thread after the
        // game loop has stopped — both are quiescent w.r.t. MapItems mutation).
        var list = _world.MapItems[mapNum];
        var snapshot = list
            .Where(mi => mi.Num > 0 && mi.Source is ItemSource.PlayerDropped or ItemSource.NpcDropped or ItemSource.PlayerDeathDropped)
            .Select(mi => new DroppedItemSaveData(mi.Num, mi.Quantity, mi.Dur, mi.X, mi.Y, mi.Source, mi.DropSeq))
            .ToArray();

        MapSaveState state;
        lock (_saveStatesLock)
        {
            if (!_saveStates.TryGetValue(mapNum, out state!))
                _saveStates[mapNum] = state = new MapSaveState();
        }

        Task worker;
        bool started = false;
        lock (state.Lock)
        {
            state.Pending = snapshot;
            if (state.Worker is null)
            {
                state.Worker = ProcessSavesAsync(mapNum, state);
                started = true;
            }
            worker = state.Worker;
        }
        // Register with IBackgroundPersistence so faults log and shutdown drain awaits the worker.
        if (started) _bg.Run(worker, nameof(SaveDroppedItemsForMapAsync));
        return worker;
    }

    private async Task ProcessSavesAsync(int mapNum, MapSaveState state)
    {
        while (true)
        {
            DroppedItemSaveData[]? snapshot;
            lock (state.Lock)
            {
                snapshot = state.Pending;
                state.Pending = null;
                if (snapshot is null)
                {
                    state.Worker = null;
                    return;
                }
            }
            await _persistence.SaveDroppedItemsAsync(mapNum, snapshot);
        }
    }

    /// <summary>Restore a map's persisted dropped items at boot, preserving their saved stack order and
    /// leaving the global drop counter ahead of every loaded sequence so later drops still land on top.</summary>
    public void LoadDroppedItems(int mapNum, DroppedItemSaveData[] drops)
    {
        foreach (var drop in drops)
        {
            var mi = new MapItemRecord
            {
                Slot = _world.AllocateMapItemSlot(mapNum),
                Num = drop.Num,
                Quantity = drop.Value,
                Dur = drop.Dur,
                X = drop.X,
                Y = drop.Y,
                Source = drop.Source,
                // Restore stack order from save. Older saves predate DropSeq (=0) — assign a fresh
                // counter so they still have a defined pickup order. Either way, keep the global
                // counter ahead of every loaded seq so new drops always land on top.
                DropSeq = drop.DropSeq > 0 ? drop.DropSeq : ++_dropSeqCounter,
            };
            if (mi.DropSeq > _dropSeqCounter) _dropSeqCounter = mi.DropSeq;
            _world.MapItems[mapNum].Add(mi);
        }
    }
}

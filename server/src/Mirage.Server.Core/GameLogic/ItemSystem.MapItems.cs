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

/// <summary>Items lying on the ground: tile-defined spawns and their respawn timers, player drops,
/// pickup, and the per-map coalesced persistence that keeps drops across a restart.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Map items ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawn an item on the map.  Allocates a fresh per-map slot id, appends to the map's item list,
    /// and broadcasts to observers.  No cap — voluntary-drop limits live in <see cref="PlayerMapDropItem"/>;
    /// death drops and NPC drops bypass them and call this directly.
    /// <paramref name="durOverride"/> >= 0 carries the caller's exact durability into the spawn record
    /// and the initial broadcast — used by player drops to preserve the equipped copy's wear instead of
    /// re-stamping the item's max durability.  Returns the assigned slot id (>0) on success, or 0 on
    /// input validation failure.
    /// </summary>
    public int SpawnItem(int itemNum, int value, int mapNum, int x, int y, ItemSource source = ItemSource.TileDefined, int durOverride = -1,
        WorldLayer layer = WorldLayer.Ground)
    {
        if (itemNum < 0 || itemNum > _world.Limits.Items || mapNum <= 0 || mapNum > _world.Limits.Maps) return 0;

        bool isEquipment = itemNum != 0 && ItemRecord.IsEquipment(_world.Items[itemNum].Type);
        int dur = durOverride >= 0 ? durOverride : (isEquipment ? _world.Items[itemNum].Durability : 0);

        int slot = _world.AllocateMapItemSlot(mapNum);
        var mi = new MapItemRecord
        {
            Slot = slot,
            Num = itemNum,
            Quantity = value,
            Dur = dur,
            X = x,
            Y = y,
            Layer = layer,   // two-layer world: drops land on the dropper's / spawn layer
            Source = itemNum > 0 ? source : ItemSource.TileDefined,
            DropSeq = itemNum > 0 ? ++_dropSeqCounter : 0,
        };
        _world.MapItems[mapNum].Add(mi);

        // A tile-defined item landing resets the respawn timer for this (tile, layer).
        if (source == ItemSource.TileDefined && itemNum > 0)
            _world.TempTiles[mapNum].RestoreTileItem(x, y, layer);

        SendToMap(_world, mapNum, new MapItemsPacket
        {
            MapNum = mapNum,
            Items = [MapItemsPacket.MapItemData.From(mi, Environment.TickCount64)]
        });
        return slot;
    }

    /// <summary>Stamp a loot claim on a just-spawned drop and tell the map about it.
    ///
    /// <para>A second packet rather than a parameter on <c>SpawnItem</c>, because the tag is decided
    /// AFTER the item exists — the roll that picks an owner needs the drop to have landed first. The
    /// re-broadcast is what stops the claim being invisible: it is set between the spawn packet and
    /// anything else, so without this every client would have been told the item is unowned and never
    /// corrected.</para></summary>
    public void TagMapItem(int mapNum, int slot, int owner, long durationMs)
    {
        var mi = _world.MapItemBySlot(mapNum, slot);
        if (mi is null || owner <= 0) return;

        mi.TaggedToPlayer = owner;
        mi.TagExpiresAt = Environment.TickCount64 + durationMs;

        SendToMap(_world, mapNum, new MapItemsPacket
        {
            MapNum = mapNum,
            Items = [MapItemsPacket.MapItemData.From(mi, Environment.TickCount64)]
        });
    }

    /// <summary>
    /// Remove the live map item with the given stable slot id, broadcasting a Num=0 packet so clients
    /// drop their copy.  No-op if the slot isn't present.  Resets the per-map slot counter when the
    /// list becomes empty, so a Wild map that eventually clears reclaims small ids the same way a Safe
    /// map (with active janitors) does.
    /// <para>Do not call this from inside a <c>foreach</c> over <c>_world.MapItems[mapNum]</c> — it
    /// mutates the same list and will throw.  For bulk removal use
    /// <see cref="RemoveMatchingMapItems(int, Predicate{MapItemRecord})"/>.</para>
    /// </summary>
    public void RemoveMapItem(int mapNum, int slotId)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps || slotId <= 0) return;
        var list = _world.MapItems[mapNum];
        int x = 0, y = 0;
        bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Slot == slotId)
            {
                x = list[i].X;
                y = list[i].Y;
                list.RemoveAt(i);
                found = true;
                break;
            }
        }

        if (!found) return;
        if (list.Count == 0) _world.ResetMapItemSlotIds(mapNum);
        SendToMap(_world, mapNum, new MapItemsPacket
        {
            MapNum = mapNum,
            Items = [new MapItemsPacket.MapItemData(slotId, 0, 0, 0, x, y)]
        });
    }

    /// <summary>
    /// Safe bulk removal: snapshots which items match (so the list isn't mutated mid-scan), removes
    /// them, and broadcasts a single MapItemsPacket carrying all the Num=0 sentinels.  Use this from
    /// callers that would otherwise want to call <see cref="RemoveMapItem"/> from inside a foreach.
    /// Returns the number of items removed.
    /// </summary>
    public int RemoveMatchingMapItems(int mapNum, Predicate<MapItemRecord> match)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return 0;
        var list = _world.MapItems[mapNum];
        if (list.Count == 0) return 0;

        // Snapshot phase: collect what we'll remove without mutating yet.
        List<MapItemsPacket.MapItemData>? removals = null;
        for (int i = 0; i < list.Count; i++)
        {
            var mi = list[i];
            if (!match(mi)) continue;
            (removals ??= new()).Add(new MapItemsPacket.MapItemData(mi.Slot, 0, 0, 0, mi.X, mi.Y));
        }
        if (removals is null) return 0;

        // Mutation phase.
        list.RemoveAll(match);
        if (list.Count == 0) _world.ResetMapItemSlotIds(mapNum);

        SendToMap(_world, mapNum, new MapItemsPacket
        {
            MapNum = mapNum,
            Items = removals.ToArray(),
        });
        return removals.Count;
    }

    /// <summary>
    /// Drop every live map item, reset the per-map slot counter, and zero every NPC's JanitorTarget on
    /// the map (so stale claims from before the wipe can't collide with reused slot ids afterward).
    /// Broadcasts a Num=0 packet per removed slot so clients sync.  Used by HandleMapRespawn before
    /// re-spawning tile-defined items.
    /// </summary>
    public void ClearMapItems(int mapNum)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        for (int s = 1; s <= Constants.MaxMapNpcs; s++)
            _world.MapNpcs[mapNum, s].JanitorTarget = 0;

        var list = _world.MapItems[mapNum];
        if (list.Count == 0)
        {
            _world.ResetMapItemSlotIds(mapNum);
            return;
        }

        var items = new MapItemsPacket.MapItemData[list.Count];
        for (int i = 0; i < list.Count; i++)
            items[i] = new MapItemsPacket.MapItemData(list[i].Slot, 0, 0, 0, list[i].X, list[i].Y);
        list.Clear();
        _world.ResetMapItemSlotIds(mapNum);
        SendToMap(_world, mapNum, new MapItemsPacket { MapNum = mapNum, Items = items });
    }

    /// <summary>Spawn every tile-defined item on a map. Scans both planes at each tile, so a Ground item
    /// and a Fringe item authored on the same tile both appear on their own layer.</summary>
    public void SpawnMapItems(int mapNum)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        var map = _world.Maps[mapNum];
        // Two-plane world: a tile-defined item can be authored on the Ground OR the Fringe layer (its FringeAttr),
        // so scan both planes at every tile and spawn each on its own layer.
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                SpawnTileItem(map.Tile[x, y], WorldLayer.Ground, mapNum, x, y);
                SpawnTileItem(map.Tile[x, y], WorldLayer.Fringe, mapNum, x, y);
            }
        }
    }

    // Spawn a tile-defined item from the attribute on one plane (Ground inline vs FringeAttr), if it is an Item.
    private void SpawnTileItem(TileRecord tile, WorldLayer layer, int mapNum, int x, int y)
    {
        var attr = LayerLogic.AttrFor(tile, layer);
        if (attr.Type != TileType.Item) return;
        int val = (_world.Items[attr.ItemNum].Type == ItemType.Currency && attr.ItemQuantity <= 0) ? 1 : attr.ItemQuantity;
        SpawnItem(attr.ItemNum, val, mapNum, x, y, layer: layer);
    }

    // Scratch list for the sweep below: the due tiles are collected before any is spawned, because
    // SpawnItem clears the entry it was read from. Reused across maps and ticks so a sweep over a
    // thousand maps allocates nothing.
    private readonly List<(int X, int Y, WorldLayer Layer)> _dueRespawns = [];

    /// <summary>Re-spawn any picked-up tile-defined item on this map whose per-(tile, layer) timer has
    /// elapsed. The delay is the tile attribute's own seconds value, or
    /// <see cref="Constants.DefaultItemRespawnSeconds"/> when it authors none.
    ///
    /// <para>Reads the map's taken-item entries, not its tiles: a map with nothing picked up costs one
    /// count check, and one with three costs three. The game loop runs this over every map in the world
    /// once a second, so the cost has to follow what is actually running rather than how big the map
    /// is.</para></summary>
    public void CheckItemRespawn(int mapNum, long now)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        var temp = _world.TempTiles[mapNum];
        if (temp.TakenTileItems.Count == 0) return;

        var map = _world.Maps[mapNum];
        _dueRespawns.Clear();
        foreach (var ((x, y, layer), takenAt) in temp.TakenTileItems)
        {
            if (!map.Contains(x, y)) continue;
            var attr = LayerLogic.AttrFor(map.Tile[x, y], layer);
            if (attr.Type != TileType.Item) continue;
            long thresholdMs = (attr.ItemRespawnSecs > 0 ? attr.ItemRespawnSecs : Constants.DefaultItemRespawnSeconds) * 1000L;
            if (now - takenAt < thresholdMs) continue;
            _dueRespawns.Add((x, y, layer));
        }

        foreach (var (x, y, layer) in _dueRespawns)
        {
            var attr = LayerLogic.AttrFor(map.Tile[x, y], layer);
            int val = (_world.Items[attr.ItemNum].Type == ItemType.Currency && attr.ItemQuantity <= 0) ? 1 : attr.ItemQuantity;
            SpawnItem(attr.ItemNum, val, mapNum, x, y, ItemSource.TileDefined, layer: layer);
        }
    }

    // ── Player pick up item ───────────────────────────────────────────────────

    /// <summary>Pick up the topmost item on the player's own tile AND layer — true LIFO, by
    /// <see cref="MapItemRecord.DropSeq"/>. Refused while another player's loot tag is still live, or when
    /// no inventory slot can take it. A tile-defined pickup arms that tile's respawn timer, and the bag gain
    /// is persisted in the same tick as the map removal.</summary>
    public void PlayerMapGetItem(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;
        int mapNum = p.Map;

        // True LIFO: scan all map items at the player's tile, pick the highest DropSeq (top of stack).
        var list = _world.MapItems[mapNum];
        MapItemRecord? top = null;
        long topSeq = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m.Num == 0 || m.Num > _world.Limits.Items) continue;
            if (m.X != p.X || m.Y != p.Y || m.Layer != p.Layer) continue;   // same tile AND same layer
            if (m.DropSeq > topSeq)
            {
                topSeq = m.DropSeq;
                top = m;
            }
        }
        if (top is null) return;

        TryTakeMapItem(index, mapNum, top, announceRefusal: true);
    }

    /// <summary>Why a pick-up did not happen, or that it did.</summary>
    public enum PickUpResult
    {
        Taken,
        /// <summary>Somebody else's loot tag is still live on it.</summary>
        Claimed,
        /// <summary>No inventory slot can take it.</summary>
        BagFull,
    }

    /// <summary>
    /// Move one map item into a player's bag, with the claim check, the bag check, and every side effect
    /// the old inline version had: the tile respawn timer, the two persistence enqueues, and the message.
    ///
    /// <para>Extracted so the three ways of picking something up — standing on it and pressing the key,
    /// the tile menu's single Pick Up, and its Pick Up All — cannot drift apart. They differ ONLY in how
    /// they choose the item and whether they announce a refusal; what "taking it" means is here.</para>
    ///
    /// <para><b>Range is NOT checked here.</b> Each caller has already established reach in its own way —
    /// standing on the tile, or <see cref="GameWorld.IsMapItemInReach"/> — and folding it in would make
    /// the key path pay for a geometry test it answers by construction.</para>
    ///
    /// <para><paramref name="announceRefusal"/> is off for bulk pick-up, which reports once at the end
    /// rather than complaining per item — eight "your inventory is full" lines is not a better answer
    /// than one.</para>
    /// </summary>
    private PickUpResult TryTakeMapItem(int index, int mapNum, MapItemRecord mi, bool announceRefusal)
    {
        var p = _pm[index].Char;

        long nowMs = Environment.TickCount64;
        if (mi.TaggedToPlayer > 0 && nowMs < mi.TagExpiresAt && mi.TaggedToPlayer != index)
        {
            if (announceRefusal)
            {
                string ownerName = _pm[mi.TaggedToPlayer].Char.Name.Trim();
                string claimedName = _world.Items[mi.Num].TrimmedName;
                int secsLeft = (int)Math.Ceiling((mi.TagExpiresAt - nowMs) / 1000.0);
                SendMsg(index, ServerStrings.ItemSystem_LootClaimed, GameColor.Yellow, ("Item", claimedName), ("Owner", ownerName), ("Seconds", secsLeft));
            }
            return PickUpResult.Claimed;
        }

        int slot = FindOpenInvSlot(p, _world.Items, mi.Num);
        if (slot == 0)
        {
            if (announceRefusal) SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return PickUpResult.BagFull;
        }

        var item = _world.Items[mi.Num];
        string msgKey;
        (string Key, object? Value)[] msgArgs;

        p.Inv[slot].Num = mi.Num;
        if (item.Type == ItemType.Currency)
        {
            p.Inv[slot].AddQuantity(mi.Quantity);
            msgKey = ServerStrings.ItemSystem_PickedUpMultiple;
            msgArgs = [("Amount", mi.Quantity), ("Item", item.TrimmedName)];
        }
        else
        {
            p.Inv[slot].Quantity = 0;
            msgKey = ServerStrings.ItemSystem_PickedUp;
            msgArgs = [("Item", item.TrimmedName)];
        }
        p.Inv[slot].Dur = mi.Dur;

        int itemX = mi.X, itemY = mi.Y;
        var itemSource = mi.Source;
        var itemLayer = mi.Layer;
        int topSlot = mi.Slot;

        RemoveMapItem(mapNum, topSlot);

        // Start the per-(tile, layer) respawn timer for tile-defined items — read the attribute on the item's
        // own plane so a Fringe tile-item arms its Fringe timer (and a Ground one its Ground timer).
        if (itemSource == ItemSource.TileDefined &&
            LayerLogic.AttrFor(_world.Maps[mapNum].Tile[itemX, itemY], itemLayer).Type == TileType.Item)
        {
            _world.TempTiles[mapNum].TakeTileItem(itemX, itemY, itemLayer, Environment.TickCount64);
        }

        // Persist the removal of a dropped item
        if (itemSource is ItemSource.PlayerDropped or ItemSource.NpcDropped or ItemSource.PlayerDeathDropped)
            EnqueueSaveDroppedItems(mapNum);

        SendInventoryUpdate(index, slot);
        SendMsg(index, msgKey, GameColor.Yellow, msgArgs);
        // Persist the inventory gain in the same tick as the map removal above, so a crash between the
        // two can't clear the item from the map without granting it.
        _pm.MarkDirty(index);
        return PickUpResult.Taken;
    }

    // ── Pick up at range, from the tile menu ──────────────────────────────────

    /// <summary>Pick up ONE named map item from a distance — the tile menu's Pick Up.
    ///
    /// <para>Identified by its stable per-map slot rather than by position, so the thing that gets taken
    /// is the thing that was clicked even if the pile shifted between the menu opening and the click.
    /// A slot that no longer resolves means somebody else got there first, which is a race and not an
    /// error: it says so quietly rather than reporting a fault.</para></summary>
    public void PlayerMapPickUpAt(int index, int mapNum, int slot)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        var mi = _world.MapItemBySlot(mapNum, slot);
        if (mi is null || mi.Num <= 0)
        {
            SendMsg(index, ServerStrings.ItemSystem_LootGone, GameColor.Yellow);
            return;
        }

        // The menu is a convenience; this is the authority. Re-checked on arrival because the player may
        // have walked away — or never been close in the first place.
        if (!_world.IsMapItemInReach(index, p, mapNum, mi))
        {
            SendMsg(index, ServerStrings.ItemSystem_LootTooFar, GameColor.BrightRed);
            return;
        }

        TryTakeMapItem(index, mapNum, mi, announceRefusal: true);
    }

    /// <summary>Pick up everything on one tile that this player can claim — the tile menu's Pick Up All.
    ///
    /// <para><b>One at a time, and a partial result is a SUCCESS.</b> A bag that fills halfway through
    /// leaves the rest on the ground and says how many; failing the whole batch because the last item
    /// would not fit is how a player loses a kill to a full bag. Ordered top-of-stack first, so what a
    /// partial pick-up takes is the same thing the pick-up key would have taken.</para>
    ///
    /// <para>Items claimed by somebody else are skipped in silence: the caller asked for THEIR loot, and
    /// a refusal per stranger's stack would turn a shared corpse into a wall of text.</para></summary>
    public void PlayerMapPickUpAllAt(int index, int mapNum, int x, int y, WorldLayer layer)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        // Snapshotted before taking anything: TryTakeMapItem removes from this same list, and mutating a
        // collection while walking it is the classic way to skip every other entry.
        var onTile = new List<MapItemRecord>();
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m.Num <= 0 || m.Num > _world.Limits.Items) continue;
            if (m.X != x || m.Y != y || m.Layer != layer) continue;
            onTile.Add(m);
        }
        if (onTile.Count == 0)
        {
            SendMsg(index, ServerStrings.ItemSystem_LootGone, GameColor.Yellow);
            return;
        }

        // Reach is a property of the TILE, so it is answered once off the first item rather than per
        // stack — every item here is on the same square by construction.
        if (!_world.IsMapItemInReach(index, p, mapNum, onTile[0]))
        {
            SendMsg(index, ServerStrings.ItemSystem_LootTooFar, GameColor.BrightRed);
            return;
        }

        onTile.Sort((a, b) => b.DropSeq.CompareTo(a.DropSeq));   // top of the stack first

        int taken = 0, left = 0;
        foreach (var mi in onTile)
        {
            switch (TryTakeMapItem(index, mapNum, mi, announceRefusal: false))
            {
                case PickUpResult.Taken: taken++; break;
                case PickUpResult.BagFull: left++; break;
                // Claimed: somebody else's. Not mine to take and not worth a line about.
            }
        }

        if (left > 0)
            SendMsg(index, ServerStrings.ItemSystem_LootLeftBehind, GameColor.BrightRed, ("Count", left));
        else if (taken == 0)
            SendMsg(index, ServerStrings.ItemSystem_LootGone, GameColor.Yellow);
    }
}

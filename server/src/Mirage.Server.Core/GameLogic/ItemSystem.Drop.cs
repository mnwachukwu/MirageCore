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

/// <summary>Everything that takes an item OUT of a player's inventory and onto the map or out of
/// existence — voluntary drops, bulk drops, and the death drops combat routes through here.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Player drop item ──────────────────────────────────────────────────────

    /// <summary>
    /// Voluntary drop: gated by the player-dropped clutter cap (counts only PlayerDropped items, so
    /// NPC loot piles can't lock players out of dropping).  Death drops bypass this and call
    /// <see cref="PlayerMapDropItemForDeath"/> directly.
    /// </summary>
    public void PlayerMapDropItem(int index, int invSlot, int amount)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidInvSlot(invSlot)) return;
        var p = _pm[index].Char;
        if (p.Inv[invSlot].Num == 0) return;
        // DestroyOnDrop items never hit the ground — dropping DESTROYS them (the client confirms first).
        // Currency is destroyed by the requested amount (partial); the ground clutter-cap below doesn't apply.
        if (_world.Items[p.Inv[invSlot].Num].DestroyOnDrop)
        {
            DestroyInventorySlot(index, invSlot, amount, ServerStrings.ItemSystem_CurrencyDestroyed, ServerStrings.ItemSystem_ItemDestroyed);
            return;
        }

        int mapNum = p.Map;
        int playerDropped = 0;
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
            if (list[i].Source == ItemSource.PlayerDropped) playerDropped++;
        if (playerDropped >= Constants.MaxMapItems)
        {
            SendMsg(index, ServerStrings.ItemSystem_TooManyOnGround, GameColor.BrightRed);
            return;
        }
        DropInventorySlotToMap(index, invSlot, amount);
    }

    /// <summary>Right-click "Drop 1 / X / All": drop up to <paramref name="amount"/> matching
    /// non-currency items from inventory onto the map at the player's tile. Equipped slots are
    /// skipped (the user must explicitly unequip to drop gear). The ground clutter cap clamps the
    /// count rather than refusing outright, and any shortfall is reported — a full cap gets its own
    /// message, a partial drop gets a moved/requested one. Currency uses the per-slot
    /// <see cref="PlayerMapDropItem"/> entrypoint instead.</summary>
    public void PlayerMapDropBulk(int index, int itemNum, int amount)
    {
        if (!_pm[index].IsPlaying) return;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;
        if (amount < 0) return;

        var item = _world.Items[itemNum];
        if (item.Type == ItemType.Currency) return;

        var p = _pm[index].Char;
        int requested = CountMatchingInvSlotsSkipEquipped(p, itemNum);
        if (requested == 0) return;
        if (amount > 0 && amount < requested) requested = amount;

        // DestroyOnDrop items are destroyed rather than dropped (no ground cap; the client confirms first).
        if (item.DestroyOnDrop)
        {
            int destroyedCount = 0;
            for (int invSlot = 1; invSlot <= Constants.MaxInv && destroyedCount < requested; invSlot++)
            {
                if (p.Inv[invSlot].Num != itemNum) continue;
                if (p.IsEquipped(invSlot)) continue;
                DestroyInventorySlot(index, invSlot, 0, ServerStrings.ItemSystem_CurrencyDestroyed, ServerStrings.ItemSystem_ItemDestroyed);
                destroyedCount++;
            }
            return;
        }

        int mapNum = p.Map;
        int playerDropped = 0;
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
            if (list[i].Source == ItemSource.PlayerDropped) playerDropped++;
        int available = Constants.MaxMapItems - playerDropped;
        if (available <= 0)
        {
            SendMsg(index, ServerStrings.ItemSystem_TooManyOnGround, GameColor.BrightRed);
            return;
        }

        int target = Math.Min(requested, available);
        int dropped = 0;
        for (int invSlot = 1; invSlot <= Constants.MaxInv && dropped < target; invSlot++)
        {
            if (p.Inv[invSlot].Num != itemNum) continue;
            if (p.IsEquipped(invSlot)) continue;
            DropInventorySlotToMap(index, invSlot, amount: 0);
            dropped++;
        }

        if (dropped < requested)
            SendMsg(index, ServerStrings.ItemSystem_DropPartial, GameColor.Yellow, ("Moved", dropped), ("Requested", requested));
    }

    private static int CountMatchingInvSlotsSkipEquipped(PlayerRecord p, int itemNum)
    {
        int count = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (p.Inv[i].Num != itemNum) continue;
            if (p.IsEquipped(i)) continue;
            count++;
        }
        return count;
    }

    /// <summary>The inventory slot holding the worn copy of <paramref name="itemNum"/>, or 0 when no
    /// copy of it is being worn.</summary>
    public static int WornInvSlotOf(PlayerRecord p, int itemNum)
    {
        foreach (int invSlot in p.Equipped.Values)
        {
            if (SlotValidation.IsValidInvSlot(invSlot) && p.Inv[invSlot].Num == itemNum) return invSlot;
        }
        return 0;
    }

    /// <summary>
    /// Death-path drop: bypasses the voluntary cap so a corpse always sheds its loot, and tags the
    /// item with <see cref="ItemSource.PlayerDeathDropped"/> so guard janitors leave the corpse
    /// alone — the victim (and other players) get a real window to recover or loot it.
    /// </summary>
    public void PlayerMapDropItemForDeath(int index, int invSlot, int amount)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidInvSlot(invSlot)) return;
        var p = _pm[index].Char;
        if (p.Inv[invSlot].Num == 0) return;
        // A DestroyOnDrop item is DESTROYED on death, never dropped to the map — so it can't be wash-farmed by
        // dying near a friend. The caller decides IF and how much is lost; this destroys the amount it passed.
        if (_world.Items[p.Inv[invSlot].Num].DestroyOnDrop)
        {
            DestroyInventorySlot(index, invSlot, amount, ServerStrings.ItemSystem_CurrencyLostOnDeath, ServerStrings.ItemSystem_ItemLostOnDeath);
            return;
        }
        DropInventorySlotToMap(index, invSlot, amount, ItemSource.PlayerDeathDropped);
    }

    // Destroy a DestroyOnDrop item with NO map drop (voluntary drop after the client confirm, or on death).
    // Currency is PARTIAL — destroys up to `amount` (Value -= amount; slot cleared only when it empties),
    // mirroring the currency branch of DropInventorySlotToMap so a "destroyed" loss matches a "dropped" one.
    // A non-currency slot clears whole (callers pass amount 0). The message names the amount for currency, the
    // item otherwise, so the caller supplies the currency/item message pair for its context (death vs voluntary).
    private void DestroyInventorySlot(int index, int invSlot, int amount, string currencyMsg, string itemMsg)
    {
        var p = _pm[index].Char;
        var item = _world.Items[p.Inv[invSlot].Num];
        string itemName = item.TrimmedName;
        if (item.Type == ItemType.Currency)
        {
            int destroyed = amount >= p.Inv[invSlot].Quantity ? p.Inv[invSlot].Quantity : Math.Max(0, amount);
            if (amount >= p.Inv[invSlot].Quantity)
            {
                p.Inv[invSlot].Num = 0;
                p.Inv[invSlot].Quantity = 0;
                p.Inv[invSlot].Dur = 0;
            }
            else
            {
                p.Inv[invSlot].Quantity -= amount;
            }

            SendMsg(index, currencyMsg, GameColor.BrightRed, ("Amount", destroyed), ("Item", itemName));
        }
        else
        {
            p.Inv[invSlot].Num = 0;
            p.Inv[invSlot].Quantity = 0;
            p.Inv[invSlot].Dur = 0;
            SendMsg(index, itemMsg, GameColor.BrightRed, ("Item", itemName));
        }
        SendInventoryUpdate(index, invSlot);
        _pm.MarkDirty(index);
    }

    private void DropInventorySlotToMap(int index, int invSlot, int amount, ItemSource source = ItemSource.PlayerDropped)
    {
        var p = _pm[index].Char;
        int mapNum = p.Map;
        var item = _world.Items[p.Inv[invSlot].Num];
        int dropNum = p.Inv[invSlot].Num;
        int dropDur = TryUnequipIfEquipped(index, p, invSlot, item.Type);

        int dropValue;
        if (item.Type == ItemType.Currency)
        {
            dropValue = amount >= p.Inv[invSlot].Quantity ? p.Inv[invSlot].Quantity : amount;
            ViewportMsg(index, ServerStrings.ItemSystem_DropMultiple, GameColor.Yellow, ("Player", p.TrimmedName), ("Amount", dropValue), ("Item", item.TrimmedName));

            if (amount >= p.Inv[invSlot].Quantity)
            {
                p.Inv[invSlot].Num = 0;
                p.Inv[invSlot].Quantity = 0;
                p.Inv[invSlot].Dur = 0;
            }
            else
            {
                p.Inv[invSlot].Quantity -= amount;
            }
        }
        else
        {
            dropValue = 0;
            if (ItemRecord.IsEquipment(item.Type))
            {
                ViewportMsg(index, ServerStrings.ItemSystem_DropWithDurability, GameColor.Yellow,
                    ("Player", p.TrimmedName), ("Item", item.TrimmedName), ("Current", p.Inv[invSlot].Dur), ("Max", item.Durability));
            }
            else
            {
                ViewportMsg(index, ServerStrings.ItemSystem_Drop, GameColor.Yellow,
                    ("Player", p.TrimmedName), ("Item", item.TrimmedName));
            }

            p.Inv[invSlot].Num = 0;
            p.Inv[invSlot].Quantity = 0;
            p.Inv[invSlot].Dur = 0;
        }

        SendInventoryUpdate(index, invSlot);

        // Pass dropDur so the broadcast carries the worn durability instead of the item's max.
        SpawnItem(dropNum, dropValue, mapNum, p.X, p.Y, source, durOverride: dropDur, layer: p.Layer);   // drop on the dropper's layer
        EnqueueSaveDroppedItems(mapNum);
        // The map side just persisted; persist the inventory loss THIS tick too, or a hard-disconnect
        // before the autosave would roll the bag back and dupe the dropped item.
        _pm.MarkDirty(index);
    }
}

using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Tidying the bag: the canonical inventory order, the sort key each item type resolves
/// to, and the in-place reorder that keeps the equipped-slot indices pointing at the same gear.</summary>
public sealed partial class ItemSystem : GameSystem
{
    /// <summary>Tidy the player's bag into the canonical order and resync. Order: gold, other currencies
    /// (alpha), worn equipment, then unworn equipment (both in the game's declared slot order, strongest
    /// first, then alpha), keys (alpha), then consumables (magnitude desc). Empty slots fall to the tail.
    /// Reorders the slot objects in place, re-points every worn slot, then sends the full inventory and
    /// worn set and marks the player dirty so the tidy persists this tick.</summary>
    public void SortInventory(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        // Capture the worn slot OBJECTS before the move — slots are reference types, so each one's new
        // index can be found by identity after reordering.
        var worn = p.Equipped
            .Where(kv => SlotValidation.IsValidInvSlot(kv.Value))
            .ToDictionary(kv => kv.Key, kv => p.Inv[kv.Value], StringComparer.Ordinal);

        SortSlots(p.Inv, Constants.MaxInv, _world.Items, _world.EquipSlots, p.IsEquipped);

        p.Equipped.Clear();
        foreach (var (key, slot) in worn) p.SetEquipped(key, IndexOfSlot(p, slot));

        SendFullInventory(index);
        SendEquippedGear(index);
        _pm.MarkDirty(index);   // persist the tidy in this tick's dirty-flush
    }

    // One entry in the bag-sort pass: the slot plus the three key components and the tiebreak name,
    // pulled out so the OrderBy chain below reads as named fields. Was an anonymous five-element tuple,
    // duplicated verbatim in BankSystem.SortBank alongside a second copy of the sort itself.
    private readonly record struct SortEntry(PlayerInvSlot Slot, int Cat, int Sub, int Mag, string Name);

    /// <summary>Reorder slots <c>1..count</c> of a slot array into the shared bag order, packing the
    /// occupied slots to the front and blanking the tail. Shared by the inventory tidy and the bank tidy
    /// (<see cref="BankSystem.SortBank"/>), which carried identical copies of this loop, list and
    /// four-key <c>OrderBy</c> chain — a change to the ordering had to be made twice to take effect in
    /// both bags. <paramref name="isEquipped"/> is asked about each slot INDEX; a bank never holds worn
    /// gear and passes a constant false.</summary>
    internal static void SortSlots(PlayerInvSlot[] slots, int count, ItemRecord[] items,
                                  EquipSlotSet equipSlots, Func<int, bool> isEquipped)
    {
        var occupied = new List<SortEntry>();
        for (int i = 1; i <= count; i++)
        {
            var s = slots[i];
            if (s.Num <= 0 || s.Num >= items.Length) continue;
            var item = items[s.Num];
            var (cat, sub, mag) = SortKey(s.Num, item, equipSlots, isEquipped(i));
            occupied.Add(new SortEntry(s, cat, sub, mag, item.TrimmedName));
        }

        var sorted = occupied
            .OrderBy(e => e.Cat)
            .ThenBy(e => e.Sub)
            .ThenByDescending(e => e.Mag)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Slot)
            .ToList();

        for (int i = 1; i <= count; i++)
            slots[i] = i <= sorted.Count ? sorted[i - 1] : new PlayerInvSlot();
    }

    // Sort key (category, sub-order, magnitude). Lower category/sub sorts first; magnitude sorts
    // DESCENDING in the OrderBy chain, so bigger potions / stronger gear rise. Gold (item id 1) pins
    // above every other currency. Gear leads the bag: equipped pieces (category 2) then unequipped
    // pieces (category 3, strongest bonus first), both above keys/scrolls/potions. Shared verbatim with
    // the bank sort (BankSystem.SortBank); a bank never holds equipped gear, so its gear all lands in
    // category 3.
    internal static (int Cat, int Sub, int Mag) SortKey(int itemNum, ItemRecord item,
                                                       EquipSlotSet equipSlots, bool equipped)
    {
        if (itemNum == Constants.GoldItemIndex) return (0, 0, 0);
        if (equipped) return (2, SlotOrder(item, equipSlots), 0);
        return item.Type switch
        {
            ItemType.Currency => (1, 0, 0),
            ItemType.Equipment => (3, SlotOrder(item, equipSlots), item.Power),
            ItemType.Key => (4, 0, 0),
            ItemType.Consumable => (5, 0, item.VitalAmount),
            _ => (6, 0, 0),
        };
    }

    // Equipment sorts into the order the game declared its slots in. A piece naming a slot this world
    // does not have sorts last among equipment rather than falling into another category.
    private static int SlotOrder(ItemRecord item, EquipSlotSet equipSlots) =>
        equipSlots.Find(item.EquipSlot)?.Ordinal ?? int.MaxValue;

    private static int IndexOfSlot(PlayerRecord p, PlayerInvSlot target)
    {
        for (int i = 1; i <= Constants.MaxInv; i++)
            if (ReferenceEquals(p.Inv[i], target)) return i;
        return 0;
    }
}

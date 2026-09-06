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

/// <summary>Tidying the bag: the canonical inventory order, the sort key each item type resolves
/// to, and the in-place reorder that keeps the equipped-slot indices pointing at the same gear.</summary>
public sealed partial class ItemSystem : GameSystem
{
    /// <summary>Tidy the player's bag into the canonical order and resync. Order: Gold, other
    /// currencies (alpha), equipped gear (Weapon/Armor/Helmet/Shield), then unequipped gear (same type
    /// order, strongest bonus first, then alpha), keys (alpha), spell scrolls (alpha), Add potions then
    /// Sub potions (each grouped by vital HP/MP/SP, magnitude desc). Empty slots fall to the tail.
    /// Reorders the slot objects in place, re-points the four equipped-slot indices, then sends the
    /// full inventory + equipped gear and marks the player dirty so the tidy persists this tick.</summary>
    public void SortInventory(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        // Capture the equipped slot OBJECTS before the move — slots are reference types, so each one's
        // new index can be found by identity after reordering.
        PlayerInvSlot? weapon = p.WeaponSlot > 0 ? p.Inv[p.WeaponSlot] : null;
        PlayerInvSlot? armor = p.ArmorSlot > 0 ? p.Inv[p.ArmorSlot] : null;
        PlayerInvSlot? helmet = p.HelmetSlot > 0 ? p.Inv[p.HelmetSlot] : null;
        PlayerInvSlot? shield = p.ShieldSlot > 0 ? p.Inv[p.ShieldSlot] : null;

        SortSlots(p.Inv, Constants.MaxInv, _world.Items,
            i => i == p.WeaponSlot || i == p.ArmorSlot || i == p.HelmetSlot || i == p.ShieldSlot);

        p.WeaponSlot = weapon is null ? 0 : IndexOfSlot(p, weapon);
        p.ArmorSlot = armor is null ? 0 : IndexOfSlot(p, armor);
        p.HelmetSlot = helmet is null ? 0 : IndexOfSlot(p, helmet);
        p.ShieldSlot = shield is null ? 0 : IndexOfSlot(p, shield);

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
    internal static void SortSlots(PlayerInvSlot[] slots, int count, ItemRecord[] items, Func<int, bool> isEquipped)
    {
        var occupied = new List<SortEntry>();
        for (int i = 1; i <= count; i++)
        {
            var s = slots[i];
            if (s.Num <= 0 || s.Num >= items.Length) continue;
            var item = items[s.Num];
            var (cat, sub, mag) = SortKey(s.Num, item, isEquipped(i));
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
    internal static (int Cat, int Sub, int Mag) SortKey(int itemNum, ItemRecord item, bool equipped)
    {
        if (itemNum == Constants.GoldItemIndex) return (0, 0, 0);
        if (equipped) return (2, TypeOrder(item.Type), 0);
        return item.Type switch
        {
            ItemType.Currency => (1, 0, 0),
            ItemType.Weapon => (3, 0, item.Power),
            ItemType.Armor => (3, 1, item.Power),
            ItemType.Helmet => (3, 2, item.Power),
            ItemType.Shield => (3, 3, item.Power),
            ItemType.Key => (4, 0, 0),
            ItemType.Spell => (5, 0, 0),
            ItemType.PotionAddHp => (6, 0, item.VitalAmount),
            ItemType.PotionAddMp => (6, 1, item.VitalAmount),
            ItemType.PotionAddSp => (6, 2, item.VitalAmount),
            ItemType.PotionSubHp => (7, 0, item.VitalAmount),
            ItemType.PotionSubMp => (7, 1, item.VitalAmount),
            ItemType.PotionSubSp => (7, 2, item.VitalAmount),
            _ => (8, 0, 0),
        };
    }

    // Equipped and unequipped gear both order Weapon, Armor, Helmet, Shield.
    private static int TypeOrder(ItemType type) => type switch
    {
        ItemType.Weapon => 0,
        ItemType.Armor => 1,
        ItemType.Helmet => 2,
        ItemType.Shield => 3,
        _ => 4,
    };

    private static int IndexOfSlot(PlayerRecord p, PlayerInvSlot target)
    {
        for (int i = 1; i <= Constants.MaxInv; i++)
            if (ReferenceEquals(p.Inv[i], target)) return i;
        return 0;
    }
}

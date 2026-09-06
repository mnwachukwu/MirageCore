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

public sealed partial class ItemSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;

    // Monotonic counter stamped on each spawned/dropped map item. Highest DropSeq at a tile = top of stack.
    private long _dropSeqCounter;

    // Per-map coalescing state for dropped-item writes. Two back-to-back enqueues on the same map
    // would otherwise race File.WriteAllTextAsync on the same path and one would lose with an
    // IOException (sharing violation), so we keep one worker per map and let the latest snapshot win.
    private sealed class MapSaveState
    {
        public readonly object Lock = new();
        public DroppedItemSaveData[]? Pending;
        public Task? Worker;
    }
    private readonly Dictionary<int, MapSaveState> _saveStates = [];
    private readonly object _saveStatesLock = new();

    public ItemSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                      IPersistenceService persistence, IBackgroundPersistence bg)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _persistence = persistence;
        _bg = bg;
    }

    /// <summary>Fire-and-forget save of a map's dropped items. Snapshots synchronously on the
    /// caller thread; the write itself coalesces with any in-flight save for the same map and
    /// runs through <see cref="IBackgroundPersistence"/> so faults are logged and shutdown drain
    /// awaits the final write.</summary>
    public void EnqueueSaveDroppedItems(int mapNum) => _ = EnqueueSaveDroppedItemsCore(mapNum);

    // ── Inventory helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the 1-based inventory slot index for itemNum, or 0 if no slot available.
    /// For currency items, returns an existing stack slot if present; otherwise first empty slot.
    /// </summary>
    public static int FindOpenInvSlot(PlayerRecord p, ItemRecord[] items, int itemNum)
    {
        if (itemNum <= 0 || itemNum >= items.Length) return 0;

        // Currency: stack onto existing slot if present
        if (items[itemNum].Type == ItemType.Currency)
        {
            for (int i = 1; i <= Constants.MaxInv; i++)
                if (p.Inv[i].Num == itemNum) return i;
        }

        // Otherwise find first empty slot (1-based)
        for (int i = 1; i <= Constants.MaxInv; i++)
            if (p.Inv[i].Num == 0) return i;

        return 0;
    }

    /// <summary>Room for <paramref name="getItem"/> once the payment has been handed over. Every exchange
    /// here takes before it gives, so the slots the payment vacates are part of the room the goods arrive
    /// into: two gems for three hats with two slots free WORKS, because paying leaves four. Counting the
    /// room first and the payment never would refuse trades that are perfectly possible — and a vendor is
    /// how a full bag gets emptied, so that refusal lands exactly when it is least wanted.</summary>
    public static long RoomAfterPaying(PlayerRecord p, ItemRecord[] items, int getItem, int giveItem, long giveCount)
    {
        long room = CountOpenInvSlots(p, items, getItem);
        if (room >= int.MaxValue) return room;   // a stack already open takes any amount
        return room + FreedByPaying(p, items, giveItem, giveCount);
    }

    /// <summary>How many slots handing over <paramref name="count"/> of an item empties. A slot-per-copy item
    /// frees one per copy; a currency frees its single slot only if the whole pile goes.</summary>
    private static long FreedByPaying(PlayerRecord p, ItemRecord[] items, int itemNum, long count)
    {
        if (count <= 0 || itemNum <= 0 || itemNum >= items.Length) return 0;
        if (items[itemNum].Type != ItemType.Currency) return count;
        return CountItem(p, items, itemNum) <= count ? 1 : 0;
    }

    /// <summary>How many of <paramref name="itemNum"/> the bag holds, counted the way the item stacks: a
    /// currency carries its amount inside its slots, everything else spends a slot each. Sums across the
    /// whole bag rather than stopping at the first match — a treasure gem takes a slot apiece, so five of
    /// them are five slots and answering "1" makes every quantity test against them fail.</summary>
    public static long CountItem(PlayerRecord p, ItemRecord[] items, int itemNum)
    {
        if (itemNum <= 0 || itemNum >= items.Length) return 0;
        bool stacks = items[itemNum].Type == ItemType.Currency;
        long total = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (p.Inv[i].Num != itemNum) continue;
            total += stacks ? p.Inv[i].Quantity : 1;
        }
        return total;
    }

    /// <summary>Whether the bag holds any at all — the same question <see cref="CountItem"/> answers,
    /// asked of callers that only need a yes.</summary>
    public static bool HasItem(PlayerRecord p, ItemRecord[] items, int itemNum) =>
        CountItem(p, items, itemNum) > 0;

    /// <summary>How many more of <paramref name="itemNum"/> the bag could take. A currency needs one slot
    /// however much is poured into it, so an existing stack means there is always room; everything else
    /// spends a slot per copy, and the answer is how many are free.</summary>
    public static int CountOpenInvSlots(PlayerRecord p, ItemRecord[] items, int itemNum)
    {
        if (itemNum <= 0 || itemNum >= items.Length) return 0;
        int free = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (p.Inv[i].Num == itemNum && items[itemNum].Type == ItemType.Currency) return int.MaxValue;
            if (p.Inv[i].Num == 0) free++;
        }
        return items[itemNum].Type == ItemType.Currency ? (free > 0 ? int.MaxValue : 0) : free;
    }

    /// <summary>Would every unclaimed item attachment in <paramref name="stacks"/> fit in <paramref name="p"/>'s bag
    /// AT ONCE? A currency stack stacks onto an existing (or already batch-placed) slot of the same item; anything
    /// else needs its own empty slot. Side-effect-free (simulates against the current inventory) so a caller can gate
    /// an all-or-nothing release — e.g. a CoD unlock, which must not half-fill the bag — before committing.</summary>
    public static bool CanReceiveAll(PlayerRecord p, ItemRecord[] items, IReadOnlyList<MailAttachment> stacks)
    {
        var usedEmpty = new HashSet<int>();       // empty slots this batch has already spoken for
        var currencyPlaced = new HashSet<int>();  // currency itemNums that now have a home (existing or batch-placed)
        foreach (var a in stacks)
        {
            if (a.Claimed || a.ItemNum <= 0 || a.ItemNum >= items.Length) continue;
            bool currency = items[a.ItemNum].Type == ItemType.Currency;
            if (currency)
            {
                if (currencyPlaced.Contains(a.ItemNum)) continue;   // stacks onto one already accounted for
                bool hasExisting = false;
                for (int i = 1; i <= Constants.MaxInv; i++)
                {
                    if (p.Inv[i].Num == a.ItemNum)
                    {
                        hasExisting = true;
                        break;
                    }
                }

                if (hasExisting)
                {
                    currencyPlaced.Add(a.ItemNum);
                    continue;
                }
            }
            int slot = 0;
            for (int i = 1; i <= Constants.MaxInv; i++)
            {
                if (p.Inv[i].Num == 0 && !usedEmpty.Contains(i))
                {
                    slot = i;
                    break;
                }
            }

            if (slot == 0) return false;
            usedEmpty.Add(slot);
            if (currency) currencyPlaced.Add(a.ItemNum);
        }
        return true;
    }

    // ── Give / take items ─────────────────────────────────────────────────────

    public void GiveItem(int index, int itemNum, int value) => TryGiveItem(index, itemNum, value);

    /// <summary>Give <paramref name="count"/> of an item, however that item is held: a currency takes the
    /// whole amount into one stack, everything else takes a slot per copy.
    /// <para><see cref="GiveItem"/> adds its value to a SINGLE slot, which is what a stack wants and what a
    /// gem does not — three gems handed over that way become one slot claiming a quantity of three, a shape
    /// nothing else in the game reads. Returns how many actually landed, which is fewer than asked when the
    /// bag fills.</para></summary>
    public int GiveItems(int index, int itemNum, int count)
    {
        if (!_pm[index].IsPlaying || itemNum <= 0 || itemNum > _world.Limits.Items || count <= 0) return 0;
        if (_world.Items[itemNum].Type == ItemType.Currency)
            return TryGiveItem(index, itemNum, count) ? count : 0;

        int placed = 0;
        while (placed < count && TryGiveItem(index, itemNum, 1)) placed++;
        return placed;
    }

    /// <summary>Take <paramref name="count"/> of an item, however that item is held: a currency loses that
    /// much off its stack, everything else loses that many whole slots.
    /// <para><see cref="TakeItem"/> clears ONE slot per call for a non-stacking item — right for drinking a
    /// potion, wrong for a barter row that asks for two gems. This is the loop that difference needs.</para></summary>
    public void TakeItems(int index, int itemNum, int count)
    {
        if (!_pm[index].IsPlaying || itemNum <= 0 || itemNum > _world.Limits.Items || count <= 0) return;
        if (_world.Items[itemNum].Type == ItemType.Currency)
        {
            TakeItem(index, itemNum, count);
            return;
        }
        for (int i = 0; i < count; i++) TakeItem(index, itemNum, 0);
    }

    /// <summary>Give an item, returning false (after an InventoryFull message) when the bag can't take it,
    /// so callers that must not lose the item (e.g. mail-attachment claim) can leave it for a later retry.
    /// A <paramref name="dur"/> above 0 overrides the placed durability, used to carry a mailed worn item's
    /// wear across delivery instead of resetting equipment to full.</summary>
    public bool TryGiveItem(int index, int itemNum, int value, int dur = 0)
    {
        if (!_pm[index].IsPlaying || itemNum <= 0 || itemNum > _world.Limits.Items) return false;

        int slot = PlaceInInventory(_pm[index].Char, _world.Items, itemNum, value, dur);
        if (slot == 0)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return false;
        }

        SendInventoryUpdate(index, slot);
        return true;
    }

    /// <summary>Put a stack in a bag, and nothing else — no message, no packet, no player slot. What "give"
    /// means to the RECORD, so the live path above and the editor's account browser (which has to do this to
    /// a character who may be nowhere near a player slot) cannot disagree about stacking or durability.
    /// <para>Returns the bag slot used, or 0 when the bag is full.</para></summary>
    public static int PlaceInInventory(PlayerRecord p, ItemRecord[] items, int itemNum, int value, int dur = 0)
    {
        if (itemNum <= 0 || itemNum >= items.Length) return 0;
        int slot = FindOpenInvSlot(p, items, itemNum);
        if (slot == 0) return 0;

        var item = items[itemNum];
        p.Inv[slot].Num = itemNum;
        p.Inv[slot].AddQuantity(value);

        if (item.Type is ItemType.Armor or ItemType.Weapon or ItemType.Helmet or ItemType.Shield)
            p.Inv[slot].Dur = dur > 0 ? dur : item.Durability;

        return slot;
    }

    /// <summary>Take a stack out of one bag slot, and nothing else. Currency takes <paramref name="amount"/>
    /// (0 or more than the pile = all of it); anything else goes whole.
    /// <para><b>A worn piece is taken, and the equipment pointer cleared with it.</b> The in-game paths
    /// refuse instead — the bank tells you to unequip first — because a player can go and do that. An
    /// operator reaching into somebody else's bag cannot, and a pointer left naming an emptied slot is a
    /// corrupt character sheet.</para>
    /// <para>Returns what came out; ItemNum 0 means the slot held nothing.</para></summary>
    public static (int ItemNum, int Quantity) TakeFromInventory(PlayerRecord p, ItemRecord[] items, int invSlot, int amount)
    {
        if (invSlot < 1 || invSlot > Constants.MaxInv) return (0, 0);
        var inv = p.Inv[invSlot];
        if (inv.Num <= 0 || inv.Num >= items.Length) return (0, 0);

        int itemNum = inv.Num;
        var item = items[itemNum];
        bool stacks = item.Type == ItemType.Currency;
        int take = stacks && amount > 0 && amount < inv.Quantity ? amount : Math.Max(inv.Quantity, 1);

        if (stacks && take < inv.Quantity)
        {
            p.Inv[invSlot].Quantity -= take;
            return (itemNum, take);
        }

        if (EquippedSlotForType(p, item.Type) == invSlot) Unequip(p, item.Type);
        p.Inv[invSlot].Num = 0;
        p.Inv[invSlot].Quantity = 0;
        p.Inv[invSlot].Dur = 0;
        return (itemNum, take);
    }

    /// <summary>Escrow a stack out of a SPECIFIC inventory slot (mail send, marketplace listing). Currency
    /// takes <paramref name="amount"/> (<= 0 or over the pile = the whole pile); a non-currency slot is
    /// taken whole but REFUSED while equipped. Returns the removed stack (ItemNum 0 = nothing removed; the
    /// equipped case already messaged the player). The caller owns the removed stack — deliver or refund it.</summary>
    public (int ItemNum, int Value, int Dur) RemoveFromSlot(int index, int invSlot, int amount)
    {
        if (!_pm[index].IsPlaying || invSlot < 1 || invSlot > Constants.MaxInv) return (0, 0, 0);
        var p = _pm[index].Char;
        var inv = p.Inv[invSlot];
        if (inv.Num <= 0 || inv.Num > _world.Limits.Items) return (0, 0, 0);

        var item = _world.Items[inv.Num];
        if (item.Type == ItemType.Currency)
        {
            int itemNum = inv.Num;   // capture before a full take zeroes the shared slot object (inv is a reference)
            int take = (amount <= 0 || amount > inv.Quantity) ? inv.Quantity : amount;
            if (take >= inv.Quantity)
            {
                p.Inv[invSlot].Num = 0;
                p.Inv[invSlot].Quantity = 0;
                p.Inv[invSlot].Dur = 0;
            }
            else
            {
                p.Inv[invSlot].Quantity -= take;
            }

            SendInventoryUpdate(index, invSlot);
            return (itemNum, take, 0);
        }

        if (EquippedSlotForType(p, item.Type) == invSlot)
        {
            SendMsg(index, ServerStrings.BankSystem_UnequipFirst, GameColor.BrightRed);
            return (0, 0, 0);
        }
        int num = inv.Num, val = inv.Quantity, dur = inv.Dur;
        p.Inv[invSlot].Num = 0;
        p.Inv[invSlot].Quantity = 0;
        p.Inv[invSlot].Dur = 0;
        SendInventoryUpdate(index, invSlot);
        return (num, val, dur);
    }

    /// <summary>Place an item into an OFFLINE character record's bag — currency stacks onto an existing pile,
    /// else the first free slot — with no packets and no online player. Used only by trade-journal boot
    /// recovery, which reconciles account files while nobody is connected. Returns false if the bag is full
    /// (the swap's CanReceive pre-check guarantees room, so a false here is a defensive signal, not expected).</summary>
    public static bool TryGiveItemOffline(PlayerRecord p, ItemRecord[] items, int itemNum, int value, int dur)
    {
        if (itemNum <= 0 || itemNum >= items.Length || items[itemNum] is null) return false;
        if (items[itemNum].Type == ItemType.Currency)
        {
            for (int i = 1; i <= Constants.MaxInv; i++)
                if (p.Inv[i] is { } s && s.Num == itemNum) { s.AddQuantity(value); return true; }
        }

        for (int i = 1; i <= Constants.MaxInv; i++)
            if (p.Inv[i] is { Num: 0 } slot) { slot.Num = itemNum; slot.Quantity = value; slot.Dur = dur; return true; }
        return false;
    }

    public void TakeItem(int index, int itemNum, int value)
    {
        if (!_pm[index].IsPlaying || itemNum <= 0 || itemNum > _world.Limits.Items) return;
        var p = _pm[index].Char;
        var item = _world.Items[itemNum];
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (p.Inv[i].Num != itemNum) continue;
            bool take = false;
            if (item.Type == ItemType.Currency)
            {
                if (value >= p.Inv[i].Quantity)
                {
                    take = true;
                }
                else
                {
                    p.Inv[i].Quantity -= value;
                    SendInventoryUpdate(index, i);
                }
            }
            else
            {
                // When the target item is the one currently
                // equipped in its gear slot, remove that exact equipped copy. Skip unequipped
                // duplicates sitting in earlier slots so the loop lands on the equipped slot instead
                // of deleting a spare and leaving the equipped (e.g. just-broken) copy behind.
                int equippedSlot = EquippedSlotForType(p, item.Type);
                if (equippedSlot > 0 && i != equippedSlot && p.Inv[equippedSlot].Num == itemNum) continue;
                TryUnequipIfEquipped(index, p, i, item.Type);  // 0 = not equipped after the call
                take = true;
            }
            if (!take) return;
            p.Inv[i].Num = 0;
            p.Inv[i].Quantity = 0;
            p.Inv[i].Dur = 0;
            SendInventoryUpdate(index, i);
            return;
        }
    }

    /// <summary>If the inventory slot is currently equipped in the matching gear slot, zero
    /// the gear slot (the convention for "not equipped") and broadcast the new equipped set.
    /// Returns the slot's durability — used by the drop path to carry the equipped copy's wear
    /// onto the dropped ground item; <see cref="TakeItem"/> ignores the value. Returns 0
    /// for non-equipment item types.</summary>
    private int TryUnequipIfEquipped(int index, PlayerRecord p, int invSlot, ItemType type)
    {
        bool wasEquipped = false;
        switch (type)
        {
            case ItemType.Weapon when p.WeaponSlot == invSlot:
                p.WeaponSlot = 0;
                wasEquipped = true;
                break;
            case ItemType.Armor when p.ArmorSlot == invSlot:
                p.ArmorSlot = 0;
                wasEquipped = true;
                break;
            case ItemType.Helmet when p.HelmetSlot == invSlot:
                p.HelmetSlot = 0;
                wasEquipped = true;
                break;
            case ItemType.Shield when p.ShieldSlot == invSlot:
                p.ShieldSlot = 0;
                wasEquipped = true;
                break;
        }
        if (wasEquipped) SendEquippedGear(index);
        return type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield
            ? p.Inv[invSlot].Dur : 0;
    }

    /// <summary>Unequip the inventory slot from whichever gear slot currently holds it (no-op if it
    /// isn't equipped anywhere), broadcasting the new equipped set. Used when a worn item breaks at
    /// 0 durability so the now-unusable piece comes off but stays in the bag, to be repaired.</summary>
    public void UnequipSlot(int index, int invSlot)
    {
        var p = _pm[index].Char;
        bool wasEquipped = false;
        if (p.WeaponSlot == invSlot)
        {
            p.WeaponSlot = 0;
            wasEquipped = true;
        }
        else if (p.ArmorSlot == invSlot)
        {
            p.ArmorSlot = 0;
            wasEquipped = true;
        }
        else if (p.HelmetSlot == invSlot)
        {
            p.HelmetSlot = 0;
            wasEquipped = true;
        }
        else if (p.ShieldSlot == invSlot)
        {
            p.ShieldSlot = 0;
            wasEquipped = true;
        }
        if (wasEquipped) SendEquippedGear(index);
    }

    /// <summary>
    /// Re-checks every equipped piece against the player's CURRENT stats and takes off any whose
    /// STR/DEF requirement is no longer met (the piece stays in the bag, to be re-equipped later),
    /// notifying the player per item. Called after a delevel drains stats
    /// (<see cref="CombatSystem"/>.ApplyExpLoss) so a player can't spec into gear, die down a level,
    /// then re-spec while still wearing it. Base stats are gear-independent, so a single pass is
    /// enough — taking off one piece never lowers the stat that gates another. Spells need no
    /// equivalent sweep: SpellSystem.CastSpell re-checks the INT requirement live on every cast.
    /// </summary>
    public void RevalidateEquipmentRequirements(int index)
    {
        var p = _pm[index].Char;
        var cls = _world.Classes[p.Class];
        bool anyRemoved = false;
        anyRemoved |= UnequipIfRequirementsUnmet(index, p, cls, ItemType.Weapon);
        anyRemoved |= UnequipIfRequirementsUnmet(index, p, cls, ItemType.Armor);
        anyRemoved |= UnequipIfRequirementsUnmet(index, p, cls, ItemType.Helmet);
        anyRemoved |= UnequipIfRequirementsUnmet(index, p, cls, ItemType.Shield);
        if (anyRemoved) SendEquippedGear(index);
    }

    /// <summary>Takes off the piece equipped in <paramref name="type"/>'s slot if the player no longer
    /// meets its requirements — either the <see cref="CombatFormulas.GearStatRequirement"/> (weapons gate
    /// on STR, armor/helmet/shield on DEF) or its <see cref="ItemRecord.LevelReq"/>. Both mirror the equip
    /// checks in <see cref="UseItem"/> exactly, so the take-off threshold can't drift from the put-on one.
    /// <para>The level half matters for the same reason the stat half does: a delevel drains stats AND
    /// drops the level, so without it a player could die below an item's tier and keep wearing it, with no
    /// way to put it back on if they ever took it off.</para>
    /// The item is left in its inventory slot; only the gear-slot pointer is cleared. Returns true if it
    /// removed the piece. Does NOT broadcast — the caller sends one <see cref="SendEquippedGear"/> after
    /// sweeping all slots.</summary>
    private bool UnequipIfRequirementsUnmet(int index, PlayerRecord p, ClassRecord cls, ItemType type)
    {
        int invSlot = EquippedSlotForType(p, type);
        if (invSlot == 0) return false;
        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return false;
        var item = _world.Items[itemNum];
        bool isWeapon = type == ItemType.Weapon;
        int playerStat = isWeapon ? p.Str : p.Def;
        int classStat = isWeapon ? cls.Str : cls.Def;
        bool statOk = playerStat >= CombatFormulas.GearStatRequirement(item.Power, classStat);
        bool levelOk = item.LevelReq <= p.Level;
        if (statOk && levelOk) return false;
        switch (type)
        {
            case ItemType.Weapon:
                p.WeaponSlot = 0;
                break;
            case ItemType.Armor:
                p.ArmorSlot = 0;
                break;
            case ItemType.Helmet:
                p.HelmetSlot = 0;
                break;
            case ItemType.Shield:
                p.ShieldSlot = 0;
                break;
        }
        SendMsg(index, ServerStrings.ItemSystem_GearUnequippedDelevel, GameColor.BrightRed, ("Item", item.TrimmedName));
        return true;
    }
}

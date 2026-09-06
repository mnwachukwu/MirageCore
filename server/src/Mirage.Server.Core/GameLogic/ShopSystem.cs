using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Shop and inn interactions: the map-enter/leave greeting, item-for-item trades, and repair.
/// <para>Every entry point re-checks that the shop is actually open for the player via
/// <see cref="ServerPlayer.ActiveShop"/> — a shop is reachable only while standing by its keeper, so a
/// stale client-side shop number can't be used to trade from across the map.</para></summary>
public sealed class ShopSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;

    public ShopSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _items = items;
    }

    // ── Barter ────────────────────────────────────────────────────────────────

    /// <summary>Execute one row of a shop's barter table: take the row's "give" stack from the player and
    /// hand back its "get" stack. An ordinary purchase is just a row whose give side is the currency item.
    /// <para>Refuses unless the shop is open for this player, is a Store, the player holds enough, and the
    /// bag has room — checked in that order so the player gets the most specific message.</para></summary>
    public void Barter(int index, int shopNum, int barterSlot, int multiples = 1)
    {
        if (!_pm[index].IsPlaying) return;
        if (shopNum <= 0 || shopNum > _world.Limits.Shops) return;
        var p = _pm[index].Char;
        var shop = _world.Shops[shopNum];

        // barterSlot is 1-based: the client sends its display index + 1, resolved here as BarterItem[slot - 1].
        if (barterSlot < 1 || barterSlot > shop.BarterItem.Count) return;

        if (_pm[index].ActiveShop(_world, index) != shopNum)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearShop, GameColor.BrightRed);
            return;
        }

        if (shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearShop, GameColor.BrightRed);
            return;
        }

        var row = shop.BarterItem[barterSlot - 1];
        // Not a real barter unless both sides carry an item AND a positive quantity. A zero give quantity
        // slips past the HasItem check below (HasItem(...) >= 0 is always true) and a zero get quantity
        // still mints the item, so a misconfigured slot would hand out free items. Reject outright. The
        // editor and the shop-save handler both keep quantities >= 1, so this only guards legacy/bad data.
        if (row.GiveItem <= 0 || row.GetItem <= 0 || row.GiveQuantity <= 0 || row.GetQuantity <= 0) return;

        // The row is a RATE, and this is how many times to apply it. Five teeth against a two-teeth row
        // buys two helpings and leaves a tooth: the remainder stays in the bag rather than being rounded
        // into the trade.
        int want = Math.Max(multiples, 1);
        long affordable = ItemSystem.CountItem(p, _world.Items, row.GiveItem) / row.GiveQuantity;
        if (want > affordable)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotEnoughTrade, GameColor.BrightRed);
            return;
        }

        // REFUSED, not trimmed to fit. Handing over three teeth for nine hats and delivering eight would
        // charge the full price for a short measure, so a trade that cannot be paid out whole does not
        // happen at all — ask for two.
        long incoming = (long)row.GetQuantity * want;
        long payment = (long)row.GiveQuantity * want;
        if (ItemSystem.RoomAfterPaying(p, _world.Items, row.GetItem, row.GiveItem, payment) < incoming)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        // Counted rather than cleared-once on both sides: a row asking for two gems has to cost two, and a
        // row paying three has to arrive as three slots.
        _items.TakeItems(index, row.GiveItem, row.GiveQuantity * want);
        _items.GiveItems(index, row.GetItem, (int)incoming);
        SendMsg(index, ServerStrings.ShopSystem_TradedWith, GameColor.Yellow, ("ShopName", shop.TrimmedName));
    }

    // ── Buy / sell ────────────────────────────────────────────────────────────
    // The gold storefront, as opposed to Barter's item-for-item table. A sales entry is just an item number:
    // the price comes from ItemRecord.Price, which is what let a shopfront be authored by picking
    // items instead of hand-writing a give→get row each (see ShopRecord.SalesItem).

    /// <summary>Buy from a sales-list entry at its <see cref="ItemRecord.Price"/>.
    /// <para><paramref name="quantity"/> applies to anything: a currency pours into one stack, everything
    /// else takes a slot per copy.</para>
    /// <para>The two limits are deliberately different. GOLD CLAMPS — asking for more than the purse covers
    /// buys as many as it does, and nothing is charged for what was not received. ROOM REFUSES — a bag that
    /// can take eight of the twenty asked for buys none, because taking the money for twenty and handing
    /// over eight is the one outcome a purchase must never have.</para>
    /// <para>Refuses unless the shop is open for this player, is a Store, the entry is priced, the purse
    /// covers at least one, and the bag has room for all of them — in that order, so the player gets the
    /// most specific message.</para></summary>
    public void Buy(int index, int shopNum, int salesSlot, int quantity = 1)
    {
        if (!_pm[index].IsPlaying) return;
        if (shopNum <= 0 || shopNum > _world.Limits.Shops) return;
        var p = _pm[index].Char;
        var shop = _world.Shops[shopNum];

        // 1-based on the wire, like BarterSlot: the client sends its display index + 1.
        if (salesSlot < 1 || salesSlot > shop.SalesItem.Count) return;

        if (_pm[index].ActiveShop(_world, index) != shopNum || shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearShop, GameColor.BrightRed);
            return;
        }

        int itemNum = shop.SalesItem[salesSlot - 1];
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;
        var item = _world.Items[itemNum];

        // An unpriced entry is a data bug, and handing it over free is the same class of bug as the
        // zero-quantity barter row that used to mint items. Refuse rather than give it away.
        int price = item.Price;
        if (price <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotForSale, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        // Bought by the handful whatever it is — a caster buys reagents in dozens and an outfitter buys
        // arrows by the score, and neither wants to click twenty times. A currency pours into one stack;
        // everything else takes a slot per copy, which is what the room check below is counting.
        bool stacks = item.Type == ItemType.Currency;
        int want = Math.Max(quantity, 1);

        // Gold is CLAMPED: buying as much as the purse covers is the useful answer, and nobody is charged
        // for what they did not receive.
        long purse = ItemSystem.CountItem(p, _world.Items, Constants.GoldItemIndex);
        int amount = (int)Math.Min(want, purse / price);
        if (amount <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_InsufficientGold, GameColor.BrightRed);
            return;
        }

        // Room is REFUSED, not trimmed. Trimming would take the money for twenty and hand over eight,
        // which is the one outcome a purchase must never have.
        if (ItemSystem.RoomAfterPaying(p, _world.Items, itemNum, Constants.GoldItemIndex, (long)price * amount) < amount)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        if (stacks && StackHeadroom(p, itemNum) < amount)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        long cost = (long)price * amount;
        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);
        _items.GiveItems(index, itemNum, amount);

        if (amount > 1)
        {
            SendMsg(index, ServerStrings.ShopSystem_BoughtMany, GameColor.Yellow,
                ("Amount", amount), ("ItemName", item.TrimmedName), ("Gold", cost));
        }
        else
        {
            SendMsg(index, ServerStrings.ShopSystem_Bought, GameColor.Yellow,
                ("ItemName", item.TrimmedName), ("Gold", cost));
        }
    }

    /// <summary>The bag slots holding a copy INDISTINGUISHABLE from the one at <paramref name="invSlot"/> —
    /// same item, same durability — with the clicked slot first and the rest in bag order, and anything
    /// currently worn left out of it.
    /// <para>This is the set a bulk sale is allowed to draw from. Every member prices the same, so the total
    /// is one multiplication rather than a per-slot sum, and no copy can be handed over by surprise: sell
    /// five of a pristine helmet and the battered one on the next row is not among them.</para></summary>
    private static List<int> IdenticalSaleableSlots(PlayerRecord p, int invSlot, int itemNum)
    {
        int dur = p.Inv[invSlot].Dur;
        var slots = new List<int> { invSlot };
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (i == invSlot || p.Inv[i].Num != itemNum || p.Inv[i].Dur != dur) continue;
            if (p.WeaponSlot == i || p.ArmorSlot == i || p.HelmetSlot == i || p.ShieldSlot == i) continue;
            slots.Add(i);
        }
        return slots;
    }

    /// <summary>How much more of <paramref name="itemNum"/> the player's existing stack can take. A stack
    /// counts in a plain int, and reagents cost a single gold each, so a deep purse is the one thing that
    /// could run one past its end.</summary>
    private static int StackHeadroom(PlayerRecord p, int itemNum)
    {
        for (int i = 1; i <= Constants.MaxInv; i++)
            if (p.Inv[i].Num == itemNum) return int.MaxValue - p.Inv[i].Quantity;
        return int.MaxValue;
    }

    /// <summary>Sell one inventory slot to the open shop for
    /// <see cref="EconomyFormulas.ItemSellValue"/> — a quarter of the item's value, scaled by condition.
    ///
    /// <para><b>A zero-gold sale still goes through.</b> A broken piece, or anything the pricing model
    /// values at nothing, is bought for nothing rather than refused: the vendor doubles as the way to
    /// empty a bag, and a slot you cannot clear is worse than a slot that clears for free. What is
    /// refused is <see cref="ItemRecord.NonJunkable"/> — gold, valor and treasure — because those either
    /// are the currency or are meant to reach a specific buyer through the barter table.</para></summary>
    public void Sell(int index, int invSlot, int quantity)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;

        var p = _pm[index].Char;
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum <= 0 || _world.Shops[shopNum].ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearShop, GameColor.BrightRed);
            return;
        }

        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoItemInSlot, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.NonJunkable)
        {
            SendMsg(index, ServerStrings.ShopSystem_CannotSell, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        // Selling gear off your own back would silently unequip it; make the player take it off first,
        // the same rule the bank deposit uses.
        if (p.WeaponSlot == invSlot || p.ArmorSlot == invSlot || p.HelmetSlot == invSlot || p.ShieldSlot == invSlot)
        {
            SendMsg(index, ServerStrings.ShopSystem_UnequipFirst, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        // Currency-style stacks sell by amount (0 or an oversized ask means the whole stack, matching
        // RemoveFromSlot). Everything else sells by the SLOT, and only alongside copies that are identical
        // to it — same item, same durability — because ItemSellValue prices on condition and a bulk sale
        // that mixed conditions would either misprice the pile or quietly hand over the good one. Grouping
        // on durability is also what lets worn gear be sold in bulk at all: five helmets straight off the
        // same drop table sell together, a battered one keeps its own price.
        bool stacks = item.Type == ItemType.Currency;
        var group = stacks ? [] : IdenticalSaleableSlots(p, invSlot, itemNum);
        int have = stacks ? Math.Max(p.Inv[invSlot].Quantity, 1) : group.Count;
        int amount = quantity <= 0 || quantity > have ? have : quantity;

        var spell = item.Type == ItemType.Spell && item.SpellNum > 0 && item.SpellNum <= _world.Limits.Spells
            ? _world.Spells[item.SpellNum] : null;
        long gold = (long)EconomyFormulas.ItemSellValue(item, p.Inv[invSlot].Dur, spell) * amount;

        if (stacks) _items.TakeItem(index, itemNum, amount);
        else for (int i = 0; i < amount; i++) _items.RemoveFromSlot(index, group[i], 0);
        if (gold > 0) _items.GiveItem(index, Constants.GoldItemIndex, (int)Math.Min(gold, int.MaxValue));

        if (gold > 0 && amount > 1)
            SendMsg(index, ServerStrings.ShopSystem_SoldMany, GameColor.Yellow,
                ("Amount", amount), ("ItemName", item.TrimmedName), ("Gold", gold));
        else if (gold > 0)
            SendMsg(index, ServerStrings.ShopSystem_Sold, GameColor.Yellow,
                ("ItemName", item.TrimmedName), ("Gold", gold));
        else
            SendMsg(index, ServerStrings.ShopSystem_SoldForNothing, GameColor.Gray, ("ItemName", item.TrimmedName));
    }

    // ── Fix item ──────────────────────────────────────────────────────────────

    /// <summary>Repair one inventory slot at a repair-capable Store, charging by the durability points
    /// actually restored.
    /// <para>A player who can't afford a full repair gets a PARTIAL one — as many points as their gold
    /// covers — rather than a refusal; only being unable to afford a single point is rejected.</para></summary>
    public void FixItem(int index, int invSlot)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;

        var p = _pm[index].Char;
        int shopNum = _pm[index].ActiveShop(_world, index);

        if (shopNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearRepairShop, GameColor.BrightRed);
            return;
        }

        var shop = _world.Shops[shopNum];
        if (shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotNearRepairShop, GameColor.BrightRed);
            return;
        }

        if (!shop.FixesItems)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoRepairType, GameColor.BrightRed);
            return;
        }

        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoItemInSlot, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.Type is not (ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield))
        {
            SendMsg(index, ServerStrings.ShopSystem_CannotRepair, GameColor.BrightRed);
            return;
        }

        int durNeeded = item.Durability - p.Inv[invSlot].Dur;
        if (durNeeded <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_PerfectCond, GameColor.White);
            return;
        }

        // Cost per durability point + total for a full repair — the shared repair formula (also used by the
        // guild-war vault-repair sink, so both price durability the same way).
        int goldNeeded = EconomyFormulas.RepairCost(durNeeded, item);

        long playerGold = ItemSystem.CountItem(p, _world.Items, Constants.GoldItemIndex);

        // How many points the purse actually covers. Asked exactly rather than as gold/ratePerPoint —
        // the rate is a floored display figure, and dividing by it can name a point count that costs a
        // gold more than the player has.
        int affordable = Math.Min(EconomyFormulas.RepairPointsAffordable(playerGold, item), durNeeded);
        if (affordable <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_InsufficientGold, GameColor.BrightRed);
            return;
        }

        if (playerGold >= goldNeeded)
        {
            _items.TakeItem(index, Constants.GoldItemIndex, goldNeeded);
            p.Inv[invSlot].Dur = item.Durability;
            _dispatcher.SendTo(index, new InventoryUpdatePacket
            {
                Slot = invSlot,
                Num = p.Inv[invSlot].Num,
                Quantity = p.Inv[invSlot].Quantity,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_FullyRestored, GameColor.BrightBlue, ("Gold", goldNeeded));
        }
        else
        {
            // Partial repair: restore as many durability points as the player can afford
            int durPartial = affordable;
            int goldActual = EconomyFormulas.RepairCost(durPartial, item);
            _items.TakeItem(index, Constants.GoldItemIndex, goldActual);
            p.Inv[invSlot].Dur += durPartial;
            _dispatcher.SendTo(index, new InventoryUpdatePacket
            {
                Slot = invSlot,
                Num = p.Inv[invSlot].Num,
                Quantity = p.Inv[invSlot].Quantity,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_PartiallyFixed, GameColor.BrightBlue, ("Gold", goldActual));
        }
    }
}

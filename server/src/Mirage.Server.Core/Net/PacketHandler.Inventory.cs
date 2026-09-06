using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>Items, stat training, and the bank and shop counters — every packet that moves an item
/// between the player, the ground, a vault, or a vendor.</summary>
public sealed partial class PacketHandler
{
    //  Item handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleUseItem(int index, UseItemPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't use items (potions, equip, scrolls)
        if (!SlotValidation.IsValidInvSlot(p.Slot))
        {
            HackingAttempt(index, "Invalid InvNum");
            return;
        }
        _items.UseItem(index, p.Slot);
    }

    private void HandleMapGetItem(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't pick up items
        _items.PlayerMapGetItem(index);
    }

    /// <summary>Tile menu → pick up one named item, possibly from a few tiles away. The same
    /// alive-and-playing gate as the pick-up key; ItemSystem owns the reach check, because reach is
    /// world geometry rather than a protocol concern.</summary>
    private void HandleMapPickUp(int index, MapPickUpPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;
        _items.PlayerMapPickUpAt(index, p.MapNum, p.Slot);
    }

    /// <summary>Tile menu → pick up everything claimable on one tile.</summary>
    private void HandleMapPickUpAll(int index, MapPickUpAllPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;
        _items.PlayerMapPickUpAllAt(index, p.MapNum, p.X, p.Y, p.Layer);
    }

    private void HandleSortInventory(int index)
    {
        if (!IsActing(index)) return;
        _items.SortInventory(index);
    }

    private void HandleMapDropItem(int index, MapDropItemPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't drop items
        if (!SlotValidation.IsValidInvSlot(p.Slot))
        {
            HackingAttempt(index, "Invalid InvNum");
            return;
        }

        var chr = _pm[index].Char;
        if (p.Quantity > chr.Inv[p.Slot].Quantity)
        {
            HackingAttempt(index, "Item quantity modification");
            return;
        }

        int itemNum = chr.Inv[p.Slot].Num;
        if (itemNum > 0 && _world.Items[itemNum].Type == ItemType.Currency && p.Quantity <= 0)
        {
            HackingAttempt(index, "Trying to drop 0 quantity of currency");
            return;
        }

        _items.PlayerMapDropItem(index, p.Slot, p.Quantity);
    }

    private void HandleMapDropBulk(int index, MapDropBulkPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't drop items
        if (p.ItemNum <= 0 || p.ItemNum > _world.Limits.Items)
        {
            HackingAttempt(index, "Invalid MapDropBulk ItemNum");
            return;
        }
        if (p.Quantity < 0)
        {
            HackingAttempt(index, "Negative MapDropBulk Quantity");
            return;
        }
        _items.PlayerMapDropBulk(index, p.ItemNum, p.Quantity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Stats handler
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleTrainStats(int index, TrainStatsPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't train stats
        if (p.Str < 0 || p.Def < 0 || p.Int < 0 || p.Spd < 0)
        {
            HackingAttempt(index, "Invalid Stat Train");
            return;
        }

        int total = p.Str + p.Def + p.Int + p.Spd;
        if (total <= 0) return;  // nothing staged

        var chr = _pm[index].Char;
        if (total > chr.Points)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_NoStatPoints, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        chr.Str += p.Str;
        chr.Def += p.Def;
        chr.Int += p.Int;
        chr.Spd += p.Spd;
        chr.Points -= total;

        // One flavor line per stat TYPE increased (not per point) — dumping 3 points into STR is a single
        // "You feel stronger."; training STR+INT sends the STR line and the INT line.
        if (p.Str > 0) _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_GainedStr, new ChatMetadata(GameColor.White, ChatChannel.System));
        if (p.Def > 0) _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_GainedDef, new ChatMetadata(GameColor.White, ChatChannel.System));
        if (p.Int > 0) _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_GainedInt, new ChatMetadata(GameColor.White, ChatChannel.System));
        if (p.Spd > 0) _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_GainedSpd, new ChatMetadata(GameColor.White, ChatChannel.System));

        StatFormulas.RefreshPlayerMaxVitals(chr, _world.Classes[chr.Class], _world.WeatherOn(chr.Map));
        chr.Hp = Math.Min(chr.Hp, chr.MaxHp);
        chr.Mp = Math.Min(chr.Mp, chr.MaxMp);
        chr.Sp = Math.Min(chr.Sp, chr.MaxSp);
        _dispatcher.SendTo(index, PacketBuilder.SendHp(index, chr.Hp, chr.MaxHp));
        SendToMap(chr.Map, PacketBuilder.SendMp(index, chr.Mp, chr.MaxMp));
        SendToMap(chr.Map, PacketBuilder.SendSp(index, chr.Sp, chr.MaxSp));
        _dispatcher.SendTo(index, PacketBuilder.SendStats(chr));
        _quests.RefreshEligibility(index);   // new stats may newly meet a quest's accept requirements → relight "?"
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Bank handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleBankOpen(int index)
    {
        if (!IsActing(index)) return;
        _bank.OpenBank(index);
    }

    private void HandleBankDeposit(int index, BankDepositPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't use the bank
        if (!SlotValidation.IsValidInvSlot(p.InvSlot))
        {
            HackingAttempt(index, "Invalid BankDeposit InvSlot");
            return;
        }
        if (p.Quantity < 0)
        {
            HackingAttempt(index, "Negative BankDeposit Quantity");
            return;
        }
        _bank.Deposit(index, p.InvSlot, p.Quantity);
    }

    private void HandleBankWithdraw(int index, BankWithdrawPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't use the bank
        if (!SlotValidation.IsValidBankSlot(p.BankSlot))
        {
            HackingAttempt(index, "Invalid BankWithdraw BankSlot");
            return;
        }
        if (p.Quantity < 0)
        {
            HackingAttempt(index, "Negative BankWithdraw Quantity");
            return;
        }
        _bank.Withdraw(index, p.BankSlot, p.Quantity);
    }

    private void HandleBankDepositBulk(int index, BankDepositBulkPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't use the bank
        if (p.ItemNum <= 0 || p.ItemNum > _world.Limits.Items)
        {
            HackingAttempt(index, "Invalid BankDepositBulk ItemNum");
            return;
        }
        if (p.Quantity < 0)
        {
            HackingAttempt(index, "Negative BankDepositBulk Quantity");
            return;
        }
        _bank.DepositBulk(index, p.ItemNum, p.Quantity);
    }

    private void HandleBankWithdrawBulk(int index, BankWithdrawBulkPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't use the bank
        if (p.ItemNum <= 0 || p.ItemNum > _world.Limits.Items)
        {
            HackingAttempt(index, "Invalid BankWithdrawBulk ItemNum");
            return;
        }
        if (p.Quantity < 0)
        {
            HackingAttempt(index, "Negative BankWithdrawBulk Quantity");
            return;
        }
        _bank.WithdrawBulk(index, p.ItemNum, p.Quantity);
    }

    private void HandleBankSort(int index)
    {
        if (!IsActing(index)) return;
        _bank.SortBank(index);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Shop handlers
    // ═══════════════════════════════════════════════════════════════════════════

    // Build + send a Store's barter table and sales list to `index`. On the client this sets ActiveShopNum, which opens the
    // ShopPanel. The single store-open path, called by OpenNpcShop.
    private void SendShopContents(int index, int shopNum, ShopRecord shop)
    {
        // Send the shop's barter rows in list order; the client's display index maps 1:1 to the slot it sends
        // back (slot = index + 1), which ShopSystem.Trade resolves as BarterItem[slot - 1].
        var trades = shop.BarterItem
            .Select(t => new ShopContentsPacket.BarterRow(t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
            .ToArray();
        // The sales list rides along as bare item numbers — the client already has every item definition,
        // so it can price and label the shopfront without a row per entry.
        _dispatcher.SendTo(index, new ShopContentsPacket
        {
            ShopNum = shopNum,
            Barters = trades,
            Sales = shop.SalesItem.ToArray(),
        });
    }

    // Resolve the map NPC at (map, slot) a player is interacting with, enforcing map visibility + the r=5 range
    // gate (cross-map-aware — mirrors the spell proximity check). Returns false (npcNum=0) if the slot is
    // empty, off-view, or out of range. Authoritative backstop: a modified client can't interact from afar.
    // Shared by the NPC-interact spine and the quest accept/turn-in proximity checks;
    // the world-geometry core lives on GameWorld so the active-shop re-validation reuses the same gate.
    private bool TryResolveInteractNpc(int index, int mapNum, int npcSlot, out int npcNum)
        => _world.IsNpcInInteractRange(index, _pm[index].Char, mapNum, npcSlot, out npcNum);

    // NPC-interaction spine: a player interacted with a map NPC
    // — via the melee attack key (Choice.Auto), or a right-click context-menu item within r=5. Choice.Shop /
    // .Talk / .Quest each FORCE one role (the context-menu items, and a conversation's terminal hand-off choices),
    // so a forced open can't loop into a different menu. Choice.Auto is TALK-FIRST: a conversation if the NPC has
    // one, else the client quest/context menu if it has an actionable quest for this player, else its keeper shop.
    private void HandleNpcInteract(int index, NpcInteractPacket p)
    {
        if (!IsActing(index)) return;
        if (!TryResolveInteractNpc(index, p.MapNum, p.NpcSlot, out int npcNum)) return;
        switch (p.Choice)
        {
            case NpcInteractChoice.Shop:
                OpenNpcShop(index, npcNum, p.MapNum, p.NpcSlot);
                return;
            case NpcInteractChoice.Talk:
                OpenNpcConversation(index, npcNum, p.MapNum, p.NpcSlot);
                return;
            case NpcInteractChoice.Quest:
                if (_quests.HasActionableQuestAt(index, npcNum))
                {
                    _pm[index].SetActiveQuestNpc(p.MapNum, p.NpcSlot);
                    _dispatcher.SendTo(index, new OpenNpcQuestMenuPacket { MapNum = p.MapNum, NpcSlot = p.NpcSlot });
                }
                return;
            // The player picked one quest by name and their client has the offer open. Same claim, no menu
            // pushed back — accept and turn-in read the NPC from the claim, so an offer opened without one
            // is refused.
            case NpcInteractChoice.QuestOffer:
                if (_quests.HasActionableQuestAt(index, npcNum))
                    _pm[index].SetActiveQuestNpc(p.MapNum, p.NpcSlot);
                return;
            default:   // Auto — talk-first, then a quest the player can accept or turn in right now, then the keeper
                       // shop; if none apply, the NPC at least speaks its AttackSay rather than doing nothing.
                if (_world.ConversationForNpc(npcNum) > 0)
                    OpenNpcConversation(index, npcNum, p.MapNum, p.NpcSlot);
                else if (_quests.HasActionableQuestAt(index, npcNum))
                {
                    _pm[index].SetActiveQuestNpc(p.MapNum, p.NpcSlot);
                    _dispatcher.SendTo(index, new OpenNpcQuestMenuPacket { MapNum = p.MapNum, NpcSlot = p.NpcSlot });
                }
                else if (!OpenNpcShop(index, npcNum, p.MapNum, p.NpcSlot))
                    _combat.SpeakAttackSayTo(index, p.MapNum, p.NpcSlot, _world.Npcs[npcNum]);
                return;
        }
    }

    // Open the NPC's conversation (dialogue tree) for `index`, if it has one (ConversationRecord.SpeakerNpc). Marks
    // it spoken — the visited-log that flips the overhead "..." glyph yellow → gray — and sends the trigger; the
    // client opens the panel and walks the cached tree locally, round-tripping only for a hand-off choice. No-op if
    // the NPC has no conversation.
    private void OpenNpcConversation(int index, int npcNum, int mapNum, int npcSlot)
    {
        int convNum = _world.ConversationForNpc(npcNum);
        if (convNum <= 0) return;
        _conversations.MarkSpoken(index, convNum);
        _dispatcher.SendTo(index, new OpenNpcConversationPacket { MapNum = mapNum, NpcSlot = npcSlot, ConvNum = convNum });
    }

    // Open the shop/inn assigned to NPC template `npcNum` (ShopRecord.Keeper) for `index`, if any. Records the
    // active shop (shop# + this keeper's map/slot) so follow-up ops re-validate r=5 of the keeper. Store → the
    // shop panel; Inn → the client-local inn panel (carrying the shop# so it resolves banking/market/
    // set-spawn against this keeper's inn from anywhere). No-op if the NPC keeps no shop.
    private bool OpenNpcShop(int index, int npcNum, int keeperMap, int keeperSlot)
    {
        int shopNum = _world.ShopAssignedToNpc(npcNum);
        if (shopNum <= 0) return false;
        var shop = _world.Shops[shopNum];
        _pm[index].SetActiveShop(shopNum, keeperMap, keeperSlot);
        if (shop.ShopType == ShopType.Store)
            SendShopContents(index, shopNum, shop);
        else
            _dispatcher.SendTo(index, new OpenInnPacket { ShopNum = shopNum });
        return true;
    }

    private void HandleShopBarter(int index, ShopBarterPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't barter at a shop
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum > 0)
            _shop.Barter(index, shopNum, p.BarterSlot, p.Multiples);
        else
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_NotNearShop, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
    }

    private void HandleShopBuy(int index, ShopBuyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't shop
        // Resolve the shop from the SERVER's active-shop record, never from the packet: the client's
        // shopNum is a display hint, and trusting it would let a modified client buy from any shop in
        // the world. Same rule HandleShopBarter follows.
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum > 0)
            _shop.Buy(index, shopNum, p.SalesSlot, p.Quantity);
        else
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_NotNearShop, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
    }

    private void HandleShopSell(int index, ShopSellPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't shop
        _shop.Sell(index, p.InvSlot, p.Quantity);
    }

    private void HandleFixItem(int index, FixItemPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't repair at a shop
        _shop.FixItem(index, p.InvSlot);
    }
}

using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
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
        if (_pm[index].Char.Downed) return;  // a corpse can't use items (potions, equip, scrolls)
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
        if (_pm[index].Char.Downed) return;  // a corpse can't pick up items
        _items.PlayerMapGetItem(index);
    }

    /// <summary>Tile menu → pick up one named item, possibly from a few tiles away. The same
    /// alive-and-playing gate as the pick-up key; ItemSystem owns the reach check, because reach is
    /// world geometry rather than a protocol concern.</summary>
    private void HandleMapPickUp(int index, MapPickUpPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Downed) return;
        _items.PlayerMapPickUpAt(index, p.MapNum, p.Slot);
    }

    /// <summary>Tile menu → pick up everything claimable on one tile.</summary>
    private void HandleMapPickUpAll(int index, MapPickUpAllPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Downed) return;
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
        if (_pm[index].Char.Downed) return;  // a corpse can't drop items
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
        if (_pm[index].Char.Downed) return;  // a corpse can't drop items
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
        if (_pm[index].Char.Downed) return;  // a corpse can't use the bank
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
        if (_pm[index].Char.Downed) return;  // a corpse can't use the bank
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
        if (_pm[index].Char.Downed) return;  // a corpse can't use the bank
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
        if (_pm[index].Char.Downed) return;  // a corpse can't use the bank
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
            default:   // Auto — talk-first, then the keeper shop; if neither applies, the body at least
                       // speaks its own line rather than doing nothing. That is the second of the two
                       // occasions NpcRecord.Says covers.
                if (_world.ConversationForNpc(npcNum) > 0)
                    OpenNpcConversation(index, npcNum, p.MapNum, p.NpcSlot);
                else OpenNpcShop(index, npcNum, p.MapNum, p.NpcSlot);
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
        if (_pm[index].Char.Downed) return;  // a corpse can't barter at a shop
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum > 0)
            _shop.Barter(index, shopNum, p.BarterSlot, p.Multiples);
        else
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_NotNearShop, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
    }

    private void HandleShopBuy(int index, ShopBuyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Downed) return;  // a corpse can't shop
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
        if (_pm[index].Char.Downed) return;  // a corpse can't shop
        _shop.Sell(index, p.InvSlot, p.Quantity);
    }

    private void HandleFixItem(int index, FixItemPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Downed) return;  // a corpse can't repair at a shop
        _shop.FixItem(index, p.InvSlot);
    }

    /// <summary>The player picked one of the game's own actions.
    ///
    /// <para><b>Core checks that somebody declared it, and nothing else.</b> Whether the player is close
    /// enough, whether they are allowed, whether the square is the right kind — every one of those is a
    /// question about a verb Core has no name for, so every one of them belongs to the game. What is
    /// checked here is the part Core does know: that the sender is in the world, and that the id names
    /// something this server was told about.</para></summary>
    private void HandleInvokeAction(int index, InvokeActionPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        Invoke(index, p.Action, p.MapNum, p.X, p.Y, p.Picked, ActionTargetOf(p));
    }

    /// <summary>Run a declared verb for this player, on a place and optionally on a body.
    ///
    /// <para>Shared by the menu, a bound key, a button on one of the game’s own panels, and the action
    /// bar. Every one of those is the same verb reaching the same rule, so every one of them has to ask
    /// the same questions first — a second copy of this is a second place for the condition check to be
    /// forgotten.</para></summary>
    private void Invoke(int index, string actionId, int mapNum, int x, int y,
                        string picked, EntityHandle on = default)
    {
        var handler = _actionHandlers.FirstOrDefault(h => h.Actions.Contains(actionId, StringComparer.Ordinal));
        if (handler is null) return;   // an id nothing declared: a stale client, or a game that changed

        // 🔴 The verb's own condition is asked HERE as well as on the client. One the client alone
        // enforced would be a rule any modified client could ignore, and the game would never learn its
        // own declaration had been skipped. Both ends call the same Holds.
        // An id a handler owns but nothing DECLARED is refused rather than run: no client could have
        // offered it, so the only way it arrives is a packet somebody wrote by hand.
        var declared = _declaredActions.All
            .FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal));
        if (declared is null || !declared.When.Holds(_pm[index].Char.Attributes)) return;

        try
        {
            // What the player has SELECTED, for a verb that asked to be aimed and was used without
            // pointing at anything - a key press, a button on one of the game's own screens, a hotkey.
            if (!on.IsSet && declared.Aimed) on = Selected(index);

            handler.Invoke(EntityHandle.ForPlayer(index), actionId, on,
                           new WorldPlace(mapNum, x, y), picked);
        }
        catch (Exception ex)
        {
            // A game's bug loses its own action, not the player holding it.
            _logger.LogError(ex, "Action handler {Handler} failed on {Action} for index {Index}",
                             handler.Name, actionId, index);
        }
    }

    /// <summary>Whoever this player has picked out - with Tab, with Ctrl+Tab, or by clicking them.
    ///
    /// <para>Read from this server's own record of the selection rather than from the packet, so an
    /// aimed verb lands on the body the player actually chose. Self is an ordinary selection here and
    /// not a special case, so casting on yourself needs nothing of its own.</para>
    ///
    /// <para>A creature is named by where it SPAWNS for the same reason it is everywhere else: a body
    /// that has wandered onto the next map is still itself.</para></summary>
    private EntityHandle Selected(int index)
    {
        var sp = _pm[index];

        // 0 player, 1 creature, 2 self, 3 a creature that is passing through.
        switch (sp.TargetType)
        {
            case 2:
                return EntityHandle.ForPlayer(index);

            case 0 when sp.Target > 0 && sp.Target <= _pm.Slots && _pm[sp.Target].IsPlaying:
                return EntityHandle.ForPlayer(sp.Target);

            case 1 when sp.TargetMap > 0 && sp.TargetMap <= _world.Limits.Maps
                        && sp.Target >= 1 && sp.Target <= Constants.MaxMapNpcs:
            {
                var body = _world.MapNpcs[sp.TargetMap, sp.Target];
                if (body.Num <= 0) return EntityHandle.None;
                var (spawnMap, spawnSlot) = body.GetSpawnIdentity(sp.TargetMap, sp.Target);
                return EntityHandle.ForNpc(spawnMap, spawnSlot);
            }

            case 3 when sp.TargetSpawnMap > 0:
                return EntityHandle.ForNpc(sp.TargetSpawnMap, sp.TargetSpawnSlot);

            default:
                return EntityHandle.None;
        }
    }

    /// <summary>What the verb was used ON, as a handle, or nobody.
    ///
    /// <para>The client names a player by name and an NPC by the slot it OCCUPIES on the map it named.
    /// Both become an <see cref="EntityHandle"/> here, so a game never sees either.</para>
    ///
    /// <para>🔴 The occupied slot is not the identity. A body that has wandered onto the next map
    /// stands in a slot belonging to that map while its own is reserved, so the slot is asked for its
    /// spawn identity rather than used as one — otherwise a verb used on a visitor names whichever
    /// native happens to own that number at home.</para>
    ///
    /// <para>A name or slot naming nobody yields <see cref="EntityHandle.None"/> rather than a refusal:
    /// a target that walked away between the click and the read is an ordinary thing to happen, and what
    /// the verb should do about it is the game's answer.</para></summary>
    private EntityHandle ActionTargetOf(InvokeActionPacket p)
    {
        if (p.TargetName.Length > 0)
        {
            int target = _pm.FindPlayerByName(p.TargetName);
            return target > 0 ? EntityHandle.ForPlayer(target) : EntityHandle.None;
        }

        if (p.NpcSlot < 1 || p.NpcSlot > Constants.MaxMapNpcs) return EntityHandle.None;
        if (p.MapNum < 1 || p.MapNum > _world.Limits.Maps) return EntityHandle.None;

        var body = _world.MapNpcs[p.MapNum, p.NpcSlot];
        if (body.Num <= 0) return EntityHandle.None;

        var (spawnMap, spawnSlot) = body.GetSpawnIdentity(p.MapNum, p.NpcSlot);
        return EntityHandle.ForNpc(spawnMap, spawnSlot);
    }
}

using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class BankSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;

    public BankSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _items = items;
    }

    // ── Open bank ─────────────────────────────────────────────────────────────

    public void OpenBank(int index)
    {
        if (!_pm[index].IsPlaying) return;
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum <= 0 || _world.Shops[shopNum].ShopType != ShopType.Inn || !_world.Shops[shopNum].AllowBanking)
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }
        SendFullBank(index);
    }

    // ── Deposit (inventory → bank) ────────────────────────────────────────────

    public void Deposit(int index, int invSlot, int amount)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;
        if (!IsAtBankingInn(index))
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }

        var sp = _pm[index];
        var p = sp.Char;
        var bank = sp.Bank;
        var inv = p.Inv[invSlot];
        if (inv.Num <= 0 || inv.Num > _world.Limits.Items) return;

        var item = _world.Items[inv.Num];
        int bankSlot;

        if (item.Type == ItemType.Currency)
        {
            int depositAmt = (amount <= 0 || amount > inv.Quantity) ? inv.Quantity : amount;
            bankSlot = FindOpenBankSlot(bank, inv.Num, isCurrency: true);
            if (bankSlot == 0)
            {
                SendMsg(index, ServerStrings.BankSystem_BankFull, GameColor.BrightRed);
                return;
            }

            bank[bankSlot].Num = inv.Num;
            bank[bankSlot].AddQuantity(depositAmt);

            if (depositAmt >= inv.Quantity)
            {
                p.Inv[invSlot].Num = 0;
                p.Inv[invSlot].Quantity = 0;
                p.Inv[invSlot].Dur = 0;
            }
            else
            {
                p.Inv[invSlot].Quantity -= depositAmt;
            }
        }
        else
        {
            bool isEquipped =
                (item.Type == ItemType.Weapon && p.WeaponSlot == invSlot) ||
                (item.Type == ItemType.Armor && p.ArmorSlot == invSlot) ||
                (item.Type == ItemType.Helmet && p.HelmetSlot == invSlot) ||
                (item.Type == ItemType.Shield && p.ShieldSlot == invSlot);
            if (isEquipped)
            {
                SendMsg(index, ServerStrings.BankSystem_UnequipFirst, GameColor.BrightRed);
                return;
            }

            bankSlot = FindOpenBankSlot(bank, inv.Num, isCurrency: false);
            if (bankSlot == 0)
            {
                SendMsg(index, ServerStrings.BankSystem_BankFull, GameColor.BrightRed);
                return;
            }

            bank[bankSlot].Num = inv.Num;
            bank[bankSlot].Quantity = 0;
            bank[bankSlot].Dur = inv.Dur;

            p.Inv[invSlot].Num = 0;
            p.Inv[invSlot].Quantity = 0;
            p.Inv[invSlot].Dur = 0;
        }

        _items.SendInventoryUpdate(index, invSlot);
        SendBankSlotUpdate(index, bankSlot);
    }

    // ── Withdraw (bank → inventory) ───────────────────────────────────────────

    public void Withdraw(int index, int bankSlot, int amount)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidBankSlot(bankSlot)) return;
        if (!IsAtBankingInn(index))
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }

        var sp = _pm[index];
        var p = sp.Char;
        var bank = sp.Bank[bankSlot];
        if (bank.Num <= 0 || bank.Num > _world.Limits.Items) return;

        var item = _world.Items[bank.Num];
        int invSlot = ItemSystem.FindOpenInvSlot(p, _world.Items, bank.Num);
        if (invSlot == 0)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        if (item.Type == ItemType.Currency)
        {
            int withdrawAmt = (amount <= 0 || amount > bank.Quantity) ? bank.Quantity : amount;
            p.Inv[invSlot].Num = bank.Num;
            p.Inv[invSlot].AddQuantity(withdrawAmt);

            if (withdrawAmt >= bank.Quantity)
            {
                sp.Bank[bankSlot].Num = 0;
                sp.Bank[bankSlot].Quantity = 0;
            }
            else
            {
                sp.Bank[bankSlot].Quantity -= withdrawAmt;
            }
        }
        else
        {
            p.Inv[invSlot].Num = bank.Num;
            p.Inv[invSlot].Quantity = bank.Quantity;
            p.Inv[invSlot].Dur = bank.Dur;

            sp.Bank[bankSlot].Num = 0;
            sp.Bank[bankSlot].Quantity = 0;
            sp.Bank[bankSlot].Dur = 0;
        }

        _items.SendInventoryUpdate(index, invSlot);
        SendBankSlotUpdate(index, bankSlot);
    }

    // ── Bulk deposit (inventory → bank, by ItemNum) ──────────────────────────

    /// <summary>Right-click "Deposit 1 / X / All": move up to <paramref name="amount"/> matching
    /// non-currency items from inventory into the bank. Equipped slots are skipped (the user
    /// must explicitly unequip to bank gear). Destination capacity is clamped silently; if any
    /// items requested couldn't fit, sends a localized "partial" message. Currency uses the
    /// per-slot <see cref="Deposit"/> entrypoint instead.</summary>
    public void DepositBulk(int index, int itemNum, int amount)
    {
        if (!_pm[index].IsPlaying) return;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;
        if (amount < 0) return;
        if (!IsAtBankingInn(index))
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.Type == ItemType.Currency) return;

        var sp = _pm[index];
        var p = sp.Char;
        var bank = sp.Bank;
        int requested = CountMatchingInvSlots(p, itemNum, skipEquipped: true);
        if (requested == 0) return;
        if (amount > 0 && amount < requested) requested = amount;

        int moved = 0;
        for (int invSlot = 1; invSlot <= Constants.MaxInv && moved < requested; invSlot++)
        {
            if (p.Inv[invSlot].Num != itemNum) continue;
            if (IsInvSlotEquipped(p, invSlot, item.Type)) continue;

            int bankSlot = FindOpenBankSlot(bank, itemNum, isCurrency: false);
            if (bankSlot == 0) break;

            bank[bankSlot].Num = itemNum;
            bank[bankSlot].Quantity = 0;
            bank[bankSlot].Dur = p.Inv[invSlot].Dur;

            p.Inv[invSlot].Num = 0;
            p.Inv[invSlot].Quantity = 0;
            p.Inv[invSlot].Dur = 0;

            _items.SendInventoryUpdate(index, invSlot);
            SendBankSlotUpdate(index, bankSlot);
            moved++;
        }

        if (moved == 0)
            SendMsg(index, ServerStrings.BankSystem_BankFull, GameColor.BrightRed);
        else if (moved < requested)
            SendMsg(index, ServerStrings.BankSystem_DepositPartial, GameColor.Yellow, ("Moved", moved), ("Requested", requested));
    }

    // ── Bulk withdraw (bank → inventory, by ItemNum) ─────────────────────────

    /// <summary>Right-click "Withdraw 1 / X / All": move up to <paramref name="amount"/> matching
    /// non-currency items from bank into the inventory. Inventory capacity is clamped silently;
    /// partial moves emit a localized message. Currency uses <see cref="Withdraw"/>.</summary>
    public void WithdrawBulk(int index, int itemNum, int amount)
    {
        if (!_pm[index].IsPlaying) return;
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;
        if (amount < 0) return;
        if (!IsAtBankingInn(index))
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.Type == ItemType.Currency) return;

        var sp = _pm[index];
        var p = sp.Char;
        var bank = sp.Bank;
        int requested = CountMatchingBankSlots(bank, itemNum);
        if (requested == 0) return;
        if (amount > 0 && amount < requested) requested = amount;

        int moved = 0;
        for (int bankSlot = 1; bankSlot <= Constants.MaxBankSlots && moved < requested; bankSlot++)
        {
            if (bank[bankSlot].Num != itemNum) continue;

            int invSlot = ItemSystem.FindOpenInvSlot(p, _world.Items, itemNum);
            if (invSlot == 0) break;

            p.Inv[invSlot].Num = itemNum;
            p.Inv[invSlot].Quantity = bank[bankSlot].Quantity;
            p.Inv[invSlot].Dur = bank[bankSlot].Dur;

            bank[bankSlot].Num = 0;
            bank[bankSlot].Quantity = 0;
            bank[bankSlot].Dur = 0;

            _items.SendInventoryUpdate(index, invSlot);
            SendBankSlotUpdate(index, bankSlot);
            moved++;
        }

        if (moved == 0)
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
        else if (moved < requested)
            SendMsg(index, ServerStrings.BankSystem_WithdrawPartial, GameColor.Yellow, ("Moved", moved), ("Requested", requested));
    }

    // ── Sort bank ─────────────────────────────────────────────────────────────

    /// <summary>Tidy the account bank into the canonical order and resync - the bank counterpart of
    /// <see cref="ItemSystem.SortInventory"/>, sharing its exact ordering (<see cref="ItemSystem.SortKey"/>):
    /// Gold, other currencies, gear (Weapon/Armor/Helmet/Shield, strongest first, then alpha), keys, spell
    /// scrolls, Add then Sub potions; empty slots fall to the tail. A bank never holds equipped gear
    /// (depositing worn gear is refused), so its gear leads the vault - right below currency, above
    /// keys/scrolls/potions. Reorders the slot objects in place and resends the full bank. Like every other
    /// bank operation it skips the eager dirty-flag (a pure reorder can't dupe), persisting on the next
    /// periodic/leave save (the bank rides the account save).</summary>
    public void SortBank(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (!IsAtBankingInn(index))
        {
            SendMsg(index, ServerStrings.BankSystem_NotNearBank, GameColor.BrightRed);
            return;
        }

        var bank = _pm[index].Bank;

        // Same tidy as the inventory, minus the equipped category: depositing worn gear is refused, so a
        // bank never holds any.
        ItemSystem.SortSlots(bank, Constants.MaxBankSlots, _world.Items, _ => false);

        SendFullBank(index);
    }

    private bool IsAtBankingInn(int index)
    {
        int shopNum = _pm[index].ActiveShop(_world, index);
        return shopNum > 0 && _world.Shops[shopNum].ShopType == ShopType.Inn && _world.Shops[shopNum].AllowBanking;
    }

    /// <summary>Put a stack in a vault, and nothing else — no player slot, no packet, no message. What
    /// "deposit" means to the RECORD, so the editor's account browser (which reaches vaults belonging to
    /// nobody who is logged in) cannot disagree with the counter about stacking or a full vault.
    /// <para>Returns the vault slot used, or 0 when it is full.</para></summary>
    public static int PlaceInBank(PlayerInvSlot[] bank, ItemRecord[] items, int itemNum, int value, int dur = 0)
    {
        if (itemNum <= 0 || itemNum >= items.Length) return 0;
        var item = items[itemNum];
        int slot = FindOpenBankSlot(bank, itemNum, item.Type == ItemType.Currency);
        if (slot == 0) return 0;

        bank[slot].Num = itemNum;
        bank[slot].AddQuantity(value);
        if (item.Type is ItemType.Armor or ItemType.Weapon or ItemType.Helmet or ItemType.Shield)
            bank[slot].Dur = dur > 0 ? dur : item.Durability;
        return slot;
    }

    /// <summary>Take a stack out of one vault slot. Currency honours <paramref name="amount"/> (0 or more
    /// than the pile = all of it); anything else goes whole. Nothing is worn out of a vault, so unlike the
    /// bag there is no gear pointer to clear.
    /// <para>Returns what came out; ItemNum 0 means the slot held nothing.</para></summary>
    public static (int ItemNum, int Quantity) TakeFromBank(PlayerInvSlot[] bank, ItemRecord[] items, int bankSlot, int amount)
    {
        if (bankSlot < 1 || bankSlot > Constants.MaxBankSlots) return (0, 0);
        var slot = bank[bankSlot];
        if (slot.Num <= 0 || slot.Num >= items.Length) return (0, 0);

        int itemNum = slot.Num;
        bool stacks = items[itemNum].Type == ItemType.Currency;
        int take = stacks && amount > 0 && amount < slot.Quantity ? amount : Math.Max(slot.Quantity, 1);

        if (stacks && take < slot.Quantity)
        {
            bank[bankSlot].Quantity -= take;
            return (itemNum, take);
        }

        bank[bankSlot].Num = 0;
        bank[bankSlot].Quantity = 0;
        bank[bankSlot].Dur = 0;
        return (itemNum, take);
    }

    private static int FindOpenBankSlot(PlayerInvSlot[] bank, int itemNum, bool isCurrency)
    {
        if (isCurrency)
        {
            for (int i = 1; i <= Constants.MaxBankSlots; i++)
                if (bank[i].Num == itemNum) return i;
        }
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
            if (bank[i].Num == 0) return i;
        return 0;
    }

    private static int CountMatchingInvSlots(PlayerRecord p, int itemNum, bool skipEquipped)
    {
        int count = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            if (p.Inv[i].Num != itemNum) continue;
            if (skipEquipped && (p.WeaponSlot == i || p.ArmorSlot == i || p.HelmetSlot == i || p.ShieldSlot == i)) continue;
            count++;
        }
        return count;
    }

    private static int CountMatchingBankSlots(PlayerInvSlot[] bank, int itemNum)
    {
        int count = 0;
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
            if (bank[i].Num == itemNum) count++;
        return count;
    }

    private static bool IsInvSlotEquipped(PlayerRecord p, int invSlot, ItemType type) => type switch
    {
        ItemType.Weapon => p.WeaponSlot == invSlot,
        ItemType.Armor => p.ArmorSlot == invSlot,
        ItemType.Helmet => p.HelmetSlot == invSlot,
        ItemType.Shield => p.ShieldSlot == invSlot,
        _ => false,
    };

    private void SendFullBank(int index)
    {
        var bank = _pm[index].Bank;
        var slots = new System.Collections.Generic.List<SendBankPacket.BankSlotData>();
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
        {
            var b = bank[i];
            if (b.Num > 0)
                slots.Add(new SendBankPacket.BankSlotData(i, b.Num, b.Quantity, b.Dur));
        }
        _dispatcher.SendTo(index, new SendBankPacket { Slots = slots.ToArray() });
    }

    private void SendBankSlotUpdate(int index, int slot)
    {
        if (slot <= 0 || slot > Constants.MaxBankSlots) return;
        var b = _pm[index].Bank[slot];
        _dispatcher.SendTo(index, new BankSlotUpdatePacket
        {
            Slot = slot,
            Num = b.Num,
            Quantity = b.Quantity,
            Dur = b.Dur
        });
    }
}

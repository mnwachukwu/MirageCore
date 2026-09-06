using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Shell.Logic;

/// <summary>Pure queries over the local player's inventory, shared by the inventory + bank panels (extracted
/// so the bulk-amount math isn't duplicated).</summary>
public static class InventoryQuery
{
    /// <summary>How many inventory slots hold item <paramref name="itemNum"/> — the max for a bulk drop/deposit
    /// prompt. When <paramref name="skipEquipped"/> is set, the four equipped slots don't count.</summary>
    public static int CountInvSlotsMatching(ClientState state, int itemNum, bool skipEquipped)
    {
        var me = state.Me;
        if (me?.Inv is null) return 0;
        int count = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var s = me.Inv[i];
            if (s is null || s.Num != itemNum) continue;
            if (skipEquipped && (me.WeaponSlot == i || me.ArmorSlot == i || me.HelmetSlot == i || me.ShieldSlot == i)) continue;
            count++;
        }
        return count;
    }

    /// <summary>How many more of <paramref name="itemNum"/> the bag could take — the client's copy of the
    /// server's room rule, so a prompt never offers an amount the server would refuse. A currency needs one
    /// slot however much is poured in, so an existing stack means there is always room.</summary>
    public static int RoomFor(ClientState state, int itemNum)
    {
        var me = state.Me;
        if (me?.Inv is null || itemNum <= 0 || itemNum >= state.Items.Length) return 0;
        bool stacks = state.Items[itemNum]?.Type == ItemType.Currency;
        int free = 0;
        for (int i = 1; i <= Constants.MaxInv && i < me.Inv.Length; i++)
        {
            if (stacks && me.Inv[i]?.Num == itemNum) return int.MaxValue;
            if ((me.Inv[i]?.Num ?? 0) == 0) free++;
        }
        return stacks ? (free > 0 ? int.MaxValue : 0) : free;
    }

    /// <summary>The bag slots holding a copy indistinguishable from the one at <paramref name="invSlot"/> —
    /// same item, same durability — with anything worn left out. Mirrors the rule the shop sells by, so the
    /// amount offered is the amount that will go.</summary>
    public static int IdenticalSaleableCount(ClientState state, int invSlot)
    {
        var me = state.Me;
        if (me?.Inv is null || invSlot < 1 || invSlot >= me.Inv.Length) return 0;
        var from = me.Inv[invSlot];
        if (from is null || from.Num <= 0) return 0;

        int count = 0;
        for (int i = 1; i <= Constants.MaxInv && i < me.Inv.Length; i++)
        {
            var s = me.Inv[i];
            if (s is null || s.Num != from.Num || s.Dur != from.Dur) continue;
            if (i != invSlot && (me.WeaponSlot == i || me.ArmorSlot == i || me.HelmetSlot == i || me.ShieldSlot == i)) continue;
            count++;
        }
        return count;
    }

    /// <summary>How many of <paramref name="itemNum"/> the bag holds, counted the way that item stacks: a
    /// currency carries its whole amount inside one slot, everything else spends a slot each. Equipped
    /// pieces don't count — a worn sword is not one you are holding.</summary>
    public static int HeldCount(ClientState state, int itemNum)
    {
        var me = state.Me;
        if (me?.Inv is null || itemNum <= 0 || itemNum >= state.Items.Length) return 0;
        if (state.Items[itemNum]?.Type != ItemType.Currency)
            return CountInvSlotsMatching(state, itemNum, skipEquipped: true);

        int total = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
            if (me.Inv[i]?.Num == itemNum) total += me.Inv[i]!.Quantity;
        return total;
    }
}

using Mirage.Client.Core.State;
using Mirage.Client.Shell.Localization;
using Mirage.Shared;
using Mirage.Shared.Records;
using System;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Ui;

/// <summary>Populates a <see cref="ListBox"/> from the local player's inventory. Two flavors:
/// <see cref="BuildDisplayRows"/> — the FULL inventory view (every slot, empties shown, currency stack size,
/// equipped/broken tags), shared by the inventory panel + the bank's inventory side; and <see cref="Rebuild"/> —
/// a FILTERED candidate list with a parallel slot-number list and preserved selection, shared by the trade,
/// market, and mail panels (each supplying its own per-item exclusion). Currency rows show their stack inline in
/// both.</summary>
public static class InventoryListBuilder
{
    /// <summary>Populates <paramref name="list"/> with the player's full inventory as display rows: every slot,
    /// empties as "{i}: empty", currency as "{i}: {name} ({value})", and equipped/broken tags on gear. Returns
    /// the number of filled slots.</summary>
    public static int BuildDisplayRows(ClientState state, ListBox list)
    {
        list.Items.Clear();
        int filled = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = state.Me?.Inv?[i];
            if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items)
            {
                list.Items.Add($"{i}: {ClientStrings.Get(ClientStrings.Common_Empty)}");
                continue;
            }
            filled++;
            var item = state.Items[slot.Num];
            string name = item?.Name?.TrimEnd() ?? $"Item {slot.Num}";
            // Currency shows its stack size inline since the tooltip is optional — money piles are read at a glance.
            if (item?.Type == ItemType.Currency)
            {
                list.Items.Add($"{i}: {name} ({slot.Quantity:N0})");
                continue;
            }
            bool equipped = state.Me != null &&
                (state.Me.WeaponSlot == i || state.Me.ArmorSlot == i ||
                 state.Me.HelmetSlot == i || state.Me.ShieldSlot == i);
            // A worn item at 0 durability sits in the bag, auto-unequipped and unusable until repaired; surface
            // that inline like the equipped flag. Broken and equipped are mutually exclusive so the tags don't collide.
            bool broken = !equipped && item is { Durability: > 0 } && slot.Dur <= 0
                && ItemRecord.IsEquipment(item.Type);
            list.Items.Add(equipped
                ? $"{i}: {name} {ClientStrings.Get(ClientStrings.Common_Equipped)}"
                : broken
                    ? $"{i}: {name} {ClientStrings.Get(ClientStrings.Common_Broken)}"
                    : $"{i}: {name}");
        }
        return filled;
    }

    /// <summary>Repopulates <paramref name="list"/> (and its parallel <paramref name="slots"/> list) with the
    /// player's non-empty, non-equipped inventory slots — <paramref name="exclude"/> drops the ones a panel can't
    /// offer (non-tradeable / non-listable / non-mailable / already-staged) — preserving the selection across the
    /// per-frame rebuild. Currency rows show their stack size inline.</summary>
    public static void Rebuild(ClientState state, ListBox list, List<int> slots, Func<int, ItemRecord?, bool> exclude)
    {
        int prevSlot = list.SelectedIndex >= 0 && list.SelectedIndex < slots.Count ? slots[list.SelectedIndex] : -1;
        list.Items.Clear();
        slots.Clear();
        var me = state.Me;
        if (me?.Inv is not null)
        {
            for (int i = 1; i <= Constants.MaxInv; i++)
            {
                var slot = me.Inv[i];
                if (slot is null || slot.Num <= 0 || slot.Num > state.Limits.Items) continue;
                if (me.WeaponSlot == i || me.ArmorSlot == i || me.HelmetSlot == i || me.ShieldSlot == i) continue;
                var item = state.Items[slot.Num];
                if (exclude(i, item)) continue;
                string name = item?.Name?.TrimEnd() ?? $"Item {slot.Num}";
                list.Items.Add(item?.Type == ItemType.Currency ? $"{name} ({slot.Quantity:N0})" : name);
                slots.Add(i);
            }
        }
        list.SelectedIndex = prevSlot >= 0 ? slots.IndexOf(prevSlot) : -1;
    }
}

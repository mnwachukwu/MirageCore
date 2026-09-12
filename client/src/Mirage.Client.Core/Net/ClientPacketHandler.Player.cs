using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>The local character: stats, vitals, inventory, equipped gear and spell list.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Player state ──────────────────────────────────────────────────────────

    private void HandleSendInventory(SendInventoryPacket p)
    {
        foreach (var slot in p.Slots)
        {
            if (!SlotValidation.IsValidInvSlot(slot.Slot)) continue;
            var inv = _state.Me.Inv[slot.Slot];
            inv.Num = slot.Num;
            inv.Quantity = slot.Quantity;
            inv.Dur = slot.Dur;
        }
        InventoryChanged?.Invoke();
    }

    private void HandleInventoryUpdate(InventoryUpdatePacket p)
    {
        if (!SlotValidation.IsValidInvSlot(p.Slot)) return;
        var inv = _state.Me.Inv[p.Slot];
        inv.Num = p.Num;
        inv.Quantity = p.Quantity;
        inv.Dur = p.Dur;
        InventoryChanged?.Invoke();
    }

    private void HandleEquippedGear(EquippedGearPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.ArmorSlot = p.Armor;
        player.WeaponSlot = p.Weapon;
        player.HelmetSlot = p.Helmet;
        player.ShieldSlot = p.Shield;
    }

    // The action bar, wholesale — sent at join and re-sent after every accepted edit, so the client never
    // has to model "did my change stick". Server sends 0-based (Kinds[0] = slot 1), as with spells.
    private void HandlePlayerHotkeys(PlayerHotkeysPacket p)
    {
        var bar = _state.Me.Hotkeys;
        for (int i = 1; i < bar.Length; i++) bar[i] = PlayerHotkey.Empty;
        int n = Math.Min(p.Kinds.Length, p.Nums.Length);
        for (int i = 0; i < n && i + 1 < bar.Length; i++)
            bar[i + 1] = new PlayerHotkey((HotkeyKind)p.Kinds[i], p.Nums[i]);
    }

}

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
        player.Equipped.Clear();
        foreach (var entry in p.Worn) player.SetEquipped(entry.Slot, entry.InvSlot);
    }

    // The action bar, wholesale - its declared width and every slot described - sent at join and re-sent
    // after every accepted edit, so the client never has to model "did my change stick". The server sends
    // 0-based (Bound[0] = slot 1), as with spells.
    private void HandlePlayerHotkeys(PlayerHotkeysPacket p)
    {
        _state.HotkeySlots = Math.Max(p.Slots, 0);

        var bar = new PlayerHotkeysPacket.Slot[_state.HotkeySlots + 1];
        for (int i = 0; i < bar.Length; i++) bar[i] = Empty;
        for (int i = 0; i < p.Bound.Count && i + 1 < bar.Length; i++) bar[i + 1] = p.Bound[i];
        _state.Hotkeys = bar;
    }

    private static readonly PlayerHotkeysPacket.Slot Empty =
        new((byte)HotkeyKind.None, string.Empty, 0, string.Empty, string.Empty, 0);

}

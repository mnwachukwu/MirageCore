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

/// <summary>Pushing inventory state to the client: one slot, the equipped gear, or the whole bag
/// after a bulk reorder.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Send helpers ──────────────────────────────────────────────────────────

    /// <summary>Push one inventory slot's current contents to its owner.</summary>
    public void SendInventoryUpdate(int index, int slot)
    {
        var p = _pm[index].Char;
        _dispatcher.SendTo(index, new InventoryUpdatePacket
        {
            Slot = slot,
            Num = p.Inv[slot].Num,
            Quantity = p.Inv[slot].Quantity,
            Dur = p.Inv[slot].Dur
        });
    }

    private void SendEquippedGear(int index)
    {
        var p = _pm[index].Char;
        SendToMap(_world, p.Map, new EquippedGearPacket
        {
            Index = index,
            Armor = p.ArmorSlot,
            Weapon = p.WeaponSlot,
            Helmet = p.HelmetSlot,
            Shield = p.ShieldSlot
        });
    }

    /// <summary>Push the player's entire inventory (all <see cref="Constants.MaxInv"/> slots) in one
    /// packet — used after a bulk reorder where per-slot updates would be noisy. Mirrors the join-time
    /// builder in <c>JoinLeaveSystem</c>.</summary>
    public void SendFullInventory(int index)
    {
        var p = _pm[index].Char;
        var slots = new SendInventoryPacket.InvSlotData[Constants.MaxInv];
        for (int i = 1; i <= Constants.MaxInv; i++)
            slots[i - 1] = new SendInventoryPacket.InvSlotData(i, p.Inv[i].Num, p.Inv[i].Quantity, p.Inv[i].Dur);
        _dispatcher.SendTo(index, new SendInventoryPacket { Slots = slots });
    }
}

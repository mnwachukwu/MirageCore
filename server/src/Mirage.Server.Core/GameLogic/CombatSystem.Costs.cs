using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>What an exchange costs the participants: the stamina drains behind block, dodge and
/// critical, the weather multipliers on those costs, and the equipment wear dying inflicts.</summary>
public sealed partial class CombatSystem : GameSystem
{

    /// <summary>Drains 5% of the player's max SP on a successful crit and broadcasts the update.</summary>
    public void DrainSpForCrit(int index)
    {
        var p = _pm[index].Char;
        p.Sp = Math.Max(p.Sp - CombatFormulas.SpCostForBlockOrCrit(p.MaxSp) * WeatherSpCostMult(p.Map), 0);
        SendToMap(_world, p.Map, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));
    }

    /// <summary>Drains the block SP cost (5% of MaxSp) from the defender and broadcasts the
    /// updated SP. Same accounting as <see cref="DrainSpForCrit"/>, isolated so block sites
    /// don't drift from crit sites.</summary>
    private void DrainSpForBlock(int defenderIndex) => DrainSpForCrit(defenderIndex);

    /// <summary>Drains the dodge SP cost (10% of MaxSp — twice block) from the defender and
    /// broadcasts the updated SP.</summary>
    private void DrainSpForDodge(int defenderIndex)
    {
        var p = _pm[defenderIndex].Char;
        p.Sp = Math.Max(p.Sp - CombatFormulas.SpCostForDodge(p.MaxSp) * WeatherSpCostMult(p.Map), 0);
        SendToMap(_world, p.Map, PacketBuilder.SendSp(defenderIndex, p.Sp, p.MaxSp));
    }

    // ── Weather stamina-cost helpers ────────────────────────────────────────────
    // Heat Wave doubles every stamina cost (block/crit/dodge/run). NPC costs route through
    // EffectiveNpcMaxSp so the % cost tracks the Snow-reduced pool. (Heavy Wind instead PREVENTS the
    // stamina procs from occurring at all — see the Can* gates below — so it needs no cost factor here.)
    private int WeatherSpCostMult(int map) =>
        _world.WeatherOn(map) == WeatherType.HeatWave ? Constants.WeatherHeatWaveSpCostMultiplier : 1;
    private int NpcSpBlockOrCrit(NpcRecord npc, int map) => CombatFormulas.SpCostForBlockOrCrit(_world.EffectiveNpcMaxSp(npc)) * WeatherSpCostMult(map);
    private int NpcSpDodge(NpcRecord npc, int map) => CombatFormulas.SpCostForDodge(_world.EffectiveNpcMaxSp(npc)) * WeatherSpCostMult(map);

    // Death-time gear wear, gated here rather than at the four death sites so their load-bearing
    // drop-before-degrade order can't drift. Per-hit combat wear (DegradeItemDurability) is NOT gated:
    // the switch is about what dying costs.
    internal void DegradeEquipped(int index, int percentOfMax)
    {
        if (!Config.DeathPenalty.DurabilityLoss) return;
        var p = _pm[index].Char;
        // Snapshot slot values upfront — UnequipSlot clears equipment slot references on breakage.
        int[] slots = [p.WeaponSlot, p.ArmorSlot, p.HelmetSlot, p.ShieldSlot];
        foreach (int slot in slots)
        {
            if (slot <= 0) continue;
            int itemNum = p.Inv[slot].Num;
            if (itemNum <= 0) continue;
            int maxDur = _world.Items[itemNum].Durability;
            if (maxDur <= 0) continue;
            int damage = EconomyFormulas.EquipmentDamageOnDeath(maxDur, percentOfMax);
            // Rain doubles durability loss on death too, mirroring per-hit combat wear (DegradeItemDurability). The
            // same "equipment wears down twice as fast" weather message covers both paths, so no separate notice.
            if (_world.WeatherOn(p.Map) == WeatherType.Rain) damage *= Constants.WeatherRainDurabilityWear;
            ApplyEquipmentWear(index, slot, itemNum, damage);
        }
    }

    // Apply a computed durability loss to one equipped slot: reduce Dur, break+unequip at 0 (kept in the bag,
    // never destroyed) or warn, then push the inventory update. Shared by the normal death degrade and the
    // guild-war death degrade (which computes its own per-item damage). No-op for a non-positive damage.
    private void ApplyEquipmentWear(int index, int slot, int itemNum, int damage)
    {
        if (damage <= 0) return;
        var p = _pm[index].Char;
        p.Inv[slot].Dur = Math.Max(p.Inv[slot].Dur - damage, 0);
        if (p.Inv[slot].Dur <= 0)
        {
            SendMsg(index, ServerStrings.CombatSystem_ItemBrokenOnDeath, GameColor.BrightRed, ChatChannel.System, ("ItemName", _world.Items[itemNum].TrimmedName));
            _items.UnequipSlot(index, slot);
            _items.SendInventoryUpdate(index, slot);
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_ItemLostDurability, GameColor.BrightRed, ChatChannel.System, ("ItemName", _world.Items[itemNum].TrimmedName), ("Damage", damage));
            WarnDurability(index, itemNum, p.Inv[slot].Dur);
            _items.SendInventoryUpdate(index, slot);
        }
    }
}

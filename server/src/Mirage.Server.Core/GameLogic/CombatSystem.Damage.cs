using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>What a hit is worth and what it costs the gear that delivered it: the attacker's
/// damage roll, the defender's protection, and the durability wear both sides take.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Player damage formulas ────────────────────────────────────────────────

    private int GetPlayerDamage(int index, int opponentMap = 0)
    {
        var p = _pm[index].Char;
        int dmg = CombatFormulas.UnarmedDamage(p.Str);
        if (p.WeaponSlot > 0)  // > 0: weapon is equipped (1-based slot index; 0 = not equipped)
        {
            int itemNum = p.Inv[p.WeaponSlot].Num;
            if (itemNum > 0)
            {
                dmg += CombatFormulas.WeaponContribution(_world.Items[itemNum].Power, p.Str);
                DegradeItemDurability(index, p.WeaponSlot, opponentMap);
            }
        }
        return dmg;
    }

    /// <summary>Player mitigation — the mirror's single universal MIT: DEF base
    /// (<see cref="CombatFormulas.PlayerProtection"/>) + armor and helmet at full
    /// <see cref="CombatFormulas.GearMitigation"/> + a 1/4 chip from the shield
    /// (<see cref="CombatFormulas.ShieldMitigation"/>).  Defends physical and magic IDENTICALLY, and every piece
    /// wears on any hit — physical or magic — via <see cref="DegradeArmor"/>: with one mitigation axis there is
    /// no separate non-degrading magic path (a shield additionally blocks spells outright, and no shield
    /// lets the player dodge them, see <see cref="TryPlayerNegateMagic"/>).</summary>
    public int GetPlayerProtection(int index, int opponentMap = 0)
    {
        var p = _pm[index].Char;
        int prot = CombatFormulas.PlayerProtection(p.Level, p.Def);
        if (p.ArmorSlot > 0) prot += CombatFormulas.GearMitigation(DegradeArmor(index, p.ArmorSlot, opponentMap), p.Def);
        if (p.HelmetSlot > 0) prot += CombatFormulas.GearMitigation(DegradeArmor(index, p.HelmetSlot, opponentMap), p.Def);
        if (p.ShieldSlot > 0) prot += CombatFormulas.ShieldMitigation(DegradeArmor(index, p.ShieldSlot, opponentMap), p.Def);
        return prot;
    }

    // Side-effect-FREE (no durability wear) player damage/mit for AwardExpForKill — sizing the reward must not
    // chip the player's gear on every kill.  GetPlayerBestDamage mirrors NpcBestHit: the LARGER of the player's
    // weapon swing (P-DMG) and their prepared DAMAGE spell (M-DMG), so a spell-primary caster is sized off their
    // spell and a weapon-primary build off their weapon — whichever they'd actually kill faster with.
    private int GetPlayerBestDamage(int index)
    {
        var p = _pm[index].Char;
        int melee = CombatFormulas.UnarmedDamage(p.Str);
        if (p.WeaponSlot > 0 && p.Inv[p.WeaponSlot].Num > 0)
            melee += CombatFormulas.WeaponContribution(_world.Items[p.Inv[p.WeaponSlot].Num].Power, p.Str);
        // The prepared spell is the player's chosen "magic weapon" — but only a SubHp (HP-damage) spell can KILL:
        // a heal / MP-SP drain / GiveItem does 0 HP damage, so magic is a no-op for time-to-kill then.
        int spell = 0;
        if (p.PreparedSpell > 0 && p.PreparedSpell < p.Spell.Length)
        {
            int spellNum = p.Spell[p.PreparedSpell];
            if (spellNum > 0 && spellNum < _world.Spells.Length && _world.Spells[spellNum].Type == SpellType.SubHp)
                spell = CombatFormulas.SpellPower(p.Int) + CombatFormulas.SpellContribution(_world.Spells[spellNum].VitalAmount, p.Int);
        }
        return Math.Max(melee, spell);
    }
    private int GetPlayerProtectionPure(int index)
    {
        var p = _pm[index].Char;
        int prot = CombatFormulas.PlayerProtection(p.Level, p.Def);
        if (p.ArmorSlot > 0 && p.Inv[p.ArmorSlot].Num > 0) prot += CombatFormulas.GearMitigation(_world.Items[p.Inv[p.ArmorSlot].Num].Power, p.Def);
        if (p.HelmetSlot > 0 && p.Inv[p.HelmetSlot].Num > 0) prot += CombatFormulas.GearMitigation(_world.Items[p.Inv[p.HelmetSlot].Num].Power, p.Def);
        if (p.ShieldSlot > 0 && p.Inv[p.ShieldSlot].Num > 0) prot += CombatFormulas.ShieldMitigation(_world.Items[p.Inv[p.ShieldSlot].Num].Power, p.Def);
        return prot;
    }

    private int DegradeArmor(int index, int slot, int opponentMap = 0)
    {
        var p = _pm[index].Char;
        int itemNum = p.Inv[slot].Num;
        if (itemNum <= 0) return 0;
        int bonus = _world.Items[itemNum].Power;
        DegradeItemDurability(index, slot, opponentMap);
        return bonus;
    }

    /// <summary>Roll condition-scaled wear on the worn item in <paramref name="slot"/>: a hit only
    /// chips 1 durability on a <see cref="CombatFormulas.DurabilityDegradeChancePercent"/> roll (fresh
    /// gear chips rarely, badly worn gear every hit), so most healthy-gear hits are no-ops.
    /// When it does chip and hits zero: announce "has broken", unequip it, and leave it in the bag at
    /// 0 durability — a broken item can't be re-equipped until repaired (see <see cref="ItemSystem.UseItem"/>).
    /// Otherwise: roll the proc-banded condition warning and resync the slot. Shared by weapon
    /// (GetPlayerDamage) and armor/helmet/shield (DegradeArmor, post-block) paths so the wear/break/warn
    /// behavior stays in one place.</summary>
    private void DegradeItemDurability(int index, int slot, int opponentMap = 0)
    {
        var p = _pm[index].Char;
        // Arena is a PvP-ONLY rule: gear never wears in a player-vs-player exchange when either player
        // is on an Arena map. Keyed on opponentMap, which is a player's map (> 0) ONLY at the PvP wear
        // sites — PvE sites pass 0, so a player fighting an NPC wears gear normally on every map moral.
        if (opponentMap > 0 &&
            (_world.MoralOf(p.Map) == MapMoral.Arena || _world.MoralOf(opponentMap) == MapMoral.Arena))
        {
            return;
        }

        int itemNum = p.Inv[slot].Num;
        if (itemNum <= 0) return;
        int maxDur = _world.Items[itemNum].Durability;
        if (maxDur <= 0) return;   // no durability budget: indestructible, never chips (mirrors the equip gate)
        // Condition-scaled wear: this hit only costs durability on a roll that gets likelier as the
        // item wears. Healthy gear shrugs off most hits; sub-25% gear chips every time.
        if (Rng.Percent() >= CombatFormulas.DurabilityDegradeChancePercent(p.Inv[slot].Dur, maxDur))
            return;
        // L2 guild perk: a chance to shrug off this hit's durability wear entirely (normal wear only —
        // death wear via DegradeEquipped is unaffected, per the perk spec).
        if (GuildPerks.IsActive(GuildOf(index), Constants.GuildPerkLevelPreventWear)
            && Rng.Percent() < Constants.GuildPerkPreventWearChancePercent)
        {
            return;
        }

        int wear = _world.WeatherOn(p.Map) == WeatherType.Rain ? Constants.WeatherRainDurabilityWear : 1;
        p.Inv[slot].Dur = Math.Max(0, p.Inv[slot].Dur - wear);   // Max(0,..): wearing 2 off an odd value (e.g. 1) lands at 0, never negative
        if (p.Inv[slot].Dur <= 0)
        {
            p.Inv[slot].Dur = 0;   // clamp; the item stays in the bag, broken, until repaired
            SendMsg(index, ServerStrings.CombatSystem_ItemBroken, GameColor.Yellow, ChatChannel.System, ("Item", _world.Items[itemNum].TrimmedName));
            _items.UnequipSlot(index, slot);
            _items.SendInventoryUpdate(index, slot);
            _pm.MarkDirty(index);   // persist the break this tick so a disconnect can't "un-break" it
        }
        else
        {
            WarnDurability(index, itemNum, p.Inv[slot].Dur);
            _items.SendInventoryUpdate(index, slot);
        }
    }

    /// <summary>
    /// Periodic, randomized condition warning for worn gear, keyed to remaining durability
    /// as a percentage of the item's max (<see cref="ItemRecord.Durability"/>).
    /// Exactly one tier applies — the lowest-percentage band the item falls into — and only
    /// that tier's message can fire, by its own proc chance. Healthier tiers never show once
    /// a lower band applies (e.g. at 25% the 75%/50% messages cannot proc).
    /// </summary>
    private void WarnDurability(int index, int itemNum, int dur)
    {
        int maxDur = _world.Items[itemNum].Durability;
        if (maxDur <= 0) return;
        double pct = (double)dur * 100 / maxDur;
        if (pct >= CombatFormulas.DurWarnExcellentPct) return;
        string name = _world.Items[itemNum].TrimmedName;
        if (pct <= CombatFormulas.DurWarnCriticalPct)
        {
            SendMsg(index, ServerStrings.CombatSystem_ItemAlmostDestroyed, GameColor.BrightRed, ChatChannel.System, ("Name", name));
        }
        else if (pct <= CombatFormulas.DurWarnRepairPct)
        {
            if (Rng.Percent() < CombatFormulas.DurWarnRepairProcPct)
                SendMsg(index, ServerStrings.CombatSystem_ItemNeedsRepair, GameColor.Yellow, ChatChannel.System, ("Name", name));
        }
        else if (pct <= CombatFormulas.DurWarnWornPct)
        {
            if (Rng.Percent() < CombatFormulas.DurWarnWornProcPct)
                SendMsg(index, ServerStrings.CombatSystem_ItemGettingWorn, GameColor.Gray, ChatChannel.System, ("Name", name));
        }
        else if (Rng.Percent() < CombatFormulas.DurWarnFineProcPct)
        {
            SendMsg(index, ServerStrings.CombatSystem_ItemSeenBetterDays, GameColor.Gray, ChatChannel.System, ("Name", name));
        }
    }
}

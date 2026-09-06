using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>A death that lands inside a guild war or territory contest: valor awards, treasury
/// and vault damage, the caster-reagent and inventory losses a war death carries, and the
/// kill-EXP split among the victim's attackers.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Guild war combat ─────────────────────────────────────────────────────────

    // The player index that dealt the most damage to the victim (the "credited killer"), or 0 if none.
    private int TopDamageContributor(int victim)
    {
        int best = 0, bestDmg = 0;
        var dmg = _pm[victim].DamageByPlayer;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (dmg[i] > bestDmg)
            {
                bestDmg = dmg[i];
                best = i;
            }
        }

        return best;
    }

    private bool IsOnArenaMap(int index) => _world.MoralOf(_pm[index].Char.Map) == MapMoral.Arena;

    // True when the victim's death is a guild-war death: the credited killer (top damage dealt) is in a guild
    // with a LIVE war against the victim's guild. Outputs the credited killer and whether the VICTIM'S guild
    // bears the war-death cost (it declared, or the war is mutual); a one-sided defender loses nothing.
    private bool IsWarKill(int victim, out int creditedKiller, out bool victimBearsCost, out bool isTerritory)
    {
        creditedKiller = 0;
        victimBearsCost = false;
        isTerritory = false;
        var victimGuild = GuildOf(victim);
        if (victimGuild is null) return false;
        creditedKiller = TopDamageContributor(victim);
        if (creditedKiller == 0) return false;
        // Arena rules take precedence over war rules: a death involving an arena map is a stakes-free duel,
        // never a war death (baked in here so it holds regardless of the caller).
        if (IsOnArenaMap(victim) || IsOnArenaMap(creditedKiller)) return false;
        var killerGuild = GuildOf(creditedKiller);
        if (killerGuild is null || killerGuild.Index == victimGuild.Index) return false;
        // Grudge war: the victim's side bears the cost only if it declared (or the war is mutual).
        var war = GuildWarFormulas.Find(victimGuild, killerGuild.Index);
        if (war is not null && GuildWarFormulas.IsLive(war, NowUtc))
        {
            victimBearsCost = war.WeDeclared;
            return true;
        }
        // Territory contest: both are participants fighting inside the contested territory during
        // the contest window. A contest is symmetric, so every participant bears the durability cost.
        if (_territory?.AreContestOpponents(creditedKiller, victim) == true)
        {
            victimBearsCost = true;
            isTerritory = true;
            return true;
        }
        return false;
    }

    // Whether these two players are guild-war opponents right now: both guilded, in different guilds, with a
    // LIVE war between them. Used to nullify the aggressor flag between war combatants.
    private bool IsWarParticipant(int a, int b)
    {
        // Arena rules take precedence over war rules — an arena spar is stakes-free, so war never applies there.
        if (IsOnArenaMap(a) || IsOnArenaMap(b)) return false;
        var ga = GuildOf(a);
        var gb = GuildOf(b);
        if (ga is null || gb is null || ga.Index == gb.Index) return false;
        var war = GuildWarFormulas.Find(ga, gb.Index);
        if (war is not null && GuildWarFormulas.IsLive(war, NowUtc)) return true;
        // Territory-contest opponents are war combatants too (no aggressor flag between them).
        return _territory?.AreContestOpponents(a, b) == true;
    }

    // A guild-war death: worn-gear durability only — no inventory drops, no EXP loss, no PK/aggressor
    // flag (the shared cleanup already clears the aggressor flag). When the victim's side bears the cost, the
    // vault absorbs 75% of the doubled wear's repair cost as a sink; a one-sided defender loses nothing.
    private void HandleWarDeath(int creditedKiller, int victim, bool victimBearsCost, bool isTerritory)
    {
        // Worn-gear durability + the caster reagent parity (both vault-absorbed) are handled together in
        // ApplyWarDeathDurability. A one-sided grudge defender bears no cost; a contest is symmetric (all bear it).
        bool vaultCovered = true;
        long treasuryDamage = 0;
        if (victimBearsCost && GuildOf(victim) is { } guild)
            (vaultCovered, treasuryDamage) = ApplyWarDeathDurability(victim, guild);
        SendMsg(victim, ServerStrings.CombatSystem_WarDeath, GameColor.BrightRed, ChatChannel.System);

        if (isTerritory)
        {
            // Territory-war death: its own War-channel readout + the higher territory valor rate,
            // a respawn into the territory, and NO grudge attrition (there's no grudge war between them).
            int terrIndex = _territory?.ContestTerritoryOf(victim) ?? 0;
            _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_TerritoryKillReadout,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.War),
                ("Territory", TerritoryDisplayName(terrIndex)),
                ("AttackerName", _pm[creditedKiller].Char.TrimmedName), ("AttackerGuild", GuildOf(creditedKiller)?.Name ?? ""),
                ("VictimName", _pm[victim].Char.TrimmedName), ("VictimGuild", GuildOf(victim)?.Name ?? ""));
            AwardTerritoryWarValor(victim, creditedKiller);
            _pm[victim].Char.DiedInTerritory = terrIndex;
            // Seasonal leaderboard K/D: +1 kill for the killer's guild, +1 death for the victim's.
            // Dirty-flagged (flushed on the save tick + shutdown) like the income accumulators — no per-death write.
            if (GuildOf(creditedKiller) is { } kGuild) { kGuild.TerritoryWarKills++; _world.DirtyGuilds.Add(kGuild.Index); }
            if (GuildOf(victim) is { } vGuild) { vGuild.TerritoryWarDeaths++; _world.DirtyGuilds.Add(vGuild.Index); }
            return;
        }

        // Public grudge death readout on the War channel (both sides are guilded — IsWarKill verified it).
        _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_WarKillReadout,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.War),
            ("AttackerName", _pm[creditedKiller].Char.TrimmedName), ("AttackerGuild", GuildOf(creditedKiller)?.Name ?? ""),
            ("VictimName", _pm[victim].Char.TrimmedName), ("VictimGuild", GuildOf(victim)?.Name ?? ""));
        // Valor: each war participant on the killer's side who dealt damage rolls for 1 valor.
        AwardGrudgeWarValor(victim, creditedKiller);
        // Attrition / bankruptcy / win-check (mutual wars only; the war system self-gates on that). The
        // treasury damage (vault gold drained) drives the asymmetric attrition swing.
        _guildWar.RecordWarDeath(victim, creditedKiller, vaultCovered, treasuryDamage);
    }

    // Territory-war valor: like grudge valor but at the higher territory rate; every contributor on
    // the credited killer's guild (a contest participant) who dealt damage independently rolls for 1 valor.
    private void AwardTerritoryWarValor(int victim, int creditedKiller)
    {
        var killerGuild = GuildOf(creditedKiller);
        if (killerGuild is null) return;
        var dmg = _pm[victim].DamageByPlayer;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (dmg[i] <= 0 || !_pm[i].IsPlaying) continue;
            if (GuildOf(i)?.Index != killerGuild.Index) continue;
            if (Rng.Percent() < Constants.GuildWarTerritoryValorChancePercent)
                _items.GiveItem(i, Constants.ValorItemIndex, 1);
        }
    }

    private string TerritoryDisplayName(int territoryIndex)
    {
        if (_world.MapGroups.GetValueOrDefault(territoryIndex) is not { } g) return "";
        return string.IsNullOrWhiteSpace(g.DisplayName) ? g.Name : g.DisplayName.Trim();
    }

    // Grudge-war valor: every contributor on the credited killer's guild (the war side) who dealt damage to
    // the victim independently rolls GuildWarGrudgeValorChancePercent for 1 valor.  Recent-healer credit and
    // the higher territory-war valor rate apply on top.
    private void AwardGrudgeWarValor(int victim, int creditedKiller)
    {
        var killerGuild = GuildOf(creditedKiller);
        if (killerGuild is null) return;
        var dmg = _pm[victim].DamageByPlayer;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (dmg[i] <= 0 || !_pm[i].IsPlaying) continue;
            if (GuildOf(i)?.Index != killerGuild.Index) continue;   // only the killer's war side earns
            if (Rng.Percent() < Constants.GuildWarGrudgeValorChancePercent)
                _items.GiveItem(i, Constants.ValorItemIndex, 1);
        }
    }

    // Doubled worn-gear wear + the vault-75% whole-or-nothing repair sink. The vault pre-pays 75% of
    // the repair cost of the full doubled wear; if it can, only 25% of the wear lands on the gear (= half a
    // normal death's wear), otherwise the vault pays nothing and the full doubled wear falls on the player.
    // Returns whether the vault covered this death (true when there was nothing to cover — drives the
    // bankruptcy streak) and the gold it drained from the vault (the attrition "treasury damage").
    // Rides the DurabilityLoss switch: a war death's only penalty IS gear wear. Off reports (covered,
    // nothing drained) — what an uncovered death already reports — so the bankruptcy streak is untripped
    // and attrition falls back to the floored base rate.
    internal (bool Covered, long Drained) ApplyWarDeathDurability(int victim, GuildRecord guild)
    {
        if (!Config.DeathPenalty.DurabilityLoss) return (true, 0);
        var p = _pm[victim].Char;
        int[] slots = [p.WeaponSlot, p.ArmorSlot, p.HelmetSlot, p.ShieldSlot];
        // Pass 1: doubled wear per slot + the repair cost of all of it (the normal repair formula).
        var wear = new int[slots.Length];
        long totalRepair = 0;
        for (int k = 0; k < slots.Length; k++)
        {
            int slot = slots[k];
            if (slot <= 0) continue;
            int itemNum = p.Inv[slot].Num;
            if (itemNum <= 0) continue;
            int maxDur = _world.Items[itemNum].Durability;
            if (maxDur <= 0) continue;
            wear[k] = EconomyFormulas.EquipmentDamageOnDeath(maxDur, Constants.GuildWarDeathWearPercent);
            totalRepair += EconomyFormulas.RepairCost(wear[k], _world.Items[itemNum]);
        }
        // Caster parity: the prepared spell's reagents wear like a weapon (doubled in war), ON TOP of
        // any actual weapon wear above — folded into the same repair total the vault absorbs 75% of
        // (1 reagent = 1 gold).
        int reagentLoss = CasterReagentLoss(victim, Constants.GuildWarDeathWearPercent);
        totalRepair += reagentLoss;

        bool vaultCovers = GuildWarFormulas.WarDeathVaultCovers(totalRepair, guild.VaultGold);
        long drained = 0;
        if (vaultCovers)
        {
            drained = GuildWarFormulas.WarDeathVaultCost(totalRepair);
            guild.VaultGold -= drained;   // a sink; this gold loss is the death's attrition "treasury damage"
            guild.WeeklyWarCosts += drained;   // vault dashboard: war spend this week
            _guilds.RecordSpending(guild, _pm[victim].Login, p.TrimmedName, drained);   // Vault-tab Spending log: account + the fallen character
            _guilds.SaveGuild(guild);
        }
        // Pass 2: apply the player's share (25% if covered, else full) of the wear + the reagents. A war
        // death has no drops, so any reagents beyond the destroyed share simply survive.
        for (int k = 0; k < slots.Length; k++)
        {
            int slot = slots[k];
            if (slot <= 0 || wear[k] <= 0) continue;
            ApplyEquipmentWear(victim, slot, p.Inv[slot].Num, GuildWarFormulas.WarDeathItemWear(wear[k], vaultCovers));
        }
        if (reagentLoss > 0)
        {
            int destroy = GuildWarFormulas.WarDeathItemWear(reagentLoss, vaultCovers);
            if (destroy > 0)
            {
                _items.TakeItem(victim, Constants.CastingReagentItemIndex, destroy);
                SendMsg(victim, ServerStrings.CombatSystem_ReagentsLostOnDeath, GameColor.BrightRed, ChatChannel.System, ("Count", destroy));
            }
        }
        // A death with nothing to repair is trivially "covered" (it must not trip the bankruptcy streak).
        return (totalRepair == 0 || vaultCovers, drained);
    }

    // The caster's offensive "tier" for reagent parity: the prepared (Q-cast) spell's power, or — if none is
    // prepared — the strongest known SubHp spell's power. 0 = no offensive tier (not a caster).
    private int CasterTierLevel(int index)
    {
        var p = _pm[index].Char;
        if (p.PreparedSpell > 0 && p.PreparedSpell < p.Spell.Length)
        {
            int prepNum = p.Spell[p.PreparedSpell];
            if (prepNum > 0 && prepNum <= _world.Limits.Spells) return _world.Spells[prepNum].LevelReq;
        }
        int best = 0;
        for (int i = 1; i < p.Spell.Length; i++)
        {
            int sn = p.Spell[i];
            if (sn <= 0 || sn > _world.Limits.Spells) continue;
            var s = _world.Spells[sn];
            if (s.Type == SpellType.SubHp && s.LevelReq > best) best = s.LevelReq;
        }
        return best;
    }

    // Reagents a caster loses at this death's wear percent — min of the tier-priced amount and what they
    // hold. INDEPENDENT of any equipped weapon: reagent loss is driven by the prepared spell (weapon wear is
    // priced off the weapon separately), so a caster loses reagents whether or not they also carry a weapon.
    // 0 for a non-caster (no prepared spell + no known SubHp spell) or one holding no reagents. Does NOT
    // consume; callers destroy the returned amount (the war path folds it into the vault sink).
    private int CasterReagentLoss(int index, int wearPercent)
    {
        var p = _pm[index].Char;
        int tier = CasterTierLevel(index);
        if (tier <= 0) return 0;
        long held = ItemSystem.CountItem(p, _world.Items, Constants.CastingReagentItemIndex);
        if (held <= 0) return 0;
        int loss = CombatFormulas.CasterDeathReagentLoss(tier, wearPercent);
        return held < loss ? (int)held : loss;   // never destroy more reagents than they hold
    }

    // General caster parity (non-war deaths): destroy the prepared-spell-priced reagents (on top
    // of any weapon wear from DegradeEquipped). The remaining reagents still face the normal drop roll, so
    // call this BEFORE the drop step. No-op for a non-caster.
    internal void DestroyCasterDeathReagents(int index, int wearPercent)
    {
        // Reagents are the caster's durability — see DeathPenaltyConfig.DurabilityLoss for why they ride
        // that switch and not the item drop.
        if (!Config.DeathPenalty.DurabilityLoss) return;
        int loss = CasterReagentLoss(index, wearPercent);
        if (loss <= 0) return;
        _items.TakeItem(index, Constants.CastingReagentItemIndex, loss);
        SendMsg(index, ServerStrings.CombatSystem_ReagentsLostOnDeath, GameColor.BrightRed, ChatChannel.System, ("Count", loss));
    }
    // The three drop helpers are gated at the top so no roll is consumed. Between them they are the whole
    // of "you drop things when you die".
    internal void DropNonEquippedInventory(int index)
    {
        if (!Config.DeathPenalty.ItemDrop) return;
        var p = _pm[index].Char;
        var equipped = new HashSet<int> { p.WeaponSlot, p.ArmorSlot, p.HelmetSlot, p.ShieldSlot };
        equipped.Remove(0);
        int count = 0;
        int[] occupied = new int[p.Inv.Length];
        for (int i = 1; i < p.Inv.Length; i++)
            if (p.Inv[i].Num > 0 && !equipped.Contains(i)) occupied[count++] = i;
        for (int i = 0; i < count; i++)
        {
            int slot = occupied[i];
            int amount = _world.Items[p.Inv[slot].Num].Type == ItemType.Currency ? p.Inv[slot].Quantity : 0;
            _items.PlayerMapDropItemForDeath(index, slot, amount);
        }
    }
    internal void DropRandomNonEquippedInventory(int index)
    {
        if (!Config.DeathPenalty.ItemDrop) return;
        var p = _pm[index].Char;
        var equipped = new HashSet<int> { p.WeaponSlot, p.ArmorSlot, p.HelmetSlot, p.ShieldSlot };
        equipped.Remove(0);
        int count = 0;
        int[] slots = new int[p.Inv.Length];
        for (int i = 1; i < p.Inv.Length; i++)
            if (p.Inv[i].Num > 0 && !equipped.Contains(i)) slots[count++] = i;
        for (int i = 0; i < count; i++)
        {
            if (Rng.Percent() >= Constants.NormalDropChancePercent) continue;
            int slot = slots[i];
            int itemNum = p.Inv[slot].Num;
            int amount = _world.Items[itemNum].Type == ItemType.Currency && p.Inv[slot].Quantity > 0
                ? Rng.Next(1, p.Inv[slot].Quantity + 1)
                : 0;
            _items.PlayerMapDropItemForDeath(index, slot, amount);
        }
    }
    internal void DropRandomEquipped(int index)
    {
        if (!Config.DeathPenalty.ItemDrop) return;
        var p = _pm[index].Char;
        int[] eqSlots = [p.WeaponSlot, p.ArmorSlot, p.HelmetSlot, p.ShieldSlot];
        foreach (int slot in eqSlots)
        {
            if (slot <= 0 || p.Inv[slot].Num <= 0) continue;
            if (Rng.Percent() >= Constants.PkEqDropChancePercent) continue;
            _items.PlayerMapDropItem(index, slot, 0);
        }
    }
    private void DistributePvpKillExp(int victim, long expPool, int mapNum)
    {
        int totalDmg = 0;
        for (int i = 1; i <= _pm.Slots; i++) totalDmg += _pm[victim].DamageByPlayer[i];
        if (totalDmg == 0) return;
        string victimName = _pm[victim].Char.TrimmedName;
        // Two-pass distribution (see ExecuteNpcDamage for the rationale): pass 1 collects base
        // contribution EXP per contributor (pre-bonus, including for max-level players so their
        // partner still earns the partner kill bonus).  Pass 2 pays own × bonus + partner kill
        // bonus to active and passive partners alike.
        var contributors = new HashSet<int>();
        var contributorBaseExp = new Dictionary<int, long>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (_pm[victim].DamageByPlayer[i] == 0) continue;
            // Cross-map aware: a contributor who can observe the victim's map earns credit even from
            // across a border (mirrors the NPC-kill EXP path), since cross-seam PvP is supported.
            if (!_pm[i].IsPlaying || !_world.IsObserving(i, mapNum)) continue;
            contributors.Add(i);
            long baseExp = Math.Max((long)((double)_pm[victim].DamageByPlayer[i] / totalDmg * expPool), 1L);
            contributorBaseExp[i] = baseExp;
        }
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying || !_world.IsObserving(i, mapNum)) continue;
            var p = _pm[i].Char;
            if (p.Level >= Constants.MaxLevel) continue;
            long contributionExp = contributorBaseExp.GetValueOrDefault(i);
            int partner = _pm[i].InParty ? _pm[i].PartyPlayer : 0;
            bool partnerInRange = partner > 0 && _pm[partner].IsPlaying && _world.IsObserving(partner, mapNum);
            int partnerLevel = partnerInRange ? _pm[partner].Char.Level : 0;
            bool partnerInBand = partnerInRange && Math.Abs(p.Level - partnerLevel) <= ExpFormulas.PartyLevelGap;
            long partyBonusExp = partnerInBand ? (long)(contributionExp * ExpFormulas.PartyExpBonus) - contributionExp : 0;
            long partnerBase = contributorBaseExp.GetValueOrDefault(partner);
            long partnerKillExp = ExpFormulas.PartnerKillBonus(p.Level, partnerLevel, partnerBase);
            long totalExp = contributionExp + partyBonusExp + partnerKillExp;
            if (totalExp <= 0) continue;
            p.Exp = Math.Min(p.Exp + totalExp, ExpFormulas.MaxTotalExp);
            if (contributionExp > 0)
                SendMsg(i, ServerStrings.CombatSystem_PvpKillExp, GameColor.BrightBlue, ChatChannel.Rewards, ("Exp", contributionExp), ("VictimName", victimName));
            if (partyBonusExp > 0)
                SendMsg(i, ServerStrings.CombatSystem_ExpPartyBonus, GameColor.BrightBlue, ChatChannel.Rewards, ("Exp", partyBonusExp));
            if (partnerKillExp > 0)
            {
                SendMsg(i,
                    contributionExp > 0 ? ServerStrings.CombatSystem_ExpPartnerEffort : ServerStrings.CombatSystem_ExpPartnerKill,
                    GameColor.BrightBlue, ChatChannel.Rewards, ("Exp", partnerKillExp));
            }

            CheckPlayerLevelUp(i);
            _dispatcher.SendTo(i, PacketBuilder.SendStats(p));
        }
    }

    private void ClearTargetIfMatches(int index, byte targetType, int targetId)
    {
        if (_pm[index].TargetType == targetType && _pm[index].Target == targetId)
        {
            _pm[index].Target = 0;
            _pm[index].TargetType = 0;
        }
    }

    // Auto-target on melee engagement: a player who swings at a valid target locks onto it server-side
    // and the client is told to mirror it as its tab target.  Behavior for being hit is unaffected —
    // only the attacker's target changes.

    private void AssignPlayerTarget(int index, int victimIndex)
    {
        var sp = _pm[index];
        sp.TargetType = 0;
        sp.Target = victimIndex;
        sp.TargetMap = 0;
        sp.TargetSpawnMap = 0;
        sp.TargetSpawnSlot = 0;
        _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 0, Target = victimIndex });
    }

    private void AssignNpcTarget(int index, int mapNum, MapNpcRecord mapNpc, int npcSlot)
    {
        var sp = _pm[index];
        if (mapNpc is TraversalNpcRecord t)
        {
            sp.TargetType = 3;
            sp.Target = 0;
            sp.TargetMap = mapNum;
            sp.TargetSpawnMap = t.SpawnMapNum;
            sp.TargetSpawnSlot = t.SpawnSlot;
            _dispatcher.SendTo(index, new SetTargetPacket
            {
                TargetType = 3,
                TargetMap = mapNum,
                SpawnMap = t.SpawnMapNum,
                SpawnSlot = t.SpawnSlot,
            });
        }
        else
        {
            sp.TargetType = 1;
            sp.Target = npcSlot;
            sp.TargetMap = mapNum;
            sp.TargetSpawnMap = 0;
            sp.TargetSpawnSlot = 0;
            _dispatcher.SendTo(index, new SetTargetPacket
            {
                TargetType = 1,
                Target = npcSlot,
                TargetMap = mapNum,
            });
        }
    }
}

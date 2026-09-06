using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Player versus NPC: attack gating, damage, and everything a kill drags behind it —
/// the damage ledger, EXP award and party split, territory income accrual, loot rolls, and the
/// death broadcast plus slot cleanup.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Player vs NPC ─────────────────────────────────────────────────────────

    public bool CanAttackNpc(int attacker, int mapNum, int npcSlot)
    {
        if (!SlotValidation.IsValidNpcSlot(npcSlot)) return false;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return false;
        return CanAttackNpc(attacker, mapNum, _world.MapNpcs[mapNum, npcSlot], npcSlot, out _);
    }

    /// <summary>Object form usable for native slot NPCs and traversal guests alike.
    /// <paramref name="npcSlot"/> is the native slot (1..MaxMapNpcs) or 0 for a traversal guest;
    /// it's used to address the friendly/shopkeeper rebuff bubble back to the right sprite.</summary>
    public bool CanAttackNpc(int attacker, int mapNum, MapNpcRecord mapNpc, int npcSlot, out bool rebuffed)
    {
        rebuffed = false;
        if (!_pm[attacker].IsPlaying) return false;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return false;
        if (mapNpc.Num <= 0 || mapNpc.Hp <= 0) return false;
        long windMult = _world.WeatherOn(mapNum) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (Environment.TickCount64 <= _pm[attacker].AttackTimer + Constants.PlayerAttackCooldownMs * windMult) return false;

        var p = _pm[attacker].Char;
        var npcRec = _world.Npcs[mapNpc.Num];

        // Cross-map melee: world-space adjacency in the attacker's facing direction.  Against a large NPC the
        // player connects if their faced tile lands on ANY tile of the NPC's footprint (not just its anchor).
        if (!IsFacingNpcAcrossMaps(p.Map, p.Dir, p.X, p.Y, mapNum, mapNpc, npcRec.EffectiveSize)) return false;
        // Two-layer connect ("layer 1.5"): the faced step must reach the NPC's layer — a ground player can't melee
        // a mob up on the bridge (or vice-versa) except where a ramp connects the two layers.
        if (!MeleeLayerConnects(p.Map, p.X, p.Y, p.Layer, p.Dir, mapNpc.Layer)) return false;

        if (npcRec.Behavior is NpcBehavior.Friendly or NpcBehavior.Stationary)
        {
            rebuffed = true;   // a non-combat NPC in the faced tile — the caller suppresses the whiff swing
            if (!string.IsNullOrWhiteSpace(npcRec.AttackSay))
            {
                // A friendly NPC's rebuff is speech, not combat — route to the Say channel, not the Combat tab.
                SendMsg(attacker, ServerStrings.CombatSystem_FriendlyNpcSays, GameColor.Npc, ChatChannel.Say, ("NpcName", npcRec.TrimmedName), ("Say", npcRec.AttackSay.TrimEnd()));
                // Green-bordered bubble over the NPC so the rebuff reads as distinct from a player Say.
                // Friendly/Stationary NPCs don't traverse, so a non-zero slot is expected here.
                if (npcSlot > 0)
                    _dispatcher.SendTo(attacker, PacketBuilder.NpcChatBubble(mapNum, npcSlot, npcRec.AttackSay.TrimEnd(), kind: 1));
            }
            else
            {
                SendMsg(attacker, ServerStrings.CombatSystem_CannotAttackFriendly, GameColor.BrightBlue, ChatChannel.System, ("NpcName", npcRec.TrimmedName));
            }

            return false;
        }
        return true;
    }

    /// <summary>Speak an NPC's idle/rebuff <see cref="NpcRecord.AttackSay"/> to one player — a Say-channel line plus a
    /// green chat bubble over the NPC. Used by the friendly-melee rebuff above and by the interaction spine's
    /// "nothing else to offer" fallback, so an NPC with no conversation / quest / shop still reacts instead of doing
    /// nothing. No-op when the NPC has no AttackSay.</summary>
    public void SpeakAttackSayTo(int playerIndex, int mapNum, int npcSlot, NpcRecord npcRec)
    {
        if (string.IsNullOrWhiteSpace(npcRec.AttackSay)) return;
        string say = npcRec.AttackSay.TrimEnd();
        SendMsg(playerIndex, ServerStrings.CombatSystem_FriendlyNpcSays, GameColor.Npc, ChatChannel.Say, ("NpcName", npcRec.TrimmedName), ("Say", say));
        if (npcSlot > 0)
            _dispatcher.SendTo(playerIndex, PacketBuilder.NpcChatBubble(mapNum, npcSlot, say, kind: 1));
    }

    public void AttackNpc(int attacker, int mapNum, int npcSlot, int dmg, bool isCrit = false)
    {
        if (!SlotValidation.IsValidNpcSlot(npcSlot)) return;
        AttackNpc(attacker, mapNum, _world.MapNpcs[mapNum, npcSlot], npcSlot, dmg, isCrit);
    }

    /// <summary>Gate, strike, stamp — for a caller holding nothing but an intent to hit. A caller that has
    /// already passed <see cref="CanAttackNpc"/> and already stamped the beat wants <see cref="StrikeNpc"/>
    /// instead.</summary>
    public void AttackNpc(int attacker, int mapNum, MapNpcRecord mapNpc, int npcSlot, int dmg, bool isCrit = false)
    {
        if (!CanAttackNpc(attacker, mapNum, mapNpc, npcSlot, out _)) return;
        StrikeNpc(attacker, mapNum, mapNpc, npcSlot, dmg, isCrit);
        _pm[attacker].AttackTimer = Environment.TickCount64;
    }

    /// <summary>Lands a melee hit whose gate has already been checked and whose beat has already been paid.
    ///
    /// <para>Separate from <see cref="AttackNpc"/> because the cooldown is BOTH a gate and a consequence of
    /// swinging. The swing path stamps it up front so every outcome pays it, which puts the attacker on
    /// cooldown before the damage is applied — so a second reading of <see cref="CanAttackNpc"/> here would
    /// refuse the very hit that stamp was made for, and the swing would land nothing.</para></summary>
    private void StrikeNpc(int attacker, int mapNum, MapNpcRecord mapNpc, int npcSlot, int dmg, bool isCrit)
    {
        var ap = _pm[attacker].Char;
        int weapNum = ap.WeaponSlot > 0 ? ap.Inv[ap.WeaponSlot].Num : 0;
        string weap = weapNum > 0 ? _world.Items[weapNum].TrimmedName : "";

        bool isGuard = _world.Npcs[mapNpc.Num].Behavior == NpcBehavior.Guard;
        BreakGraceForCombat(attacker, involvesPlayerOrGuard: isGuard);
        ExecuteNpcDamage(attacker, mapNum, mapNpc, npcSlot, dmg, weap, isCrit);
    }

    /// <summary>Apply pre-computed spell damage to an NPC without adjacency or behavior checks.
    /// CastSpell checks behavior before calling AttackNpc; AttackNpc has no such guard.</summary>
    public void ApplyNpcDamage(int attacker, int mapNum, int npcSlot, int dmg, bool isCrit = false)
    {
        if (!SlotValidation.IsValidNpcSlot(npcSlot)) return;
        ApplyNpcDamage(attacker, mapNum, _world.MapNpcs[mapNum, npcSlot], npcSlot, dmg, isCrit);
    }

    /// <summary>Object form (spell damage) usable for native slot NPCs and traversal guests (slot 0).</summary>
    public void ApplyNpcDamage(int attacker, int mapNum, MapNpcRecord mapNpc, int npcSlot, int dmg, bool isCrit = false)
    {
        if (!_pm[attacker].IsPlaying || dmg < 0) return;
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        if (mapNpc.Num <= 0 || mapNpc.Hp <= 0) return;
        bool isGuard = _world.Npcs[mapNpc.Num].Behavior == NpcBehavior.Guard;
        BreakGraceForCombat(attacker, involvesPlayerOrGuard: isGuard);
        ExecuteNpcDamage(attacker, mapNum, mapNpc, npcSlot, dmg, weapName: "", isCrit: isCrit);
    }

    private void ExecuteNpcDamage(int attacker, int mapNum, MapNpcRecord mapNpc, int npcSlot, int dmg, string weapName, bool isCrit = false)
    {
        var npcRec = _world.Npcs[mapNpc.Num];

        // Blood: deposit on the NPC's tile, sized by damage vs its effective max HP; a kill always splats.
        _blood.Deposit(mapNum, mapNpc.X, mapNpc.Y, Constants.BloodDepositStrength(dmg, _world.EffectiveNpcMaxHp(npcRec), mapNpc.Hp), npcRec.EffectiveSize, mapNpc.Layer);

        if (dmg >= mapNpc.Hp)
        {
            mapNpc.DamageByPlayer[attacker] += mapNpc.Hp;  // credit remaining HP — the true kill-blow amount
            SendYouHitMsg(attacker, $"a {npcRec.TrimmedName}", weapName, dmg, GameColor.BrightRed, killing: true);
            // Anti-farm: a safe-zone kill a guard helped land pays the player no EXP/loot, so guards
            // can't be used to tank-farm dragged mobs in town.  Mirrors the NPC-vs-NPC safe-zone
            // suppression in ExecuteNpcVsNpcDamage.  Solo kills (no guard damage) still reward.
            if (GuardAssistedKillInSafeZone(mapNum, mapNpc))
            {
                SendMsg(attacker, ServerStrings.CombatSystem_SafeZoneNoReward, GameColor.BrightBlue, ChatChannel.Rewards);
            }
            else
            {
                AwardExpForKill(mapNum, mapNpc);
                ResolveAndSpawnLoot(mapNum, mapNpc);
            }
            ResetNpcCombatLedger(mapNpc);
            BroadcastNpcDeathAndCleanup(mapNum, mapNpc, npcSlot, dmg, isCrit);
        }
        else
        {
            mapNpc.Hp -= dmg;
            mapNpc.DamageByPlayer[attacker] += dmg;

            SendYouHitMsg(attacker, $"a {npcRec.TrimmedName}", weapName, dmg, GameColor.White);
            if (mapNpc is TraversalNpcRecord t)
                SendTraversalState(t, damage: dmg, isCrit: isCrit);  // updated Hp + floating number by identity
            else
                SendToMap(_world, mapNum, new NpcDamagePacket { MapNum = mapNum, NpcSlot = npcSlot, Damage = dmg, IsCrit = isCrit });

            // Guard grace: a small hit from a non-PK attacker only earns a "Watch it!" warning until
            // the 3-swing budget runs out. Skips AttackSay too — the warning replaces it for those
            // swings, and AttackSay still fires on the eventual aggro swing because LastAttackSayTarget
            // wasn't stamped during grace.
            if (ConsumeGuardGrace(attacker, mapNum, npcSlot, mapNpc, dmg)) return;

            if (mapNpc.Target == 0 && mapNpc.LastAttackSayTarget != attacker
                && !string.IsNullOrWhiteSpace(npcRec.AttackSay))
            {
                mapNpc.LastAttackSayTarget = attacker;
                SendMsg(attacker, ServerStrings.CombatSystem_NpcSays, GameColor.Npc, ChatChannel.Say, ("NpcName", npcRec.TrimmedName), ("Say", npcRec.AttackSay.TrimEnd()));
                if (npcSlot > 0)
                    _dispatcher.SendTo(attacker, PacketBuilder.NpcChatBubble(mapNum, npcSlot, npcRec.AttackSay.TrimEnd(), kind: 0));
                else if (mapNpc is TraversalNpcRecord tg)
                    _dispatcher.SendTo(attacker, PacketBuilder.TraversalNpcChatBubble(tg.SpawnMapNum, tg.SpawnSlot, npcRec.AttackSay.TrimEnd(), kind: 0));
            }

            SetNpcAggro(mapNum, npcSlot, mapNpc, attacker);
        }
    }

    /// <summary>True when <paramref name="mapNpc"/> is dying on a Safe map and at least one Guard NPC
    /// dealt damage to it: the signal that a guard helped tank/kill a player-dragged mob.  Gates the
    /// anti-farm EXP/loot denial on the player-kill path so guard-assisted town kills pay nothing,
    /// while a solo kill in a guardless safe zone still rewards normally.  Must be called before
    /// <see cref="ResetNpcCombatLedger"/> clears the NPC damage ledger.</summary>
    private bool GuardAssistedKillInSafeZone(int mapNum, MapNpcRecord mapNpc)
    {
        if (_world.MoralOf(mapNum) != MapMoral.Safe) return false;
        if (mapNpc.DamageByNpc is not { } list) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var resolved = ResolveNpcByIdentity(list[i].SpawnMap, list[i].SpawnSlot);
            if (resolved is not null && _world.Npcs[resolved.Value.Record.Num].Behavior == NpcBehavior.Guard)
                return true;
        }
        return false;
    }

    /// <summary>Two-pass EXP distribution to every contributor and their on-map party partners.
    ///
    /// <para>Pass 1 collects each contributor's BASE contribution EXP (damage-share × tier, pre-bonus)
    /// and awards nothing — stored even for max-level contributors, so their partner still earns the
    /// partner kill bonus. Pass 2 walks every on-map, non-max-level, party-eligible player and pays
    /// ownBase × PartyExpBonus (when partnered in band) plus PartnerKillBonus off the partner's base.
    /// That applies to active co-fighters AND a passive partner who dealt no damage, so two co-fighters
    /// cut each other's base symmetrically and nobody gains by underperforming. Net party payout in band
    /// is mob_xp × 1.45 regardless of the damage split.</para>
    ///
    /// <para>Shared by the player-killed and NPC-killed paths. The denominator is NPC MAX HP, not summed
    /// player damage, so a guard who whittled half the bar proportionally reduces every player's share —
    /// you earn what you dealt.</para></summary>
    private void AwardExpForKill(int mapNum, MapNpcRecord mapNpc)
    {
        var npcRec = _world.Npcs[mapNpc.Num];
        // Night + weather boosts scale every player's reward; they compound multiplicatively (Night 1.20 × Weather).
        double expMult = 1.0;
        if (_world.TimePhase == TimePhase.Night) expMult *= Constants.NpcNightExpMultiplier;
        expMult *= ExpFormulas.WeatherExpMultiplier(_world.WeatherOn(mapNum));
        int totalDmg = Math.Max(_world.EffectiveNpcMaxHp(npcRec), 1);
        // Mob-intrinsic inputs to the per-player EXP (identical for every contributor).
        int mobMit = CombatFormulas.NpcProtection(npcRec);
        int mobHit = ExpFormulas.NpcBestHit(npcRec);

        var contributors = new HashSet<int>();
        var contributorBaseExp = new Dictionary<int, long>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (mapNpc.DamageByPlayer[i] == 0) continue;
            // Cross-map aware: a damage-dealer who can observe the NPC's map earns credit,
            // even if they landed the blows from one tile across a border.
            if (!_pm[i].IsPlaying || !_world.IsObserving(i, mapNum)) continue;
            contributors.Add(i);
            var p = _pm[i].Char;
            // EXP is PLAYER-RELATIVE: this player's own SOLO reward for this mob (toughness vs THEIR hit, danger vs
            // THEIR HP), scaled by the share of the mob they personally chewed through, then the night/weather mult.
            int solo = ExpFormulas.ExpForKill(totalDmg, mobMit, mobHit, GetPlayerBestDamage(i), GetPlayerProtectionPure(i), p.MaxHp);
            double share = (double)mapNpc.DamageByPlayer[i] / totalDmg;
            contributorBaseExp[i] = (long)Math.Round(solo * share * expMult, MidpointRounding.AwayFromZero);
        }

        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying || !_world.IsObserving(i, mapNum)) continue;
            var p = _pm[i].Char;
            if (p.Level >= Constants.MaxLevel) continue;
            bool isContributor = contributors.Contains(i);
            long contributionExp = contributorBaseExp.GetValueOrDefault(i);
            if (isContributor && contributionExp <= 0) contributionExp = 1;  // floor: contributors always earn ≥ 1 from their share
            // L3 guild perk: +10% to this member's own kill EXP (their contribution, and the party bonus
            // derived from it, both reflect the boost).
            if (GuildPerks.IsActive(GuildOf(i), Constants.GuildPerkLevelBonusExp))
                contributionExp += (long)Math.Round(contributionExp * (Constants.GuildPerkBonusExpPercent / 100.0), MidpointRounding.AwayFromZero);
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
                SendMsg(i, ServerStrings.CombatSystem_ExpGain, GameColor.BrightBlue, ChatChannel.Rewards, ("Exp", contributionExp));
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

        // Who this kill counts for on a QUEST, which is a narrower question than who earns EXP from it. Guild
        // quests, player quests and the valor below all read this one set.
        var questCredit = QuestCreditFor(mapNpc, mapNum, totalDmg, contributors);

        // Guild-quest valor (per-player): a credited player whose guild's active quest targets this mob rolls
        // for 1 valor. Rolled BEFORE the kernel advances the quest below, so a kill that COMPLETES a guild quest
        // still pays valor (the kernel's completion callback clears the quest, which would otherwise hide the target).
        foreach (int i in questCredit)
        {
            if (GuildSystem.QuestTargetsNpc(GuildOf(i), mapNpc.Num)
                && Rng.Percent() < Constants.GuildQuestValorChancePercent)
            {
                _items.GiveItem(i, Constants.ValorItemIndex, 1);
            }
        }

        // Objective-kernel kill hook: advance any tracked Kill objective this mob + its credited players match.
        // Scope-agnostic — the kernel owns no guild/player notion; BOTH guild quests and player quests register
        // here, so this one seam drives both. Runs after the valor roll above so a completing kill still pays valor.
        _objectives.RecordNpcKill(mapNpc.Num, questCredit);

        // Guild XP: one point per KO to each DISTINCT guild that had a contributor (deduped so several
        // guildmates on one kill grant the guild a single point — a minor trickle; quests are the main
        // driver). Only touches _guilds for guilded contributors, so it is null-safe for guildless play.
        var creditedGuilds = new HashSet<int>();
        foreach (int i in contributors)
        {
            int gid = _pm[i].Guild;
            if (gid < 1 || !creditedGuilds.Add(gid)) continue;
            var guild = _world.Guilds.GetValueOrDefault(gid);
            _guilds.AddGuildExp(guild, Constants.GuildExpPerKill);
            // L5 perk: a chance each KO trickles GuildPerkVaultGold into the guild's daily accumulator
            // (credited at the 00:00 settlement, after debits). Accrues in memory; the settlement persists it.
            if (GuildPerks.IsActive(guild, Constants.GuildPerkLevelVaultGold)
                && Rng.Percent() < Constants.GuildPerkVaultGoldChancePercent)
            {
                guild!.PendingPerkIncome += Constants.GuildPerkVaultGold;
                _world.DirtyGuilds.Add(gid);   // flushed on the periodic save + shutdown (never lost)
            }
            // (Guild-quest progress advances through the objective kernel above, not here.)
        }

        // Territory income: a chance for this PvE kill to trickle gold to the controlling guild.
        AccrueTerritoryIncome(mapNum, mapNpc, contributors);
    }

    /// <summary>
    /// Who this kill counts for on a QUEST — player objectives, guild objectives, and the valor rolled for
    /// advancing one. A narrower question than who earns EXP from it, and decoupled from it entirely.
    ///
    /// <para>EXP divides by damage share, so a token hit earns a token amount and gains nothing by it. An
    /// objective tick does NOT divide — it is the same size however little was done for it — so it takes a real
    /// share of the work (<see cref="Constants.QuestCreditDamagePercent"/>) rather than any damage at all.
    /// Otherwise one point of damage on a mob somebody else killed is a full tick, and tagging is the fastest
    /// way to quest.</para>
    ///
    /// <para>🔴 A PARTY PARTNER of someone who clears the floor shares that credit on ONE damaging blow. The
    /// pair still puts a real share in between them, so this SPLITS a qualifying effort rather than widening
    /// who qualifies — and each of the two has to hit the mob. Every class deals damage off the same stat
    /// spread (a healer's spellbook is taxed in MP, not in offense), so a blow is a bar any build can clear.</para>
    ///
    /// <para>A blow is also all a partner needs to be REACHED: <paramref name="contributors"/> already carries
    /// "still observing the map the kill happened on", the same 3x3 test the partner-kill EXP bonus applies,
    /// so a partner who is elsewhere is absent from it and shares nothing.</para>
    ///
    /// <para>Two passes, so the outcome never depends on slot order: every clearing player is collected first,
    /// then partners are matched against that finished set.</para>
    /// </summary>
    internal HashSet<int> QuestCreditFor(MapNpcRecord mapNpc, int mapNum, long totalDmg, HashSet<int> contributors)
    {
        var credited = new HashSet<int>();
        foreach (int i in contributors)
        {
            // Compared as whole numbers: the threshold is a percentage of a damage total, and both are ints.
            if (mapNpc.DamageByPlayer[i] * 100L >= totalDmg * Constants.QuestCreditDamagePercent) credited.Add(i);
        }

        var shared = new HashSet<int>();
        foreach (int i in contributors)
        {
            if (credited.Contains(i)) continue;
            int partner = _pm[i].InParty ? _pm[i].PartyPlayer : 0;
            if (partner > 0 && credited.Contains(partner)) shared.Add(i);
        }

        credited.UnionWith(shared);
        return credited;
    }

    // Territory income: a PvE kill in a controlled territory rolls a chance to add gold to the
    // owning guild's daily accumulator (credited at the 00:00 settlement). Earned by ANYONE hunting there —
    // the top-damage contributor's guild only decides the owner bonus (owner base if they're a member, else
    // the non-owner base), scaled by the weeks-held multiplier. PvP kills never reach here (this is the
    // NPC-kill path). Accrues in memory; the settlement persists + credits it. No-op until a guild controls
    // the territory.
    private void AccrueTerritoryIncome(int mapNum, MapNpcRecord mapNpc, HashSet<int> contributors)
    {
        if (contributors.Count == 0) return;
        if (_world.TerritoryOf(mapNum) is not { ControllingGuild: > 0 } terr) return;
        if (Rng.Percent() >= Constants.TerritoryIncomeChancePercent) return;

        int topKiller = 0, topDmg = 0;
        foreach (int i in contributors)
        {
            int d = mapNpc.DamageByPlayer[i];
            if (d > topDmg)
            {
                topDmg = d;
                topKiller = i;
            }
        }
        bool killerInOwningGuild = topKiller > 0 && _pm[topKiller].Guild == terr.ControllingGuild;
        int income = TerritoryFormulas.IncomeForKill(killerInOwningGuild, terr.WeeksHeld);
        terr.PendingTerritoryIncome = TerritoryFormulas.AccruePending(terr.PendingTerritoryIncome, income, Constants.TerritoryIncomeDailyCap);
        _world.DirtyTerritories.Add(terr.MapGroup);   // flushed on the periodic save + shutdown (never lost)
    }

    /// <summary>Resolve the loot-tag winner from <see cref="MapNpcRecord.DamageByPlayer"/> and roll
    /// the drop chance.  <b>Anti-cheese suppression:</b> if any NPC contributor outdamaged every
    /// player (max-per-entry comparison), no loot drops at all — prevents a player chipping a mob
    /// and letting a guard finish for free loot.  Loot threshold uses TOP PLAYER damage (NPC
    /// contributions are not in the threshold calc).  Caller is responsible for <see cref="ResetNpcCombatLedger"/>
    /// after.</summary>
    private void ResolveAndSpawnLoot(int mapNum, MapNpcRecord mapNpc)
    {
        var npcRec = _world.Npcs[mapNpc.Num];
        int maxDmg = 0;
        for (int i = 1; i <= _pm.Slots; i++)
            if (mapNpc.DamageByPlayer[i] > maxDmg) maxDmg = mapNpc.DamageByPlayer[i];

        int topNpcDmg = 0;
        if (mapNpc.DamageByNpc is { } list)
        {
            for (int j = 0; j < list.Count; j++)
                if (list[j].Damage > topNpcDmg) topNpcDmg = list[j].Damage;
        }

        if (topNpcDmg > maxDmg) return;  // NPC out-damaged every player → no loot, no roll
        // Nothing to roll — no table, or every line inert. Checked ahead of the loot-tag work below so a
        // drop-less mob (an ordinary state for trash) costs nothing extra on death.
        var dropTable = npcRec.Drops;
        if (dropTable is null || !dropTable.Any(d => d.IsLive)) return;

        // ── Who shared the kill ──────────────────────────────────────────────────
        // Everyone who dealt at least LootDamageContributionThreshold of the top damage, is still
        // playing, and is still watching this map. Worked out ONCE and used for everything below: the
        // per-item tag rolls and the currency split both draw from this same set, so "who earned a
        // share of this kill" has exactly one definition.
        //
        // Party membership is deliberately irrelevant. Two strangers who both fought the mob both
        // earned a cut, and a party member who stood at the back did not.
        var contributors = new List<int>();
        int topContributor = 0;

        if (maxDmg > 0)
        {
            // Clamp to 1 so 0-damage players never qualify: against a one-damage kill the fraction
            // truncates to 0, and a threshold of 0 lets everyone on the map roll.
            int threshold = Math.Max(1, (int)(maxDmg * Constants.LootDamageContributionThreshold));
            for (int i = 1; i <= _pm.Slots; i++)
            {
                if (mapNpc.DamageByPlayer[i] < threshold) continue;
                if (!_pm[i].IsPlaying || !_world.IsObserving(i, mapNum)) continue;
                contributors.Add(i);
                if (topContributor == 0 || mapNpc.DamageByPlayer[i] > mapNpc.DamageByPlayer[topContributor])
                    topContributor = i;
            }
        }

        // ── Roll the table ───────────────────────────────────────────────────────
        // EVERY LINE ROLLS INDEPENDENTLY, so a kill can yield nothing, one thing, or several. Chance is a
        // direct percent (1 = 1%, 50 = 50%, 100+ = always) and Rng.Percent() returns [0..99], so a line
        // lands when its roll is BELOW its chance.
        //
        // The guild perks keep their old meaning against the new shape: L1 lifts the rate of every line
        // (it was always "your drop rate", not "your one drop"), and L4's double is rolled PER LINE — one
        // lucky sword does not also double the gold, which would make the perk swing wildly with table
        // length rather than paying out per thing dropped.
        //
        // Both now read the TOP DAMAGE CONTRIBUTOR's guild rather than a tag winner's, because there is
        // no longer one winner per kill to ask. Whether a line lands at all is a property of the kill,
        // so it needs a single answer, and the biggest contributor is a defensible and DETERMINISTIC
        // one — strictly less arbitrary than the coin-flip winner it replaces.
        bool dropRateperk = GuildPerks.IsActive(GuildOf(topContributor), Constants.GuildPerkLevelDropRate);
        bool doublePerk = GuildPerks.IsActive(GuildOf(topContributor), Constants.GuildPerkLevelDoubleDrop);

        var landed = new List<(int ItemNum, int Value, bool IsCurrency, bool Doubled)>();
        foreach (var entry in dropTable)
        {
            if (!entry.IsLive || entry.ItemNum > _world.Limits.Items) continue;
            int chance = entry.Chance;
            if (dropRateperk) chance += chance * Constants.GuildPerkDropRateBonusPercent / 100;
            if (Rng.Percent() >= chance) continue;

            bool isCurrency = _world.Items[entry.ItemNum].Type == ItemType.Currency;
            // Currency needs a stack of at least 1 or the drop is a no-op; other items ignore Value.
            int value = isCurrency ? Math.Max((short)1, entry.Quantity) : entry.Quantity;
            bool doubled = doublePerk && Rng.Percent() < Constants.GuildPerkDoubleDropChancePercent;
            if (doubled && isCurrency) value *= 2;
            landed.Add((entry.ItemNum, value, isCurrency, doubled));
        }
        if (landed.Count == 0) return;

        string npcName = npcRec.TrimmedName;

        // Currency announces the amount ("Bandit drops 10 Gold."); other items stay unquantified
        // ("Bandit drops Sword.") since one drop is a single item regardless of stack value.
        void SendDropNotice(int player, (int ItemNum, int Value, bool IsCurrency, bool Doubled) d)
        {
            string name = _world.Items[d.ItemNum].TrimmedName;
            if (d.IsCurrency)
            {
                SendMsg(player, ServerStrings.CombatSystem_LootNpcDropsCurrency, GameColor.Yellow, ChatChannel.Rewards,
                    ("NpcName", npcName), ("Amount", d.Value), ("Item", name));
            }
            else
            {
                SendMsg(player, ServerStrings.CombatSystem_LootNpcDrops, GameColor.Yellow, ChatChannel.Rewards,
                    ("NpcName", npcName), ("Item", name));
            }
        }

        // Spawns one stack on the corpse tile, tagged to `owner` (0 = untagged, free to anyone).
        // Everything lands on the SAME tile: scattering across neighbours was considered and rejected,
        // because the only thing it bought was stopping someone standing on the pile to deny it — and
        // pickup at range solves that outright. Several tagged stacks sharing a tile is fine; they are
        // told apart by their tag, not by where they sit.
        void Spawn(int itemNum, int value, int owner)
        {
            int slot = _items.SpawnItem(itemNum, value, mapNum, mapNpc.X, mapNpc.Y, ItemSource.NpcDropped, layer: mapNpc.Layer);
            if (slot > 0 && owner > 0) _items.TagMapItem(mapNum, slot, owner, Constants.LootTagDurationMs);
        }

        foreach (var d in landed)
        {
            string itemName = _world.Items[d.ItemNum].TrimmedName;

            // ── Currency: split across everyone who earned it ────────────────────
            // Gold is the one thing on the table that CAN be divided, so tagging the whole purse to a
            // single roll winner was the sharpest edge of the old rule. The split reuses the same
            // contributor set the tag rolls use rather than inventing a second notion of who shared
            // the kill — and pointedly is not the party, since a party is not who fought the mob.
            if (d.IsCurrency && contributors.Count > 1)
            {
                int[] shares = SplitCurrency(d.Value, contributors.Count);
                int leftover = d.Value % contributors.Count;

                // Whoever comes first takes the larger share, so when the purse does not divide evenly
                // the ORDER is the whole question — and it is settled by a roll, exactly like a sword.
                // Awarding the odd coin to the top damager was the first attempt and is worse: inside
                // this set everyone is within 5% of each other by definition, so "hit hardest" is noise
                // dressed up as merit, and it would hand the same player the extra coin every kill.
                var order = new List<int>(contributors);
                List<(int player, int roll)> remainderRolls = [];

                if (leftover > 0)
                {
                    // One winner drawn at a time out of a shrinking pool, so two spare coins go to two
                    // different people. ResolveLootRoll re-rolls ties, which is why this reuses it
                    // rather than sorting one round of rolls — a tie for gold should break the same way
                    // a tie for loot does.
                    var pool = new List<int>(contributors);
                    var winners = new List<int>(leftover);
                    for (int k = 0; k < leftover; k++)
                    {
                        int winner = ResolveLootRoll(pool, out var rolls);
                        if (k == 0) remainderRolls = rolls;   // the round everyone took part in
                        winners.Add(winner);
                        pool.Remove(winner);
                    }
                    order = [.. winners, .. pool];
                }

                for (int i = 0; i < order.Count; i++)
                {
                    if (shares[i] <= 0) continue;         // no empty stacks for a purse too small to go round
                    Spawn(d.ItemNum, shares[i], order[i]);

                    // Reported before the share, so "a roll happened" arrives ahead of "here is why
                    // yours is smaller than theirs" rather than after it.
                    if (remainderRolls.Count > 0)
                    {
                        string rollList = string.Join(", ", remainderRolls.Select(r =>
                            $"{(r.player == order[i] ? "You" : _pm[r.player].Char.Name.Trim())} ({r.roll})"));
                        SendMsg(order[i], ServerStrings.CombatSystem_LootRolling, GameColor.Yellow, ChatChannel.Rewards,
                            ("Item", itemName), ("Rolls", rollList));
                    }

                    SendMsg(order[i], ServerStrings.CombatSystem_LootCurrencySplit, GameColor.Yellow, ChatChannel.Rewards,
                        ("NpcName", npcName), ("Amount", d.Value), ("Item", itemName),
                        ("Ways", order.Count), ("Share", shares[i]));
                }
                continue;
            }

            // ── Everything else: one owner, decided PER ITEM ─────────────────────
            // A kill that drops a sword and a potion can tag them to different people. The eligibility
            // gate is unchanged; what changed is that it is rolled once per thing dropped instead of
            // once per corpse, so a single unlucky roll no longer costs a contributor the entire kill.
            int owner = 0;
            if (contributors.Count == 1)
            {
                owner = contributors[0];
                SendDropNotice(owner, d);
            }
            else if (contributors.Count > 1)
            {
                owner = ResolveLootRoll(contributors, out var rolls);
                string winnerName = _pm[owner].Char.Name.Trim();
                foreach (var (player, _) in rolls)
                {
                    string rollList = string.Join(", ", rolls.Select(r =>
                        $"{(r.player == player ? "You" : _pm[r.player].Char.Name.Trim())} ({r.roll})"));
                    SendDropNotice(player, d);
                    SendMsg(player, ServerStrings.CombatSystem_LootRolling, GameColor.Yellow, ChatChannel.Rewards,
                        ("Item", itemName), ("Rolls", rollList));
                    if (player == owner)
                        SendMsg(player, ServerStrings.CombatSystem_LootWon, GameColor.BrightGreen, ChatChannel.Rewards,
                            ("Item", itemName), ("Seconds", Constants.LootTagDurationMs / 1000));
                    else
                        SendMsg(player, ServerStrings.CombatSystem_LootLost, GameColor.Yellow, ChatChannel.Rewards,
                            ("WinnerName", winnerName), ("Item", itemName));
                }
            }

            Spawn(d.ItemNum, d.Value, owner);

            // L4 double-drop of a non-currency item: a second identical copy on the same tile, and
            // UNTAGGED — a free bonus on the ground rather than more of the winner's pile. (Currency
            // was doubled in place above, before any split, so the whole party shares the perk.)
            if (d.Doubled && !d.IsCurrency)
                Spawn(d.ItemNum, d.Value, owner: 0);
        }
        _items.EnqueueSaveDroppedItems(mapNum);
    }

    /// <summary>
    /// Divide a currency drop among the players who earned a share of the kill.
    ///
    /// <para>Returns one amount per recipient, in the order they were given. This decides the SHAPE of
    /// the split and nothing about who stands where — the caller orders the recipients, and does it by
    /// rolling, so keeping the arithmetic separate from the draw is what makes the arithmetic
    /// testable.</para>
    ///
    /// <para><b>The remainder rule:</b> an even share each, then the leftover coins one apiece to
    /// whoever comes first. Gold is integral, so three players splitting 10 must either lose a coin or
    /// invent one; the odd coin goes to a roll winner, which is the same answer the engine already
    /// gives for a sword two people both want.</para>
    ///
    /// <para>It degrades correctly when the purse is smaller than the party: 3 gold among 4 pays three
    /// of them a coin each and the fourth nothing, rather than rounding everybody to zero. The caller
    /// skips a zero rather than spawning an empty stack.</para>
    ///
    /// <para><b>Conserves the total exactly</b> — nothing is created and nothing is destroyed, which
    /// is the property worth pinning, since this is the only place in the engine that divides
    /// currency.</para>
    /// </summary>
    public static int[] SplitCurrency(int total, int recipients)
    {
        if (recipients <= 0) return [];
        if (total <= 0) return new int[recipients];

        int share = total / recipients;
        int leftover = total % recipients;

        var shares = new int[recipients];
        for (int i = 0; i < recipients; i++) shares[i] = share + (i < leftover ? 1 : 0);
        return shares;
    }

    /// <summary>Zero a victim NPC's combat ledger — damage credits, attack-say dedup, and the
    /// new NPC-target fields — called after EXP/loot resolved and before the death broadcast.</summary>
    private static void ResetNpcCombatLedger(MapNpcRecord mapNpc)
    {
        mapNpc.ClearDamageCredit();
        mapNpc.LastAttackSayTarget = 0;
        mapNpc.LastAttackSayNpcTarget = 0;
        mapNpc.NpcTargetSpawnMap = 0;
        mapNpc.NpcTargetSpawnSlot = 0;
    }

    /// <summary>Shared death-side broadcast + slot cleanup.  Native: zero the slot, broadcast
    /// NpcDead, drop player click-targets, AND clear other NPCs whose NpcTarget pointed at the
    /// dead one.  Guest: float the kill packet, remove from MapTraversalNpcs, free the home slot
    /// + drop guest-keyed player targets, AND clear NPC-targets keyed to the guest's identity.</summary>
    private void BroadcastNpcDeathAndCleanup(int mapNum, MapNpcRecord mapNpc, int npcSlot, int dmg, bool isCrit)
    {
        if (mapNpc is TraversalNpcRecord t)
        {
            mapNpc.Hp = 0;
            SendTraversalState(t, damage: dmg, isCrit: isCrit, dead: true);
            _world.MapTraversalNpcs[mapNum].Remove(t);
            var home = _world.MapNpcs[t.SpawnMapNum, t.SpawnSlot];
            home.IsReservedSlot = false;
            home.Num = 0;
            home.Hp = 0;
            home.SpawnWait = Environment.TickCount64;
            DropPlayerTargetsOnTraversal(t.SpawnMapNum, t.SpawnSlot);
            ClearNpcTargetsForNpc(mapNum, t.SpawnMapNum, t.SpawnSlot);
        }
        else
        {
            int spawnMap = mapNum, spawnSlot = npcSlot;
            mapNpc.Num = 0;
            mapNpc.Hp = 0;
            mapNpc.SpawnWait = Environment.TickCount64;
            SendToMap(_world, mapNum, new NpcDeadPacket { MapNum = mapNum, NpcSlot = npcSlot, Damage = dmg, IsCrit = isCrit });
            DropPlayerTargetsOnNpcSlot(mapNum, npcSlot);
            ClearNpcTargetsForNpc(mapNum, spawnMap, spawnSlot);
        }
    }

    /// <summary>Clear every NPC's lock on a now-dead NPC identity.  Sweeps the 9-map area around
    /// the death tile (where attackers could still be holding the target) plus all game-wide guests
    /// (a chaser could have followed across many seams).  Broadcasts an NpcTargetPacket with the
    /// surviving target state so clients drop the combat outline appropriately.  Also used by the
    /// guest-returns-home path so other NPCs don't silently re-bind to the freshly respawned native.</summary>
    public void ClearNpcTargetsForNpc(int deathMap, int deathSpawnMap, int deathSpawnSlot)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, deathMap);
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var mn = _world.MapNpcs[m, s];
                    // Void any combat credit this NPC holds against the dead identity — otherwise a
                    // respawn into the same slot inherits it and steals aggro (the "chases the just-
                    // respawned imp it remembers fighting" bug).  Done for EVERY swept NPC, since a
                    // credit-holder is usually fighting a DIFFERENT target than the one that just died.
                    mn.RemoveNpcDamageBySource(deathSpawnMap, deathSpawnSlot);
                    if (mn.NpcTargetSpawnMap != deathSpawnMap || mn.NpcTargetSpawnSlot != deathSpawnSlot) continue;
                    mn.NpcTargetSpawnMap = 0;
                    mn.NpcTargetSpawnSlot = 0;
                    SendToMap(_world, m, new NpcTargetPacket { MapNum = m, NpcSlot = s, HasTarget = mn.Target != 0 });
                }
            }
        }
        // Guests are few — cheap game-wide sweep.
        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            var guests = _world.MapTraversalNpcs[m];
            for (int g = 0; g < guests.Count; g++)
            {
                var t = guests[g];
                t.RemoveNpcDamageBySource(deathSpawnMap, deathSpawnSlot);  // void stale credit, same as the native sweep above
                if (t.NpcTargetSpawnMap == deathSpawnMap && t.NpcTargetSpawnSlot == deathSpawnSlot)
                {
                    t.NpcTargetSpawnMap = 0;
                    t.NpcTargetSpawnSlot = 0;
                }
            }
        }
    }

    /// <summary>Broadcasts an NPC heal: native slot → NpcDamage with negative Damage (the
    /// client adds it to Hp and floats a green heal number, same path as a damage tick in
    /// reverse). Traversal guest → full state with Damage = -healed by identity. Caller has
    /// already mutated mapNpc.Hp and decided this is non-zero.</summary>
    public void BroadcastNpcHeal(int mapNum, MapNpcRecord mapNpc, int npcSlot, int healed, bool isCrit)
    {
        if (healed <= 0) return;
        if (mapNpc is TraversalNpcRecord t)
            SendTraversalState(t, damage: -healed, isCrit: isCrit);
        else if (npcSlot > 0)
            SendToMap(_world, mapNum, new NpcDamagePacket { MapNum = mapNum, NpcSlot = npcSlot, Damage = -healed, IsCrit = isCrit });
    }

    /// <summary>Reduces accumulated damage credit by <paramref name="healed"/> total HP,
    /// split proportionally across current contributors. Keeps the credit ledger and
    /// (MaxHp − currentHp) in sync so a later kill can't award EXP shares summing past
    /// 100% — a player who damaged for X then healed for Y nets X − Y of credit, and
    /// when a friend heals, each contributor's share shrinks pro-rata. No-op if no
    /// contributors (no credit to scale).</summary>
    public void ScaleDownNpcDamageCredit(MapNpcRecord mapNpc, int healed)
    {
        if (healed <= 0) return;
        long sumDmg = 0;
        for (int i = 1; i <= _pm.Slots; i++) sumDmg += mapNpc.DamageByPlayer[i];
        if (sumDmg <= 0) return;
        // Clamp: if the heal exceeds tracked credit (e.g. a contributor died and their
        // entry was cleared), there's nothing left to cancel out for — zero it all out.
        long reduce = Math.Min(healed, sumDmg);
        for (int i = 1; i <= _pm.Slots; i++)
        {
            int dmg = mapNpc.DamageByPlayer[i];
            if (dmg <= 0) continue;
            int delta = (int)(reduce * dmg / sumDmg);  // integer floor — total under-scales slightly, never over
            mapNpc.DamageByPlayer[i] = Math.Max(0, dmg - delta);
        }
    }

    // Picks one winner out of `all` by a d100 each, re-rolling among the tied leaders until one stands alone.
    private int ResolveLootRoll(List<int> all, out List<(int player, int roll)> finalRolls)
    {
        finalRolls = new List<(int player, int roll)>(all.Count);
        for (int i = 0; i < all.Count; i++) finalRolls.Add((all[i], 0));
        // Two working buffers swapped each round; allocated once, reused as winners narrow.
        var remaining = new List<int>(all);
        var nextRound = new List<int>(all.Count);
        while (remaining.Count > 1)
        {
            int best = 0;
            for (int i = 0; i < remaining.Count; i++)
            {
                int player = remaining[i];
                int roll = Rng.Next(1, Constants.LootRollSides + 1);
                int idx = finalRolls.FindIndex(x => x.player == player);
                finalRolls[idx] = (player, roll);
                if (roll > best) best = roll;
            }
            nextRound.Clear();
            for (int i = 0; i < remaining.Count; i++)
            {
                if (finalRolls[finalRolls.FindIndex(x => x.player == remaining[i])].roll == best)
                    nextRound.Add(remaining[i]);
            }

            (remaining, nextRound) = (nextRound, remaining);
        }
        return remaining[0];
    }
}

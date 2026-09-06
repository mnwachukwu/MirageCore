using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Player versus player: the gates that permit or refuse a strike (admin, safe zone,
/// level gap, party/guild friendly fire, contest truce), damage resolution, and the death
/// penalty a normal PvP kill carries.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Player vs Player ──────────────────────────────────────────────────────

    /// <summary>Checks shared PvP eligibility rules (admin, safe zone, level, party not included).
    /// Does not check adjacency, cooldown, map, or alive status — callers handle those.</summary>
    public PvpBlock GetPvpBlock(int attacker, int victim)
    {
        var ap = _pm[attacker].Char;
        var vp = _pm[victim].Char;
        // Monitor+ (any admin access) cannot engage in PvP at all, on either side — the access-gate rule
        // that pairs with "Monitor+ cannot join a guild". (Access is per-account, mirrored per char.)
        if (ap.Access > AdminLevel.Player) return PvpBlock.AttackerAdmin;
        if (vp.Access > AdminLevel.Player) return PvpBlock.VictimAdmin;
        // Safe zone protects when EITHER party's map is Safe (cross-map combat aware). An Arena map
        // is NOT protected — it permits open PvP like None — but Arena↔Safe stays blocked, since a
        // Safe map on either side trips this check regardless of what the other side is. A
        // grace-period PKer counts as effectively non-PK here so they get the same safe-zone
        // immunity as a normal player; the attacker sees the standard "This is a safe zone!" reply.
        // Active aggressors get the same safe-zone bypass as a non-grace PKer — guards treat them
        // as PK and players can hit them anywhere, matching the "no penalty for killing the player"
        // contract.
        long nowUtc = NowUtc;
        long nowTick = Environment.TickCount64;
        bool effectivelyPk = (vp.IsPk(nowUtc) && _pm[victim].PkGraceUntilUtc <= nowUtc)
                             || _pm[victim].IsAggressor(nowTick);
        if ((_world.MoralOf(ap.Map) == MapMoral.Safe || _world.MoralOf(vp.Map) == MapMoral.Safe) && !effectivelyPk)
            return PvpBlock.SafeZone;
        if (ap.Level < 10) return PvpBlock.AttackerLevel;
        if (vp.Level < 10) return PvpBlock.VictimLevel;
        // Territory-contest truce: participants may not PvP each other in the territory during setup/cooldown
        // — the contest window itself lifts this (AreContestOpponents drives the war combat instead). The two
        // suppressing phases are told apart because a truce before the contest and one after it are opposite
        // situations from where the player stands.
        if (_territory?.ContestTrucePhase(attacker, victim) is { } trucePhase)
            return trucePhase == ContestPhase.Cooldown ? PvpBlock.ContestSettled : PvpBlock.ContestTruce;
        return PvpBlock.None;
    }

    // Returns the key + format args for the per-block message, so callers can route through the
    // recipient-localizing SendMsg overloads (the key is resolved per recipient at send time).
    public static (string Key, (string K, object? V)[] Args) PvpBlockMessage(PvpBlock block, string victimName) => block switch
    {
        PvpBlock.AttackerAdmin => (ServerStrings.CombatSystem_AdminCannotAttack, []),
        PvpBlock.VictimAdmin => (ServerStrings.CombatSystem_TargetIsAdmin, [("VictimName", victimName)]),
        PvpBlock.SafeZone => (ServerStrings.CombatSystem_SafeZone, []),
        PvpBlock.AttackerLevel => (ServerStrings.CombatSystem_YouTooLowLevel, []),
        PvpBlock.VictimLevel => (ServerStrings.CombatSystem_TargetTooLowLevel, [("VictimName", victimName)]),
        PvpBlock.ContestTruce => (ServerStrings.CombatSystem_ContestTruce, []),
        PvpBlock.ContestSettled => (ServerStrings.CombatSystem_ContestSettled, []),
        _ => ("", [])
    };

    public static int PvpBlockColor(PvpBlock block) => block switch
    {
        PvpBlock.AttackerAdmin => GameColor.BrightBlue,
        PvpBlock.AttackerLevel => GameColor.DarkGray,
        PvpBlock.VictimLevel => GameColor.DarkGray,
        _ => GameColor.BrightRed,
    };

    /// <summary>Classifies the same-side relationship between two players for friendly-fire gating.
    /// Partymates and guildmates must not be able to harm each other, with one asymmetry: party
    /// protection is unconditional (partymates can't fight ANYWHERE, including the arena — this keeps
    /// organized team-vs-team arena matches free of teammate friendly fire), while GUILD protection
    /// carries an arena carve-out (on an Arena-moral map either side, guildmates may duel, since the
    /// arena is already stakes-free open PvP — mirrors the arena precedence in <see cref="IsWarKill"/>).
    /// Party is checked first, so it wins when a pair is both partymates and guildmates. Both PvP
    /// damage gates classify through here — the melee gate (<see cref="CanAttackPlayer"/>) and the
    /// spell sub-spell gate in SpellSystem — and each picks its own party/guild rejection message.
    /// Both call sites pass indices already validated as playing.</summary>
    public FriendlyRelation GetFriendlyRelation(int attacker, int victim)
    {
        // Party (a strict 2-person pair, so the single-index PartyPlayer compare is complete) — no
        // arena exception, checked first so it wins for a pair that is both party and guild.
        if (_pm[attacker].InParty && _pm[attacker].PartyPlayer == victim) return FriendlyRelation.Party;
        // Guild identity is the ServerPlayer.Guild mirror (0 = guildless); the != 0 guard is
        // load-bearing, or two guildless players would match on 0 == 0. Lifted on an arena map.
        if (_pm[attacker].Guild != 0 && _pm[attacker].Guild == _pm[victim].Guild
            && !IsOnArenaMap(attacker) && !IsOnArenaMap(victim))
        {
            return FriendlyRelation.Guild;
        }

        return FriendlyRelation.None;
    }

    public bool CanAttackPlayer(int attacker, int victim)
    {
        if (!_pm[attacker].IsPlaying || !_pm[victim].IsPlaying) return false;
        if (_pm[victim].Char.Hp <= 0) return false;
        if (_pm[victim].GettingMap) return false;
        // Observer mode on either side: one cannot raise a hand, the other cannot be reached.
        if (_pm[attacker].Char.GodMode || _pm[victim].Char.GodMode) return false;

        var ap = _pm[attacker].Char;
        var vp = _pm[victim].Char;

        long windMult = _world.WeatherOn(ap.Map) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (Environment.TickCount64 <= _pm[attacker].AttackTimer + Constants.PlayerAttackCooldownMs * windMult) return false;
        // Cross-map melee: adjacency is checked in world space; the helper also rejects
        // targets on maps the attacker can't observe.
        if (!IsFacingTargetAcrossMaps(ap.Map, ap.Dir, ap.X, ap.Y, vp.Map, vp.X, vp.Y)) return false;
        // Two-layer connect ("layer 1.5"): a bridge-top defender is out of reach of a ground attacker below (and
        // vice-versa) unless a legal step connects the two layers (e.g. at a ramp's foot).  A silent whiff.
        if (!MeleeLayerConnects(ap.Map, ap.X, ap.Y, ap.Layer, ap.Dir, vp.Layer)) return false;

        // Same-side protection is the highest-priority PvP gate — it applies regardless of PK
        // status, safe-zone, or level gap. Checked before GetPvpBlock so a friendly attack attempt
        // reports the party/guild reason, not (for example) "This is a safe zone!". Matches the
        // order used in the spell sub-spell branch. GetFriendlyRelation is the shared classifier.
        var relation = GetFriendlyRelation(attacker, victim);
        if (relation != FriendlyRelation.None)
        {
            SendMsg(attacker, relation == FriendlyRelation.Guild
                        ? ServerStrings.CombatSystem_CannotAttackGuild
                        : ServerStrings.CombatSystem_CannotAttackParty,
                    GameColor.BrightRed, ChatChannel.System);
            return false;
        }
        var block = GetPvpBlock(attacker, victim);
        if (block != PvpBlock.None)
        {
            var (pvpKey, pvpArgs) = PvpBlockMessage(block, vp.TrimmedName);
            SendMsg(attacker, pvpKey, PvpBlockColor(block), ChatChannel.System, pvpArgs);
            return false;
        }
        return true;
    }

    public void AttackPlayer(int attacker, int victim, int damage, bool isCrit = false)
    {
        if (!_pm[attacker].IsPlaying || !_pm[victim].IsPlaying || damage < 0) return;

        var ap = _pm[attacker].Char;
        int weapNum = ap.WeaponSlot > 0 ? ap.Inv[ap.WeaponSlot].Num : 0;
        string weap = weapNum > 0 ? _world.Items[weapNum].TrimmedName : "";

        BreakGraceForCombat(attacker, involvesPlayerOrGuard: true);
        ExecutePlayerDamage(attacker, victim, damage, weap, isCrit);
        _pm[attacker].AttackTimer = Environment.TickCount64;
    }

    public void ApplyPlayerDamage(int attacker, int victim, int dmg, bool isCrit = false)
    {
        if (!_pm[attacker].IsPlaying || !_pm[victim].IsPlaying || dmg < 0) return;
        if (_pm[attacker].Char.GodMode || _pm[victim].Char.GodMode) return;   // nothing an observer does or receives lands
        BreakGraceForCombat(attacker, involvesPlayerOrGuard: true);
        ExecutePlayerDamage(attacker, victim, dmg, weapName: "", isCrit: isCrit);
    }

    // Applies the standard player-death loss body (10% degrade + random non-equipped drop + 10% TNL EXP loss).
    // Attacker EXP gain is gated by the level gap, matching the doubled-penalty path. Full-protection-on-gap
    // is the caller's responsibility — only the non-PK-victim path wraps this with that wrapper today.
    private void ApplyNormalPlayerDeathPenalty(int attacker, int victim)
    {
        var ap = _pm[attacker].Char;
        var vp = _pm[victim].Char;

        // Caster parity: destroy prepared-spell-priced reagents (on top of weapon wear below) BEFORE drops,
        // so only the leftover reagents face the drop roll.
        DestroyCasterDeathReagents(victim, 10);
        // Drops run BEFORE durability damage so a piece that breaks on death is still treated as
        // equipped for this death's drop rules (a broken piece is then unequipped + kept, not swept
        // into the unequipped drop bucket) — order is load-bearing, don't swap.
        DropRandomNonEquippedInventory(victim);
        DegradeEquipped(victim, 10);

        // A PvP kill pays out of the victim's loss, so with no loss there is nothing to report and
        // nothing to pay — including the level-gap notice, which explains a share never on the table.
        long loss = ApplyExpLoss(victim, ExpFormulas.DeathExpLossNormal(vp.Level));
        if (loss > 0)
        {
            SendMsg(victim, ServerStrings.CombatSystem_ExpLoss, GameColor.BrightRed, ChatChannel.Rewards, ("Loss", loss));
            if (Math.Abs(ap.Level - vp.Level) >= Constants.PvpLevelGapMax)
                SendMsg(attacker, ServerStrings.CombatSystem_LevelGapNoExp, GameColor.BrightBlue, ChatChannel.Rewards);
            else
                DistributePvpKillExp(victim, loss, ap.Map);
        }
    }

    // Arena duel result broadcast: reaches the kill map's observers + both duelists, once each. The
    // victim is warped to spawn immediately after the kill, so they're messaged individually to
    // guarantee delivery even if they land outside the kill map's observable area. The attacker is
    // always an observer of the victim's map (you can't hit what you can't observe) and is never
    // warped, so the observer broadcast already covers them — we only strip the victim from it (via
    // the ...But overload) to avoid a double-send. Call BEFORE the warp, while vp.Map is the kill map.
    private void BroadcastArenaDuelResult(int attacker, int victim)
    {
        var ap = _pm[attacker].Char;
        var vp = _pm[victim].Char;
        var meta = new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice);
        ChatToMapBut(_world, vp.Map, victim, ServerStrings.CombatSystem_ArenaDuelWin, meta,
            ("AttackerName", ap.TrimmedName), ("VictimName", vp.TrimmedName));
        _dispatcher.SendLocalizedChatTo(victim, ServerStrings.CombatSystem_ArenaDuelWin, meta,
            ("AttackerName", ap.TrimmedName), ("VictimName", vp.TrimmedName));
    }

    private void ExecutePlayerDamage(int attacker, int victim, int damage, string weapName, bool isCrit = false)
    {
        BreakGraceForCombat(victim, involvesPlayerOrGuard: true);

        var ap = _pm[attacker].Char;
        var vp = _pm[victim].Char;

        // Blood: deposit on the victim's tile (still their pre-warp tile even on a kill); a kill always splats.
        _blood.Deposit(vp.Map, vp.X, vp.Y, Constants.BloodDepositStrength(damage, vp.MaxHp, vp.Hp), layer: vp.Layer);

        if (damage >= vp.Hp)
        {
            _pm[victim].DamageByPlayer[attacker] += vp.Hp;  // credit remaining HP, not overkill
            vp.Hp = 0;
            SendToMapBut(_world, vp.Map, victim, PacketBuilder.SendHp(victim, 0, vp.MaxHp, showFloat: true, isCrit: isCrit, damage: damage));
            _dispatcher.SendTo(victim, PacketBuilder.SendHp(victim, 0, vp.MaxHp));

            SendYouHitMsg(attacker, vp.TrimmedName, weapName, damage, GameColor.White);
            SendTheyHitYouMsg(victim, ap.TrimmedName, weapName, damage, GameColor.BrightRed);
            // Arena: a kill where either party is on an Arena map has no stakes — announce the duel to
            // the kill map + both duelists (see BroadcastArenaDuelResult) instead of the global murder
            // feed. Computed here so the arena branch below reuses it.
            bool arenaKill = _world.MoralOf(ap.Map) == MapMoral.Arena || _world.MoralOf(vp.Map) == MapMoral.Arena;
            // Guild-war death: decided by the CREDITED killer (most damage dealt), independent of the
            // last-hit attacker and of PK status. It takes priority over the normal PK/non-PK penalties below;
            // arena (no stakes) still wins over it. victimBearsWarCost is false for a one-sided defender.
            // (Pre-initialized so they're definitely assigned even when the && short-circuits on an arena kill.)
            int warCreditedKiller = 0;
            bool victimBearsWarCost = false;
            bool territoryWarKill = false;
            bool warKill = !arenaKill && IsWarKill(victim, out warCreditedKiller, out victimBearsWarCost, out territoryWarKill);
            if (arenaKill)
            {
                BroadcastArenaDuelResult(attacker, victim);
            }
            else if (!warKill)   // war death readouts ride the War channel; no murder feed
            {
                _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_PlayerKilledBy,
                    new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                    ("VictimName", vp.TrimmedName), ("AttackerName", ap.TrimmedName));
            }

            // Capture aggressor state BEFORE the victim cleanup zeroes PvpAttackerUntil — drives
            // the "killing an aggressor is clean" branch below and the post-warp clear broadcast.
            long nowTickKill = Environment.TickCount64;
            bool victimWasAggressor = _pm[victim].IsAggressor(nowTickKill);
            long nowUtcPk = NowUtc;
            if (arenaKill)
            {
                // No stakes: no EXP loss, no drops, no durability damage, no PK/aggressor flag. Tell the
                // loser they lost nothing; the shared cleanup + warp + full-vitals restore below still run,
                // so they respawn ready to go again.
                SendMsg(victim, ServerStrings.CombatSystem_ArenaDeath, GameColor.BrightBlue, ChatChannel.Rewards);
            }
            else if (warKill)
            {
                // War death: worn-gear durability only (vault absorbs 75% when the victim's side bears it) —
                // no drops, no EXP loss, no PK/aggressor flag. Overrides the PK/non-PK penalties below.
                HandleWarDeath(warCreditedKiller, victim, victimBearsWarCost, territoryWarKill);
            }
            else if (vp.IsPk(nowUtcPk))
            {
                if (ap.IsPk(nowUtcPk))
                {
                    // PKer-vs-PKer: open-PvP duel between flagged players. Normal (non-doubled) penalty
                    // applies — and the level-gap full-protection wrapper deliberately doesn't (both
                    // chose to be PKers). Attacker EXP is still gated by the gap. No timer reduction
                    // (this death doesn't pay the price) and no flag change on the attacker.
                    ApplyNormalPlayerDeathPenalty(attacker, victim);
                }
                else
                {
                    // PK victims have no level-gap exemption — always suffer full penalties.
                    SendMsg(victim, ServerStrings.CombatSystem_PkDeathPenalty, GameColor.BrightRed, ChatChannel.System);
                    // Caster parity BEFORE drops (destroy prepared-spell-priced reagents; the rest drop).
                    DestroyCasterDeathReagents(victim, 20);
                    // Drops run BEFORE durability damage so a piece that breaks on death still gets the
                    // equipped drop CHANCE rather than being force-dropped as unequipped (see
                    // DegradeEquipped) — order is load-bearing, don't swap.
                    DropNonEquippedInventory(victim);
                    DropRandomEquipped(victim);
                    DegradeEquipped(victim, 20);

                    long loss = ApplyExpLoss(victim, ExpFormulas.DeathExpLossPk(vp.Level));
                    if (loss > 0)   // nothing lost, so nothing to report and nothing to transfer
                    {
                        SendMsg(victim, ServerStrings.CombatSystem_ExpLoss, GameColor.BrightRed, ChatChannel.Rewards, ("Loss", loss));
                        if (Math.Abs(ap.Level - vp.Level) >= Constants.PvpLevelGapMax)
                            SendMsg(attacker, ServerStrings.CombatSystem_LevelGapNoExp, GameColor.BrightBlue, ChatChannel.Rewards);
                        else
                            DistributePvpKillExp(victim, loss, ap.Map);
                    }

                    long reducedExpiry = vp.PkExpiryUtc - Constants.PkKillReductionSeconds;
                    if (reducedExpiry <= nowUtcPk)
                    {
                        vp.PkExpiryUtc = 0;
                        SendToMap(_world, vp.Map, PacketBuilder.PlayerData(victim, vp, vp.Map, _pm[victim].PkGraceUntilUtc, _pm[victim].AggressorUntilUtcNow));
                        _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_VictimPaidPrice,
                            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                            ("VictimName", vp.TrimmedName));
                    }
                    else
                    {
                        vp.PkExpiryUtc = reducedExpiry;
                        SendToMap(_world, vp.Map, PacketBuilder.PlayerData(victim, vp, vp.Map, _pm[victim].PkGraceUntilUtc, _pm[victim].AggressorUntilUtcNow));
                        _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_VictimSufferingPenalty,
                            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                            ("VictimName", vp.TrimmedName));
                    }
                }
                // Killing a PK player does NOT flag the attacker.
            }
            else
            {
                if (Math.Abs(ap.Level - vp.Level) >= Constants.PvpLevelGapMax)
                {
                    // Level gap: EXP, equipment, and items all protected for non-PK victims.
                    SendMsg(victim, ServerStrings.CombatSystem_LevelGapProtected, GameColor.BrightBlue, ChatChannel.Rewards);
                    SendMsg(attacker, ServerStrings.CombatSystem_LevelGapNoExp, GameColor.BrightBlue, ChatChannel.Rewards);
                }
                else
                {
                    ApplyNormalPlayerDeathPenalty(attacker, victim);
                }

                if (victimWasAggressor)
                {
                    // Aggressor kill: clean — no PK flag for the attacker. Victim still pays the
                    // normal penalty applied above. Announce the free kill so it doesn't read as
                    // a silent murder, and broadcast nothing for the attacker (their PK state
                    // didn't change).
                    SendMsg(attacker, ServerStrings.CombatSystem_AggressorKillClean, GameColor.BrightGreen, ChatChannel.System, ("VictimName", vp.TrimmedName));
                }
                else
                {
                    bool wasAlreadyPk = ap.IsPk(nowUtcPk);
                    ap.PkExpiryUtc = (wasAlreadyPk ? ap.PkExpiryUtc : nowUtcPk) + Constants.PkFlagDurationSeconds;
                    // Becoming a PKer subsumes the aggressor flag: solid red replaces flashing.
                    _pm[attacker].PvpAttackerUntil = 0;
                    SendToMap(_world, ap.Map, PacketBuilder.PlayerData(attacker, ap, ap.Map, _pm[attacker].PkGraceUntilUtc, _pm[attacker].AggressorUntilUtcNow));
                    if (!wasAlreadyPk)
                    {
                        _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_BecamePk,
                            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                            ("AttackerName", ap.TrimmedName));
                    }
                }
            }

            _pm[victim].ClearDamageCredit();

            // Death ends combat like a natural expiry — capture the flag before zeroing so the combat-exit notice
            // can fire after the death broadcast. RegenerationSystem's falling-edge detector is bypassed here (we
            // zero WasInCombat directly, and it skips dead players), so it would otherwise never send that line.
            bool victimWasInCombat = _pm[victim].WasInCombat;
            _pm[victim].CombatExpiresAt = 0;
            _pm[victim].WasInCombat = false;
            _pm[victim].PvpAttackerUntil = 0;
            // Clear the attack cooldown so the respawned player is act-ready at the spawn tile (the client
            // mirrors this in ClearMapState).
            _pm[victim].AttackTimer = 0;
            ClearPlayerNpcContributions(victim, vp.Map);
            ClearNpcTargetsFor(vp.Map, victim);
            BroadcastPlayerDeathFx(victim);   // death animation at the tile the player fell on
            if (victimWasInCombat)   // combat-exit notice, AFTER the death broadcast, matching a natural expiry
                SendMsg(victim, ServerStrings.RegenerationSystem_CombatEnded, GameColor.BrightGreen, ChatChannel.System);
            // Enter the timed dead state instead of respawning: the victim stays a corpse here
            // until the timer elapses and they click Respawn (see RespawnPlayer). EnterDeadState's broadcast
            // carries aggressorUntilUtc=0, which also clears any non-PK aggressor flash. A war death
            // uses the flat war timer + on-map respawn.
            EnterDeadState(victim, warParticipant: warKill);

            ClearTargetIfMatches(attacker, 0, victim);
            if (_pm[victim].IsGhost)
                _joinLeave.ClearGhost(victim);
        }
        else
        {
            vp.Hp -= damage;
            _pm[victim].DamageByPlayer[attacker] += damage;
            SendToMap(_world, vp.Map, PacketBuilder.SendHp(victim, vp.Hp, vp.MaxHp, showFloat: true, isCrit: isCrit, msSinceCombat: VictimCombatStamp(victim)));

            SendYouHitMsg(attacker, vp.TrimmedName, weapName, damage, GameColor.White);
            SendTheyHitYouMsg(victim, ap.TrimmedName, weapName, damage, GameColor.BrightRed);
        }
    }
}

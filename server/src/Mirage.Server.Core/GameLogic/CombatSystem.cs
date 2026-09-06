using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Why a player-versus-player attack was refused, or <c>None</c> when it is allowed.</summary>
public enum PvpBlock { None, AttackerAdmin, VictimAdmin, SafeZone, AttackerLevel, VictimLevel, ContestTruce, ContestSettled }

/// <summary>Why two players are on the same side for friendly-fire gating (see
/// <see cref="CombatSystem.GetFriendlyRelation"/>). None = they may harm each other.</summary>
public enum FriendlyRelation { None, Party, Guild }

/// <summary>Resolves every damaging interaction in the world — player melee and spells, NPC melee and spells,
/// and NPC-versus-NPC fights — along with everything a landed hit drags behind it: the aggro ledger, PK and
/// PvP flagging, durability wear, blood, death, EXP and loot payout, and the guild-war bookkeeping a kill
/// feeds. Runs on the game thread, so it mutates world state lock-free.
///
/// <para>Split across concern-named partials: .Damage, .Procs, .Attack, .Pvp, .PlayerVsNpc, .NpcVsPlayer,
/// .NpcVsNpc, .Progression, .Costs, .GuildWar, .Aggro, .Messaging. This file holds the constructor and the
/// combat-state spine every path touches — the combat and aggressor timers, PK grace, and death/respawn.</para></summary>
public sealed partial class CombatSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly MovementSystem _movement;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly BloodSystem _blood;
    private readonly ObjectiveSystem _objectives;
    private readonly GuildSystem _guilds;
    private readonly GuildWarSystem _guildWar;
    private readonly GuildTerritorySystem _territory;

    public const long CombatDurationMs = 10_000;

    public CombatSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                        ItemSystem items, MovementSystem movement, JoinLeaveSystem joinLeave, BloodSystem blood,
                        ObjectiveSystem objectives, GuildSystem guilds, GuildWarSystem guildWar,
                        GuildTerritorySystem territory,
                        IClock? clock = null, IRandomSource? rng = null, ServerConfig? config = null)
        : base(dispatcher, ChatChannel.Combat, clock: clock, rng: rng, config: config)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _movement = movement;
        _joinLeave = joinLeave;
        _blood = blood;
        _objectives = objectives;
        _guilds = guilds;
        _guildWar = guildWar;
        _territory = territory;
    }

    // The guild a player's account belongs to, or null if guildless — for the guild-perk gates + guild XP
    // on the kill/loot paths. Resolves straight off GameWorld (no GuildSystem needed for a read), so it is
    // null-safe for guildless players even when _guilds is absent (unit tests).
    private GuildRecord? GuildOf(int index) => _world.Guilds.GetValueOrDefault(_pm[index].Guild);

    /// <summary>Broadcasts a no-damage combat outcome (block/dodge/zero-hit) to every observer of
    /// <paramref name="mapNum"/> so clients float localized text over the entity. Players and native
    /// slot NPCs resolve by <paramref name="index"/> (player index / NPC slot); traversal guests pass
    /// <paramref name="index"/> = 0 with their (<paramref name="x"/>,<paramref name="y"/>) tile.
    /// <paramref name="vital"/> applies only to <see cref="CombatTextKind.ZeroHit"/>.</summary>
    public void BroadcastCombatText(int mapNum, bool isNpc, int index, CombatTextKind kind, int x = 0, int y = 0, CombatVital vital = CombatVital.Hp) =>
        SendToMap(_world, mapNum,
            new CombatTextPacket { IsNpc = isNpc, Index = index, MapNum = mapNum, Kind = kind, X = x, Y = y, Vital = vital });

    /// <summary>Says why nothing happened, at most once every <see cref="GodModeNoticeIntervalMs"/>. Attack
    /// and cast are both held-key inputs, so an unthrottled line would bury the chat log the moment an
    /// observer leant on a key.</summary>
    public void SayGodModeRefusal(int index)
    {
        long now = Environment.TickCount64;
        if (now < _pm[index].GodModeNoticeAt) return;
        _pm[index].GodModeNoticeAt = now + GodModeNoticeIntervalMs;
        SendMsg(index, ServerStrings.CombatSystem_GodModeInert, GameColor.BrightRed, ChatChannel.System);
    }

    private const long GodModeNoticeIntervalMs = 5_000;

    public void MarkPlayerCombat(int index, long now, bool asAttacker)
    {
        bool wasInCombat = _pm[index].IsInCombat(now);
        _pm[index].CombatExpiresAt = now + CombatDurationMs;
        if (!wasInCombat)
        {
            _pm[index].WasInCombat = true;
            SendMsg(index, asAttacker ? ServerStrings.CombatSystem_YouInitiatedCombat : ServerStrings.CombatSystem_DrawnIntoCombat,
                    asAttacker ? GameColor.Yellow : GameColor.BrightRed, ChatChannel.System);
        }
        // Any combat hit — given or received, by player or NPC — keeps the aggressor 30s timer
        // alive. Only refreshes an existing flag; first-time lighting goes through MarkPvpInitiator.
        RefreshAggressor(index, now);
        // Push a combat-stamp refresh to the player so their client-side LastCombatMs (which
        // drives bar visibility and the enter/end-combat float text) stays aligned with the
        // server's CombatExpiresAt window.  Without this the client only stamps combat on
        // damage receipt (HandleSendHp's `delta < 0` branch) — which silently desyncs when
        // an attack lands for 0 damage (e.g. NPC cast vs a high-Int player whose magic
        // protection eats the hit), or when melee is blocked/dodged, or when a 0-Str hit
        // happens.  In those cases the server keeps extending combat but the client's 10 s
        // window expires, bars vanish, the "end combat" float fires, and no end-of-combat
        // notice arrives because the server still has the player in combat.  Sending a
        // bare SendHp with the unchanged HP + the authoritative msSinceCombat is the
        // cheapest carrier — client treats Hp == oldHp as a no-op except for stamping
        // LastCombatMs from the msSinceCombat field.
        var p = _pm[index].Char;
        _dispatcher.SendTo(index, PacketBuilder.SendHp(index, p.Hp, p.MaxHp,
            msSinceCombat: PacketBuilder.MsSinceCombat(_pm[index].CombatExpiresAt, now, CombatDurationMs)));
    }

    // Wire-format combat stamp for a victim's CURRENT combat window, attached to the showFloat
    // vital broadcasts below.  Observers don't receive the per-victim MarkPlayerCombat carrier,
    // and the client does not infer combat from a bare HP decrease (that would misread a
    // voluntary Sub-HP potion drain as combat), so this is what lights a victim's combat bar on
    // observer clients for a non-lethal hit.
    private int VictimCombatStamp(int victimIndex) =>
        PacketBuilder.MsSinceCombat(_pm[victimIndex].CombatExpiresAt, Environment.TickCount64, CombatDurationMs);

    // Grant a fresh post-respawn grace window to a PK player who just respawned.
    // Called from both the PvP- and NPC-kill respawn paths after warp + vitals reset.
    public void BeginPostDeathGrace(int index)
    {
        var sp = _pm[index];
        var p = sp.Char;
        // Both death paths (PvP + NPC kill) funnel through here after warp + vitals reset — the single
        // post-death chokepoint. Persist the death this tick regardless of PK status: corpse drops,
        // EXP loss, durability damage and the PK timer all just mutated, so a hard-disconnect must not
        // roll them back. Placed BEFORE the non-PK early return below so every death is captured.
        _pm.MarkDirty(index);
        long nowUtc = NowUtc;
        if (!p.IsPk(nowUtc)) return;
        sp.PkGraceUntilUtc = nowUtc + Constants.PkGraceDurationSeconds;
        SendToMap(_world, p.Map, PacketBuilder.PlayerData(index, p, p.Map, sp.PkGraceUntilUtc, sp.AggressorUntilUtcNow));
        SendMsg(index, ServerStrings.CombatSystem_GraceSeconds, GameColor.BrightGreen, ChatChannel.System, ("Seconds", Constants.PkGraceDurationSeconds));
    }

    /// <summary>Tell observers a player just died at their current (pre-respawn) tile, so their clients can hold
    /// a delayed-death sprite in sync with a killing spell bolt (matching the NPC path). Call BEFORE the warp.</summary>
    private void BroadcastPlayerDeathFx(int victimIndex)
    {
        var vp = _pm[victimIndex].Char;
        SendToMap(_world, vp.Map,
            new PlayerDeathPacket { Index = victimIndex, MapNum = vp.Map, X = vp.X, Y = vp.Y, Dir = vp.Dir });
    }

    /// <summary>Enter the timed dead state instead of respawning: compute the respawn timer,
    /// mark the player a corpse in place (no warp; HP stays 0), persist it, and broadcast so observers
    /// render the corpse and the victim's client opens the death panel. Call after
    /// <see cref="BroadcastPlayerDeathFx"/>. <paramref name="warParticipant"/> true (a guild-war death)
    /// uses a flat timer that neither reads nor touches the escalating penalty counter, and flags
    /// <see cref="PlayerRecord.DiedInWar"/> so <see cref="RespawnPlayer"/> respawns on the death map.</summary>
    private void EnterDeadState(int victim, bool warParticipant = false)
    {
        var vp = _pm[victim].Char;
        long nowUtc = NowUtc;
        if (warParticipant)
        {
            vp.RespawnReadyUtc = nowUtc + Constants.GuildWarRespawnSeconds;   // flat; escalation untouched
            vp.DiedInWar = true;
        }
        else
        {
            int steps = DeathFormulas.NextPenaltySteps(vp.RespawnPenaltySteps, vp.LastDeathUtc, nowUtc);
            vp.RespawnPenaltySteps = steps;
            vp.LastDeathUtc = nowUtc;
            vp.RespawnReadyUtc = nowUtc + DeathFormulas.RespawnDelaySeconds(steps);
            vp.DiedInWar = false;
        }
        vp.Dead = true;
        vp.Hp = 0;
        _pm.MarkDirty(victim);   // persist the dead state (relogin re-prompts) + this death's drops / EXP loss
        // Observers render the corpse; the victim's own copy opens the death panel (it carries Dead +
        // RespawnReadyUtc). aggressorUntilUtc defaults to 0, so this also clears any flashing-name overlay.
        SendToMap(_world, vp.Map, PacketBuilder.PlayerData(victim, vp, vp.Map, _pm[victim].PkGraceUntilUtc));
    }

    /// <summary>Respawn a dead player once their timer has elapsed: warp them back, restore vitals, clear
    /// the dead state, and grace-protect. Public for the RespawnRequest handler; self-gates on
    /// <see cref="PlayerRecord.RespawnReadyUtc"/> so an early request is ignored.
    ///
    /// <para>Where they come back, in order:</para>
    /// <list type="number">
    /// <item>a guild-war death returns to the contested territory, or to the tile they fell on when the
    /// war has no bounded area;</item>
    /// <item>the boot point of the map they died on, resolved through its MapGroup — the map author's say
    /// over what dying there costs, which outranks anything the player bought;</item>
    /// <item>their own Inn-purchased respawn point;</item>
    /// <item>the server's configured spawn.</item>
    /// </list></summary>
    public void RespawnPlayer(int victim)
    {
        var vp = _pm[victim].Char;
        if (!vp.Dead) return;
        if (NowUtc < vp.RespawnReadyUtc) return;   // timer not up yet

        int respawnMap, respawnX, respawnY;
        if (vp.DiedInWar)
        {
            // War death. A territory war respawns at a random walkable tile in the contested territory;
            // a grudge war has no bounded area, so it's the death tile.
            if (vp.DiedInTerritory > 0 && _territory is not null &&
                _territory.TerritoryRespawnTile(vp.DiedInTerritory, out int tm, out int tx, out int ty))
            {
                respawnMap = tm;
                respawnX = tx;
                respawnY = ty;
            }
            else
            {
                respawnMap = vp.Map;
                respawnX = vp.X;
                respawnY = vp.Y;
            }
        }
        else if (_world.BootMapOf(vp.Map) > 0)
        {
            // The map they fell on says where dying there puts you, resolved through its MapGroup so one
            // dungeon declares one exit for all its maps. It outranks a purchased Inn point: the consequence
            // of dying in a place belongs to whoever authored the place.
            respawnMap = _world.BootMapOf(vp.Map);
            respawnX = _world.BootXOf(vp.Map);
            respawnY = _world.BootYOf(vp.Map);
        }
        else
        {
            (respawnMap, respawnX, respawnY) = Config.Spawn.HomeFor(vp);
        }
        vp.Dead = false;
        vp.DiedInWar = false;
        vp.DiedInTerritory = 0;
        vp.RespawnReadyUtc = 0;
        // Coming back is the one warp that may never be refused, so a respawn point naming a tile that no
        // longer exists is repaired onto real ground and the player is told their point is gone.
        var landing = _world.RepairPosition(respawnMap, respawnX, respawnY, Config.Spawn.HomeFor(vp));
        if (landing != (respawnMap, respawnX, respawnY))
            Notify(victim, ServerStrings.CombatSystem_RespawnPointMissing);
        (respawnMap, respawnX, respawnY) = landing;
        _movement.PlayerWarp(victim, respawnMap, respawnX, respawnY);   // re-broadcasts the live (Dead=false) player on the spawn map
        vp.Hp = vp.MaxHp;
        vp.Mp = vp.MaxMp;
        vp.Sp = vp.MaxSp;
        _dispatcher.SendTo(victim, PacketBuilder.SendHp(victim, vp.Hp, vp.MaxHp));
        _dispatcher.SendTo(victim, PacketBuilder.SendMp(victim, vp.Mp, vp.MaxMp));
        _dispatcher.SendTo(victim, PacketBuilder.SendSp(victim, vp.Sp, vp.MaxSp));
        _dispatcher.SendTo(victim, PacketBuilder.SendStats(vp));
        BeginPostDeathGrace(victim);   // PK spawn-grace + persist
    }

    // Clears grace unconditionally and notifies observers + the player. Used by the auto-expiry tick
    // and the threshold wrapper below.
    public void BreakGrace(int index)
    {
        var sp = _pm[index];
        if (sp.PkGraceUntilUtc == 0) return;
        sp.PkGraceUntilUtc = 0;
        var p = sp.Char;
        SendToMap(_world, p.Map, PacketBuilder.PlayerData(index, p, p.Map, 0, sp.AggressorUntilUtcNow));
        SendMsg(index, ServerStrings.CombatSystem_GraceEnded, GameColor.BrightRed, ChatChannel.System);
    }

    // Combat-driven grace break. involvesPlayerOrGuard==true always breaks; otherwise only when
    // the grace player is on a non-safe map (so safe-zone regular-mob skirmishes are shrugged off).
    public void BreakGraceForCombat(int index, bool involvesPlayerOrGuard)
    {
        var sp = _pm[index];
        if (sp.PkGraceUntilUtc == 0) return;
        if (!involvesPlayerOrGuard && _world.MoralOf(sp.Char.Map) == MapMoral.Safe) return;
        BreakGrace(index);
    }

    public void MarkNpcCombat(int mapNum, int npcSlot, long now) =>
        MarkNpcCombat(_world.MapNpcs[mapNum, npcSlot], now);

    public void MarkNpcCombat(MapNpcRecord mn, long now)
    {
        mn.CombatExpiresAt = now + CombatDurationMs;
        mn.WasInCombat = true;
    }

    public const int GuardGraceWarnLimit = 3;

    // Aggro DEF-weighting: a contributor's aggro score is its cumulative ledger damage times a weight
    // that rises with DEF, so damage from a higher-DEF attacker (player or NPC) counts for more and a
    // tanky front-liner naturally holds aggro over a squishier attacker dealing similar damage.  The
    // damage ledger itself is never modified — only how the target is SELECTED from it.
    // Base = the DEF that DOUBLES the weight: weight = 1 + max(def,0)/Base, so DEF==Base counts twice as
    // much as DEF==0, and DEF==0 gives weight 1.0 (plain highest-damage).  Same raw-DEF scale for
    // players and NPCs so the cross-side comparison stays valid.  Smaller = tanks pull harder; a very
    // large value effectively disables weighting.
    private const double AggroDefWeightBase = 25.0;

    // Aggro stickiness (hysteresis): the current target keeps aggro unless a DIFFERENT contributor's
    // weighted score exceeds the incumbent's by this factor.  1.25 = a challenger must out-threaten the
    // current target by 25% to steal it — damps the every-hit ping-pong between similarly-statted
    // attackers while still bailing out on a decisive aggro move.  1.0 disables stickiness (objective
    // best always wins); higher = stickier / harder to peel.
    private const double AggroStickinessFactor = 1.25;

    // DEF → aggro weight multiplier.  Monotonic increasing; DEF==0 → 1.0.  Clamp at 0 so a
    // (hypothetical) negative DEF can never pull the weight below 1.0 and zero out a live contributor's
    // score, which would false-trip the pick.Damage<=0 liveness gate in ReEvaluateAggro.
    private static double AggroWeight(double def) => 1.0 + Math.Max(def, 0.0) / AggroDefWeightBase;

    // Guard "Watch it!" grace gate. Returns true iff the swing should be absorbed (caller skips
    // aggro / AttackSay). On consume, increments the per-attacker warn count, fires the warning
    // chat + bubble, and stamps combat — the existing 10 s combat-exit cleanup (RunAiForMap →
    // ResetNativeNpc → ClearDamageCredit) is what zeroes the counter, so the same plumbing
    // that resets the damage ledger resets grace.
    //
    // Counter semantics: counter in (0, GuardGraceWarnLimit] = still in grace (skipped by
    // SelectAggroTargetEx so the player's damage doesn't drive an aggro flip while warnings
    // are still being issued).  Counter > GuardGraceWarnLimit = grace broken, player is a real
    // contributor for aggro purposes.  The exemption paths (non-guard, traversal, PK, 10%
    // override) either return false without touching the counter or jump it past the limit
    // immediately (override) so the next aggro re-eval includes the player as a real attacker.
    private bool ConsumeGuardGrace(int attacker, int mapNum, int npcSlot, MapNpcRecord mn, int dmg)
    {
        var npcRec = _world.Npcs[mn.Num];
        if (npcRec.Behavior != NpcBehavior.Guard) return false;
        if (!_pm[attacker].IsPlaying) return false;
        long nowUtc = NowUtc;
        if (_pm[attacker].Char.IsPk(nowUtc)) return false;
        if (dmg * 10 >= _world.EffectiveNpcMaxHp(npcRec))
        {
            // Big hit (>= 10% max HP) breaks grace immediately so subsequent aggro re-eval treats
            // the attacker as a real contributor.  Jumps past the limit rather than incrementing
            // by one — partial-grace state on top of an override would still skip the player.
            if (mn.WarnHitsByPlayer[attacker] <= GuardGraceWarnLimit)
                mn.WarnHitsByPlayer[attacker] = GuardGraceWarnLimit + 1;
            return false;
        }
        // Increment first so the counter and the SelectAggroTargetEx skip check stay in sync.
        // On the swing that crosses the limit (e.g. 3 → 4 with limit=3) the warning does NOT
        // fire — that swing is "grace just broke", and the caller falls through to set aggro.
        mn.WarnHitsByPlayer[attacker]++;
        if (mn.WarnHitsByPlayer[attacker] > GuardGraceWarnLimit) return false;

        string say = ServerStrings.ForPlayer(attacker, ServerStrings.CombatSystem_GuardGraceWarn);
        // Speech, not combat: a guard's "Watch it!" warning belongs in the Say channel, not buried in the Combat tab.
        SendMsg(attacker, ServerStrings.CombatSystem_NpcSays, GameColor.Npc, ChatChannel.Say,
            ("NpcName", npcRec.TrimmedName), ("Say", say));
        // Native: address bubble by (mapNum, npcSlot).  Guest: bubble carries the guest's
        // permanent (SpawnMap, SpawnSlot) identity since it doesn't occupy a current-map slot;
        // the client looks it up in TraversalNpcs and stamps the bubble on the same record.
        if (npcSlot > 0)
            _dispatcher.SendTo(attacker, PacketBuilder.NpcChatBubble(mapNum, npcSlot, say, kind: 0));
        else if (mn is TraversalNpcRecord tg)
            _dispatcher.SendTo(attacker, PacketBuilder.TraversalNpcChatBubble(tg.SpawnMapNum, tg.SpawnSlot, say, kind: 0));
        // Use the object overload — for a guest guard npcSlot=0 would mark the wrong slot record.
        MarkNpcCombat(mn, Environment.TickCount64);
        return true;
    }

    /// <summary>Light the aggressor flag on <paramref name="attacker"/> when they throw the first
    /// hit at a non-PK / non-aggressor target.  No-op when the attacker is already an aggressor
    /// (the refresh path inside <see cref="MarkPlayerCombat"/> handles extension) or when the
    /// victim is already flagged hostile (PK or aggressor — per spec, attacking those targets
    /// never marks you).  Broadcasts SendPlayerData on the off→on edge so observers' renderer
    /// starts the flashing-red name.</summary>
    public void MarkPvpInitiator(int attacker, int victim, long now)
    {
        var sp = _pm[attacker];
        if (sp.IsAggressor(now)) return;
        // Arena spars never light the aggressor flag — with no PvP stakes there, the flag would
        // wrongly follow a duelist out of the arena (guards treat them as PK, no safe-zone cover).
        // Keyed on either party's map, the same "an Arena is involved" rule as the stake-free kill.
        if (_world.MoralOf(sp.Char.Map) == MapMoral.Arena || _world.MoralOf(_pm[victim].Char.Map) == MapMoral.Arena) return;
        // War nullifies the normal PvP penalties between opponents — a declared war is consenting hostility,
        // not griefing — so attacking a war opponent never lights the aggressor flag (matches skipping the PK
        // flag on a war kill). Still bounded by the same safe-zone/guard rules that gate the attack itself.
        if (IsWarParticipant(attacker, victim)) return;
        // Territory-contest offensive license: a participant fighting inside the contested territory
        // during the live contest takes no aggressor/attack penalty for striking anyone there — a non-participant
        // included (who still eats the normal full death penalty, since they are not a war combatant).
        if (_territory?.IsActiveContestParticipant(attacker) == true) return;
        long nowUtc = NowUtc;
        var vp = _pm[victim].Char;
        if (vp.IsPk(nowUtc)) return;
        if (_pm[victim].IsAggressor(now)) return;
        sp.PvpAttackerUntil = now + Constants.AggressorDurationMs;
        long expiryUtc = sp.ToAggressorUntilUtc(now, nowUtc);
        SendToMap(_world, sp.Char.Map,
            PacketBuilder.PlayerData(attacker, sp.Char, sp.Char.Map, sp.PkGraceUntilUtc, expiryUtc));
        SendMsg(attacker, ServerStrings.CombatSystem_AggressorFlagged, GameColor.Yellow, ChatChannel.System);
    }

    /// <summary>Extend an already-active aggressor flag by the full 30s window and push a slim
    /// refresh packet so observers stay aligned with the rolling expiry.  No-op when the flag is
    /// inactive — only <see cref="MarkPvpInitiator"/> may light it.  Called from every
    /// <see cref="MarkPlayerCombat"/> (give or receive), so guards-on-aggressor and aggressor-
    /// fighting-back both keep the flag alive.</summary>
    private void RefreshAggressor(int index, long now)
    {
        var sp = _pm[index];
        if (!sp.IsAggressor(now)) return;
        sp.PvpAttackerUntil = now + Constants.AggressorDurationMs;
        long nowUtc = NowUtc;
        long expiryUtc = sp.ToAggressorUntilUtc(now, nowUtc);
        SendToMap(_world, sp.Char.Map,
            PacketBuilder.AggressorRefresh(index, expiryUtc));
    }

    /// <summary>Per-tick natural-expiry sweep.  Detects the (non-zero, now-passed) transition that
    /// IsAggressor doesn't expose, clears it, and broadcasts PlayerData so observers' name renderer
    /// stops flashing.  Also drops a player-facing notice so the aggressor knows their window
    /// closed.  Called from <see cref="RegenerationSystem.Tick"/> which already iterates every
    /// player each tick.</summary>
    public bool ExpireAggressorIfLapsed(int index, long now)
    {
        var sp = _pm[index];
        if (sp.PvpAttackerUntil == 0 || now < sp.PvpAttackerUntil) return false;
        sp.PvpAttackerUntil = 0;
        var p = sp.Char;
        SendToMap(_world, p.Map,
            PacketBuilder.PlayerData(index, p, p.Map, sp.PkGraceUntilUtc));
        SendMsg(index, ServerStrings.CombatSystem_AggressorCleared, GameColor.BrightGreen, ChatChannel.System);
        return true;
    }
}

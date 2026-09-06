using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// The interactive guild-war lifecycle: declaring war, returning a declaration into a
/// mutual war, and retracting a still one-sided declaration. Runs on the game thread (war packet handlers
/// dispatch there), so it mutates the mirrored <see cref="GuildRecord.Wars"/> lists lock-free and persists
/// each touched guild through <see cref="GuildSystem.SaveGuild"/>.
///
/// Authority: the Leader confirms war state changes directly; an Officer may only REQUEST them
/// — the request is QUEUED on the guild (<see cref="GuildRecord.WarRequests"/>) and nudged to leadership on
/// the Guild Officer channel, and the Leader later accepts (it executes) or denies it
/// (<see cref="ReviewRequest"/>); a Member cannot.
///
/// The daily war-maintenance debit lives in <see cref="GuildScheduleSystem"/> (unified with the 00:00
/// settlement, debits-before-credits). War COMBAT (durability-only deaths, attrition, resolution) and the
/// peace/cold/attrition end-conditions arrive in later chunks; this chunk is how a war STARTS and how a
/// one-sided grievance is dropped. All the pure math + mirror maintenance is in <see cref="GuildWarFormulas"/>.
/// </summary>
public sealed class GuildWarSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly GuildSystem _guilds;
    private readonly GuildTerritorySystem _territory;
    private readonly ILogger<GuildWarSystem> _logger;

    // Real-clock throttle for the go-live tick (warmup expiry is minute-granular, like PkExpiry/settlement).
    private const int CheckIntervalSeconds = 30;
    private long _lastCheckUtc;

    public GuildWarSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                          GuildSystem guilds, GuildTerritorySystem territory, ILogger<GuildWarSystem> logger,
                          IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _world = world;
        _pm = pm;
        _guilds = guilds;
        _territory = territory;
        _logger = logger;
    }

    private long UtcNow() => NowUtc;

    // ── Declare / return ────────────────────────────────────────────────────────

    /// <summary>Declare war on the guild at <paramref name="targetGuildIndex"/>. If that guild has already
    /// declared on the sender's guild, this instead RETURNS the declaration (free) and the war goes mutual.
    /// Leader acts directly; an Officer's attempt QUEUES the action for Leader review (and posts a
    /// leadership-channel nudge); a Member is refused. Returns true only when a war was actually
    /// declared/returned this call (false for a queued request, a refusal, or a validation failure) — the
    /// review flow reads this to know whether an accepted request executed.</summary>
    public bool DeclareWar(int index, int targetGuildIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return false;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return false;
        }

        var target = _guilds.GuildById(targetGuildIndex);
        if (target is null || target.Index == guild.Index)
        {
            Notify(index, ServerStrings.GuildWar_NoTarget);
            return false;
        }

        var existing = GuildWarFormulas.Find(guild, target.Index);
        bool isReturn = existing is { WeDeclared: false, TheyDeclared: true };
        // Already our aggression (one-sided or mutual) → can't re-declare; short-circuit for everyone
        // (an officer shouldn't queue a redundant request for a war that already exists).
        if (existing is not null && !isReturn)
        {
            Notify(index, ServerStrings.GuildWar_AlreadyAtWar, ("GuildName", target.Name));
            return false;
        }
        // Post-war re-declare cooldown (anti-pile-on) — a fresh declaration only; returning a live war is exempt.
        if (!isReturn)
        {
            long cd = GuildWarFormulas.RemainingCooldownSeconds(guild, target.Index, UtcNow());
            if (cd > 0)
            {
                Notify(index, ServerStrings.GuildWar_OnCooldown, ("GuildName", target.Name), ("Minutes", (int)Math.Ceiling(cd / 60.0)));
                return false;
            }
        }

        // Authority — resolved after the target so an officer's request names it.
        if (sp.GuildRank == GuildRank.Officer)
        {
            QueueRequest(guild, GuildWarRequestKind.Declare, target.Index, target.Name, sp, index,
                isReturn ? ServerStrings.GuildWar_OfficerReqReturn : ServerStrings.GuildWar_OfficerReqDeclare);
            return false;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return false;
        }

        // Leader executes directly.
        return isReturn ? ReturnDeclaration(index, guild, target, existing!)
                        : ExecuteFreshDeclaration(index, guild, target);
    }

    /// <summary>Declare war on a guild identified by NAME (the client-facing form — a player has no guild-index
    /// list to pick from). Resolves the name to a guild and runs the same <see cref="DeclareWar"/> logic
    /// (rank/level/cost re-validated there, and a return detected automatically).</summary>
    public bool DeclareWarByName(int index, string targetName)
    {
        var target = _guilds.GuildByName(targetName);
        if (target is null)
        {
            Notify(index, ServerStrings.GuildWar_NoTarget);
            return false;
        }
        return DeclareWar(index, target.Index);
    }

    // Fresh declaration (leader-authorized): level gate, concurrent-declaration cap, vault cost, warmup.
    private bool ExecuteFreshDeclaration(int index, GuildRecord guild, GuildRecord target)
    {
        if (guild.Level < Constants.GuildWarMinLevelToDeclare)
        {
            Notify(index, ServerStrings.GuildWar_NeedLevel);
            return false;
        }
        int outgoing = 0;
        foreach (var w in guild.Wars) if (w.WeDeclared) outgoing++;
        if (outgoing >= Constants.GuildWarMaxConcurrentDeclarations)
        {
            Notify(index, ServerStrings.GuildWar_TooMany, ("Max", Constants.GuildWarMaxConcurrentDeclarations));
            return false;
        }

        long cost = GuildWarFormulas.DeclareCost(guild.Level, target.Level);
        if (guild.VaultGold < cost)
        {
            Notify(index, ServerStrings.GuildWar_VaultCantAfford, ("Cost", cost));
            return false;
        }
        guild.VaultGold -= cost;   // a sink: the declaration cost is consumed, not transferred

        long now = UtcNow();
        long goLive = now + Constants.GuildWarWarmupSeconds;
        guild.Wars.Add(new GuildWar
        {
            OpponentIndex = target.Index, OpponentName = target.Name,
            WeDeclared = true, DeclaredUtc = now, GoLiveUtc = goLive, DeclareCost = cost,
        });
        target.Wars.Add(new GuildWar
        {
            OpponentIndex = guild.Index, OpponentName = guild.Name,
            TheyDeclared = true, GoLiveUtc = goLive,
        });
        GuildWarFormulas.RemoveRequest(guild, GuildWarRequestKind.Declare, target.Index);   // fulfilled
        _guilds.SaveGuild(guild);
        _guilds.SaveGuild(target);

        // Private notices now; the PUBLIC "declared war" announcement fires at go-live (see Tick), so a
        // grievance reciprocated during warmup goes straight to the mutual "elevated pitch" line instead.
        int warmupMin = Constants.GuildWarWarmupSeconds / 60;
        GuildNotice(guild.Index, ServerStrings.GuildWar_YouDeclared, ("GuildName", target.Name), ("Minutes", warmupMin));
        GuildNotice(target.Index, ServerStrings.GuildWar_DeclaredOnYou, ("GuildName", guild.Name), ("Minutes", warmupMin));
        _logger.LogInformation("Guild {A} declared war on {B} (cost {Cost}).", guild.Name, target.Name, cost);
        return true;
    }

    // Reciprocate a declaration made against us → mutual war (free; pops immediately, nullifying the
    // original declarer's daily maintenance since maintenance is waived once mutual).
    private bool ReturnDeclaration(int index, GuildRecord guild, GuildRecord target, GuildWar ourEntry)
    {
        if (guild.Level < Constants.GuildWarMinLevelToDeclare)
        {
            Notify(index, ServerStrings.GuildWar_NeedLevelReturn);
            return false;
        }

        long now = UtcNow();
        ourEntry.WeDeclared = true;
        ourEntry.DeclaredUtc = now;
        ourEntry.GoLiveUtc = Math.Min(ourEntry.GoLiveUtc, now);   // mutual is live at once (or already live)
        ourEntry.Announced = true;
        GuildWarFormulas.InitMutualAttrition(ourEntry, now);      // the tug-of-war meter starts now
        if (GuildWarFormulas.Find(target, guild.Index) is { } theirEntry)
        {
            theirEntry.TheyDeclared = true;
            theirEntry.GoLiveUtc = Math.Min(theirEntry.GoLiveUtc, now);
            theirEntry.Announced = true;
            GuildWarFormulas.InitMutualAttrition(theirEntry, now);
        }
        GuildWarFormulas.RemoveRequest(guild, GuildWarRequestKind.Declare, target.Index);   // a "declare" request resolved as a return
        _guilds.SaveGuild(guild);
        _guilds.SaveGuild(target);

        AnnounceWarPublic(ServerStrings.GuildWar_ReachesElevatedPitch, ("Guild1", target.Name), ("Guild2", guild.Name));
        _logger.LogInformation("Guild {A} returned {B}'s declaration; the war is now mutual.", guild.Name, target.Name);
        return true;
    }

    // ── Retract ──────────────────────────────────────────────────────────────────

    /// <summary>Retract a still one-sided declaration against <paramref name="opponentIndex"/>, allowed only
    /// after the retraction lock elapses. A mutual war can't be retracted (it ends via peace/attrition, a
    /// later chunk). Leader acts directly; an Officer's attempt QUEUES it for review; a Member is refused.
    /// Returns true only when a declaration was actually retracted this call.</summary>
    public bool RetractWar(int index, int opponentIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return false;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return false;
        }

        var war = GuildWarFormulas.Find(guild, opponentIndex);
        if (war is null || !war.WeDeclared)
        {
            Notify(index, ServerStrings.GuildWar_NotDeclaredByYou);
            return false;
        }

        if (sp.GuildRank == GuildRank.Officer)
        {
            QueueRequest(guild, GuildWarRequestKind.Retract, opponentIndex, war.OpponentName, sp, index,
                ServerStrings.GuildWar_OfficerReqRetract);
            return false;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return false;
        }

        if (war.TheyDeclared)
        {
            Notify(index, ServerStrings.GuildWar_CantRetractMutual);
            return false;
        }
        long remaining = war.DeclaredUtc + Constants.GuildWarRetractionLockSeconds - UtcNow();
        if (remaining > 0)
        {
            Notify(index, ServerStrings.GuildWar_RetractLocked, ("Minutes", (int)Math.Ceiling(remaining / 60.0)));
            return false;
        }

        string opponentName = war.OpponentName;
        var target = _guilds.GuildById(opponentIndex);
        if (target is not null)
        {
            GuildWarFormulas.Unlink(guild, target);
            SetRedeclareCooldown(guild, target);
            _guilds.SaveGuild(target);
        }
        else
        {
            guild.Wars.RemoveAll(w => w.OpponentIndex == opponentIndex);
        }

        GuildWarFormulas.RemoveRequest(guild, GuildWarRequestKind.Retract, opponentIndex);   // fulfilled
        _guilds.SaveGuild(guild);

        AnnounceWarPublic(ServerStrings.GuildWar_Retracted, ("Guild1", guild.Name), ("Guild2", opponentName));
        _logger.LogInformation("Guild {A} retracted its declaration against {B}.", guild.Name, opponentName);
        return true;
    }

    // ── Officer request queue (Leader accepts / denies) ──────────────────────────

    // Queue an officer's war action for Leader review (deduped by kind+target, capped), persist so the
    // leader's panel shows it, and nudge leadership on the Guild Officer channel.
    private void QueueRequest(GuildRecord guild, GuildWarRequestKind kind, int targetIndex, string targetName,
                              ServerPlayer requester, int requesterIndex, string officerNudgeKey, long amount = 0)
    {
        var result = GuildWarFormulas.TryQueueRequest(guild, kind, targetIndex, targetName,
            requester.Login, requester.Char.TrimmedName, UtcNow(), Constants.GuildWarMaxPendingRequests, amount);
        switch (result)
        {
            case WarRequestQueueResult.AlreadyPending:
                NotifyOk(requesterIndex, ServerStrings.GuildWar_RequestAlreadyPending);
                return;
            case WarRequestQueueResult.Full:
                Notify(requesterIndex, ServerStrings.GuildWar_RequestsFull);
                return;
        }
        _guilds.SaveGuild(guild);   // persist + broadcast so the leader's War panel shows the pending request
        _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, officerNudgeKey,
            new ChatMetadata(GameColor.GuildOfficer, ChatChannel.GuildOfficer),
            ("Name", requester.Char.TrimmedName), ("GuildName", targetName));
        NotifyOk(requesterIndex, ServerStrings.GuildWar_RequestSent);
    }

    /// <summary>Leader-only: accept or deny a pending officer war-request (addressed by kind + target).
    /// Accept re-runs the action as the Leader (which re-validates + executes, clearing the request on
    /// success); deny simply discards it. Either way leadership is notified on the Guild Officer channel.</summary>
    public void ReviewRequest(int index, GuildWarRequestKind kind, int targetIndex, bool accept)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }

        var req = GuildWarFormulas.FindRequest(guild, kind, targetIndex);
        if (req is null)
        {
            Notify(index, ServerStrings.GuildWar_NoSuchRequest);
            return;
        }
        string requesterName = req.RequesterName, targetName = req.TargetName;

        if (!accept)
        {
            GuildWarFormulas.RemoveRequest(guild, kind, targetIndex);
            _guilds.SaveGuild(guild);
            _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, ServerStrings.GuildWar_RequestDenied,
                new ChatMetadata(GameColor.GuildOfficer, ChatChannel.GuildOfficer),
                ("Name", requesterName), ("GuildName", targetName));
            return;
        }

        // Accept = perform the action as the Leader. On success the executor clears the request + persists;
        // on failure it has already told the leader why, and the request stays queued for a later retry.
        bool ok = kind switch
        {
            GuildWarRequestKind.Declare => DeclareWar(index, targetIndex),
            GuildWarRequestKind.Retract => RetractWar(index, targetIndex),
            GuildWarRequestKind.Peace => OfferPeace(index, targetIndex, req.Amount),
            GuildWarRequestKind.TerritoryChallenge => _territory.ChallengeTerritory(index, targetIndex),
            _ => false,
        };
        if (ok)
        {
            _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, ServerStrings.GuildWar_RequestAccepted,
                new ChatMetadata(GameColor.GuildOfficer, ChatChannel.GuildOfficer),
                ("Name", requesterName), ("GuildName", targetName));
        }
    }

    // ── Attrition, bankruptcy & resolution (mutual wars) ──────────────────────────

    /// <summary>Record a guild-war death for the attrition economy — MUTUAL wars only (a one-sided war has no
    /// attrition; it self-limits via daily tax). Swings the tug-of-war meter (a flat floor + the
    /// <paramref name="treasuryDamage"/> vault-gold drained, DR-adjusted per the victim's farm level), tracks
    /// the bankruptcy streak, and ends the war on an attrition-0 win or a
    /// <see cref="Constants.GuildWarBankruptcyStreak"/>-uncovered-death bankruptcy. Called from the combat
    /// war-death chokepoint; <paramref name="vaultCovered"/> = whether the victim's vault absorbed the death.</summary>
    public void RecordWarDeath(int victimIndex, int killerIndex, bool vaultCovered, long treasuryDamage)
    {
        var vGuild = _guilds.GuildOf(_pm[victimIndex]);
        var kGuild = _guilds.GuildOf(_pm[killerIndex]);
        if (vGuild is null || kGuild is null || vGuild.Index == kGuild.Index) return;
        var vWar = GuildWarFormulas.Find(vGuild, kGuild.Index);
        var kWar = GuildWarFormulas.Find(kGuild, vGuild.Index);
        if (vWar is null || kWar is null) return;
        if (!(vWar.WeDeclared && vWar.TheyDeclared)) return;   // mutual only

        long now = UtcNow();

        // Per-target DR: score off the victim's decayed stage, then advance it; the killer recovers a stage
        // (earning a kill keeps them worth attrition to the enemy). Swing = full treasury "war spend" (no DR)
        // + the DR-scaled base-death rate (floored at the DR minimum, so a naked/farmed death still counts).
        int stage = GuildWarFormulas.DecayedDrStage(_pm[victimIndex].WarDrStage, _pm[victimIndex].WarDrLastUtc, now);
        int score = GuildWarFormulas.AttritionScore(treasuryDamage, stage);
        _pm[victimIndex].WarDrStage = stage + 1;
        _pm[victimIndex].WarDrLastUtc = now;
        int killerStage = GuildWarFormulas.DecayedDrStage(_pm[killerIndex].WarDrStage, _pm[killerIndex].WarDrLastUtc, now);
        _pm[killerIndex].WarDrStage = Math.Max(1, killerStage - 1);
        _pm[killerIndex].WarDrLastUtc = now;

        // Zero-sum swing: the victim's side depletes, the killer's restores (capped at the pool). A new low
        // for the victim = the killer made real progress (feeds cold detection).
        vWar.Attrition = Math.Max(0, vWar.Attrition - score);
        kWar.Attrition = Math.Min(Constants.GuildWarAttritionPool, kWar.Attrition + score);
        if (vWar.Attrition < vWar.MinAttritionSeen)
        {
            vWar.MinAttritionSeen = vWar.Attrition;
            kWar.LastProgressUtc = now;
        }

        // Post-death standing readout: both guilds' level + current war score, to each combatant
        // (from their own side's perspective). Sent now so it reflects the post-death numbers even if this
        // death ends the war below.
        SendPostDeathReadout(victimIndex, vGuild, vWar, killerIndex, kGuild, kWar);

        // Bankruptcy short-circuit: a run of vault-uncovered deaths auto-loses the war.
        if (vaultCovered)
        {
            vWar.UncoveredDeathStreak = 0;
        }
        else
        {
            vWar.UncoveredDeathStreak++;
            // An urgent warning (red) on the private Guild War channel to spur donations.
            GuildWarNotice(vGuild.Index, ServerStrings.GuildWar_UncoveredDeath, GameColor.Warning, ("GuildName", kGuild.Name));
            if (vWar.UncoveredDeathStreak >= Constants.GuildWarBankruptcyStreak)
            {
                EndWarDecisive(kGuild, vGuild, ServerStrings.GuildWar_WonBankruptcy);   // killer wins
                return;
            }
        }

        if (vWar.Attrition <= 0)
        {
            EndWarDecisive(kGuild, vGuild, ServerStrings.GuildWar_WonAttrition);   // killer wins
            return;
        }

        _guilds.PersistGuild(vGuild);   // persist the meter without per-death broadcast spam
        _guilds.PersistGuild(kGuild);

        // Push the live meters to both sides so the War panel's tug-of-war bar + trend arrow animate
        // between full syncs (each perspective sees its own side as "ours"). Kept lightweight — no full
        // GuildInfo broadcast per death.
        _dispatcher.SendToGuild(vGuild.Index, new GuildWarAttritionPacket
        { OpponentIndex = kGuild.Index, OurAttrition = vWar.Attrition, TheirAttrition = kWar.Attrition });
        _dispatcher.SendToGuild(kGuild.Index, new GuildWarAttritionPacket
        { OpponentIndex = vGuild.Index, OurAttrition = kWar.Attrition, TheirAttrition = vWar.Attrition });
    }

    // End a mutual war decisively (attrition-0 / bankruptcy / a peace concession): pay the wager pot to the
    // winner (before unlink, while the escrow entries still exist), sever both sides, persist + broadcast the
    // change, and announce the outcome (winner then loser) + a member-visible pot readout.
    private void EndWarDecisive(GuildRecord winner, GuildRecord loser, string publicKey)
    {
        long pot = GuildWarFormulas.SettleWagerPot(winner, loser, winner);   // winner-take-all, pre-unlink
        GuildWarFormulas.Unlink(winner, loser);
        SetRedeclareCooldown(winner, loser);
        _guilds.SaveGuild(winner);
        _guilds.SaveGuild(loser);
        AnnounceWarPublic(publicKey, ("Guild1", winner.Name), ("Guild2", loser.Name));
        if (pot > 0) GuildNotice(winner.Index, ServerStrings.GuildWar_WonPot, ("GuildName", loser.Name), ("Gold", pot));
        _logger.LogInformation("Guild war ended: {Winner} defeated {Loser} (pot {Pot}).", winner.Name, loser.Name, pot);
    }

    // End a mutual war as a cold draw: return each side's own wager stake (before unlink), sever both sides,
    // persist + broadcast, announce the cold end.
    private void EndWarCold(GuildRecord a, GuildRecord b)
    {
        GuildWarFormulas.SettleWagerPot(a, b, null);   // draw — return each own stake, pre-unlink
        GuildWarFormulas.Unlink(a, b);
        SetRedeclareCooldown(a, b);
        _guilds.SaveGuild(a);
        _guilds.SaveGuild(b);
        AnnounceWarPublic(ServerStrings.GuildWar_ColdEnd, ("Guild1", a.Name), ("Guild2", b.Name));
        _logger.LogInformation("Guild war {A} vs {B} went cold (draw).", a.Name, b.Name);
    }

    // Set the post-war re-declare cooldown on both guilds when their war ends (anti-pile-on).
    private void SetRedeclareCooldown(GuildRecord a, GuildRecord b)
    {
        long now = UtcNow();
        long until = now + Constants.GuildWarRedeclareCooldownSeconds;
        GuildWarFormulas.SetCooldown(a, b.Index, until, now);
        GuildWarFormulas.SetCooldown(b, a.Index, until, now);
    }

    // ── Peace (concession) ───────────────────────────────────────────────────────

    /// <summary>Sue for peace on a MUTUAL war with <paramref name="opponentIndex"/> — a concession: the
    /// opponent may accept (they win + take the pot, war ends) or reject (war continues); we may withdraw it.
    /// Leader acts directly; an Officer's attempt queues it for Leader approval. With an ante already locked
    /// the plea just concedes it (<paramref name="offering"/> ignored); with NO ante the plea MUST carry a
    /// vault <paramref name="offering"/> (escrowed while on the table), which becomes the pot. Returns
    /// true only when the plea was actually placed.</summary>
    public bool OfferPeace(int index, int opponentIndex, long offering)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return false;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return false;
        }
        var war = GuildWarFormulas.Find(guild, opponentIndex);
        if (war is null)
        {
            Notify(index, ServerStrings.GuildWar_NotAtWar);
            return false;
        }
        if (!(war.WeDeclared && war.TheyDeclared))
        {
            Notify(index, ServerStrings.GuildWar_PeaceNeedsMutual);
            return false;
        }

        // With no ante locked, a plea for peace MUST carry a vault offering (it becomes the pot the accepter wins).
        bool anteLocked = war.AnteEscrow > 0;
        long offer = anteLocked ? 0 : offering;
        if (!anteLocked && offer <= 0)
        {
            Notify(index, ServerStrings.GuildWar_PeaceNeedsOffering);
            return false;
        }

        if (sp.GuildRank == GuildRank.Officer)
        {
            QueueRequest(guild, GuildWarRequestKind.Peace, opponentIndex, war.OpponentName, sp, index, ServerStrings.GuildWar_OfficerReqPeace, offer);
            return false;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return false;
        }

        if (war.PeaceOfferedByUs)
        {
            Notify(index, ServerStrings.GuildWar_PeaceAlreadyOffered, ("GuildName", war.OpponentName));
            return false;
        }

        // Escrow the plea offering (no-ante case only): set it aside from the vault, capped at 50% of it.
        if (!anteLocked)
        {
            long cap = GuildWarFormulas.MaxWager(guild.VaultGold);
            if (offer > cap)
            {
                Notify(index, ServerStrings.GuildWar_OfferingTooLarge, ("Max", cap));
                return false;
            }
            guild.VaultGold -= offer;
            war.PeaceEscrow = offer;
        }
        war.PeaceOfferedByUs = true;
        GuildWarFormulas.RemoveRequest(guild, GuildWarRequestKind.Peace, opponentIndex);
        _guilds.SaveGuild(guild);
        var target = _guilds.GuildById(opponentIndex);
        if (target is not null) _guilds.BroadcastGuildInfo(target.Index);   // refresh their panel (they see the incoming plea)
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_PeaceSought, GameColor.GuildWar, ("GuildName", war.OpponentName));
        if (target is not null) GuildWarNotice(target.Index, ServerStrings.GuildWar_PeaceSoughtByThem, GameColor.GuildWar, ("GuildName", guild.Name));
        return true;
    }

    /// <summary>Respond (Leader-only) to a pending plea for peace FROM <paramref name="opponentIndex"/> (the
    /// offerer, who is conceding): accept ends the war with US as the winner, reject leaves it running.</summary>
    public void RespondPeace(int index, int opponentIndex, bool accept)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }

        var offerer = _guilds.GuildById(opponentIndex);
        var theirWar = offerer is null ? null : GuildWarFormulas.Find(offerer, guild.Index);
        if (offerer is null || theirWar is null || !theirWar.PeaceOfferedByUs)
        {
            Notify(index, ServerStrings.GuildWar_NoPendingPeace);
            return;
        }

        if (accept)
        {
            // The offerer conceded; we (the accepter) win. EndWarDecisive pays the pot (ante escrows + the
            // offerer's peace offering) to us, then severs + cooldowns + announces.
            EndWarDecisive(guild, offerer, ServerStrings.GuildWar_PeaceAccepted);
        }
        else
        {
            theirWar.PeaceOfferedByUs = false;
            // Release the offerer's transient peace offering back to their vault (an ante stays locked).
            if (theirWar.PeaceEscrow > 0)
            {
                offerer.VaultGold += theirWar.PeaceEscrow;
                theirWar.PeaceEscrow = 0;
            }
            _guilds.SaveGuild(offerer);
            _guilds.BroadcastGuildInfo(guild.Index);   // the plea is gone from our panel too
            GuildWarNotice(offerer.Index, ServerStrings.GuildWar_PeaceRejected, GameColor.Warning, ("GuildName", guild.Name));
        }
    }

    /// <summary>Withdraw our own pending plea for peace with <paramref name="opponentIndex"/> (Leader-only).</summary>
    public void WithdrawPeace(int index, int opponentIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        var war = GuildWarFormulas.Find(guild, opponentIndex);
        if (war is null || !war.PeaceOfferedByUs)
        {
            Notify(index, ServerStrings.GuildWar_NoPendingPeace);
            return;
        }
        war.PeaceOfferedByUs = false;
        // Release our transient peace offering back to our vault (an ante stays locked).
        if (war.PeaceEscrow > 0)
        {
            guild.VaultGold += war.PeaceEscrow;
            war.PeaceEscrow = 0;
        }
        _guilds.SaveGuild(guild);
        var target = _guilds.GuildById(opponentIndex);
        if (target is not null) _guilds.BroadcastGuildInfo(target.Index);
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_PeaceWithdrawn, GameColor.GuildWar, ("GuildName", war.OpponentName));
        if (target is not null) GuildWarNotice(target.Index, ServerStrings.GuildWar_PeaceWithdrawnByThem, GameColor.GuildWar, ("GuildName", guild.Name));
    }

    // ── Wagers (consensual matched ante; MUTUAL wars only, Leader-only) ────────────

    /// <summary>Propose a matched ante to the opponent on a MUTUAL war (Leader-only) — up to
    /// <see cref="Constants.GuildWarWagerMaxVaultPercent"/>% of our vault, within the wager window, and only
    /// while no ante is locked yet. Nothing is escrowed until the opponent accepts; this just puts the proposal
    /// on their War panel.</summary>
    public void ProposeWager(int index, int opponentIndex, long amount)
    {
        if (ResolveMutualWar(index, opponentIndex, leaderOnly: true) is not { } ctx) return;
        var (guild, war, target) = (ctx.Guild, ctx.War, ctx.Target);
        if (war.AnteEscrow > 0)
        {
            Notify(index, ServerStrings.GuildWar_WagerActive);
            return;
        }
        if (!GuildWarFormulas.WagerWindowOpen(war, UtcNow()))
        {
            Notify(index, ServerStrings.GuildWar_WagerWindowClosed);
            return;
        }
        long cap = GuildWarFormulas.MaxWager(guild.VaultGold);
        if (amount <= 0 || amount > cap)
        {
            Notify(index, ServerStrings.GuildWar_WagerTooLarge, ("Max", cap));
            return;
        }

        war.WagerProposedByUs = amount;
        _guilds.SaveGuild(guild);
        if (target is not null) _guilds.BroadcastGuildInfo(target.Index);
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_WagerProposed, GameColor.GuildWar, ("GuildName", war.OpponentName), ("Gold", amount));
        if (target is not null) GuildWarNotice(target.Index, ServerStrings.GuildWar_WagerProposedByThem, GameColor.GuildWar, ("GuildName", guild.Name), ("Gold", amount));
    }

    /// <summary>Withdraw our own pending ante proposal (Leader-only) — nothing was escrowed yet, so this just
    /// clears the proposal from both panels.</summary>
    public void WithdrawWager(int index, int opponentIndex)
    {
        if (ResolveMutualWar(index, opponentIndex, leaderOnly: true) is not { } ctx) return;
        var (guild, war, target) = (ctx.Guild, ctx.War, ctx.Target);
        if (war.WagerProposedByUs <= 0)
        {
            Notify(index, ServerStrings.GuildWar_WagerNoneToWithdraw);
            return;
        }
        war.WagerProposedByUs = 0;
        _guilds.SaveGuild(guild);
        if (target is not null) _guilds.BroadcastGuildInfo(target.Index);
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_WagerWithdrawn, GameColor.GuildWar, ("GuildName", war.OpponentName));
        if (target is not null) GuildWarNotice(target.Index, ServerStrings.GuildWar_WagerWithdrawnByThem, GameColor.GuildWar, ("GuildName", guild.Name));
    }

    /// <summary>Reject the opponent's pending ante proposal (Leader-only) — clears their proposal, nothing
    /// escrowed.</summary>
    public void RejectWager(int index, int opponentIndex)
    {
        if (ResolveMutualWar(index, opponentIndex, leaderOnly: true) is not { } ctx) return;
        var (guild, war, target, theirWar) = (ctx.Guild, ctx.War, ctx.Target, ctx.TheirWar);
        if (target is null || theirWar is null || theirWar.WagerProposedByUs <= 0)
        {
            Notify(index, ServerStrings.GuildWar_WagerNonePending);
            return;
        }
        theirWar.WagerProposedByUs = 0;
        _guilds.SaveGuild(target);
        _guilds.BroadcastGuildInfo(guild.Index);   // clear the incoming-proposal line on our panel
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_WagerRejected, GameColor.Warning, ("GuildName", war.OpponentName));
        GuildWarNotice(target.Index, ServerStrings.GuildWar_WagerRejectedByThem, GameColor.Warning, ("GuildName", guild.Name));
    }

    /// <summary>Accept the opponent's pending ante proposal (Leader-only): both sides escrow the matched amount
    /// out of their vaults — re-validated affordable + within the 50% cap on BOTH sides (vaults move between
    /// propose and accept) — locking the pot until the war ends (winner-take-all, or returned on a cold draw).</summary>
    public void AcceptWager(int index, int opponentIndex)
    {
        if (ResolveMutualWar(index, opponentIndex, leaderOnly: true) is not { } ctx) return;
        var (guild, war, target, theirWar) = (ctx.Guild, ctx.War, ctx.Target, ctx.TheirWar);
        if (war.AnteEscrow > 0)
        {
            Notify(index, ServerStrings.GuildWar_WagerActive);
            return;
        }
        if (target is null || theirWar is null || theirWar.WagerProposedByUs <= 0)
        {
            Notify(index, ServerStrings.GuildWar_WagerNonePending);
            return;
        }
        if (!GuildWarFormulas.WagerWindowOpen(war, UtcNow()))
        {
            Notify(index, ServerStrings.GuildWar_WagerWindowClosed);
            return;
        }

        long amount = theirWar.WagerProposedByUs;
        if (amount > GuildWarFormulas.MaxWager(guild.VaultGold) || guild.VaultGold < amount)
        {
            Notify(index, ServerStrings.GuildWar_WagerCantAfford, ("Gold", amount));
            return;
        }
        if (amount > GuildWarFormulas.MaxWager(target.VaultGold) || target.VaultGold < amount)
        {   // the proposer's vault dropped below the stake — void their stale proposal
            theirWar.WagerProposedByUs = 0;
            _guilds.SaveGuild(target);
            Notify(index, ServerStrings.GuildWar_WagerOppCantAfford, ("GuildName", target.Name));
            return;
        }

        // Lock both antes into escrow (out of each vault) and clear any pending proposals on both sides.
        guild.VaultGold -= amount;
        war.AnteEscrow = amount;
        war.WagerProposedByUs = 0;
        target.VaultGold -= amount;
        theirWar.AnteEscrow = amount;
        theirWar.WagerProposedByUs = 0;
        _guilds.SaveGuild(guild);
        _guilds.SaveGuild(target);
        GuildWarNotice(guild.Index, ServerStrings.GuildWar_WagerAccepted, GameColor.Guild, ("GuildName", war.OpponentName), ("Gold", amount));
        GuildWarNotice(target.Index, ServerStrings.GuildWar_WagerAccepted, GameColor.Guild, ("GuildName", guild.Name), ("Gold", amount));
        _logger.LogInformation("Guild war wager locked: {A} vs {B}, {Amount} each.", guild.Name, target.Name, amount);
    }

    /// <summary>The common context for a MUTUAL-war wager action, seen from one guild's perspective:
    /// <see cref="Guild"/>/<see cref="War"/> are ours, <see cref="Target"/>/<see cref="TheirWar"/> the
    /// opponent's mirror (null only when their record is unloaded).
    ///
    /// <para>Named rather than a four-element tuple because the members pair up by type — two
    /// <c>GuildRecord</c>s and two <c>GuildWar</c>s — so a transposed destructure would compile and then
    /// escrow gold out of the wrong vault.</para></summary>
    private readonly record struct MutualWarContext(GuildRecord Guild, GuildWar War, GuildRecord? Target, GuildWar? TheirWar);

    // Resolve the wager context, messaging + returning null on any failure (not playing / guildless /
    // no war / not mutual / not leader).
    private MutualWarContext? ResolveMutualWar(int index, int opponentIndex, bool leaderOnly)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return null;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return null;
        }
        if (leaderOnly && sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.GuildWar_WagerNeedLeader);
            return null;
        }
        var war = GuildWarFormulas.Find(guild, opponentIndex);
        if (war is null)
        {
            Notify(index, ServerStrings.GuildWar_NotAtWar);
            return null;
        }
        if (!(war.WeDeclared && war.TheyDeclared))
        {
            Notify(index, ServerStrings.GuildWar_WagerNeedsMutual);
            return null;
        }
        var target = _guilds.GuildById(opponentIndex);
        var theirWar = target is null ? null : GuildWarFormulas.Find(target, guild.Index);
        return new MutualWarContext(guild, war, target, theirWar);
    }

    // ── Go-live tick ─────────────────────────────────────────────────────────────

    /// <summary>Fire the public "declared war" announcement the moment a one-sided grievance leaves its
    /// warmup. Driven from <c>GameLoop.AiTick</c>; self-throttled. (Mutual reciprocations announce
    /// immediately in <see cref="ReturnDeclaration"/>, so their entries are already marked announced.)</summary>
    public void Tick()
    {
        long now = UtcNow();
        if (now - _lastCheckUtc < CheckIntervalSeconds) return;
        _lastCheckUtc = now;

        foreach (var guild in _world.Guilds.Values)
        {
            bool changed = false;
            foreach (var war in guild.Wars)
            {
                if (war.Announced || !war.WeDeclared || war.TheyDeclared || now < war.GoLiveUtc) continue;
                war.Announced = true;
                changed = true;
                AnnounceWarPublic(ServerStrings.GuildWar_Declared, ("Guild1", guild.Name), ("Guild2", war.OpponentName));
                _logger.LogInformation("Guild war {A} -> {B} has gone live.", guild.Name, war.OpponentName);
            }
            if (changed) _guilds.SaveGuild(guild);
        }

        // Cold-war sweep: end any MUTUAL war where neither side has pushed the other to a new attrition low
        // for GuildWarColdSeconds (a stalemate or an abandoned war → a draw). Collect first — EndWarCold
        // mutates the Wars lists — and handle each pair once (guild.Index < opponent).
        List<(GuildRecord A, GuildRecord B)>? cold = null;
        foreach (var guild in _world.Guilds.Values)
        {
            foreach (var war in guild.Wars)
            {
                if (!(war.WeDeclared && war.TheyDeclared) || guild.Index >= war.OpponentIndex) continue;
                var opp = _guilds.GuildById(war.OpponentIndex);
                var oppWar = opp is null ? null : GuildWarFormulas.Find(opp, guild.Index);
                if (opp is null || oppWar is null) continue;
                long lastProgress = Math.Max(war.LastProgressUtc, oppWar.LastProgressUtc);
                if (GuildWarFormulas.IsCold(lastProgress, now))
                    (cold ??= new List<(GuildRecord, GuildRecord)>()).Add((guild, opp));
            }
        }
        if (cold is not null)
            foreach (var (a, b) in cold) EndWarCold(a, b);
    }

    // ── Notice helpers ───────────────────────────────────────────────────────────

    // Public war announcement (grudge declarations / retractions / elevated pitch / resolutions) — the
    // public War channel (a combat-group channel, opt-in like Combat).
    private void AnnounceWarPublic(string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.War, ChatChannel.War), args);

    // A guild-wide system notice (to all online members), carried on the Guild channel like other guild
    // notices (declaration made / received).
    private void GuildNotice(int guildId, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToGuild(guildId, key,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild), args);

    // A private war notice to a guild's members — the Guild War channel (peace negotiation, danger warnings).
    private void GuildWarNotice(int guildId, string key, int color, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToGuild(guildId, key,
            new ChatMetadata(color, ChatChannel.GuildWar), args);

    // Private post-death standing: both guilds' level + current war score, sent to the victim and
    // the killer, each from their own guild's perspective, on the Guild War channel.
    private void SendPostDeathReadout(int victimIndex, GuildRecord vGuild, GuildWar vWar,
                                      int killerIndex, GuildRecord kGuild, GuildWar kWar)
    {
        _dispatcher.SendLocalizedChatTo(victimIndex, ServerStrings.GuildWar_DeathScoreReadout,
            new ChatMetadata(GameColor.GuildWar, ChatChannel.GuildWar),
            ("Opp", kGuild.Name), ("MyLvl", vGuild.Level), ("MyScore", vWar.Attrition),
            ("OppLvl", kGuild.Level), ("OppScore", kWar.Attrition));
        _dispatcher.SendLocalizedChatTo(killerIndex, ServerStrings.GuildWar_DeathScoreReadout,
            new ChatMetadata(GameColor.GuildWar, ChatChannel.GuildWar),
            ("Opp", vGuild.Name), ("MyLvl", kGuild.Level), ("MyScore", kWar.Attrition),
            ("OppLvl", vGuild.Level), ("OppScore", vWar.Attrition));
    }
}

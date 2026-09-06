using Microsoft.Extensions.Logging;
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

/// <summary>
/// Territory war night: the weekly contest cadence, the challenge-registration queue, and the
/// resolution that sets each territory's <see cref="TerritoryRecord.ControllingGuild"/> + weeks-held (which
/// activates the chunk-3 income). Authority mirrors grudge wars — a Leader challenges directly; an Officer
/// REQUESTS it (queued as <see cref="GuildWarRequestKind.TerritoryChallenge"/>, approved via
/// <see cref="GuildWarSystem.ReviewRequest"/>).
///
/// A genuinely contested territory is decided by the LIVE king-of-the-hill contest — capture points, the
/// 20-min meter, setup/cooldown, no-PvP (see <see cref="TickContests"/> / <see cref="ScoreContestTick"/>).
/// The TRIVIAL cases short-circuit it: a lone claimant takes an unclaimed territory, an unchallenged
/// defender keeps, and an abandoned territory falls unclaimed.
///
/// Scheduling is wall-clock with catch-up-once (like the settlement): <see cref="NextWarNightUtc"/> persists
/// in environment.json; a slot missed during downtime fires once on boot, then reschedules.
/// </summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly GuildSystem _guilds;
    private readonly SpawnSystem _spawn;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly ILogger<GuildTerritorySystem> _logger;

    private const int CheckIntervalSeconds = 30;
    private long _lastCheckUtc;
    private long _nextWarNightUtc;

    // Live contests (runtime only; a restart abandons them). Scored on the 5s contest tick.
    private readonly List<TerritoryContest> _contests = new();
    private long _lastContestTickUtc;

    // SpawnSystem (NPC despawn/resume) is a plain constructor dep: it does not reference GuildTerritorySystem
    // back, so this stays acyclic. The reverse reads (entry warnings, spawn suppression) go through
    // GameWorld.ContestZones, so those systems need no reference here.
    public GuildTerritorySystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                                GuildSystem guilds, SpawnSystem spawn,
                                IPersistenceService persistence, IBackgroundPersistence bg,
                                ILogger<GuildTerritorySystem> logger,
                                IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _world = world;
        _pm = pm;
        _guilds = guilds;
        _spawn = spawn;
        _persistence = persistence;
        _bg = bg;
        _logger = logger;
    }

    private long UtcNow() => NowUtc;

    /// <summary>UTC-seconds of the next scheduled war night; persisted in environment.json (read by
    /// GameLoop.PersistEnvironmentNow) and restored via <see cref="Seed"/> on boot.</summary>
    public long NextWarNightUtc => _nextWarNightUtc;

    /// <summary>Seed the scheduler from the persisted blob on boot. 0 = unscheduled → the first tick computes
    /// the next slot (no immediate resolution).</summary>
    public void Seed(long nextWarNightUtc) => _nextWarNightUtc = nextWarNightUtc;

    // ── Scheduler ─────────────────────────────────────────────────────────────
    public void Tick()
    {
        long nowUtc = UtcNow();

        // War-night boundary (coarse throttle): start the night when its slot arrives.
        if (nowUtc - _lastCheckUtc >= CheckIntervalSeconds)
        {
            _lastCheckUtc = nowUtc;
            if (_nextWarNightUtc == 0)
            {
                Reschedule();                    // first boot: just schedule
            }
            else if (nowUtc >= _nextWarNightUtc)
            {
                ResolveWarNight();
                Reschedule();
            }  // due (or missed) → fire once
        }

        // Live contests score/advance on the 5s tick (only while any are running).
        if (_contests.Count > 0 && nowUtc - _lastContestTickUtc >= Constants.TerritoryContestTickSeconds)
        {
            _lastContestTickUtc = nowUtc;
            TickContests(nowUtc);
        }
    }

    private void Reschedule()
    {
        var slot = TerritoryFormulas.NextWarNight(Clock.LocalNow, Config.Schedule.WarNightDay, Config.Schedule.WarNightHour);
        _nextWarNightUtc = ((DateTimeOffset)slot).ToUnixTimeSeconds();
    }

    // ── Challenge registration ────────────────────────────────────────────────
    /// <summary>Register the sender's guild to contest the territory at <paramref name="territoryIndex"/> at
    /// the next war night. Leader executes; an Officer's attempt queues a leadership request; a Member is
    /// refused. Returns true only when a challenge was actually registered (so the review flow knows an
    /// accepted request executed). The cost is a non-refundable sink from the guild vault.</summary>
    public bool ChallengeTerritory(int index, int territoryIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return false;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return false;
        }

        var group = _world.MapGroups.GetValueOrDefault(territoryIndex);
        if (group is null || !group.Territory)
        {
            Notify(index, ServerStrings.GuildTerritory_NotATerritory);
            return false;
        }
        var terr = _world.TerritoryFor(territoryIndex);
        if (HasActiveContest(territoryIndex))
        {
            Notify(index, ServerStrings.GuildTerritory_ContestActive);
            return false;
        }
        if (terr.ControllingGuild == guild.Index)
        {
            Notify(index, ServerStrings.GuildTerritory_CantChallengeOwn);
            return false;
        }
        if (terr.Challengers.Count >= Constants.TerritoryMaxChallengers)
        {
            Notify(index, ServerStrings.GuildTerritory_ChallengersFull);
            return false;
        }
        if (IsChallengingAny(guild.Index))
        {
            Notify(index, ServerStrings.GuildTerritory_AlreadyChallenging);
            return false;
        }

        long cost = ChallengeCost(guild, terr, _pm[index].Char.Level);
        if (guild.VaultGold < cost)
        {
            Notify(index, ServerStrings.GuildTerritory_CantAfford, ("Cost", cost));
            return false;
        }

        // Authority — resolved after validation so an officer's queued request names a real, affordable target.
        if (sp.GuildRank == GuildRank.Officer)
        {
            QueueChallengeRequest(guild, group, sp, index);
            return false;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return false;
        }

        // Leader executes: charge the sink, apply the one-territory cap (abandon any held territory), register.
        guild.VaultGold -= cost;
        guild.WeeklyWarCosts += cost;   // vault dashboard: a challenge is a war spend
        AbandonOwnedTerritory(guild.Index, index);
        terr.Challengers.Add(guild.Index);
        GuildWarFormulas.RemoveRequest(guild, GuildWarRequestKind.TerritoryChallenge, territoryIndex);
        _guilds.SaveGuild(guild);
        SaveTerritory(terr);

        // Public War-channel announcement: contesting an owned claim, or laying claim to an unclaimed one.
        if (terr.ControllingGuild > 0)
        {
            AnnounceWarPublic(ServerStrings.GuildTerritory_ContestOwned,
                ("Guild", guild.Name), ("Owner", OwnerName(terr)), ("Territory", TerritoryName(group)));
        }
        else
        {
            AnnounceWarPublic(ServerStrings.GuildTerritory_LayClaim, ("Guild", guild.Name), ("Territory", TerritoryName(group)));
        }

        NotifyOk(index, ServerStrings.GuildTerritory_ChallengeOk, ("Territory", TerritoryName(group)));
        // The Territories table reads ChallengedByUs and the vault dashboard reads the gold, and both just
        // changed for the WHOLE guild — not only for whoever pressed the button. Without this an open panel
        // keeps showing the state from before the challenge until something else happens to refresh it.
        _guilds.BroadcastGuildInfo(guild.Index);
        _logger.LogInformation("Guild {Guild} challenged territory {Terr} (cost {Cost}).", guild.Name, group.Index, cost);
        return true;
    }

    /// <summary>Withdraw the sender guild's pending challenge for <paramref name="territoryIndex"/> before war
    /// night (Officer+). The cost is not refunded, and this does NOT restore a territory the guild abandoned.
    ///
    /// <para>Both of those are spent the moment the challenge is registered, which is why the CHALLENGE is
    /// what asks for confirmation and this does not: by the time anyone reaches this, there is nothing left
    /// to talk them out of.</para></summary>
    public void WithdrawChallenge(int index, int territoryIndex)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = _guilds.GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank < GuildRank.Officer)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return;
        }

        var group = _world.MapGroups.GetValueOrDefault(territoryIndex);
        var terr = group is { Territory: true } ? _world.TerritoryFor(territoryIndex) : null;
        if (terr is null || !terr.Challengers.Remove(guild.Index))
        {
            Notify(index, ServerStrings.GuildTerritory_NoChallenge);
            return;
        }
        SaveTerritory(terr);
        NotifyOk(index, ServerStrings.GuildTerritory_WithdrawnOk, ("Territory", TerritoryName(group!)));
        _guilds.BroadcastGuildInfo(guild.Index);   // the row's Challenge/Withdraw state just flipped for everyone
    }

    // playerLevel is the challenging member's character level — both branches scale with it
    // (EconomyFormulas.BandScale), since guild level alone says nothing about how rich the members are.
    private long ChallengeCost(GuildRecord guild, TerritoryRecord terr, int playerLevel)
    {
        if (terr.ControllingGuild <= 0)
            return Constants.TerritoryUnclaimedChallengeCost;
        int ownerLevel = _guilds.GuildById(terr.ControllingGuild)?.Level ?? 0;
        return GuildWarFormulas.BaseDeclareCost(guild.Level, ownerLevel);   // no level-0-target doubling
    }

    // True if the guild is already registered to challenge ANY territory (the one-challenge-at-a-time cap).
    private bool IsChallengingAny(int guildIndex)
    {
        foreach (var (_, state) in _world.AllTerritories())
            if (state.Challengers.Contains(guildIndex)) return true;
        return false;
    }

    // One-territory cap: a guild challenging elsewhere abandons any territory it currently owns —
    // that territory becomes an unclaimed contest (no defender) at war night, irrevocably. Cap = 1, so at most
    // one owned territory exists to abandon.
    private void AbandonOwnedTerritory(int guildIndex, int playerIndex)
    {
        foreach (var (group, state) in _world.AllTerritories())
        {
            if (state.ControllingGuild == guildIndex && !state.DefenderAbandoned)
            {
                state.DefenderAbandoned = true;
                SaveTerritory(state);
                Notify(playerIndex, ServerStrings.GuildTerritory_Abandoned, ("Territory", TerritoryName(group)));
                return;
            }
        }
    }

    // ── War-night resolution ──────────────────────────────────────────────────
    /// <summary>Kick off the war night: announce the public start, then per territory either resolve the
    /// trivial case instantly (0-1 participant) or START a live KotH contest (2+). The public "concluded"
    /// bookend fires here only if nothing contested; otherwise it fires when the last contest's cooldown ends
    /// (see <see cref="TickContests"/>). Called on the scheduled slot and by the creator debug command.</summary>
    public void ResolveWarNight()
    {
        AnnounceWarPublic(ServerStrings.GuildTerritory_WarNightStart);
        int contests = 0;
        foreach (var (group, state) in _world.AllTerritories().ToList())
        {
            int defenderId = state.DefenderAbandoned ? 0 : state.ControllingGuild;
            var challengers = state.Challengers.Where(c => _guilds.GuildById(c) is not null).ToList();
            if ((defenderId > 0 ? 1 : 0) + challengers.Count >= 2)
            {
                StartContest(group, defenderId, challengers);
                contests++;
            }
            else
            {
                ResolveTrivial(group, state, defenderId, challengers);
            }
        }
        if (contests == 0) AnnounceWarPublic(ServerStrings.GuildTerritory_WarNightEnd);
        _logger.LogInformation("Territory war night: {Contests} contest(s) started.", contests);
    }

    // ── Creator debug triggers — drive the REAL lifecycle off-schedule; they never
    //    touch _nextWarNightUtc, so the normal weekly war night still fires on its own timer. ──────────────
    /// <summary>Kick off a war night now (full ramp-up), off the normal schedule. Returns the number of
    /// contests that started (a territory needs 2+ participants challenging/defending to run one).</summary>
    public int DebugStartWarNight()
    {
        ResolveWarNight();
        return _contests.Count;
    }

    /// <summary>Advance every live contest one phase now (Setup→Contest→Cooldown→end) by forcing this tick's
    /// phase timer to elapse and running the real transition. False if no contest is in progress.</summary>
    public bool DebugAdvanceWar()
    {
        if (_contests.Count == 0) return false;
        long now = UtcNow();
        foreach (var c in _contests) c.PhaseEndUtc = now;
        TickContests(now);
        return true;
    }

    /// <summary>Bring every live contest straight to its cooldown now: resolve the winner (Setup/Contest →
    /// finalize) and enter the real 10-min cooldown. Returns the number of contests ended. Already-cooling
    /// contests are left alone.</summary>
    public int DebugEndWar()
    {
        long now = UtcNow();
        int ended = 0;
        foreach (var c in _contests)
        {
            if (c.Phase == ContestPhase.Cooldown) continue;
            FinalizeContest(c);                                     // resolve + apply outcome (defender holds if unscored)
            c.Phase = ContestPhase.Cooldown;
            c.PhaseEndUtc = now + Constants.TerritoryContestCooldownSeconds;
            int mins = Constants.TerritoryContestCooldownSeconds / 60;
            foreach (int g in c.Participants)
                GuildWarNotice(g, ServerStrings.GuildTerritory_CooldownBegun, ("Territory", TerritoryNameOf(c)), ("Minutes", mins));
            BroadcastContest(c);
            ended++;
        }
        return ended;
    }

    // 0-1 participant: no live contest is needed. A lone claimant takes an unclaimed/abandoned
    // territory; an unchallenged defender keeps; anything else stays/falls unclaimed.
    private void ResolveTrivial(MapGroupRecord group, TerritoryRecord state, int defenderId, List<int> challengers)
        => ApplyOutcome(group, state, TerritoryFormulas.ResolveWinner(defenderId, challengers), challengers);

    // Set a resolved territory's owner + weeks-held, clear its challenge state, persist, and announce the
    // per-guild results. Shared by the trivial path and the contest finalize.
    private void ApplyOutcome(MapGroupRecord group, TerritoryRecord state, int winner, List<int> challengers)
    {
        int oldOwner = state.ControllingGuild;
        bool abandoned = state.DefenderAbandoned;
        bool retained = winner > 0 && winner == oldOwner && !abandoned;
        if (retained)
        {
            state.WeeksHeld++;                                   // held another week (defended/unchallenged)
        }
        else
        {
            state.ControllingGuild = winner;
            state.WeeksHeld = 0;
        }  // fresh capture, or fell unclaimed
        bool hadChallengers = challengers.Count > 0;
        state.Challengers.Clear();
        state.DefenderAbandoned = false;
        SaveTerritory(state);
        AnnounceResults(group, oldOwner, winner, challengers, retained, hadChallengers);
    }
}

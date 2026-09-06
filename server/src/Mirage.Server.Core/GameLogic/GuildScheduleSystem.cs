using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// The guild scheduler: the daily 00:00 Server-Time settlement. Ticked from <c>GameLoop.AiTick</c> on a
/// 60s real-clock throttle (like <see cref="PkExpirySystem"/>). Unlike Time-of-Day / weather — which
/// PAUSE while the server is offline — the settlement is WALL-CLOCK with catch-up: every server-local
/// calendar day missed during downtime is settled on boot, so the weekly guild tax can never be skipped
/// by a restart. The cursor (last-settled date) persists in environment.json.
///
/// A settlement processes DEBITS before CREDITS ("debts before credits"): the weekly guild tax, then
/// one-sided war daily maintenance, then the credits — the L5 perk gold and, later,
/// territory income (at the marked seam). War-night scheduling lives in GuildTerritorySystem, next to
/// its only consumer.
///
/// The scheduling + tax + war-maintenance logic lives in the pure static helpers
/// (<see cref="DatesToSettle"/>, <see cref="SettleGuild"/>, <see cref="ApplyWeeklyTax"/>,
/// <see cref="ApplyWarMaintenance"/>) so it is directly unit-testable; the instance methods only wire
/// persistence + guild-channel notices around them (including severing a dropped war's opponent mirror).
/// Runs on the game thread (no locks).
/// </summary>
public sealed class GuildScheduleSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly GuildSystem _guilds;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly MailSystem _mail;
    private readonly ILogger<GuildScheduleSystem> _logger;

    // Real-clock throttle between boundary checks; the settlement itself is date-driven, not timer-driven.
    private const int CheckIntervalSeconds = 60;
    private long _lastCheckUtc;
    private DateOnly _lastSettled;
    // Seasonal leaderboard cursor: the current season number + the date it began. Persisted in
    // environment.json (like _lastSettled) and seeded on boot. MinValue start = uninitialized (adopted on the
    // first weekly boundary, no scoring/payout that week).
    private int _seasonNumber;
    private DateOnly _seasonStart;
    // The last season number whose END effects (payout/archive/reset) ran — guards PerformSeasonEnd so a manual
    // /guildreset season is idempotent. In-memory only (a debug-command guard); the normal boundary self-gates.
    private int _lastEndedSeason;

    public GuildScheduleSystem(GameWorld world, GuildSystem guilds, IPacketDispatcher dispatcher,
                               IPersistenceService persistence, IBackgroundPersistence bg, MailSystem mail,
                               ILogger<GuildScheduleSystem> logger,
                               IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _world = world;
        _guilds = guilds;
        _persistence = persistence;
        _bg = bg;
        _mail = mail;
        _logger = logger;
    }

    /// <summary>The last server-local date fully settled; persisted to environment.json (read by the
    /// GameLoop when it writes the blob) and restored via <see cref="Seed"/> on boot.</summary>
    public DateOnly LastSettledDate => _lastSettled;

    /// <summary>Seed the cursor from the persisted blob on boot, before the loop starts — mirroring how
    /// Time-of-Day / weather are seeded. <see cref="DateOnly.MinValue"/> (a never-run server) adopts the
    /// current day on first tick with no retroactive settlement.</summary>
    public void Seed(DateOnly lastSettled) => _lastSettled = lastSettled;

    /// <summary>The current seasonal-leaderboard season + its start date; persisted to environment.json (read
    /// by the GameLoop) and restored via <see cref="SeedSeason"/> on boot.</summary>
    public int SeasonNumber => _seasonNumber;
    public DateOnly SeasonStartDate => _seasonStart;

    /// <summary>Seed the season cursor from the persisted blob on boot. Season 0 / a MinValue start = a
    /// never-run server; both are adopted on the first weekly boundary (no retroactive scoring/payout).</summary>
    public void SeedSeason(int seasonNumber, DateOnly seasonStart)
    {
        _seasonNumber = seasonNumber;
        _seasonStart = seasonStart;
    }

    /// <summary>Build + send the seasonal leaderboard to one player — every guild, ordered
    /// best-first (season score desc, then territory-war K/D, then size, then name). Sent on the Standings
    /// sub-tab open; reads live guild records.</summary>
    public void SendLeaderboard(int index)
    {
        var rows = RecomputeStandings()
            .Select(g => new LeaderboardEntry
            {
                Rank = g.SeasonStanding,
                Guild = g.Name, Size = g.Members.Count, Score = g.SeasonScore,
                Kills = g.TerritoryWarKills, Deaths = g.TerritoryWarDeaths,
            })
            .ToList();
        _dispatcher.SendTo(index, new GuildLeaderboardPacket { Season = _seasonNumber, Rows = rows });
    }

    /// <summary>Order every guild by the canonical leaderboard rule (season score desc, then territory-war K/D,
    /// then size, then name) and assign each its 1-based seasonal <see cref="GuildRecord.SeasonStanding"/> —
    /// scoring guilds get 1..N, non-scorers (score 0) get 0. Returns the ordered list so the leaderboard reuses
    /// it. Cheap (few guilds); called on boot, after the scoring/season settlement, and on a leaderboard view,
    /// so the Ranks tab + the overhead standing sync read a fresh value.</summary>
    public IReadOnlyList<GuildRecord> RecomputeStandings()
    {
        var ordered = _world.Guilds.Values
            .OrderByDescending(g => g.SeasonScore)
            .ThenByDescending(g => g.TerritoryWarKills - g.TerritoryWarDeaths)
            .ThenByDescending(g => g.Members.Count)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int rank = 0;
        foreach (var g in ordered)
            g.SeasonStanding = g.SeasonScore > 0 ? ++rank : 0;
        return ordered;
    }

    /// <summary>Send an archived past season to the historical-season browser. <paramref name="season"/>
    /// = 0 (or an unknown number) falls back to the latest archived season; Found = false when none exist.</summary>
    public void SendSeasonArchive(int index, int season)
    {
        var archives = _world.SeasonArchives;
        if (archives.Count == 0)
        {
            _dispatcher.SendTo(index, new SeasonArchivePacket { Found = false });
            return;
        }
        var pick = (season > 0 ? archives.FirstOrDefault(a => a.Season == season) : null) ?? archives[^1];
        _dispatcher.SendTo(index, new SeasonArchivePacket
        {
            Found = true,
            Season = pick.Season,
            EndDate = pick.EndDate,
            AvailableSeasons = archives.Select(a => a.Season).ToList(),
            Standings = pick.Standings,
        });
    }

    public void Tick()
    {
        long nowUtc = NowUtc;
        if (nowUtc - _lastCheckUtc < CheckIntervalSeconds) return;
        _lastCheckUtc = nowUtc;
        AdvanceTo(DateOnly.FromDateTime(Clock.LocalNow));   // server-LOCAL date = the 00:00 Server-Time boundary
        _guilds.ExpireDueQuests();                        // drop any guild quest past its 24h limit
    }

    /// <summary>Run one daily settlement for every calendar day strictly after the cursor up to and
    /// including <paramref name="today"/> (catch-up across downtime), then park the cursor on today. A
    /// never-run server (cursor unset) simply adopts today with no retroactive settlement.</summary>
    public void AdvanceTo(DateOnly today)
    {
        if (_lastSettled == default)   // first boot: nothing to catch up on
        {
            _lastSettled = today;
            return;
        }
        var due = DatesToSettle(_lastSettled, today);
        foreach (var date in due)
            RunDailySettlement(date);
        if (today > _lastSettled) _lastSettled = today;
        if (due.Count > 0)
            _logger.LogInformation("Guild settlement ran for {Days} day(s) up to {Date}.", due.Count, today);
    }

    /// <summary>The calendar dates to settle to advance the cursor from <paramref name="last"/> up to
    /// <paramref name="today"/> — <c>(last, today]</c>. Empty when the cursor is unset (a never-run
    /// server, so no retroactive settlement) or already at/after today. Pure; exposed for tests.</summary>
    public static IReadOnlyList<DateOnly> DatesToSettle(DateOnly last, DateOnly today)
    {
        var dates = new List<DateOnly>();
        if (last == default) return dates;
        for (var d = last.AddDays(1); d <= today; d = d.AddDays(1))
            dates.Add(d);
        return dates;
    }

    /// <summary>The 00:00 settlement for one calendar <paramref name="date"/>, across every guild:
    /// applies the pure per-guild settlement, completes any war-maintenance drops (the impure opponent-side
    /// mirror severance + announcements), then persists + announces any guild that changed.</summary>
    public void RunDailySettlement(DateOnly date)
    {
        // NORMAL 00:00 settlement (per caught-up day): derive which weekly/season steps are due from the date,
        // run them, then advance the season cursor. All steps are marker-guarded (idempotent).
        bool resetDay = date.DayOfWeek == Config.Schedule.WeekResetDay;
        bool seasonKnown = _seasonStart != default;
        int week = seasonKnown ? SeasonFormulas.WeeksElapsed(_seasonStart, date) : -1;
        bool doScoring = resetDay && seasonKnown && week >= Constants.TerritorySeasonScoringStartWeek && week < Constants.TerritorySeasonWeeks;
        bool doSeason = resetDay && seasonKnown && week >= Constants.TerritorySeasonWeeks;
        RunSettlement(date, doWeeklyReset: resetDay, doScoring: doScoring, doSeason: doSeason);
        AdvanceSeasonCursor(date);
    }

    /// <summary>Creator <c>/guildreset day|week|season</c>: force one cadence's routines to run NOW,
    /// idempotently, WITHOUT touching the real schedule cursors (<see cref="LastSettledDate"/> / the season
    /// cursor) — so the normal 00:00 / weekly / season events still fire on their own timers. Repeated runs in
    /// the same period only add incremental (accrue-and-zero) credits; debits/scores/rolls guard per date.</summary>
    public void RunManualSettlement(SettlementScope scope)
    {
        var today = DateOnly.FromDateTime(Clock.LocalNow);
        RunSettlement(today,
            doWeeklyReset: scope >= SettlementScope.Week,
            doScoring: scope >= SettlementScope.Week,
            doSeason: scope >= SettlementScope.Season);
    }

    // The shared settlement body (normal + manual /guildreset). DEBITS before CREDITS per guild, then the
    // MapGroup-keyed territory income, then the weekly score accrual + season end when requested.
    private void RunSettlement(DateOnly date, bool doWeeklyReset, bool doScoring, bool doSeason)
    {
        long nowUtc = NowUtc;
        // Snapshot: severing a dropped war mutates the opponent guild's Wars list (not the dictionary).
        foreach (var guild in _world.Guilds.Values.ToList())
        {
            var result = SettleGuild(guild, date, nowUtc, forceWeekly: doWeeklyReset,
                                     weekResetDay: Config.Schedule.WeekResetDay);
            foreach (int opponentIndex in result.Dropped)
                DropWarMirrorAndAnnounce(guild, opponentIndex);
            if (!result.Changed) continue;
            _guilds.SaveGuild(guild);
            AnnounceOutcome(guild, result);
        }

        // Territory income is a CREDIT keyed by MapGroup (not guild), so it runs AFTER every guild's debits above
        // — preserving "debts before credits" (same-day income can't cover a same-day tax).
        SettleTerritoryIncome(date, doWeeklyReset);

        // Seasonal leaderboard: weekly hold scoring + the 13-week season end (payouts + archive +
        // reset). Reads the just-settled territory state; each step guards per date/season (idempotent).
        if (doScoring) AccrueWeeklyScores(date);
        if (doSeason) PerformSeasonEnd(date);
        // Season scores just moved — refresh every guild's cached SeasonStanding so the overhead standing sync
        // (which broadcasts off the cached value, not a leaderboard view) reflects the new order.
        if (doScoring || doSeason) RecomputeStandings();
    }

    // ── Seasonal leaderboard ──────────────────────────────────────────────────────
    // Add each controlled territory's weekly hold score (weeks-held + consecutive-hold bonus) to its owner's
    // season total — one credit per owning guild (the 1-territory cap). Guarded per date via LastScoredDate so
    // a re-run/force (/guildreset week) never double-scores.
    private void AccrueWeeklyScores(DateOnly date)
    {
        foreach (var (_, terr) in _world.AllTerritories())
        {
            if (terr.ControllingGuild <= 0) continue;
            var guild = _guilds.GuildById(terr.ControllingGuild);
            if (guild is null || guild.LastScoredDate == date) continue;   // already scored this date
            guild.LastScoredDate = date;
            guild.SeasonScore += SeasonFormulas.WeeklyHoldScore(terr.WeeksHeld);
            _guilds.SaveGuild(guild);
        }
    }

    // End the current season's EFFECTS (payout + archive + reset) at most once per season number (guarded by
    // _lastEndedSeason), so a manual /guildreset season is idempotent; the normal 13-week boundary then just
    // advances the cursor (AdvanceSeasonCursor). Returns whether it ran.
    private bool PerformSeasonEnd(DateOnly date)
    {
        if (_seasonNumber < 1 || _lastEndedSeason >= _seasonNumber) return false;
        EndSeason(date);
        _lastEndedSeason = _seasonNumber;
        return true;
    }

    // NORMAL weekly season-cursor advance (never called by /guildreset, which must not disturb the schedule):
    // adopt the season on first run, roll it after 13 weeks.
    private void AdvanceSeasonCursor(DateOnly date)
    {
        if (date.DayOfWeek != Config.Schedule.WeekResetDay) return;
        if (_seasonStart == default)   // first boot: adopt this week as season 1, no retroactive scoring
        {
            _seasonStart = date;
            if (_seasonNumber < 1) _seasonNumber = 1;
            return;
        }
        if (SeasonFormulas.WeeksElapsed(_seasonStart, date) >= Constants.TerritorySeasonWeeks)
        {
            _seasonStart = date;
            _seasonNumber++;
        }
    }

    // End the current season: rank the scoring guilds, pay placings (vault gold now; per-member gold DEFERRED),
    // archive the final standings in perpetuity, announce, then reset every guild's seasonal counters (territory
    // ownership + the WeeksHeld streak persist across the reset).
    private void EndSeason(DateOnly date)
    {
        var scorers = _world.Guilds.Values.Where(g => g.SeasonScore > 0)
            .OrderByDescending(g => g.SeasonScore)
            .ThenByDescending(g => g.TerritoryWarKills - g.TerritoryWarDeaths)
            .ToList();

        var standings = new List<SeasonStanding>();
        for (int i = 0; i < scorers.Count; i++)
        {
            var g = scorers[i];
            int placing = i + 1;
            var (perMember, vaultGold) = SeasonFormulas.PlacingPayout(placing);
            g.VaultGold += vaultGold;
            g.WeeklyIncome += vaultGold;     // vault dashboard: a season payout counts as income
            Announce(g, ServerStrings.GuildSchedule_SeasonPlaced,
                ("Season", _seasonNumber), ("Placing", placing), ("Vault", vaultGold));
            PayActiveMembers(g, perMember, placing, date);   // per-member gold via mail (offline-safe)
            standings.Add(new SeasonStanding
            {
                Placing = placing, Guild = g.Name, Score = g.SeasonScore,
                Kills = g.TerritoryWarKills, Deaths = g.TerritoryWarDeaths,
            });
        }
        // Non-scorers are archived too (placing 0) for a complete perpetual record.
        foreach (var g in _world.Guilds.Values.Where(g => g.SeasonScore <= 0))
        {
            standings.Add(new SeasonStanding
            {
                Placing = 0, Guild = g.Name, Score = 0,
                Kills = g.TerritoryWarKills, Deaths = g.TerritoryWarDeaths,
            });
        }

        if (scorers.Count > 0)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.GuildSchedule_SeasonChampion,
                new ChatMetadata(GameColor.BrightGreen, ChatChannel.War),
                ("Season", _seasonNumber), ("Guild", scorers[0].Name));
        }

        var archive = new SeasonArchive
        {
            Season = _seasonNumber,
            EndDate = date.ToString("yyyy-MM-dd"),
            Standings = standings,
        };
        _bg.Run(_persistence.SaveSeasonArchiveAsync(_seasonNumber, archive), nameof(IPersistenceService.SaveSeasonArchiveAsync));
        _world.SeasonArchives.Add(archive);   // keep the in-memory browser list current without a reload

        // Reset every guild's seasonal counters; territory ownership + WeeksHeld (the streak) persist.
        foreach (var g in _world.Guilds.Values)
        {
            if (g.SeasonScore == 0 && g.TerritoryWarKills == 0 && g.TerritoryWarDeaths == 0) continue;
            g.SeasonScore = 0;
            g.TerritoryWarKills = 0;
            g.TerritoryWarDeaths = 0;
            _guilds.SaveGuild(g);
        }
        _logger.LogInformation("Season {N} ended: {Scorers} scoring guild(s).", _seasonNumber, scorers.Count);
    }

    // Deliver each ACTIVE member's per-member placing gold as a claimable mail attachment — the mail
    // system handles offline recipients, so a member who wasn't online at season end still collects it on login.
    private void PayActiveMembers(GuildRecord guild, long perMember, int placing, DateOnly date)
    {
        if (perMember <= 0) return;
        long now = NowUtc;
        string sender = ServerStrings.Get(ServerStrings.Mail_SystemSender);
        foreach (var m in guild.Members)
        {
            if (!SeasonFormulas.IsActiveMember(m.ActiveSeconds, m.LastSeenUtc, now)) continue;
            _mail.Deliver(m.Login, sender,
                ServerStrings.Get(ServerStrings.GuildSchedule_SeasonMemberSubject),
                ServerStrings.Format(ServerStrings.GuildSchedule_SeasonMemberBody,
                    ("Season", _seasonNumber), ("Placing", placing), ("Gold", perMember)),
                new List<MailAttachment> { new() { ItemNum = Constants.GoldItemIndex, Quantity = (int)perMember } });
        }
    }

    /// <summary>Credit each controlled territory's accrued daily income to its owning guild's vault, then on
    /// the weekly-reset day roll IncomeThisWeek into PreviousWeekIncome. WeeksHeld itself ticks at war-night
    /// retention, not here. Persists each changed territory + credited guild.</summary>
    private void SettleTerritoryIncome(DateOnly date, bool doWeekRoll)
    {
        foreach (var (group, terr) in _world.AllTerritories())
        {
            bool changed = false;

            if (terr.ControllingGuild > 0 && terr.PendingTerritoryIncome > 0)
            {
                long amount = CreditTerritoryIncome(terr);   // zeroes PendingTerritoryIncome, adds to IncomeThisWeek
                var guild = _guilds.GuildById(terr.ControllingGuild);
                if (guild is not null)
                {
                    guild.VaultGold += amount;
                    guild.WeeklyIncome += amount;   // vault dashboard: territory income is weekly income
                    _guilds.SaveGuild(guild);
                    Announce(guild, ServerStrings.GuildSchedule_TerritoryIncome,
                        ("Territory", TerritoryName(group)), ("Amount", amount));
                }
                // (guild == null: the owning record is gone, so the income is dropped. A disband releases its
                //  territory, so this is a dangling-owner safeguard rather than a path the normal flow takes.)
                changed = true;
            }

            // Weekly roll of IncomeThisWeek → PreviousWeekIncome, guarded per date so a re-run/force can't wipe
            // the real previous-week figure by rolling a now-zeroed IncomeThisWeek over it (idempotent).
            if (doWeekRoll && terr.LastWeekRollDate != date)
            {
                terr.LastWeekRollDate = date;
                if (terr.IncomeThisWeek != 0 || terr.PreviousWeekIncome != 0)
                {
                    terr.PreviousWeekIncome = terr.IncomeThisWeek;
                    terr.IncomeThisWeek = 0;
                }
                changed = true;
            }

            if (changed) SaveTerritory(terr);
        }
    }

    /// <summary>Move a territory's accrued daily income out of PendingTerritoryIncome into IncomeThisWeek and return
    /// the amount (the caller credits the owning guild's vault). Runs AFTER guild debits. Pure; for tests.</summary>
    public static long CreditTerritoryIncome(TerritoryRecord terr)
    {
        long amount = terr.PendingTerritoryIncome;
        if (amount <= 0) return 0;
        terr.PendingTerritoryIncome = 0;
        terr.IncomeThisWeek += amount;
        return amount;
    }

    // Off-thread persist of a mutated territory (Clone so a concurrent per-kill accrual can't corrupt the write).
    private void SaveTerritory(TerritoryRecord terr) =>
        _bg.Run(_persistence.SaveTerritoryAsync(terr.MapGroup, terr.Clone()), nameof(IPersistenceService.SaveTerritoryAsync));

    private static string TerritoryName(MapGroupRecord group) =>
        string.IsNullOrWhiteSpace(group.DisplayName) ? group.Name : group.DisplayName.Trim();

    /// <summary>Persist any guild / territory flagged with unsaved per-kill income accrual
    /// (<see cref="GameWorld.DirtyGuilds"/> / <see cref="GameWorld.DirtyTerritories"/>), then clear the flags.
    /// Called on the periodic save tick AND at shutdown, so <c>PendingPerkIncome</c> + a territory's
    /// <c>PendingTerritoryIncome</c> are never lost to a restart. Uses PersistGuild (no broadcast) — the vault
    /// dashboard refreshes on the next full sync.</summary>
    public void FlushDirtyAccumulators()
    {
        if (_world.DirtyGuilds.Count > 0)
        {
            foreach (int gid in _world.DirtyGuilds)
                if (_world.Guilds.TryGetValue(gid, out var g)) _guilds.PersistGuild(g);
            _world.DirtyGuilds.Clear();
        }
        if (_world.DirtyTerritories.Count > 0)
        {
            foreach (int mgid in _world.DirtyTerritories)
                if (_world.Territories.TryGetValue(mgid, out var t)) SaveTerritory(t);
            _world.DirtyTerritories.Clear();
        }
    }

    /// <summary>Await all pending guild-file writes — call at shutdown (after the loop stops + a final flush)
    /// so a queued guild save can't be lost. The map-group writes go through IBackgroundPersistence, drained
    /// separately.</summary>
    public Task DrainGuildWritesAsync() => _guilds.DrainAsync();

    // Finish a maintenance drop that the pure settlement removed from THIS guild's war list: sever the
    // opponent's mirror entry + persist it, and announce the dropped declaration (public line + a guild
    // notice) — the same visible effect as a manual retraction.
    private void DropWarMirrorAndAnnounce(GuildRecord guild, int opponentIndex)
    {
        var opponent = _guilds.GuildById(opponentIndex);
        string oppName = opponent?.Name ?? "";
        // Post-war re-declare cooldown (anti-pile-on), like any other war end. guild's own entry was already
        // removed by the pure ApplyWarMaintenance; guild is persisted by the caller after this returns.
        long now = NowUtc;
        long until = now + Constants.GuildWarRedeclareCooldownSeconds;
        GuildWarFormulas.SetCooldown(guild, opponentIndex, until, now);
        if (opponent is not null)
        {
            opponent.Wars.RemoveAll(w => w.OpponentIndex == guild.Index);
            GuildWarFormulas.SetCooldown(opponent, guild.Index, until, now);
            _guilds.SaveGuild(opponent);
        }
        Announce(guild, ServerStrings.GuildWar_MaintenanceDropped, ("GuildName", oppName));
        // The public line says the declaration LAPSED, not that the guild retracted it. Only the guild
        // sees the private notice naming the vault, so a shared "retracted" line would tell everyone
        // else that a guild which ran out of gold had chosen to back down.
        _dispatcher.SendLocalizedChatToAll(ServerStrings.GuildWar_UpkeepLapsed,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.War),
            ("Guild1", guild.Name), ("Guild2", oppName));
    }

    /// <summary>Pure per-guild settlement for one date: DEBITS (the weekly tax due on the guild's founding
    /// weekday, then one-sided war daily maintenance) then CREDITS (accumulated daily gold — the L5 perk
    /// trickle and territory income) — "debts before credits", so same-day income can't
    /// cover a same-day debt, and the weekly tax is taken before war upkeep. Mutates the guild and returns
    /// what happened, including any wars dropped for non-payment (the caller severs the opponent mirror +
    /// announces). <paramref name="nowUtc"/> gates a war's "is it live yet" check. Exposed for tests.</summary>
    public static SettlementResult SettleGuild(GuildRecord guild, DateOnly date, long nowUtc, bool forceWeekly = false,
                                               DayOfWeek weekResetDay = DayOfWeek.Sunday)
    {
        // Weekly financial-health reset (start of a new week): zero the running totals BEFORE this day's flows,
        // so today's tax/war-spend/income count toward the fresh week. Guarded per date so a re-run/force can't
        // double-zero (idempotent — see ResetWeeklyTotalsIfDue).
        bool weeklyReset = ResetWeeklyTotalsIfDue(guild, date, forceWeekly, weekResetDay);

        // ── DEBITS (before credits) — each skipped if already applied for this date, so a manual /guildreset
        //    re-run never double-charges (the CREDITS below are naturally idempotent: accrue-and-zero). ──
        TaxOutcome tax = TaxOutcome.None;
        if (guild.FoundingWeekday == date.DayOfWeek && guild.LastTaxPaidDate != date)
        {
            tax = ApplyWeeklyTax(guild);
            if (tax is TaxOutcome.Paid or TaxOutcome.RestoredAndPaid) guild.LastTaxPaidDate = date;   // a Miss retries later
        }
        var war = new WarMaintenanceResult(0, Array.Empty<int>());
        if (guild.LastMaintDate != date)
        {
            war = ApplyWarMaintenance(guild, nowUtc);   // second debit, after the weekly tax (accrues WeeklyWarCosts)
            guild.LastMaintDate = date;
        }

        // ── CREDITS (after debits) ───────────────────────────────────────────
        long credited = CreditPerkIncome(guild);
        guild.WeeklyIncome += credited;   // the L5 perk trickle counts as weekly income (territory income adds later)
        return new SettlementResult(tax, credited, war.Paid, war.DroppedOpponents, weeklyReset);
    }

    /// <summary>Zero the vault-dashboard weekly running totals when due — on the weekly reset day, OR when
    /// <paramref name="force"/> (a manual /guildreset week/season). Guarded per date via
    /// <see cref="GuildRecord.LastWeeklyResetDate"/> so it runs once per week (idempotent). Returns whether it
    /// actually zeroed anything.</summary>
    public static bool ResetWeeklyTotalsIfDue(GuildRecord guild, DateOnly date, bool force,
                                              DayOfWeek weekResetDay = DayOfWeek.Sunday)
    {
        if (!force && date.DayOfWeek != weekResetDay) return false;
        if (guild.LastWeeklyResetDate == date) return false;   // already reset for this date
        guild.LastWeeklyResetDate = date;
        if (guild.WeeklyIncome == 0 && guild.WeeklyDonations == 0 && guild.WeeklyWarCosts == 0) return false;
        guild.WeeklyIncome = guild.WeeklyDonations = guild.WeeklyWarCosts = 0;
        return true;
    }

    /// <summary>Charge each live one-sided-AGGRESSOR war's daily maintenance (a fraction of its declare
    /// cost) from the vault, whole-or-nothing per war. A war the vault can't cover is DROPPED — removed
    /// from THIS guild's list here, with its opponent index returned so the caller can sever the mirror +
    /// announce. Mutual wars waive maintenance; a pure defender pays nothing; a warmup war isn't live yet.
    /// Pure; exposed for tests.</summary>
    public static WarMaintenanceResult ApplyWarMaintenance(GuildRecord guild, long nowUtc)
    {
        long paid = 0;
        List<int>? dropped = null;
        foreach (var war in guild.Wars.ToList())   // ToList: we may Remove during iteration
        {
            if (!war.WeDeclared || war.TheyDeclared || !GuildWarFormulas.IsLive(war, nowUtc)) continue;
            long due = GuildWarFormulas.DailyMaintenance(war.DeclareCost);
            if (due <= 0) continue;
            if (guild.VaultGold >= due)
            {
                guild.VaultGold -= due;
                guild.WeeklyWarCosts += due;   // vault dashboard: war spend this week
                paid += due;
            }
            else
            {
                guild.Wars.Remove(war);
                (dropped ??= new List<int>()).Add(war.OpponentIndex);
            }
        }
        return new WarMaintenanceResult(paid, dropped ?? (IReadOnlyList<int>)Array.Empty<int>());
    }

    /// <summary>Credit the guild's accumulated daily gold — the L5 perk trickle ONLY — into the vault and
    /// zero the accumulator, returning the amount. Runs AFTER debits so same-day income can't bail out a
    /// same-day debt. Pure; exposed for tests.
    ///
    /// <para>Territory income does NOT pass through here. <see cref="SettleTerritoryIncome"/> credits the
    /// vault straight from each territory's own pending pot, so the two arrive at the same settlement by
    /// separate routes and anything reporting "what is about to be banked" has to add both.</para></summary>
    public static long CreditPerkIncome(GuildRecord guild)
    {
        long amount = guild.PendingPerkIncome;
        if (amount <= 0) return 0;
        guild.VaultGold += amount;
        guild.PendingPerkIncome = 0;
        return amount;
    }

    /// <summary>Apply one week's guild tax (<c>Level * <see cref="Constants.GuildTaxPerLevel"/></c>) from
    /// the vault, whole-or-nothing. A level-0 guild owes nothing (and has no perks). If the vault can't
    /// cover it, perks are suspended (no back taxes accrue); a single week's tax later restores them.
    /// Pure — mutates <paramref name="guild"/> and returns the outcome; exposed for tests.</summary>
    public static TaxOutcome ApplyWeeklyTax(GuildRecord guild)
    {
        long tax = (long)guild.Level * Constants.GuildTaxPerLevel;
        if (tax <= 0) return TaxOutcome.None;               // L0: free, no perks to suspend

        // Vault valor auto-offsets the tax before gold (10 valor = 100 gold off, capped at 50%). It's spent
        // only on a successful payment (atomic with the gold deduction) — a failed/unaffordable tax consumes
        // nothing, matching the existing whole-or-nothing behavior.
        var (valorSpent, discount) = GuildValorTaxOffset(guild.VaultValor, tax);
        long goldDue = tax - discount;

        if (guild.VaultGold >= goldDue)
        {
            guild.VaultGold -= goldDue;
            guild.VaultValor -= valorSpent;
            bool wasSuspended = !guild.PerksActive;
            guild.PerksActive = true;
            return wasSuspended ? TaxOutcome.RestoredAndPaid : TaxOutcome.Paid;
        }

        if (guild.PerksActive)                              // first miss: suspend perks
        {
            guild.PerksActive = false;
            return TaxOutcome.Missed;
        }
        return TaxOutcome.None;                             // already suspended and still can't pay — no back taxes, no repeat notice
    }

    /// <summary>How much vault valor offsets a <paramref name="tax"/> bill — delegates to the shared
    /// <see cref="GuildTaxFormulas.ValorTaxOffset"/> so the Vault dashboard's "what to expect" figure is computed
    /// the SAME way this settlement charges it (no drift). Returns the valor spent + the gold discount it buys.</summary>
    public static (int ValorSpent, long GoldDiscount) GuildValorTaxOffset(int vaultValor, long tax)
        => GuildTaxFormulas.ValorTaxOffset(vaultValor, tax);

    private void AnnounceOutcome(GuildRecord guild, SettlementResult result)
    {
        long tax = (long)guild.Level * Constants.GuildTaxPerLevel;
        switch (result.Tax)
        {
            case TaxOutcome.Paid:
                Announce(guild, ServerStrings.GuildSchedule_TaxPaid, ("Amount", tax));
                break;
            case TaxOutcome.RestoredAndPaid:
                Announce(guild, ServerStrings.GuildSchedule_TaxPaid, ("Amount", tax));
                Announce(guild, ServerStrings.GuildSchedule_PerksRestored);
                break;
            case TaxOutcome.Missed:
                Announce(guild, ServerStrings.GuildSchedule_TaxMissed);
                break;
        }
        if (result.WarMaintenancePaid > 0)
            Announce(guild, ServerStrings.GuildWar_MaintenancePaid, ("Amount", result.WarMaintenancePaid));
        if (result.GoldCredited > 0)
            Announce(guild, ServerStrings.GuildSchedule_IncomeCredited, ("Amount", result.GoldCredited));
    }

    // Guild-channel system notice (no speaker → not ignore-suppressible). The Guild channel carries these
    // notices, and each is also written to the guild's unified log.
    private void Announce(GuildRecord guild, string key, params (string Key, object? Value)[] args)
        => _dispatcher.SendLocalizedChatToGuild(guild.Index, key,
               new ChatMetadata(GameColor.BrightGreen, ChatChannel.Guild), args);
}

/// <summary>What a guild's daily settlement did: the weekly-tax outcome, any war maintenance paid, any
/// daily gold credited, and the opponent indices of any wars dropped for non-payment (the caller severs
/// their mirrors + announces).</summary>
public readonly record struct SettlementResult(
    TaxOutcome Tax, long GoldCredited, long WarMaintenancePaid = 0, IReadOnlyList<int>? DroppedWars = null,
    bool WeeklyReset = false)
{
    /// <summary>True if anything changed — the settlement then needs a persist + a member notice.</summary>
    public bool Changed => Tax != TaxOutcome.None || GoldCredited > 0 || WarMaintenancePaid > 0 || Dropped.Count > 0 || WeeklyReset;
    /// <summary>Opponent indices of wars dropped for unpaid maintenance (never null).</summary>
    public IReadOnlyList<int> Dropped => DroppedWars ?? Array.Empty<int>();
}

/// <summary>Outcome of a guild's war-maintenance debit: the total gold paid, plus the opponent indices of
/// any wars dropped because the vault couldn't cover them.</summary>
public readonly record struct WarMaintenanceResult(long Paid, IReadOnlyList<int> DroppedOpponents);

/// <summary>Result of a guild's weekly-tax settlement (see <see cref="GuildScheduleSystem.ApplyWeeklyTax"/>).</summary>
public enum TaxOutcome
{
    /// <summary>Nothing happened (level 0, or already suspended and still unaffordable).</summary>
    None,
    /// <summary>Tax paid on time; perks were already active.</summary>
    Paid,
    /// <summary>Tax paid and perks re-enabled after a prior suspension.</summary>
    RestoredAndPaid,
    /// <summary>Tax unaffordable; perks suspended.</summary>
    Missed,
}

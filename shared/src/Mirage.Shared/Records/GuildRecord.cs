using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>A guild — the per-guild save unit (one JSON file per guild under
/// <c>guilds/guild{Index}.json</c>, keyed by <see cref="Index"/>). Membership is per-ACCOUNT: each
/// member's <see cref="AccountRecord.Guild"/> holds this guild's <see cref="Index"/> and
/// <see cref="AccountRecord.GuildRank"/> their rank. <see cref="Members"/> is a roster cache for
/// offline display + fast enumeration, kept in sync with the member accounts at each mutation.</summary>
public sealed class GuildRecord
{
    // ── Identity ─────────────────────────────────────────────────────────────
    /// <summary>Filename stem inside <c>guilds/</c>; the trailing number is the <see cref="Index"/>.</summary>
    public const string FileStem = "guild";

    /// <summary>Set when the guild disbands. The record is KEPT — name, roster, war and territory history
    /// and all — and its file stays where it was, which is what retires the number: nothing still holding
    /// it can be re-pointed at a guild founded later.
    ///
    /// <para>A disbanded guild is not loaded into the live map, so it takes part in nothing and its name
    /// is free again.</para></summary>
    public bool Disbanded { get; set; }

    /// <summary>Guild id (>= 1). In memory only — the guild and territory code holds a guild detached from
    /// any dictionary key and asks it which one it is.
    ///
    /// <para>NOT serialized. The number lives in the filename, <c>guilds/guild{Index}.json</c>, and the
    /// loader fills this in from it, so a file copied into another slot cannot claim a number that is not
    /// its own.</para></summary>
    [JsonIgnore]
    public int Index { get; set; }
    /// <summary>Unique, player-chosen name (bounded by <see cref="Constants.NameLength"/>).</summary>
    public string Name { get; set; } = "";
    /// <summary>Overhead guild-name color — a free 24-bit RGB value packed <c>0xRRGGBB</c>, leader-chosen
    /// (the guild's visual identity). Constrained only by <see cref="GuildColorPolicy"/> (no reserved
    /// palette colors). 0 = unset, rendered with a neutral default until the leader picks one.</summary>
    public int Color { get; set; }
    /// <summary>Leader-set message shown only in the guild panel (deliberately never on login).</summary>
    public string Motd { get; set; } = "";
    /// <summary>Up to <see cref="Constants.MaxGuildLabels"/> descriptive tags; shown in the info panel
    /// and the open-guild browser.</summary>
    public List<GuildLabel> Labels { get; set; } = new();
    /// <summary>When true the guild appears in the open-guild browser and accepts applications.</summary>
    public bool OpenForMembership { get; set; }
    /// <summary>Leader toggle: append the member's rank in parentheses to the overhead name
    /// (e.g. "(Officer)").</summary>
    public bool ShowRankOverhead { get; set; }
    /// <summary>Weekday the guild was founded — the weekly-tax due day, collected at that day's
    /// 00:00 Server Time settlement.</summary>
    public DayOfWeek FoundingWeekday { get; set; }

    // ── Settlement idempotency markers (the last date each periodic routine ran for this guild) ───────────
    // So the daily settlement + the creator /guildreset command are idempotent: a routine skips if already run
    // for the given date, and a manual re-run only applies incremental (accrue-and-zero) credits. Default
    // MinValue = "never run". Persisted with the guild.
    public DateOnly LastTaxPaidDate { get; set; }       // weekly tax charged (Paid/RestoredAndPaid) for this date
    public DateOnly LastMaintDate { get; set; }         // war daily-maintenance charged for this date
    public DateOnly LastWeeklyResetDate { get; set; }   // weekly financial-dashboard totals reset for this date
    public DateOnly LastScoredDate { get; set; }        // weekly season-hold score accrued for this date

    // ── Roster (display cache) ───────────────────────────────────────────────
    /// <summary>One entry per member ACCOUNT. Authoritative membership/rank lives on each member's
    /// <see cref="AccountRecord"/> (Guild/GuildRank); this list mirrors it for offline roster rows +
    /// fast enumeration, and is resynced at every membership mutation.</summary>
    public List<GuildMember> Members { get; set; } = new();

    /// <summary>Pending membership applications — account logins of guildless players who applied via the
    /// open-guild browser. A Leader/Officer approves (they join) or rejects (removed); the outcome is
    /// mailed to the applicant so it reaches them even if offline. Only meaningful while
    /// <see cref="OpenForMembership"/>.</summary>
    public List<string> Applications { get; set; } = new();

    // ── Wars ─────────────────────────────────────────────────────────────────
    /// <summary>Active wars, one entry per opposing guild. Like <see cref="Members"/>, this is a MIRRORED
    /// cache: each war has a matching entry on the opponent's list, kept in lockstep by
    /// <see cref="GuildWarFormulas"/> (Link/Unlink). A pair is at war iff both lists hold an entry for the
    /// other; an ended war is removed from both. See <see cref="GuildWar"/>.</summary>
    public List<GuildWar> Wars { get; set; } = new();

    /// <summary>War actions an Officer has queued for the Leader to accept or deny. Unlike
    /// <see cref="Wars"/>, this is NOT mirrored — it is this guild's own leadership queue. Cleared when the
    /// Leader resolves the request (accept executes it, deny discards it) or when the action is performed
    /// directly. See <see cref="GuildWarRequest"/>.</summary>
    public List<GuildWarRequest> WarRequests { get; set; } = new();

    /// <summary>Per-opponent re-declare cooldowns set when a war with them ends (anti-pile-on).
    /// Not mirrored; expired entries pruned lazily. See <see cref="GuildWarCooldown"/>.</summary>
    public List<GuildWarCooldown> WarCooldowns { get; set; } = new();

    // ── Vault & progression ──────────────────────────────────────────────────
    /// <summary>Guild vault gold — absorbs war-death repair costs and pays tax; never an individual payout.</summary>
    public long VaultGold { get; set; }
    /// <summary>Guild vault valor (the war currency); donated valor offsets the weekly tax.</summary>
    public int VaultValor { get; set; }
    /// <summary>Gold accrued today from the L5 perk (a 25%-per-mob-kill trickle) awaiting the daily 00:00
    /// settlement, which credits it to <see cref="GuildRecord.VaultGold"/> AFTER debits (so it can't cover a same-day
    /// debt) and zeroes this. Accrues in memory between kills but is flushed to disk on the periodic save +
    /// at shutdown (via GameWorld.DirtyGuilds), so a restart never loses it.</summary>
    public long PendingPerkIncome { get; set; }

    // ── Weekly financial-health running totals (vault dashboard) ──────────────────────────────────────────
    // Discrete per-type weekly figures shown on the vault page. Reset every week at the settlement's weekly
    // boundary (ScheduleConfig.WeekResetDay). NOT part of the daily debit/credit math — purely a health
    // readout of the week's flows. Persisted with the guild (survive restarts).
    /// <summary>Gold CREDITED to the vault this week from income sources (L5 perk gold + territory income).</summary>
    public long WeeklyIncome { get; set; }
    /// <summary>Gold DONATED to the vault by members this week.</summary>
    public long WeeklyDonations { get; set; }
    /// <summary>Gold the vault SPENT on wars this week (declaration cost + daily maintenance + war-death repairs).</summary>
    public long WeeklyWarCosts { get; set; }
    /// <summary>Recent vault DONATIONS (incoming) for the Vault tab's Donations view — newest first, capped at
    /// <see cref="Constants.GuildRecentVaultLogMax"/>. Each records the donor ACCOUNT (membership is per-account),
    /// gold vs valor, the amount, and when. Persisted with the guild + sent on GuildInfoPacket.</summary>
    public List<GuildDonationEntry> RecentDonations { get; set; } = new();
    /// <summary>Recent vault SPENDING (outgoing) for the Vault tab's Spending view — currently war-death repairs
    /// the vault absorbed (its 75% share of the doubled wear). Newest first, capped like the donations log. Each
    /// records the member ACCOUNT the payment was on behalf of, the gold amount, and when. Persisted + sent.</summary>
    public List<GuildSpendingEntry> RecentSpending { get; set; } = new();
    /// <summary>Guild level 0-5 (starts at 0; perks begin at level 1).</summary>
    public int Level { get; set; }
    /// <summary>Accumulated guild XP toward the next level (from mob kills + guild quests).</summary>
    public long Exp { get; set; }
    /// <summary>Whether the guild's level perks are currently in force. Flipped off by the daily 00:00
    /// settlement when a weekly tax goes unpaid and back on when a later week's tax is covered (no back
    /// taxes). Default true; only meaningful once <see cref="Level"/> >= 1. Perk effects gate on
    /// <c>Level >= 1 && PerksActive</c>.</summary>
    public bool PerksActive { get; set; } = true;

    // ── Quests ───────────────────────────────────────────────────────────────
    /// <summary>The guild's one active quest, or null if none. See <see cref="GuildQuestDef"/>.</summary>
    public GuildQuestDef? Quest { get; set; }
    /// <summary>Quests ACQUIRED today (capped at <see cref="Constants.GuildQuestMaxPerDay"/>); resets when
    /// <see cref="QuestCounterDate"/> rolls over. Acquiring counts against the cap even if the quest is later
    /// abandoned — the daily limit is on how many quests a guild picks up, not how many it completes.</summary>
    public int QuestsAcquiredToday { get; set; }
    /// <summary>Server-local date the counter above applies to; a different date resets it.</summary>
    public DateOnly QuestCounterDate { get; set; }

    // ── Seasonal leaderboard ──────────────────────────────────────────────────
    /// <summary>Accumulated leaderboard score for the CURRENT season — weeks in territory control with the
    /// consecutive-hold bonus (<see cref="SeasonFormulas.WeeklyHoldScore"/>), tallied at each weekly boundary.
    /// Reset to 0 at season end; territory ownership + the WeeksHeld streak persist across the reset.</summary>
    public long SeasonScore { get; set; }
    /// <summary>This guild's 1-based seasonal STANDING (leaderboard position among scoring guilds; 0 = unranked,
    /// i.e. season score 0). Recomputed by GuildScheduleSystem.RecomputeStandings (not authored); surfaced in the
    /// Ranks tab and, via the leader toggle, the overhead cluster. Persisted-or-not is irrelevant — it is
    /// recomputed on boot and whenever scores change.</summary>
    public int SeasonStanding { get; set; }
    /// <summary>Territory-war kills this season (the leaderboard's secondary sort). Reset at season end.</summary>
    public int TerritoryWarKills { get; set; }
    /// <summary>Territory-war deaths this season (paired with <see cref="TerritoryWarKills"/> for K/D). Reset at season end.</summary>
    public int TerritoryWarDeaths { get; set; }

    /// <summary>Deep copy for an off-thread save snapshot (the game thread keeps mutating the live
    /// record). Clones the mutable lists so the writer never observes a half-applied change.</summary>
    public GuildRecord Clone()
    {
        var c = (GuildRecord)MemberwiseClone();
        c.Labels = new List<GuildLabel>(Labels);
        c.Applications = new List<string>(Applications);
        c.Members = new List<GuildMember>(Members.Count);
        foreach (var m in Members) c.Members.Add(m.Clone());
        c.Wars = new List<GuildWar>(Wars.Count);
        foreach (var w in Wars) c.Wars.Add(w.Clone());
        c.WarRequests = new List<GuildWarRequest>(WarRequests.Count);
        foreach (var r in WarRequests) c.WarRequests.Add(r.Clone());
        c.WarCooldowns = new List<GuildWarCooldown>(WarCooldowns.Count);
        foreach (var cd in WarCooldowns) c.WarCooldowns.Add(cd.Clone());
        c.Quest = Quest?.Clone();
        c.RecentDonations = new List<GuildDonationEntry>(RecentDonations);   // entries are immutable records
        c.RecentSpending = new List<GuildSpendingEntry>(RecentSpending);
        return c;
    }
}

/// <summary>A roster row cached on the <see cref="GuildRecord"/> for offline display. <see cref="Rank"/>
/// mirrors the member's <see cref="AccountRecord.GuildRank"/>; the character snapshot is that account's
/// most-recently-active character (so an offline member still shows a meaningful row).</summary>
public sealed class GuildMember
{
    /// <summary>Account login — the membership unit (guild membership is per-account).</summary>
    public string Login { get; set; } = "";
    /// <summary>Display mirror of the account's <see cref="AccountRecord.GuildRank"/>.</summary>
    public GuildRank Rank { get; set; }
    /// <summary>UTC-seconds of the account's last logout; 0 = never recorded. Online-ness is NOT stored
    /// here — it is derived live from the online player slots when the roster is built, so a crash can
    /// never leave this file claiming a member is online.</summary>
    public long LastSeenUtc { get; set; }
    /// <summary>Rolling "recently active" seconds for the season active-member gate: accrued at
    /// logout by session length, RESET when the offline gap before a session exceeds the active window. Read
    /// with <see cref="LastSeenUtc"/> by <c>SeasonFormulas.IsActiveMember</c>. Persisted (offline-safe).</summary>
    public long ActiveSeconds { get; set; }

    // Snapshot of the account's most-recently-active character, for the roster row when offline.
    public string CharName { get; set; } = "";
    public int CharClass { get; set; }
    public int CharLevel { get; set; }

    /// <summary>A shallow copy is a full copy — every field is a value type or an immutable string.</summary>
    public GuildMember Clone() => (GuildMember)MemberwiseClone();
}

/// <summary>One recent vault donation for the Vault tab's donor log. Records the donor's ACCOUNT login (guild
/// membership is per-account, so the log credits the account for posterity — the transient chat announce still
/// names the character), whether it was valor (else gold), the amount, and the UTC-seconds time. Held
/// newest-first + capped on <see cref="GuildRecord.RecentDonations"/>; persisted + sent on GuildInfoPacket.</summary>
public sealed record GuildDonationEntry
{
    [JsonPropertyName("account")] public string Account { get; init; } = "";
    [JsonPropertyName("valor")] public bool Valor { get; init; }   // true = valor donation, false = gold
    [JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonPropertyName("time")] public long TimeUtc { get; init; }
}

/// <summary>One recent vault SPENDING entry for the Vault tab's Spending view — an outgoing gold payment
/// (currently a war-death repair the vault absorbed). Records the member ACCOUNT the payment was on behalf of
/// plus the specific CHARACTER whose gear was repaired (shown in parens), the gold amount, and the UTC-seconds
/// time. Held newest-first + capped on <see cref="GuildRecord.RecentSpending"/>; persisted + sent on GuildInfoPacket.</summary>
public sealed record GuildSpendingEntry
{
    [JsonPropertyName("account")] public string Account { get; init; } = "";
    [JsonPropertyName("char")] public string Character { get; init; } = "";
    [JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonPropertyName("time")] public long TimeUtc { get; init; }
}

/// <summary>One guild's view of its war with a single opposing guild — MIRRORED on both guilds'
/// <see cref="GuildRecord.Wars"/> lists (like the roster cache), kept in lockstep by
/// <see cref="GuildWarFormulas"/>. The <see cref="WeDeclared"/>/<see cref="TheyDeclared"/> pair encodes
/// the relationship: aggressor-only, defender-only, or (both) a mutual war. The derived
/// <see cref="GuildWarStatus"/> and live/warmup state come from <see cref="GuildWarFormulas.Status"/> /
/// <see cref="GuildWarFormulas.IsLive"/>. An ended war is removed from both lists.</summary>
public sealed class GuildWar
{
    /// <summary>The opposing guild's <see cref="GuildRecord.Index"/>.</summary>
    public int OpponentIndex { get; set; }
    /// <summary>Opponent's name, cached for display + announcements when it isn't loaded/online (mirrors
    /// the roster snapshot approach).</summary>
    public string OpponentName { get; set; } = "";
    /// <summary>This guild declared war on the opponent.</summary>
    public bool WeDeclared { get; set; }
    /// <summary>The opponent declared war on this guild. Both flags true = a mutual war.</summary>
    public bool TheyDeclared { get; set; }
    /// <summary>UTC-seconds this guild's own declaration was made — anchors the retraction lock and (for a
    /// one-sided grievance) the warmup. 0 when this guild didn't declare (a pure defender).</summary>
    public long DeclaredUtc { get; set; }
    /// <summary>UTC-seconds hostilities go live: warmup end for a one-sided grievance, or the moment of
    /// reciprocation for a mutual war (immediate). Combat rules apply only once <c>now >= GoLiveUtc</c>.</summary>
    public long GoLiveUtc { get; set; }
    /// <summary>Gold this guild paid to declare — the daily maintenance is a fraction of it
    /// (<see cref="Constants.GuildWarDailyMaintenancePercent"/>). 0 when this guild didn't declare.</summary>
    public long DeclareCost { get; set; }
    /// <summary>Whether the public go-live announcement has fired (so the war tick announces exactly once).
    /// Set immediately for a mutual reciprocation; set at go-live for a one-sided grievance.</summary>
    public bool Announced { get; set; }

    // ── Attrition (MUTUAL wars only) ─────────────────────────────────────────
    /// <summary>This guild's remaining tug-of-war meter (0 = we've been beaten). A death depletes the
    /// victim's side and restores the killer's side, zero-sum. Only meaningful for a mutual war; set to
    /// <see cref="Constants.GuildWarAttritionPool"/> when the war becomes mutual.</summary>
    public int Attrition { get; set; }
    /// <summary>The lowest <see cref="Attrition"/> has reached — used to detect a stalled (cold) war: a new
    /// low means the opponent made real progress, refreshing their <see cref="LastProgressUtc"/>.</summary>
    public int MinAttritionSeen { get; set; }
    /// <summary>UTC-seconds this guild last pushed the OPPONENT's attrition to a new low (real progress
    /// toward a win). If neither side progresses for <see cref="Constants.GuildWarColdSeconds"/>, the war
    /// goes cold (a draw).</summary>
    public long LastProgressUtc { get; set; }
    /// <summary>Consecutive war deaths this guild's vault couldn't cover — 5 in a row auto-loses a mutual war
    /// (the bankruptcy short-circuit); a covered death resets it.</summary>
    public int UncoveredDeathStreak { get; set; }
    /// <summary>This guild has a pending plea for peace out to the opponent (a concession): the opponent
    /// may accept (they win, war ends) or reject (war continues); we may withdraw it. Read from the offerer's
    /// entry — the opponent sees "they seek peace" by inspecting our entry.</summary>
    public bool PeaceOfferedByUs { get; set; }

    // ── Wagers (MUTUAL wars only) ────────────────────────────────────────────
    /// <summary>UTC-seconds this war became mutual — anchors the wager window (an ante must be agreed within
    /// <see cref="Constants.GuildWarWagerWindowSeconds"/> of this). Stamped by
    /// <see cref="GuildWarFormulas.InitMutualAttrition"/>; 0 for a war that isn't mutual.</summary>
    public long MutualSinceUtc { get; set; }
    /// <summary>An ante amount this guild has PROPOSED to the opponent, still awaiting their accept/reject
    /// (0 = none). The opponent reads our entry to see the incoming proposal; on accept both sides escrow
    /// this amount and it clears. Only one proposal per side at a time.</summary>
    public long WagerProposedByUs { get; set; }
    /// <summary>Gold this guild has locked as its matched ante — moved out of <see cref="GuildRecord.VaultGold"/> into
    /// escrow (so it can't fund taxes/repairs/war costs) until the war concludes: paid to the winner on a
    /// decisive result, returned to us on a cold draw. Symmetric with the opponent's ante. 0 = no ante.</summary>
    public long AnteEscrow { get; set; }
    /// <summary>Gold this guild has locked as a peace-plea offering (the no-ante case): escrowed out of
    /// <see cref="GuildRecord.VaultGold"/> while our peace plea is on the table, paid to the accepter (the winner) on
    /// accept, or released back to us on reject/withdraw. 0 = none (an ante concession carries no offering).</summary>
    public long PeaceEscrow { get; set; }

    /// <summary>A shallow copy is a full copy — every field is a value type or an immutable string.</summary>
    public GuildWar Clone() => (GuildWar)MemberwiseClone();
}

/// <summary>A per-opponent cooldown after a war with them ends, so a guild can't immediately re-declare and
/// spam-pile-on. Held on <see cref="GuildRecord.WarCooldowns"/>; expired entries are pruned.</summary>
public sealed class GuildWarCooldown
{
    public int OpponentIndex { get; set; }
    /// <summary>UTC-seconds the cooldown against this opponent expires.</summary>
    public long UntilUtc { get; set; }

    public GuildWarCooldown Clone() => (GuildWarCooldown)MemberwiseClone();
}

/// <summary>An Officer's queued war action awaiting Leader approval: declare on / retract
/// against the guild at <see cref="TargetIndex"/>. Held on the requesting guild's
/// <see cref="GuildRecord.WarRequests"/>; the Leader accepts (it executes) or denies (it is discarded).
/// De-duplicated by (<see cref="Kind"/>, <see cref="TargetIndex"/>) — a given action is queued at most
/// once regardless of how many officers ask.</summary>
public sealed class GuildWarRequest
{
    public GuildWarRequestKind Kind { get; set; }
    /// <summary>The guild to declare on (<see cref="GuildWarRequestKind.Declare"/>) or the opponent to
    /// retract against (<see cref="GuildWarRequestKind.Retract"/>).</summary>
    public int TargetIndex { get; set; }
    /// <summary>Target guild's name, cached for display in the review UI + notices.</summary>
    public string TargetName { get; set; } = "";
    /// <summary>Requesting officer's account login (the request is per-account like membership).</summary>
    public string RequesterLogin { get; set; } = "";
    /// <summary>Requesting officer's character name at request time, for display.</summary>
    public string RequesterName { get; set; } = "";
    /// <summary>UTC-seconds the request was made.</summary>
    public long RequestedUtc { get; set; }
    /// <summary>For a <see cref="GuildWarRequestKind.Peace"/> request with no ante in play, the gold offering
    /// the officer attached to the plea (it becomes the pot the accepter wins); 0 for declare/retract or an
    /// ante-concession peace. The Leader's approval executes the plea with exactly this amount.</summary>
    public long Amount { get; set; }

    /// <summary>A shallow copy is a full copy — every field is a value type or an immutable string.</summary>
    public GuildWarRequest Clone() => (GuildWarRequest)MemberwiseClone();
}

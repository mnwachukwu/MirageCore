using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Pure guild-war math + the mirror-list maintenance for <see cref="GuildRecord.Wars"/>. Kept free of any
/// server type so it is directly unit-testable and can be shared by every caller that touches war entries
/// (the war system, the daily settlement, and disband cleanup — the last avoids a GuildSystem→war
/// dependency cycle). Nothing here does I/O or messaging; callers persist + announce.
/// </summary>
public static class GuildWarFormulas
{
    private const double PercentDenominator = 100.0;

    // BOTH declare costs are keyed on the two GUILD levels only, and are otherwise FLAT.
    //
    // They briefly took the declaring player's character level and scaled by it. That is unenforceable: the
    // cost comes out of a shared vault, so a guild simply has its lowest-level member press Declare and the
    // scale floors at 1.0 — turning a 906,226-gold declaration back into 1,000. A price set by whoever
    // clicks is a price the payer chooses. It is also arbitrary on its own terms, since the vault was filled
    // by the whole roster rather than by that member.

    /// <summary>Gold to declare war (paid from the declarer's vault): base cost minus a step per level the
    /// target sits BELOW the declarer (punching down costs more, up costs less, same level = flat base),
    /// doubled when the target is level 0 (that war can never go mutual, so it self-limits). Floored at
    /// <see cref="Constants.GuildWarDeclareMinCost"/>.</summary>
    public static long DeclareCost(int declarerLevel, int targetLevel)
    {
        long cost = Constants.GuildWarDeclareBaseCost
                    - (long)(targetLevel - declarerLevel) * Constants.GuildWarDeclareLevelStep;
        if (targetLevel <= 0) cost *= Constants.GuildWarL0TargetCostMultiplier;
        return Math.Max(Constants.GuildWarDeclareMinCost, cost);
    }

    /// <summary>The declare cost WITHOUT the level-0-target doubling (floored the same). The cost to
    /// challenge an OWNED territory: the base 11.2 formula on the two guilds' levels, no L0-target 2x.</summary>
    public static long BaseDeclareCost(int declarerLevel, int targetLevel) =>
        Math.Max(Constants.GuildWarDeclareMinCost,
                 Constants.GuildWarDeclareBaseCost - (long)(targetLevel - declarerLevel) * Constants.GuildWarDeclareLevelStep);

    /// <summary>The declarer's daily maintenance = <see cref="Constants.GuildWarDailyMaintenancePercent"/>%
    /// of the declare cost, taken each 00:00 settlement while the war stays one-sided.</summary>
    public static long DailyMaintenance(long declareCost) =>
        (long)Math.Round(declareCost * (Constants.GuildWarDailyMaintenancePercent / PercentDenominator),
            MidpointRounding.AwayFromZero);

    /// <summary>Derive a war entry's status from one guild's perspective as of <paramref name="nowUtc"/>:
    /// mutual once both declared; otherwise warmup until go-live, then aggressor (we declared) or defender.</summary>
    public static GuildWarStatus Status(GuildWar war, long nowUtc)
    {
        if (war.WeDeclared && war.TheyDeclared) return GuildWarStatus.Mutual;
        if (nowUtc < war.GoLiveUtc) return GuildWarStatus.Warmup;
        return war.WeDeclared ? GuildWarStatus.OneSidedAggressor : GuildWarStatus.OneSidedDefender;
    }

    /// <summary>Whether hostilities are live for combat (the warmup has elapsed). Always true for a mutual
    /// war, whose go-live is stamped at the moment of reciprocation.</summary>
    public static bool IsLive(GuildWar war, long nowUtc) => nowUtc >= war.GoLiveUtc;

    // ── War-death durability cost ────────────────────────────────────────────────

    /// <summary>The gold the vault must pay to absorb a war death:
    /// <see cref="Constants.GuildWarVaultRepairPercent"/>% of <paramref name="totalRepairCost"/> (the repair
    /// cost of the full doubled wear).</summary>
    public static long WarDeathVaultCost(long totalRepairCost) =>
        (long)Math.Round(totalRepairCost * (Constants.GuildWarVaultRepairPercent / PercentDenominator),
            MidpointRounding.AwayFromZero);

    /// <summary>Whether the vault covers the war-death repair share (whole-or-nothing): there is wear to pay
    /// for and the vault holds at least <see cref="WarDeathVaultCost"/>.</summary>
    public static bool WarDeathVaultCovers(long totalRepairCost, long vaultGold) =>
        totalRepairCost > 0 && vaultGold >= WarDeathVaultCost(totalRepairCost);

    /// <summary>Durability an item actually loses in a war death: only
    /// <see cref="Constants.GuildWarPlayerWearPercent"/>% of the doubled wear if the vault covered the death
    /// (it pre-paid the rest as a repair sink), else the FULL doubled wear.</summary>
    public static int WarDeathItemWear(int doubledWear, bool vaultCovered) =>
        vaultCovered
            ? (int)Math.Round(doubledWear * (Constants.GuildWarPlayerWearPercent / PercentDenominator),
                MidpointRounding.AwayFromZero)
            : doubledWear;

    // ── Mirror-list maintenance (keep both guilds' entries in lockstep) ──────────

    /// <summary>This guild's war entry against <paramref name="opponentIndex"/>, or null if none.</summary>
    public static GuildWar? Find(GuildRecord guild, int opponentIndex)
    {
        foreach (var w in guild.Wars)
            if (w.OpponentIndex == opponentIndex) return w;
        return null;
    }

    /// <summary>Remove the war between the two guilds from BOTH lists (an ended/retracted/forfeited war).
    /// Safe when either side has no entry. Callers persist both guilds afterward.</summary>
    public static void Unlink(GuildRecord a, GuildRecord b)
    {
        a.Wars.RemoveAll(w => w.OpponentIndex == b.Index);
        b.Wars.RemoveAll(w => w.OpponentIndex == a.Index);
    }

    // ── Re-declare cooldowns (anti-pile-on) ──────────────────────────────────────

    /// <summary>Set/refresh <paramref name="guild"/>'s re-declare cooldown against <paramref name="opponentIndex"/>
    /// to expire at <paramref name="untilUtc"/>, and prune any already-expired cooldowns (so the list stays
    /// bounded). Set on both guilds when their war ends.</summary>
    public static void SetCooldown(GuildRecord guild, int opponentIndex, long untilUtc, long nowUtc)
    {
        guild.WarCooldowns.RemoveAll(c => c.OpponentIndex == opponentIndex || c.UntilUtc <= nowUtc);
        guild.WarCooldowns.Add(new GuildWarCooldown { OpponentIndex = opponentIndex, UntilUtc = untilUtc });
    }

    /// <summary>Seconds remaining on <paramref name="guild"/>'s re-declare cooldown against
    /// <paramref name="opponentIndex"/>, or 0 if none/expired.</summary>
    public static long RemainingCooldownSeconds(GuildRecord guild, int opponentIndex, long nowUtc)
    {
        foreach (var c in guild.WarCooldowns)
        {
            if (c.OpponentIndex == opponentIndex)
                return c.UntilUtc > nowUtc ? c.UntilUtc - nowUtc : 0;
        }

        return 0;
    }

    // ── Attrition & per-target DR (mutual wars) ──────────────────────────────────

    /// <summary>A target's current DR stage after decaying from <paramref name="lastUtc"/> to
    /// <paramref name="nowUtc"/> — one stage recovered per <see cref="Constants.GuildWarDrRecoverySeconds"/>,
    /// floored at 1 (a fresh target). Stage 0/never-killed reads as 1.</summary>
    public static int DecayedDrStage(int stage, long lastUtc, long nowUtc)
    {
        if (stage <= 1 || lastUtc <= 0) return Math.Max(1, stage);
        long recovered = (nowUtc - lastUtc) / Constants.GuildWarDrRecoverySeconds;
        return (int)Math.Max(1, stage - recovered);
    }

    /// <summary>The percent of the BASE-DEATH rate counted at a DR stage: the stage table for stages 1..N,
    /// and beyond the last stage it stays at the table's minimum (never 0) — so a heavily-farmed target is
    /// still worth some attrition, guaranteeing every death moves the meter.</summary>
    public static int DrAttritionPercent(int stage)
    {
        var table = Constants.GuildWarDrStagePercents;
        if (stage < 1) return table[0];
        if (stage > table.Length) return table[^1];   // clamp to the minimum, never 0
        return table[stage - 1];
    }

    /// <summary>The attrition a war kill swings = the "war spend" (<paramref name="treasuryDamage"/>, the
    /// gold this death drained from the victim's vault) in FULL with no DR, PLUS the flat
    /// <see cref="Constants.GuildWarBaseDeathAttrition"/> rate scaled by the target's DR (floored at the DR
    /// minimum, never 0). A naked / uncovered death has 0 treasury damage but still swings by the DR-scaled
    /// base — so it always moves the meter.</summary>
    public static int AttritionScore(long treasuryDamage, int drStage) =>
        (int)treasuryDamage + Constants.GuildWarBaseDeathAttrition * DrAttritionPercent(drStage) / 100;

    /// <summary>Whether a mutual war has gone cold (a draw): neither side has pushed the other to a new
    /// attrition low within <see cref="Constants.GuildWarColdSeconds"/>. <paramref name="lastProgressUtc"/>
    /// is the more recent of the two sides' <see cref="GuildWar.LastProgressUtc"/>.</summary>
    public static bool IsCold(long lastProgressUtc, long nowUtc) =>
        nowUtc - lastProgressUtc >= Constants.GuildWarColdSeconds;

    /// <summary>Seed a war entry's attrition state when it becomes mutual: a full meter, no progress yet, and
    /// the mutual-start stamp that anchors the wager window.</summary>
    public static void InitMutualAttrition(GuildWar war, long nowUtc)
    {
        war.Attrition = Constants.GuildWarAttritionPool;
        war.MinAttritionSeen = Constants.GuildWarAttritionPool;
        war.LastProgressUtc = nowUtc;
        war.UncoveredDeathStreak = 0;
        war.MutualSinceUtc = nowUtc;
    }

    // ── Wagers (consensual matched ante) ─────────────────────────────────────────

    /// <summary>Whether a war's wager window is still open: it is mutual (has a
    /// <see cref="GuildWar.MutualSinceUtc"/>) and <paramref name="nowUtc"/> is within
    /// <see cref="Constants.GuildWarWagerWindowSeconds"/> of it. A new ante can only be agreed while open.</summary>
    public static bool WagerWindowOpen(GuildWar war, long nowUtc) =>
        war.MutualSinceUtc > 0 && nowUtc <= war.MutualSinceUtc + Constants.GuildWarWagerWindowSeconds;

    /// <summary>The most a guild may stake (ante or peace offering): <see cref="Constants.GuildWarWagerMaxVaultPercent"/>%
    /// of its vault gold, rounded down (so it can always afford what it stakes).</summary>
    public static long MaxWager(long vaultGold) =>
        vaultGold * Constants.GuildWarWagerMaxVaultPercent / 100;

    /// <summary>Settle the wager pot when a war ends (call BEFORE <see cref="Unlink"/>, while both entries still
    /// exist). The pot = both sides' locked ante + any peace offering. On a decisive result pass the
    /// <paramref name="winner"/> (they take the whole pot into their vault); on a cold draw pass null (each
    /// side's own stake returns to it). Zeroes the escrow fields on both entries. Returns the gold moved into
    /// the winner's vault (0 on a draw or an empty pot). Safe when either entry is missing. Callers persist.</summary>
    public static long SettleWagerPot(GuildRecord a, GuildRecord b, GuildRecord? winner)
    {
        var aWar = Find(a, b.Index);
        var bWar = Find(b, a.Index);
        long aStake = (aWar?.AnteEscrow ?? 0) + (aWar?.PeaceEscrow ?? 0);
        long bStake = (bWar?.AnteEscrow ?? 0) + (bWar?.PeaceEscrow ?? 0);
        if (aWar is not null)
        {
            aWar.AnteEscrow = 0;
            aWar.PeaceEscrow = 0;
        }
        if (bWar is not null)
        {
            bWar.AnteEscrow = 0;
            bWar.PeaceEscrow = 0;
        }
        if (winner is null)   // cold draw — return each side's own stake
        {
            a.VaultGold += aStake;
            b.VaultGold += bStake;
            return 0;
        }
        long pot = aStake + bStake;   // winner-take-all
        winner.VaultGold += pot;
        return pot;
    }

    // ── Officer war-request queue (pure list ops; the system wraps them with messaging) ──────────

    /// <summary>This guild's pending war request of the given kind against <paramref name="targetIndex"/>,
    /// or null. Requests are de-duplicated by (kind, target), so at most one matches.</summary>
    public static GuildWarRequest? FindRequest(GuildRecord guild, GuildWarRequestKind kind, int targetIndex)
    {
        foreach (var r in guild.WarRequests)
            if (r.Kind == kind && r.TargetIndex == targetIndex) return r;
        return null;
    }

    /// <summary>Remove this guild's pending request for (kind, target); returns true if one was removed.</summary>
    public static bool RemoveRequest(GuildRecord guild, GuildWarRequestKind kind, int targetIndex) =>
        guild.WarRequests.RemoveAll(r => r.Kind == kind && r.TargetIndex == targetIndex) > 0;

    /// <summary>Queue an officer war-request, de-duplicated by (kind, target) and capped at
    /// <paramref name="max"/>. <paramref name="amount"/> carries a peace plea's gold offering (0 otherwise).
    /// Returns whether it was added, was already pending, or the queue is full. Pure; the system layer turns
    /// the result into messaging + persistence.</summary>
    public static WarRequestQueueResult TryQueueRequest(GuildRecord guild, GuildWarRequestKind kind,
        int targetIndex, string targetName, string requesterLogin, string requesterName, long nowUtc, int max,
        long amount = 0)
    {
        if (FindRequest(guild, kind, targetIndex) is not null) return WarRequestQueueResult.AlreadyPending;
        if (guild.WarRequests.Count >= max) return WarRequestQueueResult.Full;
        guild.WarRequests.Add(new GuildWarRequest
        {
            Kind = kind, TargetIndex = targetIndex, TargetName = targetName,
            RequesterLogin = requesterLogin, RequesterName = requesterName, RequestedUtc = nowUtc,
            Amount = amount,
        });
        return WarRequestQueueResult.Added;
    }
}

/// <summary>Outcome of queuing an officer war-request via <see cref="GuildWarFormulas.TryQueueRequest"/>.</summary>
public enum WarRequestQueueResult
{
    /// <summary>The request was added to the queue.</summary>
    Added,
    /// <summary>A matching request (same kind + target) is already queued — nothing added.</summary>
    AlreadyPending,
    /// <summary>The queue is at capacity — nothing added.</summary>
    Full,
}

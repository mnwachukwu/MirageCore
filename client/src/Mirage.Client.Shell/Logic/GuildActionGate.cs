using Mirage.Shared;

namespace Mirage.Client.Shell.Logic;

/// <summary>
/// Pure rank-gate predicates for the Social panel's Guild-tab actions. They mirror the server's
/// authoritative checks in <c>GuildSystem</c> so the UI only enables an action the server would honor —
/// the server still re-validates every request, so these are UI affordance, not security. Extracted from
/// <c>SocialPanel</c> so the gating is unit-testable and the client/server parity is explicit.
///
/// <paramref name="hasTarget"/> means a roster member OTHER than yourself is selected (you can't act on
/// your own row); <paramref name="targetRank"/> is that member's rank.
/// </summary>
public static class GuildActionGate
{
    /// <summary>Kick: an officer or leader may remove a strictly lower-ranked member.</summary>
    public static bool CanKick(GuildRank myRank, GuildRank targetRank, bool hasTarget)
        => hasTarget && myRank >= GuildRank.Officer && targetRank < myRank;

    /// <summary>Promote a Member to Officer — leader only.</summary>
    public static bool CanPromote(GuildRank myRank, GuildRank targetRank, bool hasTarget)
        => hasTarget && myRank == GuildRank.Leader && targetRank == GuildRank.Member;

    /// <summary>Demote an Officer to Member — leader only.</summary>
    public static bool CanDemote(GuildRank myRank, GuildRank targetRank, bool hasTarget)
        => hasTarget && myRank == GuildRank.Leader && targetRank == GuildRank.Officer;

    /// <summary>Hand leadership to an Officer — leader only (the target then confirms via the offer dialog).</summary>
    public static bool CanTransfer(GuildRank myRank, GuildRank targetRank, bool hasTarget)
        => hasTarget && myRank == GuildRank.Leader && targetRank == GuildRank.Officer;

    /// <summary>Leave the guild — anyone but the leader (a leader must transfer or disband instead).</summary>
    public static bool CanLeave(GuildRank myRank)
        => myRank != GuildRank.Leader;

    /// <summary>Disband — leader only, and only once no other member remains.</summary>
    public static bool CanDisband(GuildRank myRank, int memberCount)
        => myRank == GuildRank.Leader && memberCount <= 1;

    /// <summary>Edit MOTD / labels (and the open-for-membership flag) — leader only.</summary>
    public static bool CanEditSettings(GuildRank myRank)
        => myRank == GuildRank.Leader;

    /// <summary>Acquire a new guild quest — leader only, and only when no quest is active.</summary>
    public static bool CanAcquireQuest(GuildRank myRank, bool hasActiveQuest)
        => myRank == GuildRank.Leader && !hasActiveQuest;

    /// <summary>Abandon the active guild quest (freeing a fresh acquire) — leader only, and only when one is active.</summary>
    public static bool CanAbandonQuest(GuildRank myRank, bool hasActiveQuest)
        => myRank == GuildRank.Leader && hasActiveQuest;

    /// <summary>Pay the weekly tax late to restore perks — officer+, and only while perks are suspended.</summary>
    public static bool CanPayTax(GuildRank myRank, bool perksActive)
        => myRank >= GuildRank.Officer && !perksActive;

    // ── War actions ───────────────────────────────────────────────────────────
    // Officer+ may DECLARE/RETRACT/offer-PEACE (a non-leader's send is queued server-side for Leader
    // review); only the Leader RESOLVES the queue and the incoming/outgoing peace pleas directly. The
    // server re-validates level, cost, cooldowns, warmup, and the retraction lock.

    /// <summary>Declare (or return) a war — officer+, and the guild must be at least the minimum war level
    /// (both a fresh declaration and returning one require it).</summary>
    public static bool CanDeclareWar(GuildRank myRank, int guildLevel)
        => myRank >= GuildRank.Officer && guildLevel >= Constants.GuildWarMinLevelToDeclare;

    /// <summary>Request a war action that an officer may queue — retracting a one-sided declaration, or suing
    /// for peace on a mutual war (leader direct, officer queued).</summary>
    public static bool CanRequestWar(GuildRank myRank)
        => myRank >= GuildRank.Officer;

    /// <summary>Resolve a war decision the Leader alone makes: reviewing the officer request queue, and
    /// withdrawing our own / accepting / rejecting a peace plea.</summary>
    public static bool CanResolveWar(GuildRank myRank)
        => myRank == GuildRank.Leader;

    /// <summary>Register/withdraw a territory challenge — Officer+ (Officer queues, Leader acts), mirroring
    /// the grudge-war declare authority.</summary>
    public static bool CanChallengeTerritory(GuildRank myRank)
        => myRank >= GuildRank.Officer;

    /// <summary>Set/accept/reject a wager — the Leader alone can ante the guild's gold.</summary>
    public static bool CanWager(GuildRank myRank)
        => myRank == GuildRank.Leader;
}

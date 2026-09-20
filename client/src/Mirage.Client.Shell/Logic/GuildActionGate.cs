using Mirage.Shared;

namespace Mirage.Client.Shell.Logic;

/// <summary>
/// Pure rank-gate predicates for the Social panel's Guild-tab actions. They mirror the server's
/// authoritative checks in <c>GuildSystem</c> so the UI only enables an action the server would honor —
/// the server still re-validates every request, so these are UI affordance, not security. Extracted from
/// <c>SocialPanel</c> so the gating is unit-testable and the client/server parity is explicit.
///
/// Every predicate below takes the same two: <c>hasTarget</c> means a roster member OTHER than
/// yourself is selected (you cannot act on your own row), and <c>targetRank</c> is that member's rank.
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

}

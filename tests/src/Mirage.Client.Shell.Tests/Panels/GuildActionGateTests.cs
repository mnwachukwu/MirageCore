using Mirage.Client.Shell.Logic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>The Social panel's Guild-tab rank gates (<see cref="GuildActionGate"/>) — which member/guild
/// actions each rank may take against a selected member. These mirror the server's authoritative checks;
/// the panel only uses them to enable/disable buttons, but locking the matrix down here catches drift
/// between the UI affordance and the server's rules.</summary>
[TestFixture]
public class GuildActionGateTests
{
    // ── Kick: officer+ removes a strictly lower-ranked member ──────────────────
    [TestCase(GuildRank.Leader, GuildRank.Officer, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, GuildRank.Member, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, GuildRank.Member, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, GuildRank.Officer, ExpectedResult = false)] // equal rank, not lower
    [TestCase(GuildRank.Officer, GuildRank.Leader, ExpectedResult = false)]
    [TestCase(GuildRank.Member, GuildRank.Member, ExpectedResult = false)]
    public bool CanKick_ByRank(GuildRank me, GuildRank target) => GuildActionGate.CanKick(me, target, hasTarget: true);

    [Test]
    public void CanKick_NoTarget_False()
        => Assert.That(GuildActionGate.CanKick(GuildRank.Leader, GuildRank.Member, hasTarget: false), Is.False);

    // ── Promote: leader raises a Member to Officer ─────────────────────────────
    [TestCase(GuildRank.Leader, GuildRank.Member, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, GuildRank.Officer, ExpectedResult = false)] // already an officer
    [TestCase(GuildRank.Officer, GuildRank.Member, ExpectedResult = false)] // officers can't promote
    public bool CanPromote_ByRank(GuildRank me, GuildRank target) => GuildActionGate.CanPromote(me, target, hasTarget: true);

    // ── Demote / Transfer: leader acts on an Officer ───────────────────────────
    [TestCase(GuildRank.Leader, GuildRank.Officer, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, GuildRank.Member, ExpectedResult = false)] // a member isn't demotable
    [TestCase(GuildRank.Officer, GuildRank.Officer, ExpectedResult = false)]
    public bool CanDemote_ByRank(GuildRank me, GuildRank target) => GuildActionGate.CanDemote(me, target, hasTarget: true);

    [TestCase(GuildRank.Leader, GuildRank.Officer, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, GuildRank.Member, ExpectedResult = false)] // can only hand off to an officer
    [TestCase(GuildRank.Officer, GuildRank.Officer, ExpectedResult = false)]
    public bool CanTransfer_ByRank(GuildRank me, GuildRank target) => GuildActionGate.CanTransfer(me, target, hasTarget: true);

    [Test]
    public void PromoteDemoteTransfer_NoTarget_False()
    {
        Assert.That(GuildActionGate.CanPromote(GuildRank.Leader, GuildRank.Member, hasTarget: false), Is.False);
        Assert.That(GuildActionGate.CanDemote(GuildRank.Leader, GuildRank.Officer, hasTarget: false), Is.False);
        Assert.That(GuildActionGate.CanTransfer(GuildRank.Leader, GuildRank.Officer, hasTarget: false), Is.False);
    }

    // ── Leave: anyone but the leader ───────────────────────────────────────────
    [TestCase(GuildRank.Member, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, ExpectedResult = false)] // a leader must transfer or disband
    public bool CanLeave_ByRank(GuildRank me) => GuildActionGate.CanLeave(me);

    // ── Disband: leader only, and only when alone ──────────────────────────────
    [TestCase(GuildRank.Leader, 1, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, 0, ExpectedResult = true)]  // defensive: an empty roster still disbands
    [TestCase(GuildRank.Leader, 2, ExpectedResult = false)] // another member remains
    [TestCase(GuildRank.Officer, 1, ExpectedResult = false)]
    public bool CanDisband_ByRankAndSize(GuildRank me, int count) => GuildActionGate.CanDisband(me, count);

    // ── Settings (MOTD / labels): leader only ──────────────────────────────────
    [TestCase(GuildRank.Leader, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, ExpectedResult = false)]
    [TestCase(GuildRank.Member, ExpectedResult = false)]
    public bool CanEditSettings_ByRank(GuildRank me) => GuildActionGate.CanEditSettings(me);

    // ── Acquire quest: leader only, when no quest is active ─────────────────────
    [TestCase(GuildRank.Leader, false, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, true, ExpectedResult = false)]  // one already active
    [TestCase(GuildRank.Officer, false, ExpectedResult = false)]
    [TestCase(GuildRank.Member, false, ExpectedResult = false)]
    public bool CanAcquireQuest_ByRank(GuildRank me, bool hasQuest) => GuildActionGate.CanAcquireQuest(me, hasQuest);

    // ── Abandon quest: leader only, when a quest is active ──────────────────────
    [TestCase(GuildRank.Leader, true, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, false, ExpectedResult = false)] // nothing to abandon
    [TestCase(GuildRank.Officer, true, ExpectedResult = false)]
    public bool CanAbandonQuest_ByRank(GuildRank me, bool hasQuest) => GuildActionGate.CanAbandonQuest(me, hasQuest);

    // ── Pay tax late: officer+, only while perks are suspended ──────────────────
    [TestCase(GuildRank.Officer, false, ExpectedResult = true)]  // suspended → can pay
    [TestCase(GuildRank.Leader, false, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, true, ExpectedResult = false)]  // perks active → nothing to pay
    [TestCase(GuildRank.Member, false, ExpectedResult = false)]  // members can't
    public bool CanPayTax_ByRankAndPerks(GuildRank me, bool perksActive) => GuildActionGate.CanPayTax(me, perksActive);

    // ── Declare war: officer+, and the guild must meet the minimum war level ────
    [TestCase(GuildRank.Leader, 1, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, 1, ExpectedResult = true)]      // officer's send is queued server-side
    [TestCase(GuildRank.Officer, 5, ExpectedResult = true)]
    [TestCase(GuildRank.Leader, 0, ExpectedResult = false)]      // level-0 guild can't declare or return
    [TestCase(GuildRank.Member, 1, ExpectedResult = false)]      // members can't
    public bool CanDeclareWar_ByRankAndLevel(GuildRank me, int level) => GuildActionGate.CanDeclareWar(me, level);

    // ── Request war (retract / sue-for-peace): officer+ ─────────────────────────
    [TestCase(GuildRank.Leader, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, ExpectedResult = true)]
    [TestCase(GuildRank.Member, ExpectedResult = false)]
    public bool CanRequestWar_ByRank(GuildRank me) => GuildActionGate.CanRequestWar(me);

    // ── Resolve war (review queue / withdraw / accept / reject peace): leader only
    [TestCase(GuildRank.Leader, ExpectedResult = true)]
    [TestCase(GuildRank.Officer, ExpectedResult = false)]
    [TestCase(GuildRank.Member, ExpectedResult = false)]
    public bool CanResolveWar_ByRank(GuildRank me) => GuildActionGate.CanResolveWar(me);
}

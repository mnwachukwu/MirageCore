using Mirage.Editor.Controls;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>Placement of a dialogue graph on the canvas. Positions are derived rather than authored, so this
/// is the whole of what the picture knows — the control only turns grid slots into pixels.</summary>
[TestFixture]
public class ConversationGraphLayoutTests
{
    private static ConversationGraphBranch To(int nodeId) => new(nodeId, ConversationEndKind.None);
    private static ConversationGraphBranch Ends() => new(0, ConversationEndKind.None);
    private static ConversationGraphBranch Shop() => new(0, ConversationEndKind.OpensShop);
    private static ConversationGraphBranch Quests() => new(0, ConversationEndKind.OpensQuests);

    private static ConversationGraphNode Node(int id, params ConversationGraphBranch[] branches) => new(id, branches);
    private static ConversationGraphNode Leads(int id, params int[] targets) =>
        new(id, [.. targets.Select(To)]);

    private static int RowOf(ConversationGraph g, int id) => g.Find(id)!.Value.Row;
    private static int ColumnOf(ConversationGraph g, int id) => g.Find(id)!.Value.Column;

    [Test]
    public void AnEmptyConversation_LaysOutToNothing()
    {
        var g = ConversationGraphLayout.Build([], rootNodeId: 0);
        Assert.Multiple(() =>
        {
            Assert.That(g.Nodes, Is.Empty);
            Assert.That(g.Links, Is.Empty);
            Assert.That(g.Terminals, Is.Empty);
            Assert.That(g.Rows, Is.Zero);
            Assert.That(g.Columns, Is.Zero);
        });
    }

    [Test]
    public void TheRootSitsOnTheTopRow_AndItsChoicesOneBelow()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2), Leads(3)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(RowOf(g, 1), Is.Zero);
            Assert.That(RowOf(g, 2), Is.EqualTo(1));
            Assert.That(RowOf(g, 3), Is.EqualTo(1));
            Assert.That(ColumnOf(g, 2), Is.Zero, "choices lay out left to right in choice order");
            Assert.That(ColumnOf(g, 3), Is.EqualTo(1));
            Assert.That(g.Rows, Is.EqualTo(2));
            Assert.That(g.Columns, Is.EqualTo(2));
        });
    }

    [Test]
    public void TheRootIsFlagged_AndOnlyTheRoot()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2), Leads(2)], rootNodeId: 2);
        Assert.Multiple(() =>
        {
            Assert.That(g.Find(2)!.Value.IsRoot, Is.True);
            Assert.That(g.Find(1)!.Value.IsRoot, Is.False);
        });
    }

    /// <summary>The same fallback the runtime uses when <c>RootNodeId</c> names nothing: the first node.</summary>
    [Test]
    public void AnUnresolvableRoot_FallsBackToTheFirstNode()
    {
        var g = ConversationGraphLayout.Build([Leads(4, 5), Leads(5)], rootNodeId: 99);
        Assert.Multiple(() =>
        {
            Assert.That(g.Find(4)!.Value.IsRoot, Is.True);
            Assert.That(RowOf(g, 4), Is.Zero);
        });
    }

    [Test]
    public void ANodeIsPlacedByTheShortestWalkToIt()
    {
        // 1 → 2 → 4 and 1 → 3 → 4: the two-step walk wins, so 4 is on row 2, not row 3.
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2, 4), Leads(3, 4), Leads(4)], rootNodeId: 1);
        Assert.That(RowOf(g, 4), Is.EqualTo(2));
    }

    // ── Links ─────────────────────────────────────────────────────────────────

    [Test]
    public void AChoiceThatEnds_DrawsNoLink()
    {
        var g = ConversationGraphLayout.Build([Node(1, To(2), Ends()), Leads(2)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Links, Has.Count.EqualTo(1));
            Assert.That(g.Links[0].ToNodeId, Is.EqualTo(2));
            Assert.That(g.Links[0].ChoiceIndex, Is.Zero);
        });
    }

    [Test]
    public void AChoicePointingAtNothing_DrawsNoLink()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 77)], rootNodeId: 1);
        Assert.That(g.Links, Is.Empty, "a target that names no node ends the conversation at runtime");
    }

    [Test]
    public void ALoopBackUpTheGraph_IsFlaggedBackward()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2), Leads(2, 1)], rootNodeId: 1);
        var back = g.Links.Single(l => l.FromNodeId == 2);
        Assert.Multiple(() =>
        {
            Assert.That(back.IsBackward, Is.True);
            Assert.That(back.IsSelf, Is.False);
            Assert.That(g.Links.Single(l => l.FromNodeId == 1).IsBackward, Is.False);
        });
    }

    [Test]
    public void ALinkAcrossOneRow_IsBackwardToo()
    {
        // 2 and 3 are siblings on the same row, so 2 → 3 goes sideways rather than down.
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2, 3), Leads(3)], rootNodeId: 1);
        Assert.That(g.Links.Single(l => l.FromNodeId == 2).IsBackward, Is.True);
    }

    [Test]
    public void ANodePointingAtItself_IsFlaggedSelf_AndNeverMovesItsOwnRow()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 1)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Links, Has.Count.EqualTo(1));
            Assert.That(g.Links[0].IsSelf, Is.True);
            Assert.That(g.Links[0].IsBackward, Is.False, "a self link is drawn as a loop, not as a climb");
            Assert.That(RowOf(g, 1), Is.Zero);
        });
    }

    [Test]
    public void EveryChoiceGetsItsOwnLink_EvenWhenTwoPointAtTheSameNode()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 2), Leads(2)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Links, Has.Count.EqualTo(2));
            Assert.That(g.Links.Select(l => l.ChoiceIndex), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    // ── Endings ───────────────────────────────────────────────────────────────

    [Test]
    public void ANodeWithNoEndings_GetsNoTerminalSlot()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2), Leads(2, 1)], rootNodeId: 1);
        Assert.That(g.Terminals, Is.Empty);
    }

    [Test]
    public void AGoodbye_GetsAMarkerBelowItsNode()
    {
        var g = ConversationGraphLayout.Build([Node(1, Ends())], rootNodeId: 1);
        var terminal = g.Terminals.Single();
        Assert.Multiple(() =>
        {
            Assert.That(terminal.OwnerNodeId, Is.EqualTo(1));
            Assert.That(terminal.Row, Is.EqualTo(RowOf(g, 1) + 1));
            Assert.That(terminal.Endings.Single().Kind, Is.EqualTo(ConversationEndKind.Ends));
            Assert.That(g.Rows, Is.EqualTo(2), "the marker row counts toward the extent");
        });
    }

    /// <summary>A branch naming a node that is not there is a goodbye at runtime, so it is drawn as one.</summary>
    [Test]
    public void ABranchPointingAtNothing_CountsAsAGoodbye()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 77)], rootNodeId: 1);
        Assert.That(g.Terminals.Single().Endings.Single().Kind, Is.EqualTo(ConversationEndKind.Ends));
    }

    [Test]
    public void SeveralChoicesEndingTheSameWay_AreCountedNotRepeated()
    {
        var g = ConversationGraphLayout.Build([Node(1, Ends(), Ends(), Ends())], rootNodeId: 1);
        var ending = g.Terminals.Single().Endings.Single();
        Assert.Multiple(() =>
        {
            Assert.That(ending.Kind, Is.EqualTo(ConversationEndKind.Ends));
            Assert.That(ending.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void EachKindOfExitGetsItsOwnMarker_InAFixedOrder()
    {
        var g = ConversationGraphLayout.Build([Node(1, Quests(), Shop(), Ends())], rootNodeId: 1);
        Assert.That(g.Terminals.Single().Endings.Select(e => e.Kind), Is.EqualTo(new[]
        {
            ConversationEndKind.Ends, ConversationEndKind.OpensShop, ConversationEndKind.OpensQuests,
        }));
    }

    [Test]
    public void AHandOffNeverBecomesALink()
    {
        var g = ConversationGraphLayout.Build([Node(1, Shop()), Leads(2)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Links, Is.Empty);
            Assert.That(g.Terminals.Single().Endings.Single().Kind, Is.EqualTo(ConversationEndKind.OpensShop));
        });
    }

    /// <summary>The endings sit beside the node's real branches on the row below, so a marker never lands on
    /// top of a child node.</summary>
    [Test]
    public void EndingsShareTheRowBelow_WithoutCollidingWithChildren()
    {
        var g = ConversationGraphLayout.Build([Node(1, To(2), Ends(), To(3)), Leads(2), Leads(3)], rootNodeId: 1);
        var terminal = g.Terminals.Single();
        var occupied = g.Nodes.Where(n => n.Row == terminal.Row).Select(n => n.Column).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(occupied, Is.EquivalentTo(new[] { 0, 1 }), "the two children take the first columns");
            Assert.That(terminal.Column, Is.EqualTo(2), "the marker trails them");
            Assert.That(g.Columns, Is.EqualTo(3));
        });
    }

    // ── Exit anchors ──────────────────────────────────────────────────────────
    // Every way out of a node hangs off its own place on the bottom edge. Sharing one point makes four
    // branches read as a single fan, and a branch that leaves where another one does cannot be told apart.

    [Test]
    public void EachBranchGetsItsOwnAnchor_InChoiceOrder()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2), Leads(3)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Links.Single(l => l.ToNodeId == 2).AnchorIndex, Is.Zero);
            Assert.That(g.Links.Single(l => l.ToNodeId == 3).AnchorIndex, Is.EqualTo(1));
            Assert.That(g.Links.Select(l => l.AnchorCount), Is.All.EqualTo(2));
        });
    }

    /// <summary>The endings share one anchor however many choices take them, because they share one marker
    /// slot — two stubs to the same place would draw as one line anyway.</summary>
    [Test]
    public void EveryEndingSharesOneAnchor_AndItCountsAsOne()
    {
        var g = ConversationGraphLayout.Build([Node(1, To(2), Ends(), Shop(), Ends()), Leads(2)], rootNodeId: 1);
        var terminal = g.Terminals.Single();

        Assert.Multiple(() =>
        {
            Assert.That(g.Links.Single().AnchorIndex, Is.Zero);
            Assert.That(terminal.AnchorIndex, Is.EqualTo(1), "the endings take the slot after the link");
            Assert.That(terminal.AnchorCount, Is.EqualTo(2), "four choices, two places to leave from");
            Assert.That(g.Links.Single().AnchorCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void AnEndingBeforeALink_TakesTheEarlierAnchor()
    {
        var g = ConversationGraphLayout.Build([Node(1, Ends(), To(2)), Leads(2)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Terminals.Single().AnchorIndex, Is.Zero);
            Assert.That(g.Links.Single().AnchorIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void NoTwoWaysOutOfANodeShareAnAnchor()
    {
        var g = ConversationGraphLayout.Build(
            [Node(1, To(2), To(3), Ends(), To(2)), Leads(2), Leads(3)], rootNodeId: 1);

        var used = g.Links.Where(l => l.FromNodeId == 1).Select(l => l.AnchorIndex)
            .Concat(g.Terminals.Where(t => t.OwnerNodeId == 1).Select(t => t.AnchorIndex))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(used, Has.Count.EqualTo(4), "three branches and the endings");
            Assert.That(used.Distinct().Count(), Is.EqualTo(used.Count));
            Assert.That(used.Max(), Is.LessThan(g.Links.First(l => l.FromNodeId == 1).AnchorCount));
        });
    }

    // ── Room for a loop ───────────────────────────────────────────────────────
    // A curve between two nodes on one row, or a node answering itself, has to be drawn somewhere other than
    // through a box. The layout leaves it a column of its own rather than cramming it into the gap.

    [Test]
    public void SiblingsJoinedAlongTheRow_AreNotLeftAdjacent()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2, 3), Leads(3)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(RowOf(g, 2), Is.EqualTo(RowOf(g, 3)));
            Assert.That(ColumnOf(g, 3) - ColumnOf(g, 2), Is.EqualTo(2), "a clear column between them");
            Assert.That(g.Columns, Is.EqualTo(3), "and the extent counts it");
        });
    }

    [Test]
    public void SiblingsThatAnswerEachOther_GetTheSameOneColumn()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2, 3), Leads(3, 2)], rootNodeId: 1);
        Assert.That(ColumnOf(g, 3) - ColumnOf(g, 2), Is.EqualTo(2), "two links, one gap between the pair");
    }

    [Test]
    public void SiblingsWithNothingBetweenThem_StayAdjacent()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2), Leads(3)], rootNodeId: 1);
        Assert.That(ColumnOf(g, 3) - ColumnOf(g, 2), Is.EqualTo(1));
    }

    [Test]
    public void ANodeThatAnswersItself_KeepsTheColumnBesideItClear()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2, 3), Leads(2, 2), Leads(3)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(ColumnOf(g, 3) - ColumnOf(g, 2), Is.EqualTo(2), "the loop draws in the gap");
            Assert.That(g.Links.Single(l => l.FromNodeId == 2).IsSelf, Is.True);
        });
    }

    /// <summary>The gap belongs to the pair, not to every node on the row: nodes with no loop between them
    /// stay packed.</summary>
    [Test]
    public void OnlyThePairThatNeedsRoom_GetsIt()
    {
        var g = ConversationGraphLayout.Build(
            [Leads(1, 2, 3, 4), Leads(2), Leads(3, 4), Leads(4)], rootNodeId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ColumnOf(g, 3) - ColumnOf(g, 2), Is.EqualTo(1), "nothing joins 2 and 3");
            Assert.That(ColumnOf(g, 4) - ColumnOf(g, 3), Is.EqualTo(2), "3 answers 4 along the row");
        });
    }

    /// <summary>A marker slot is not a node, so a node beside one never needs room made for a link that
    /// cannot exist.</summary>
    [Test]
    public void AMarkerBesideANode_NeedsNoRoom()
    {
        var g = ConversationGraphLayout.Build([Node(1, To(2), Ends()), Leads(2)], rootNodeId: 1);
        var terminal = g.Terminals.Single();
        Assert.That(Math.Abs(terminal.Column - ColumnOf(g, 2)), Is.EqualTo(1));
    }

    // ── Orphans ───────────────────────────────────────────────────────────────

    [Test]
    public void ANodeNothingPointsAt_IsDrawnBelowTheGraphAndFlagged()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2), Leads(2), Leads(9)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(g.Find(9)!.Value.IsReachable, Is.False);
            Assert.That(g.Find(1)!.Value.IsReachable, Is.True);
            Assert.That(g.Find(2)!.Value.IsReachable, Is.True);
            Assert.That(RowOf(g, 9), Is.EqualTo(1 + 1 + ConversationGraphLayout.OrphanGapRows),
                "past a blank band, so the split from the reachable graph reads at a glance");
        });
    }

    [Test]
    public void AnOrphanSubtree_KeepsItsOwnShape()
    {
        var g = ConversationGraphLayout.Build([Leads(1), Leads(8, 9), Leads(9)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(RowOf(g, 9), Is.EqualTo(RowOf(g, 8) + 1));
            Assert.That(g.Find(8)!.Value.IsReachable, Is.False);
            Assert.That(g.Find(9)!.Value.IsReachable, Is.False);
            Assert.That(g.Links.Single(l => l.FromNodeId == 8).ToNodeId, Is.EqualTo(9));
        });
    }

    [Test]
    public void TwoOrphanComponents_ShareTheBandRatherThanStacking()
    {
        var g = ConversationGraphLayout.Build([Leads(1), Leads(5), Leads(6)], rootNodeId: 1);
        Assert.Multiple(() =>
        {
            Assert.That(RowOf(g, 5), Is.EqualTo(RowOf(g, 6)));
            Assert.That(ColumnOf(g, 5), Is.Not.EqualTo(ColumnOf(g, 6)));
        });
    }

    /// <summary>Everything authored gets a place, and no two nodes share one. The graph is the only way to
    /// reach a node in this view, so a node missing from it is a node the author cannot fix.</summary>
    [Test]
    public void EveryNodeIsPlacedExactlyOnce()
    {
        var g = ConversationGraphLayout.Build(
            [Leads(1, 2, 3), Leads(2, 3), Leads(3, 1), Leads(4), Leads(5, 4)], rootNodeId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(g.Nodes.Select(n => n.NodeId).OrderBy(n => n), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(g.Nodes.Select(n => (n.Column, n.Row)).Distinct().Count(), Is.EqualTo(5),
                "no two nodes share a slot");
        });
    }

    /// <summary>Markers occupy slots too, so they have to be counted against the nodes as well as each
    /// other.</summary>
    [Test]
    public void NoMarkerSharesASlotWithAnythingElse()
    {
        var g = ConversationGraphLayout.Build(
            [Node(1, To(2), Ends()), Node(2, Shop(), To(3)), Node(3, Ends(), Quests())], rootNodeId: 1);

        var slots = g.Nodes.Select(n => (n.Column, n.Row))
            .Concat(g.Terminals.Select(t => (t.Column, t.Row)))
            .ToList();

        Assert.That(slots.Distinct().Count(), Is.EqualTo(slots.Count));
    }

    [Test]
    public void ADuplicateIdIsTakenOnce()
    {
        var g = ConversationGraphLayout.Build([Leads(1, 2), Leads(2), Leads(2)], rootNodeId: 1);
        Assert.That(g.Nodes.Count(n => n.NodeId == 2), Is.EqualTo(1));
    }
}

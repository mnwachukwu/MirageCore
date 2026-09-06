using Mirage.Editor.Controls;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>What the conversation row hands the visual tree, and when it tells the tree to redraw. The canvas
/// derives every position from this projection, so anything the projection gets wrong is a picture that
/// disagrees with the conversation the runtime will actually walk.</summary>
[TestFixture]
public class ConversationGraphSourceTests
{
    private static ConversationRowViewModel Conv(ConversationRecord? r = null) =>
        new(1, r ?? new ConversationRecord(), () => []);

    private static ConversationRecord Record(params ConversationNode[] nodes)
    {
        var r = new ConversationRecord { RootNodeId = nodes.Length > 0 ? nodes[0].Id : 0 };
        r.Nodes.AddRange(nodes);
        return r;
    }

    private static ConversationNode Node(int id, params ConversationChoice[] choices)
    {
        var n = new ConversationNode { Id = id, Text = $"line {id}" };
        n.Choices.AddRange(choices);
        return n;
    }

    private static ConversationChoice Go(string label, int next) => new() { Label = label, NextNodeId = next };

    // ── The projection ────────────────────────────────────────────────────────

    [Test]
    public void EveryNodeIsProjected_WithItsBranchesInChoiceOrder()
    {
        var c = Conv(Record(Node(1, Go("a", 2), Go("b", 3)), Node(2), Node(3)));
        var graph = c.GraphNodes();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Select(n => n.Id), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(graph[0].Branches.Select(b => b.TargetNodeId), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(graph[0].Branches.Select(b => b.End),
                Is.EqualTo(new[] { ConversationEndKind.None, ConversationEndKind.None }));
            Assert.That(graph[1].Branches, Is.Empty);
        });
    }

    /// <summary>A hand-off leaves the conversation for the shop or the quest list and its next-node id is
    /// never read, so the graph must not draw a branch the player can never take.</summary>
    [Test]
    public void AHandOffChoiceProjectsAsItsOwnExit_WhateverItsNextNodeSays()
    {
        var c = Conv(Record(Node(1, Go("shop", 2)), Node(2)));
        c.Nodes[0].Choices[0].Action = ConversationAction.OpenShop;

        var branch = c.GraphNodes()[0].Branches.Single();
        Assert.Multiple(() =>
        {
            Assert.That(branch.End, Is.EqualTo(ConversationEndKind.OpensShop));
            Assert.That(branch.TargetNodeId, Is.Zero, "the id it still carries must not become a link");
        });
    }

    [Test]
    public void AQuestHandOffProjectsAsTheQuestExit()
    {
        var c = Conv(Record(Node(1, Go("quests", 0))));
        c.Nodes[0].Choices[0].Action = ConversationAction.OpenQuests;

        Assert.That(c.GraphNodes()[0].Branches.Single().End, Is.EqualTo(ConversationEndKind.OpensQuests));
    }

    [Test]
    public void ClearingTheHandOffBringsTheBranchBack()
    {
        var c = Conv(Record(Node(1, Go("quests", 2)), Node(2)));
        c.Nodes[0].Choices[0].Action = ConversationAction.OpenQuests;
        Assume.That(c.GraphNodes()[0].Branches.Single().End, Is.EqualTo(ConversationEndKind.OpensQuests));

        c.Nodes[0].Choices[0].Action = ConversationAction.None;
        var branch = c.GraphNodes()[0].Branches.Single();
        Assert.Multiple(() =>
        {
            Assert.That(branch.End, Is.EqualTo(ConversationEndKind.None));
            Assert.That(branch.TargetNodeId, Is.EqualTo(2));
        });
    }

    // ── When the picture has to be redrawn ────────────────────────────────────

    private static int CountGraphChanges(ConversationRowViewModel c, Action edit)
    {
        int raised = 0;
        void Handler() => raised++;
        c.GraphChanged += Handler;
        try { edit(); }
        finally { c.GraphChanged -= Handler; }
        return raised;
    }

    [Test]
    public void AddingANode_RedrawsTheTree()
    {
        var c = Conv();
        Assert.That(CountGraphChanges(c, () => c.AddNodeCommand.Execute(null)), Is.GreaterThan(0));
    }

    [Test]
    public void RemovingANode_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1), Node(2)));
        Assert.That(CountGraphChanges(c, () => c.RemoveNodeCommand.Execute(c.Nodes[1])), Is.GreaterThan(0));
    }

    [Test]
    public void RepointingAChoice_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1, Go("a", 0)), Node(2)));
        Assert.That(CountGraphChanges(c, () => c.Nodes[0].Choices[0].NextNodeId = 2), Is.GreaterThan(0));
    }

    [Test]
    public void AddingAChoiceToANode_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1)));
        Assert.That(CountGraphChanges(c, () => c.Nodes[0].AddChoiceCommand.Execute(null)), Is.GreaterThan(0));
    }

    [Test]
    public void RetitlingANode_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1)));
        Assert.That(CountGraphChanges(c, () => c.Nodes[0].Text = "something else"), Is.GreaterThan(0));
    }

    [Test]
    public void PickingADifferentOpeningNode_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1), Node(2)));
        Assert.That(CountGraphChanges(c, () => c.RootNodeId = 2), Is.GreaterThan(0));
    }

    /// <summary>A whole conversation arriving from the server replaces every node, so the canvas cannot keep
    /// drawing the one it had.</summary>
    [Test]
    public void LoadingADifferentRecord_RedrawsTheTree()
    {
        var c = Conv(Record(Node(1)));
        Assert.That(CountGraphChanges(c, () => c.LoadFromRecord(Record(Node(7), Node(8)))), Is.GreaterThan(0));
    }

    // ── The two views are one preference ──────────────────────────────────────

    private static ConversationEditorViewModel Editor() =>
        new(new EditorDataService(), new EditorConnection());

    [Test]
    public void TheGraphIsTheDefaultView()
    {
        var vm = Editor();
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsGraphView, Is.True);
            Assert.That(vm.IsTextView, Is.False);
        });
    }

    [Test]
    public void TheTwoViewsAreAlwaysOpposites()
    {
        var vm = Editor();
        vm.IsTextView = true;
        Assert.That(vm.IsGraphView, Is.False);

        vm.IsGraphView = true;
        Assert.That(vm.IsTextView, Is.False);
    }

    /// <summary>A radio button clears the one it replaces before the new one reports in. Honouring that clear
    /// would put the pane back where it started.</summary>
    [Test]
    public void ClearingAViewDoesNothing_OnlyCheckingOneChooses()
    {
        var vm = Editor();
        vm.IsGraphView = true;

        vm.IsTextView = false;
        Assert.That(vm.IsGraphView, Is.True);
    }
}

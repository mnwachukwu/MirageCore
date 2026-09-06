using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The conversation-editor row: a dynamic NODES table where each node owns its own dynamic CHOICES table
/// (two levels over the quest pattern). Covers blank-start, add/remove + ceilings at both levels, nested dirty
/// bubbling (choice → node → conversation), STABLE node ids that survive a middle removal (never reused), the
/// self-referential NextNode picker, empties dropped on save (ids preserved), and ApplyPacket loading clean.</summary>
[TestFixture]
public class ConversationRowViewModelTests
{
    static ConversationRowViewModel Conv(ConversationRecord? r = null) =>
        new(1, r ?? new ConversationRecord(), () => []);

    [Test]
    public void NewConversation_StartsBlank()
    {
        var c = Conv();
        Assert.Multiple(() =>
        {
            Assert.That(c.Nodes, Is.Empty);
            Assert.That(c.HasNoNodes, Is.True);
            Assert.That(c.IsDirty, Is.False);
        });
    }

    [Test]
    public void AddNode_AppendsAndDirties_Remove_RemovesIt()
    {
        var c = Conv();
        c.AddNodeCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(c.Nodes, Has.Count.EqualTo(1));
            Assert.That(c.HasNoNodes, Is.False);
            Assert.That(c.IsDirty, Is.True);
        });

        c.RemoveNodeCommand.Execute(c.Nodes[0]);
        Assert.That(c.Nodes, Is.Empty);
    }

    [Test]
    public void AddNode_IsDisabledAtTheCeiling()
    {
        var c = Conv();
        for (int i = 0; i < Constants.MaxConversationNodes; i++) c.AddNodeCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(c.Nodes, Has.Count.EqualTo(Constants.MaxConversationNodes));
            Assert.That(c.AddNodeCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void AddChoice_OnANode_AppendsAndDirtiesTheConversation()
    {
        var c = Conv();
        c.AddNodeCommand.Execute(null);
        var node = c.Nodes[0];
        c.ClearDirty();
        Assume.That(c.IsDirty, Is.False);

        node.AddChoiceCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(node.Choices, Has.Count.EqualTo(1));
            Assert.That(node.HasNoChoices, Is.False);
            Assert.That(c.IsDirty, Is.True, "a nested choice add bubbles up to dirty the conversation");
        });
    }

    [Test]
    public void AddChoice_IsDisabledAtTheCeiling()
    {
        var c = Conv();
        c.AddNodeCommand.Execute(null);
        var node = c.Nodes[0];
        for (int i = 0; i < Constants.MaxConversationChoices; i++) node.AddChoiceCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(node.Choices, Has.Count.EqualTo(Constants.MaxConversationChoices));
            Assert.That(node.AddChoiceCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void EditingAChoiceLabel_DirtiesTheConversation()
    {
        var c = Conv(new ConversationRecord
        {
            Name = "C",
            Nodes = { new ConversationNode { Id = 1, Text = "A", Choices = { new ConversationChoice { Label = "go" } } } },
        });
        Assume.That(c.IsDirty, Is.False, "a freshly loaded conversation is clean");

        c.Nodes[0].Choices[0].Label = "leave";

        Assert.That(c.IsDirty, Is.True);
    }

    [Test]
    public void NodeIds_AreStable_SurviveMiddleRemoval_AndAreNeverReused()
    {
        var c = Conv();
        c.AddNodeCommand.Execute(null);   // id 1
        c.AddNodeCommand.Execute(null);   // id 2
        c.AddNodeCommand.Execute(null);   // id 3
        Assume.That(c.Nodes.Select(n => n.NodeId), Is.EqualTo(new[] { 1, 2, 3 }));

        c.RemoveNodeCommand.Execute(c.Nodes[1]);   // drop id 2
        c.AddNodeCommand.Execute(null);            // must be id 4, NOT a reused 2

        Assert.That(c.Nodes.Select(n => n.NodeId), Is.EqualTo(new[] { 1, 3, 4 }));
    }

    [Test]
    public void ChoiceNextNode_ResolvesToATargetNode_WithAnEndOption()
    {
        var c = Conv(new ConversationRecord
        {
            Name = "C",
            Nodes =
            {
                new ConversationNode { Id = 1, Text = "A", Choices = { new ConversationChoice { Label = "go", NextNodeId = 2 } } },
                new ConversationNode { Id = 2, Text = "B" },
            },
        });
        var choice = c.Nodes[0].Choices[0];

        Assert.Multiple(() =>
        {
            Assert.That(choice.SelectedNextNode?.Id, Is.EqualTo(2), "the choice resolves to node 2");
            Assert.That(choice.NextNodeEntries.Any(e => e.Id == 0), Is.True, "an (End) option is offered");
            Assert.That(choice.NextNodeEntries.Any(e => e.Id == 2), Is.True, "the target node is offered");
        });
    }

    [Test]
    public void ZeroIdPickers_ResolveToNullSentinel_SoTheDropdownStaysSelectable()
    {
        // id 0 ("(End)"/"(first node)") must resolve SelectedX to NULL — the id=0-is-null-sentinel contract
        // DropdownAutoCompleteBox relies on to clear its text box. If it returned the real (0, "...") entry the
        // box would read "0: (End...)" and the type-ahead filter would hide every node, freezing the picker.
        var c = Conv();
        c.AddNodeCommand.Execute(null);              // node id 1
        c.Nodes[0].AddChoiceCommand.Execute(null);   // a choice; NextNodeId defaults to 0
        var choice = c.Nodes[0].Choices[0];

        Assert.Multiple(() =>
        {
            Assert.That(choice.NextNodeId, Is.EqualTo(0));
            Assert.That(choice.SelectedNextNode, Is.Null, "id 0 is the null sentinel, not the (End) entry");
            Assert.That(choice.NextNodeEntries.Any(e => e.Id == 0), Is.True, "but (End) is still offered in the list");
            Assert.That(c.RootNodeId, Is.EqualTo(0));
            Assert.That(c.SelectedRootNode, Is.Null, "root id 0 is the null sentinel too");
        });
    }

    [Test]
    public void ToRecord_DropsEmptyNodesAndChoices_PreservesNodeIds()
    {
        var c = Conv();
        c.AddNodeCommand.Execute(null);   // node id 1 — will fill
        c.AddNodeCommand.Execute(null);   // node id 2 — leave empty (no text, no choices) → dropped
        c.Nodes[0].Text = "Hello.";
        c.Nodes[0].AddChoiceCommand.Execute(null);   // fill a choice
        c.Nodes[0].AddChoiceCommand.Execute(null);   // leave a blank choice → dropped
        c.Nodes[0].Choices[0].Label = "Bye";
        c.Nodes[0].Choices[0].NextNodeId = 0;

        var rec = c.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(rec.Nodes, Has.Count.EqualTo(1), "the empty node is dropped");
            Assert.That(rec.Nodes[0].Id, Is.EqualTo(1), "the stable node id is preserved");
            Assert.That(rec.Nodes[0].Choices, Has.Count.EqualTo(1), "the blank choice is dropped");
            Assert.That(rec.Nodes[0].Choices[0].Label, Is.EqualTo("Bye"));
        });
    }

    [Test]
    public void ApplyPacket_LoadsTree_AndIsNotDirty()
    {
        var c = Conv();
        c.ApplyPacket(new UpdateConversationPacket
        {
            Name = "Greeting",
            SpeakerNpc = 7,
            RootNodeId = 1,
            Nodes =
            {
                new ConversationNode { Id = 1, Text = "Well met.", Choices = { new ConversationChoice { Label = "Wares", Action = ConversationAction.OpenShop } } },
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(c.Name, Is.EqualTo("Greeting"));
            Assert.That(c.SpeakerNpc, Is.EqualTo(7));
            Assert.That(c.Nodes, Has.Count.EqualTo(1));
            Assert.That(c.Nodes[0].Choices[0].Action, Is.EqualTo(ConversationAction.OpenShop));
            Assert.That(c.IsLoaded, Is.True);
            Assert.That(c.IsDirty, Is.False, "loading from the wire is not an edit");
        });
    }

    [Test]
    public void BuildSavePacket_CarriesIndexAndTree()
    {
        var c = Conv();
        c.Name = "C";
        c.AddNodeCommand.Execute(null);
        c.Nodes[0].Text = "Hi.";

        var pkt = c.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.ConvNum, Is.EqualTo(1));
            Assert.That(pkt.Nodes, Has.Count.EqualTo(1));
            Assert.That(pkt.Nodes[0].Text, Is.EqualTo("Hi."));
        });
    }
}

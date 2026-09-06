using Mirage.Client.Core.State;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>Client-side conversation derivation: the overhead "..." glyph is computed from the conversation DEFS
/// (SendConversations) + the character's spoken-set (ConversationLog). Yellow "..." = a conversation this character
/// hasn't opened yet; gray "..." = already spoken. Glyph codes: 0 none / 1 gray (spoken) / 2 yellow (unspoken) —
/// higher wins, so an unspoken conversation still invites a talk on an NPC that has several.</summary>
[TestFixture]
public class ClientConversationStateTests
{
    const int Npc = 7;

    static ConversationRecord Convo(int speakerNpc, string name = "C")
    {
        var c = new ConversationRecord { Name = name, SpeakerNpc = speakerNpc, RootNodeId = 1 };
        c.Nodes.Add(new ConversationNode { Id = 1, Text = "Hello." });
        return c;
    }

    [Test]
    public void UnspokenConversation_YellowGlyph_AndResolves()
    {
        var s = new ClientState();
        s.SetConvDefs(new[] { (5, Convo(Npc)) });

        Assert.Multiple(() =>
        {
            Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphUnspoken));
            Assert.That(s.ConversationForNpc(Npc), Is.EqualTo(5));
        });
    }

    [Test]
    public void SpokenConversation_GrayGlyph()
    {
        var s = new ClientState();
        s.SetConvDefs(new[] { (5, Convo(Npc)) });
        s.SetConversationsSpoken(new[] { 5 });

        Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphSpoken));
    }

    [Test]
    public void UnattachedConversation_NoGlyphForAnyNpc()
    {
        var s = new ClientState();
        s.SetConvDefs(new[] { (5, Convo(speakerNpc: 0)) });

        Assert.Multiple(() =>
        {
            Assert.That(s.ConversationForNpc(Npc), Is.EqualTo(0), "an unattached conversation matches no NPC");
            Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphNone));
        });
    }

    [Test]
    public void SpokenSet_RefreshesGlyphLive()
    {
        var s = new ClientState();
        s.SetConvDefs(new[] { (5, Convo(Npc)) });
        Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphUnspoken));

        s.SetConversationsSpoken(new[] { 5 });   // the character just talked to it
        Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphSpoken), "yellow -> gray after speaking");
    }

    [Test]
    public void UnspokenOutranksSpoken_OnSharedNpc()
    {
        // NPC 7 has two conversations: #3 spoken (gray), #6 unspoken (yellow). Yellow (higher) wins.
        var s = new ClientState();
        s.SetConvDefs(new[] { (3, Convo(Npc)), (6, Convo(Npc)) });
        s.SetConversationsSpoken(new[] { 3 });

        Assert.That(s.NpcConvGlyph[Npc], Is.EqualTo(ClientState.ConvGlyphUnspoken), "an unspoken conversation still invites a talk");
    }
}

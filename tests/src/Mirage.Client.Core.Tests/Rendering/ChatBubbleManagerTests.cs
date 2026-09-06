using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>Chat-bubble head → drifter demotion. Silent entities pay zero allocation (the drifter list is
/// lazy). Demote-only keeps the head text (a replacement is arriving); natural expiry demotes AND clears.</summary>
[TestFixture]
public class ChatBubbleManagerTests
{
    [Test]
    public void DemoteHeadToDrifter_NoHead_NoAllocation()
    {
        var p = new PlayerRecord();   // ChatBubbleText is null
        ChatBubbleManager.DemoteHeadToDrifter(p, 1000);
        Assert.That(p.ChatBubbleDrifters, Is.Null, "no head bubble means no drifter list is allocated");
    }

    [Test]
    public void DemoteHeadToDrifter_MovesHeadIntoDrifters_KeepsText()
    {
        var p = new PlayerRecord { ChatBubbleText = "hi", ChatBubbleColor = 3 };
        ChatBubbleManager.DemoteHeadToDrifter(p, 1234);
        Assert.Multiple(() =>
        {
            Assert.That(p.ChatBubbleDrifters, Has.Count.EqualTo(1));
            Assert.That(p.ChatBubbleDrifters![0], Is.EqualTo(new ChatBubbleDrifter("hi", 3, 1234)));
            Assert.That(p.ChatBubbleText, Is.EqualTo("hi"), "demote-only keeps the head (a replacement follows)");
        });
    }

    [Test]
    public void NaturallyExpire_DemotesAndClearsHead()
    {
        var p = new PlayerRecord { ChatBubbleText = "bye", ChatBubbleColor = 2 };
        ChatBubbleManager.NaturallyExpire(p, 5000);
        Assert.Multiple(() =>
        {
            Assert.That(p.ChatBubbleDrifters, Has.Count.EqualTo(1));
            Assert.That(p.ChatBubbleText, Is.Null, "natural expiry clears the head");
        });
    }

    // The NPC overload has the same head+drifter model.
    [Test]
    public void NpcOverload_DemotesAndClears()
    {
        var n = new ClientMapNpc { ChatBubbleText = "grr", ChatBubbleColor = 1 };
        ChatBubbleManager.NaturallyExpire(n, 10);
        Assert.Multiple(() =>
        {
            Assert.That(n.ChatBubbleDrifters, Has.Count.EqualTo(1));
            Assert.That(n.ChatBubbleText, Is.Null);
        });
    }
}

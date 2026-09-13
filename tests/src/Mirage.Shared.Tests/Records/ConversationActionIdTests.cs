using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using System.Text.Json;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// The seam that lets a conversation reach a verb Core has no name for.
///
/// <para>🔴 <b>A field, and not an enum member.</b> <see cref="ConversationAction"/> is persisted BY
/// NAME, and a name no build knows loses the whole record — so that enum is closed to a game, and the
/// engine's two hand-offs are all it holds. A string id beside it is open to every game, reads back
/// blank on a file that omits it, and costs the enum nothing.</para>
/// </summary>
[TestFixture]
public class ConversationActionIdTests
{
    private static ConversationRecord WithChoice(ConversationChoice choice) => new()
    {
        Name = "Ranger", SpeakerNpc = 3, RootNodeId = 1,
        Nodes = [new ConversationNode { Id = 1, Text = "Well met.", Choices = [choice] }],
    };

    private static ConversationChoice First(ConversationRecord r) => r.Nodes[0].Choices[0];

    [Test]
    public void AChoiceThatOnlyNavigates_NamesNoAction()
        => Assert.That(new ConversationChoice().ActionId, Is.Empty);

    [Test]
    public void TheActionIdSurvivesADiskRoundTrip()
    {
        var saved = WithChoice(new ConversationChoice { Label = "Note it down", ActionId = "survey.note" });

        var json = JsonSerializer.Serialize(saved, RecordJson.Options);
        var back = JsonSerializer.Deserialize<ConversationRecord>(json, RecordJson.Options)!;

        Assert.That(First(back).ActionId, Is.EqualTo("survey.note"));
    }

    /// <summary>🔴 The openness claim, made load-bearing: a conversation file that omits the id reads
    /// with a blank one rather than failing, so a game never using this seam never sees it.</summary>
    [Test]
    public void AConversationFileOmittingTheId_StillReads()
    {
        const string onDisk = """
        {
          "Name": "Ranger",
          "SpeakerNpc": 3,
          "RootNodeId": 1,
          "Nodes": [
            { "Id": 1, "Speaker": "", "Text": "Well met.",
              "Choices": [ { "Label": "Goodbye", "NextNodeId": 0, "Action": "None" } ] }
          ]
        }
        """;

        var back = JsonSerializer.Deserialize<ConversationRecord>(onDisk, RecordJson.Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.TrimmedName, Is.EqualTo("Ranger"), "the whole record reads, not just the field");
            Assert.That(First(back).Label, Is.EqualTo("Goodbye"));
            Assert.That(First(back).ActionId, Is.Empty);
        });
    }

    [Test]
    public void TheActionIdReachesTheClientOnTheJoinTimeDefinition()
    {
        var sent = new SendConversationsPacket
        {
            Conversations =
            [
                new SendConversationsPacket.ConvData
                {
                    Num = 1, Name = "Ranger", SpeakerNpc = 3, RootNodeId = 1,
                    Nodes = WithChoice(new ConversationChoice
                    {
                        Label = "Note it down", ActionId = "survey.note",
                    }).Nodes,
                },
            ],
        };

        var back = (SendConversationsPacket)PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent))!;

        Assert.That(back.Conversations[0].Nodes[0].Choices[0].ActionId, Is.EqualTo("survey.note"),
                    "the client walks the tree locally, so an id that does not travel can never be picked");
    }

    /// <summary>A game's verb and an engine hand-off are separate fields, so a choice carrying an id is
    /// still <see cref="ConversationAction.None"/> — the client must read the id first or a declared
    /// verb would silently become "just navigate".</summary>
    [Test]
    public void AGameVerbDoesNotOccupyTheEnginesHandOff()
    {
        var choice = new ConversationChoice { ActionId = "survey.note", NextNodeId = 4 };

        Assert.Multiple(() =>
        {
            Assert.That(choice.Action, Is.EqualTo(ConversationAction.None));
            Assert.That(choice.NextNodeId, Is.EqualTo(4), "a navigation target is not consulted once an id is set");
        });
    }

    [Test]
    public void CloningAChoiceCarriesTheActionId()
    {
        var clone = WithChoice(new ConversationChoice { ActionId = "survey.note" }).Clone();

        Assert.That(First(clone).ActionId, Is.EqualTo("survey.note"),
                    "the broadcast snapshot is a deep copy — an id lost there never reaches a player");
    }
}

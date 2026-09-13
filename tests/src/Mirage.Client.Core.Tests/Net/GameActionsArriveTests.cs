using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>
/// The client's half of a verb it was never compiled against.
///
/// <para>🔴 The client's whole job here is to forget nothing and understand nothing. It holds a caption
/// and an id, offers the first, and sends the second back unaltered — any cleverness in between is a
/// client deciding something about a game it does not have, which is the failure this seam exists to make
/// impossible.</para>
/// </summary>
[TestFixture]
public class GameActionsArriveTests
{
    private static (ClientState State, ClientPacketHandler Handler) Playing()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        state.NeighborMapNums[1, 1] = 1;
        state.Me.Name = "Me";
        state.Me.Map = 1;

        return (state, new ClientPacketHandler(state, null!, null!));
    }

    private static void Send(ClientPacketHandler handler, params GameAction[] actions)
        => handler.Handle(PacketSerializer.Serialize(PacketBuilder.GameActions(new GameActions(actions))));

    [Test]
    public void ADeclaredActionArrivesWithItsCaptionAndItsGroup()
    {
        var (state, handler) = Playing();

        Send(handler, new GameAction
        {
            Id = "survey.note", LabelKey = "Note this down", GroupKey = "Survey", Surface = ActionSurface.Tile,
        });

        var offered = state.Actions.For(ActionSurface.Tile);
        Assert.Multiple(() =>
        {
            Assert.That(offered, Has.Count.EqualTo(1));
            Assert.That(offered[0].Id, Is.EqualTo("survey.note"), "the id goes back untouched or nothing works");
            Assert.That(offered[0].LabelKey, Is.EqualTo("Note this down"));
            Assert.That(offered[0].GroupKey, Is.EqualTo("Survey"));
        });
    }

    [Test]
    public void ActionsArriveInTheOrderTheGameDeclaredThem()
    {
        var (state, handler) = Playing();

        Send(handler,
            new GameAction { Id = "b", LabelKey = "Second", Ordinal = 1 },
            new GameAction { Id = "a", LabelKey = "First", Ordinal = 0 });

        Assert.That(state.Actions.For(ActionSurface.Tile).Select(a => a.Id), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void AGameThatOffersNothing_LeavesTheMenusAsCoreDrawsThem()
    {
        var (state, handler) = Playing();

        Send(handler);

        Assert.That(state.Actions.For(ActionSurface.Tile), Is.Empty);
    }

    /// <summary>The menu draws on a click, which can land before the join traffic finishes.</summary>
    [Test]
    public void TheSetIsAskableBeforeAnythingArrives()
    {
        var (state, _) = Playing();

        Assert.That(state.Actions.For(ActionSurface.Tile), Is.Empty);
    }

    /// <summary>🔴 The id is opaque to this client and has to survive as written. A client that
    /// normalised, trimmed or cased it would send back something no handler owns, and the action would
    /// silently do nothing.</summary>
    [Test]
    public void AnIdSurvivesExactlyAsTheGameWroteIt()
    {
        var (state, handler) = Playing();

        Send(handler, new GameAction { Id = "Survey.Note_v2 ", LabelKey = "x" });

        Assert.That(state.Actions.All[0].Id, Is.EqualTo("Survey.Note_v2 "));
    }

    [Test]
    public void TheInvokeCarriesTheIdAndTheSquare()
    {
        var sent = PacketSerializer.Serialize(
            new InvokeActionPacket { Action = "survey.note", MapNum = 3, X = 7, Y = 9 });

        var back = (InvokeActionPacket)PacketSerializer.TryDeserialize(sent)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Action, Is.EqualTo("survey.note"));
            Assert.That((back.MapNum, back.X, back.Y), Is.EqualTo((3, 7, 9)));
        });
    }
}

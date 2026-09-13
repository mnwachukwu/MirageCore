using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>
/// A game's heads-up display, from the packet the server sends to the rows a surface draws.
///
/// <para>🔴 Every step of this is invisible when it fails. A declaration that does not arrive, a
/// declaration that arrives and is not stored, a stored declaration nothing resolves — all three leave
/// the same empty sidebar, which is also what a game that declared nothing correctly produces. So the
/// path is walked end to end here rather than asserted a piece at a time.</para>
/// </summary>
[TestFixture]
public class DisplayFieldsArriveTests
{
    private const int Me = 1;

    private static (ClientState State, ClientPacketHandler Handler) Playing()
    {
        var state = new ClientState { MyIndex = Me, InGame = true, CenterMapNum = 1 };
        state.NeighborMapNums[1, 1] = 1;
        state.Me.Name = "Me";
        state.Me.Map = 1;

        return (state, new ClientPacketHandler(state, null!, null!));
    }

    /// <summary>The production path exactly: built by the server's own builder, written as a line,
    /// handled by the real dispatcher.</summary>
    private static void Send(ClientPacketHandler handler, params DisplayField[] fields)
        => handler.Handle(PacketSerializer.Serialize(PacketBuilder.DisplayFields(new DisplayFieldSet(fields))));

    [Test]
    public void ADeclarationArrivesAndResolvesAgainstWhatTheBodyCarries()
    {
        var (state, handler) = Playing();

        Send(handler,
            new DisplayField { ValueKey = "title", LabelKey = "hud.title", Ordinal = 0 },
            new DisplayField
            {
                ValueKey = "fuel", MaxKey = "fuelMax", LabelKey = "hud.fuel",
                Style = DisplayStyle.Meter, Ordinal = 1,
            });
        state.Me.Attributes.Set("title", "Pilot").Set("fuel", 60).Set("fuelMax", 80);

        var rows = state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).Rows;

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Text, Is.EqualTo("Pilot"));
            Assert.That(rows[1].Style, Is.EqualTo(DisplayStyle.Meter));
            Assert.That(rows[1].Fill, Is.EqualTo(0.75).Within(0.0001));
        });
    }

    /// <summary>🔴 The declaration arrives ONCE and the values keep moving. A row that did not follow
    /// its attribute would read correctly at login and be wrong for the rest of the session.</summary>
    [Test]
    public void ARowFollowsItsValueWithoutAnotherPacket()
    {
        var (state, handler) = Playing();
        Send(handler, new DisplayField { ValueKey = "fuel", MaxKey = "fuelMax", Style = DisplayStyle.Meter });
        state.Me.Attributes.Set("fuel", 10).Set("fuelMax", 100);
        Assume.That(state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).Rows[0].Fill,
                    Is.EqualTo(0.1).Within(0.0001));

        state.Me.Attributes.Set("fuel", 90);

        Assert.That(state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).Rows[0].Fill,
                    Is.EqualTo(0.9).Within(0.0001));
    }

    [Test]
    public void AGameThatDeclaredNothing_DrawsNothing()
    {
        var (state, handler) = Playing();

        Send(handler);

        Assert.That(state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).IsEmpty, Is.True);
    }

    /// <summary>A surface Core never draws still reaches the client intact, so a game's own client code
    /// can ask for it.</summary>
    [Test]
    public void ASurfaceCoreDoesNotDraw_StillArrives()
    {
        var (state, handler) = Playing();

        Send(handler, new DisplayField { Surface = "sheet", ValueKey = "gold" });

        Assert.Multiple(() =>
        {
            Assert.That(state.DisplayFields.For(DisplaySurfaces.Hud), Is.Empty);
            Assert.That(state.DisplayFields.For("sheet"), Has.Count.EqualTo(1));
        });
    }

    /// <summary>Before the packet lands there is a set to ask, not a null to guard against — the HUD
    /// draws on the first frame, which is before the join traffic finishes.</summary>
    [Test]
    public void TheSetIsAskableBeforeAnythingArrives()
    {
        var (state, _) = Playing();

        Assert.That(state.DisplayFields.Project(DisplaySurfaces.Hud, state.Me.Attributes).IsEmpty, Is.True);
    }
}

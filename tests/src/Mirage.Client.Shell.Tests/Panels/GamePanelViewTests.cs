using Microsoft.Xna.Framework;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Panels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// The one panel the client does not know the contents of.
///
/// <para>Every other panel in the registry was written against a thing the client understands — a bag,
/// a shop, a mailbox. This one is opened by an id that arrived over the wire, and what it shows is
/// whatever display rows the game declared for its surface. So the interesting assertions are about
/// RESOLUTION: an id naming a panel opens it at the declared size, and an id naming nothing opens
/// nothing rather than a blank frame.</para>
/// </summary>
[TestFixture]
public class GamePanelViewTests
{
    private static ClientState StateWith(params GamePanel[] panels)
        => new() { Panels = new GamePanels(panels) };

    private static GamePanel Book(int w = 0, int h = 0) => new()
    {
        Id = "book", TitleKey = "Field Book", Surface = "book", Width = w, Height = h,
        Buttons = [new PanelButton("Note it", "note")],
    };

    [Test]
    public void AnIdNamingNothing_OpensNothing()
    {
        var view = new GamePanelView();

        view.Open(StateWith(Book()), "no-such-panel");

        Assert.That(view.IsOpen, Is.False,
                    "a client still offering a removed panel's action must not raise a blank frame");
    }

    [Test]
    public void AClientHoldingNoPanels_OpensNothing()
    {
        var view = new GamePanelView();

        view.Open(new ClientState(), "book");

        Assert.That(view.IsOpen, Is.False);
    }

    [Test]
    public void AnIdNamingAPanel_OpensIt()
    {
        var view = new GamePanelView();

        view.Open(StateWith(Book()), "book");

        Assert.That(view.IsOpen, Is.True);
    }

    [Test]
    public void ADeclaredSizeIsHonored()
    {
        var view = new GamePanelView();

        view.Open(StateWith(Book(w: 240, h: 180)), "book");

        Assert.That((view.Bounds.Width, view.Bounds.Height), Is.EqualTo((240, 180)));
    }

    /// <summary>A game that declares no size gets the client's own, not a zero-sized window.</summary>
    [Test]
    public void ADeclaredSizeOfZero_FallsBackToTheClientsDefault()
    {
        var view = new GamePanelView();

        view.Open(StateWith(Book()), "book");

        Assert.Multiple(() =>
        {
            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.Bounds.Height, Is.GreaterThan(0));
        });
    }

    /// <summary>Opening a second panel replaces the first: there is one slot, so a game cannot bury the
    /// player's own windows under a stack of its own.</summary>
    [Test]
    public void OpeningASecondPanel_ReplacesTheFirst()
    {
        var view = new GamePanelView();
        var state = StateWith(Book(240, 180), new GamePanel { Id = "map", Surface = "map", Width = 300, Height = 200 });

        view.Open(state, "book");
        view.Open(state, "map");

        Assert.Multiple(() =>
        {
            Assert.That(view.IsOpen, Is.True);
            Assert.That((view.Bounds.Width, view.Bounds.Height), Is.EqualTo((300, 200)));
        });
    }

    /// <summary>Reopening keeps where the player dragged it. A panel that jumped home every time the
    /// player pressed its button would be unusable as a thing to keep open beside the world.</summary>
    [Test]
    public void ReopeningKeepsWhereThePlayerPutIt()
    {
        var view = new GamePanelView();
        var state = StateWith(Book(240, 180));

        view.Open(state, "book");
        view.SetBounds(new Rectangle(400, 300, 240, 180));
        view.Close();
        view.Open(state, "book");

        Assert.That((view.Bounds.X, view.Bounds.Y), Is.EqualTo((400, 300)));
    }

    [Test]
    public void AnOpenPanelClaimsThePointerOverItself()
    {
        var view = new GamePanelView();
        view.Open(StateWith(Book(240, 180)), "book");
        view.SetBounds(new Rectangle(0, 0, 240, 180));

        Assert.Multiple(() =>
        {
            Assert.That(view.ContainsMouse(new Point(120, 90)), Is.True);
            Assert.That(view.ContainsMouse(new Point(400, 400)), Is.False);
        });
    }
}

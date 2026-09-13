using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// A screen a game paints, and the two halves it is made of.
///
/// <para>🔴 Neither half is new. A panel's body is <see cref="DisplayField"/> rows on a surface, and its
/// buttons are <see cref="GameAction"/> ids — so what a screen SAYS and what it DOES are both things the
/// engine already carries, and neither is code. That is the whole reason a client can paint a window for
/// a game it has never heard of: there is nothing to run, only names to resolve.</para>
/// </summary>
[TestFixture]
public class GamePanelTests
{
    private const string Surface = "book";

    private sealed class Module(params GamePanel[] panels) : ICoreModule
    {
        public string Name => "Bookish";

        public void Configure(ICoreBuilder builder)
        {
            builder.AddDisplayField(new DisplayField
            {
                Surface = Surface, ValueKey = "rank", LabelKey = "Rank", Ordinal = 0,
            });
            builder.AddDisplayField(new DisplayField
            {
                Surface = Surface, ValueKey = "seen", MaxKey = "seenMax",
                LabelKey = "Seen", Style = DisplayStyle.Meter, Ordinal = 1,
            });
            foreach (var panel in panels) builder.AddPanel(panel);
        }
    }

    private static GamePanel Book(string id = "book") => new()
    {
        Id = id,
        TitleKey = "Field Book",
        Surface = Surface,
        Buttons = [new PanelButton("Note it", "note")],
    };

    [Test]
    public void AnEngineWithNoGameLoaded_PaintsNoScreensOfItsOwn()
        => Assert.That(CoreRegistry.CoreOnly.Panels.All, Is.Empty);

    [Test]
    public void APanelIsFoundByTheIdThatOpensIt()
    {
        var panels = CoreRegistry.Build(new Module(Book())).Panels;

        Assert.Multiple(() =>
        {
            Assert.That(panels.Find("book")?.TitleKey, Is.EqualTo("Field Book"));
            Assert.That(panels.Find("nothing"), Is.Null, "an id naming nothing opens nothing");
            Assert.That(panels.Find(null), Is.Null);
        });
    }

    [Test]
    public void APanelWithNoIdIsRefused()
        => Assert.That(() => CoreRegistry.Build(new Module(new GamePanel { Id = "" })),
                       Throws.TypeOf<CoreModuleException>());

    [Test]
    public void TwoPanelsCannotShareAnId()
        => Assert.That(() => CoreRegistry.Build(new Module(Book(), Book())),
                       Throws.TypeOf<CoreModuleException>());

    /// <summary>🔴 The body is a projection, not a stored copy. A value moving is the whole of what it
    /// takes to change what the screen says — anything else would be a second copy to keep in step.</summary>
    [Test]
    public void ThePanelsBodyIsWhateverTheSurfaceProjects()
    {
        var registry = CoreRegistry.Build(new Module(Book()));
        var panel = registry.Panels.Find("book")!;
        var bag = new AttributeBag().Set("rank", "Apprentice").Set("seen", 3).Set("seenMax", 10);

        var rows = registry.DisplayFields.Project(panel.Surface, bag).Rows;

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Text, Is.EqualTo("Apprentice"));
            Assert.That(rows[1].Fill, Is.EqualTo(0.3).Within(0.0001));
        });
    }

    [Test]
    public void ABodyCarryingNothing_LeavesThePanelWithOnlyItsButtons()
    {
        var registry = CoreRegistry.Build(new Module(Book()));
        var panel = registry.Panels.Find("book")!;

        Assert.Multiple(() =>
        {
            Assert.That(registry.DisplayFields.Project(panel.Surface, new AttributeBag()).IsEmpty, Is.True);
            Assert.That(panel.Buttons, Has.Count.EqualTo(1), "a panel of only buttons is an ordinary panel");
        });
    }

    [Test]
    public void ThePanelSurvivesTheWireWithItsButtonsAndItsSize()
    {
        var declared = new GamePanel
        {
            Id = "book", TitleKey = "Field Book", Surface = Surface, Width = 240, Height = 180,
            Buttons = [new PanelButton("Note it", "note"), new PanelButton("Close up", "shut")],
        };

        var sent = PacketBuilder.GamePanels(new GamePanels([declared]));
        var back = (Mirage.Shared.Protocol.Packets.GamePanelsPacket)
            PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Panels, Has.Count.EqualTo(1));
            var panel = back.Panels[0];
            Assert.That(panel.Id, Is.EqualTo("book"));
            Assert.That(panel.Surface, Is.EqualTo(Surface));
            Assert.That((panel.Width, panel.Height), Is.EqualTo((240, 180)));
            Assert.That(panel.Buttons.Select(b => b.ActionId), Is.EqualTo(new[] { "note", "shut" }),
                "a button's id goes back untouched or its verb does nothing");
        });
    }

    /// <summary>An action that opens a screen and an action that tells the server are the same kind of
    /// thing, and one may be both.</summary>
    [Test]
    public void AnActionCanNameThePanelItOpens()
    {
        var action = new GameAction { Id = "open", LabelKey = "Open it", OpensPanel = "book" };

        Assert.That(action.OpensPanel, Is.EqualTo("book"));
    }
}

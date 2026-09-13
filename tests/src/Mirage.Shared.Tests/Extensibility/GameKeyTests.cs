using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// A game binding a key, and the three ways that goes wrong.
///
/// <para>A shortcut is the one declaration whose failure the player meets rather than the author: a key
/// that was never bound, or was bound twice, does nothing when pressed and says nothing about why. So
/// all three are refused where they are declared — at startup, naming the module — rather than becoming
/// a client-side coin toss.</para>
/// </summary>
[TestFixture]
public class GameKeyTests
{
    private sealed class Module(string name, Action<ICoreBuilder> configure) : ICoreModule
    {
        public string Name { get; } = name;
        public void Configure(ICoreBuilder builder) => configure(builder);
    }

    private static GameAction Verb(string id, string key = "") =>
        new() { Id = id, LabelKey = id, Key = key };

    private static GamePanel Screen(string id, string key = "") =>
        new() { Id = id, TitleKey = id, Key = key };

    [Test]
    public void NoKeyAtAll_IsTheOrdinaryCase()
    {
        var registry = CoreRegistry.Build(new Module("Quiet", b =>
        {
            b.AddAction(Verb("quiet.look"));
            b.AddPanel(Screen("quiet.book"));
        }));

        Assert.Multiple(() =>
        {
            Assert.That(registry.Actions.All[0].Key, Is.Empty);
            Assert.That(registry.Panels.All[0].Key, Is.Empty);
        });
    }

    [Test]
    public void AKeyOffTheList_IsRefusedWithTheList()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Grabby", b => b.AddAction(Verb("grabby.run", "W")))));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ModuleName, Is.EqualTo("Grabby"));

            // The list is IN the message: an author told only that their key is wrong has to go looking
            // for a set they cannot see from where they are standing.
            Assert.That(ex.Message, Does.Contain(GameKey.Listed));
        });
    }

    [Test]
    public void APanelOnAKeyOffTheList_IsRefusedToo()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Grabby", b => b.AddPanel(Screen("grabby.book", "F")))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Grabby"));
    }

    [Test]
    public void TwoActionsOnOneKey_AreRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Double", b =>
            {
                b.AddAction(Verb("double.one", "Q"));
                b.AddAction(Verb("double.two", "Q"));
            })));

        Assert.That(ex!.Message, Does.Contain("double.one"), "the refusal names what already holds it");
    }

    [Test]
    public void APanelAndAnActionOnOneKey_AreRefused()
    {
        // The two live in different lists and share one keyboard. Checked across both, or the client
        // binds whichever it walks into first and which one that is depends on declaration order.
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Double", b =>
            {
                b.AddPanel(Screen("double.book", "B"));
                b.AddAction(Verb("double.note", "B"));
            })));

        Assert.That(ex!.Message, Does.Contain("double.book"));
    }

    [Test]
    public void TwoModulesOnOneKey_AreRefusedWithTheSecondNamed()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.AddAction(Verb("first.act", "R"))),
            new Module("Second", b => b.AddAction(Verb("second.act", "R")))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"));
    }

    [Test]
    public void ABoundKey_ReachesTheClient()
    {
        // The half a declaration test does not cover: the key has to travel. A registry that holds it
        // and a packet that drops it is a shortcut that works in every test and on nobody's machine.
        var registry = CoreRegistry.Build(new Module("Wired", b =>
        {
            b.AddAction(Verb("wired.note", "Q"));
            b.AddPanel(Screen("wired.book", "B"));
        }));

        var actions = PacketBuilder.GameActions(registry.Actions);
        var panels = PacketBuilder.GamePanels(registry.Panels);

        Assert.Multiple(() =>
        {
            Assert.That(actions.Actions[0].Key, Is.EqualTo("Q"));
            Assert.That(panels.Panels[0].Key, Is.EqualTo("B"));
        });
    }
}

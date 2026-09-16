using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The action bar as a DECLARATION: whether a game has one, how wide, and what may go in a slot.
///
/// <para>🔴 <b>Opt in, and the engine has no opinion about what goes on it.</b> Declare none and there is
/// no bar — a world of conversations and walking wants none, and a row of empty boxes would be chrome
/// nobody asked for. What a slot holds is a declaration the game already made, so the bar adds no
/// vocabulary of its own.</para>
/// </summary>
[TestFixture]
public class HotkeyBarDeclarationTests
{
    private sealed class Module(int slots, params GameAction[] actions) : ICoreModule
    {
        public string Name => "Brawler";

        public void Configure(ICoreBuilder builder)
        {
            builder.SetHotkeyBar(slots);
            foreach (var action in actions) builder.AddAction(action);
        }
    }

    private sealed class Families(params RecordFamily[] families) : ICoreModule
    {
        public string Name => "Brawler";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var family in families) builder.AddFamily(family);
        }
    }

    private static GameAction Verb(string id = "brawl.swing", bool hotkeyable = false) => new()
    {
        Id = id,
        LabelKey = "Swing",
        Surface = ActionSurface.Tile,
        Hotkeyable = hotkeyable,
    };

    [Test]
    public void AnEngineWithNoGameLoaded_HasNoBar()
        => Assert.That(CoreRegistry.CoreOnly.HotkeyBarSlots, Is.EqualTo(HotkeyBar.None));

    [Test]
    public void AGameSaysHowManySlotsThePlayerGets()
        => Assert.That(CoreRegistry.Build(new Module(6)).HotkeyBarSlots, Is.EqualTo(6));

    /// <summary>Refused by name rather than clamped: the client draws the bar as one row in the sidebar,
    /// and a game learns at load rather than from a screenshot.</summary>
    [Test]
    public void AskingForMoreSlotsThanTheRowHolds_IsRefused()
    {
        var refused = Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Module(HotkeyBar.Max + 1)));

        Assert.That(refused!.Message, Does.Contain(HotkeyBar.Max.ToString()));
    }

    [Test]
    public void AskingForANegativeBarIsRefused()
        => Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(new Module(-1)));

    /// <summary>Two games in one world, each sizing the bar, would leave the player with whichever loaded
    /// last and no way to tell which.</summary>
    [Test]
    public void TwoModulesSizingTheBarIsRefused()
    {
        var refused = Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Module(4), new Other(6)));

        Assert.That(refused!.Message, Does.Contain("one bar"));
    }

    private sealed class Other(int slots) : ICoreModule
    {
        public string Name => "Second";

        public void Configure(ICoreBuilder builder) => builder.SetHotkeyBar(slots);
    }

    // ── What may go in a slot ────────────────────────────────────────────────

    /// <summary>Off by default. A menu of a game's whole vocabulary offering "assign to hotkey" on every
    /// line is a menu nobody reads.</summary>
    [Test]
    public void AVerbIsNotBindableUnlessItSaysSo()
    {
        var actions = CoreRegistry.Build(new Module(4, Verb(), Verb("brawl.shout", hotkeyable: true))).Actions;

        Assert.Multiple(() =>
        {
            Assert.That(actions.All.Single(a => a.Id == "brawl.swing").Hotkeyable, Is.False);
            Assert.That(actions.All.Single(a => a.Id == "brawl.shout").Hotkeyable, Is.True);
        });
    }

    /// <summary>🔴 Two halves, and the missing one is silent. A family a player may bind, with no verb
    /// saying what firing one DOES, gives a slot that draws an icon and answers nothing — which looks
    /// exactly like a feature somebody has not finished.</summary>
    [Test]
    public void AHotkeyableFamilyNamingNoVerbIsRefused()
    {
        var refused = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(new Families(new RecordFamily
        {
            Id = "Spell",
            LabelKey = "Spells",
            Hotkeyable = true,
        })));

        Assert.That(refused!.Message, Does.Contain("HotkeyAction"));
    }

    [Test]
    public void AHotkeyableFamilyNamingItsVerbIsAccepted()
    {
        var schema = CoreRegistry.Build(new Families(new RecordFamily
        {
            Id = "Spell",
            LabelKey = "Spells",
            Hotkeyable = true,
            HotkeyAction = "brawl.cast",
        })).Schema;

        Assert.That(schema.Family("Spell")?.HotkeyAction, Is.EqualTo("brawl.cast"));
    }

    /// <summary>Core's own item family is the exception: a blank action there means the engine uses it
    /// the way the bag does, which only the engine's own families can mean.</summary>
    [Test]
    public void CoresOwnItemsAreBindableWithNoVerbNamed()
    {
        var items = CoreRegistry.CoreOnly.Schema.Family(CoreRecordFamilies.Items);

        Assert.Multiple(() =>
        {
            Assert.That(items?.Hotkeyable, Is.True);
            Assert.That(items?.HotkeyAction, Is.Empty);
        });
    }

    [Test]
    public void AFamilyNobodyCalledBindableStaysUnbindable()
        => Assert.That(CoreRegistry.CoreOnly.Schema.Family(CoreRecordFamilies.Maps)?.Hotkeyable, Is.False);
}

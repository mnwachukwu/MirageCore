using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>The character-create class list — <see cref="PacketBuilder.NewCharClasses"/>.
///
/// <para>What this packet must get right is that the screen shows exactly what creation will grant. It
/// carries the RESOLVED loadout (worn already separated from carried) plus the handful of item and spell
/// definitions those entries name, because a player here has not joined and holds no tables to resolve
/// numbers against.</para></summary>
[TestFixture]
public class NewCharClassesPacketTests
{
    // 1 gold, 2 the casting reagent (its real index — the SubHp tooltip quotes it by name),
    // 3 a light sword, 4 a heavy sword out of reach at STR 5, 5 a potion.
    private static ItemRecord[] Items() =>
    [
        new(),
        new() { Name = "Gold", Type = ItemType.Currency },
        new() { Name = "Magical Reagent", Type = ItemType.Currency },
        new() { Name = "Light Sword", Type = ItemType.Weapon, Power = 6, Durability = 40 },
        new() { Name = "Heavy Sword", Type = ItemType.Weapon, Power = 10, Durability = 60 },
        new() { Name = "Elixir", Type = ItemType.PotionAddHp, VitalAmount = 20 },
    ];

    private static SpellRecord[] Spells() =>
    [
        new(),
        new() { Name = "Spark", Type = SpellType.SubHp, VitalAmount = 6 },
        new() { Name = "Mend", Type = SpellType.AddHp, VitalAmount = 6 },
    ];

    /// <summary>1-based, index 0 unused — the shape the world's own tables have.</summary>
    private static ClassRecord[] Classes() =>
    [
        new(),
        new()
        {
            Name = "Fighter",
            Description = "Hits things.",
            Str = 15,
            Def = 15,
            StartingItems = [new() { ItemNum = 1, Quantity = 200 }, new() { ItemNum = 3 }, new() { ItemNum = 5 }],
        },
        new()
        {
            Name = "Caster",
            Str = 5,
            Def = 5,
            Int = 6,
            StartingItems = [new() { ItemNum = 4 }],   // fails the STR gate — must not reach the screen
            StartingSpells = [1],
        },
    ];

    [Test]
    public void SplitsWornFromCarried()
    {
        var p = PacketBuilder.NewCharClasses(Classes(), Items(), Spells());

        var fighter = p.Classes[0];
        Assert.Multiple(() =>
        {
            Assert.That(fighter.Worn, Is.EqualTo(new[] { 3 }));
            Assert.That(fighter.Carried!.Select(c => c.Num), Is.EqualTo(new[] { 1, 5 }));
            Assert.That(fighter.Carried![0].Quantity, Is.EqualTo(200), "currency keeps its stack");
            Assert.That(fighter.Description, Is.EqualTo("Hits things."));
        });
    }

    [Test]
    public void NeverOffersWhatCreationWouldSkip()
    {
        // The caster's authored weapon fails the STR gate, so creation drops it. Showing it here would
        // promise a sword the player never receives.
        var p = PacketBuilder.NewCharClasses(Classes(), Items(), Spells());

        var caster = p.Classes[1];
        Assert.Multiple(() =>
        {
            Assert.That(caster.Worn, Is.Null);
            Assert.That(caster.Carried, Is.Null);
            Assert.That(caster.Spells, Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void CatalogCoversEveryReferencedEntryExactlyOnce()
    {
        var p = PacketBuilder.NewCharClasses(Classes(), Items(), Spells());

        int[] referenced = [.. p.Classes
            .SelectMany(c => (c.Worn ?? []).Concat((c.Carried ?? []).Select(x => x.Num)))
            .Distinct()];

        Assert.Multiple(() =>
        {
            Assert.That(p.ItemDefs.Select(d => d.Num), Is.SupersetOf(referenced));
            Assert.That(p.ItemDefs.Select(d => d.Num), Is.Unique);
            Assert.That(p.SpellDefs.Select(d => d.Num), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void IncludesTheCastingReagentWhenAStartingSpellDrainsHp()
    {
        // Not granted to anyone — sent so the SubHp tooltip can name the reagent it consumes per cast
        // instead of rendering "?".
        var p = PacketBuilder.NewCharClasses(Classes(), Items(), Spells());

        Assert.That(p.ItemDefs.Select(d => d.Num), Does.Contain(Constants.CastingReagentItemIndex));
    }

    [Test]
    public void OmitsTheReagentWhenNobodyOpensWithAnHpDrain()
    {
        var classes = Classes();
        classes[2].StartingSpells = [2];   // a heal, which pays no reagent

        var p = PacketBuilder.NewCharClasses(classes, Items(), Spells());

        Assert.That(p.ItemDefs.Select(d => d.Num), Does.Not.Contain(Constants.CastingReagentItemIndex));
    }

    [Test]
    public void KeepsBlankClassSlotsSoPositionsStillMapToClassNumbers()
    {
        // The client picks by list position. Filtering a nameless slot out here would renumber every
        // class after it, and the character created would not be the one clicked.
        var classes = Classes();
        classes[1] = new ClassRecord();   // blank out class 1

        var p = PacketBuilder.NewCharClasses(classes, Items(), Spells());

        Assert.Multiple(() =>
        {
            Assert.That(p.Classes, Has.Length.EqualTo(2));
            Assert.That(p.Classes[0].Name, Is.Empty);
            Assert.That(p.Classes[1].Name, Is.EqualTo("Caster"));
        });
    }
}

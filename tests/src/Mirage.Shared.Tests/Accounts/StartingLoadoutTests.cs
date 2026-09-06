using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>The per-class starting loadout — <see cref="ClassRecord.StartingItems"/> and
/// <see cref="ClassRecord.StartingSpells"/>, their canonicalization, and the resolver that decides what
/// a new character of a class actually receives.
///
/// <para>The rule under test throughout is that NOBODY STARTS WITH SOMETHING THEY CANNOT USE. The
/// resolver is shared by the grant path and the character-create preview precisely so those two cannot
/// answer it differently — a class whose authored gear silently vanishes at creation, or a screen that
/// promises a sword the server then withholds, are the same bug seen from two ends.</para></summary>
[TestFixture]
public class StartingLoadoutTests
{
    [Test]
    public void Normalize_DropsLinesNamingNoItem()
    {
        var cls = new ClassRecord
        {
            Name = "Warrior",
            StartingItems = [new ClassStartingItem { ItemNum = 4 }, new ClassStartingItem { ItemNum = 0 }],
        };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(1));
            Assert.That(cls.StartingItems[0].ItemNum, Is.EqualTo(4));
        });
    }

    [Test]
    public void Normalize_DedupesSpells()
    {
        // The spellbook is a set. A duplicate would burn one of MaxPlayerSpells slots for nothing, since
        // every other learn path refuses a spell already known.
        var cls = new ClassRecord { Name = "Sage", StartingSpells = [3, 13, 3, 13, 7] };

        cls.Normalize();

        Assert.That(cls.StartingSpells, Is.EqualTo(new[] { 3, 13, 7 }));
    }

    [Test]
    public void Normalize_CollapsesEmptyToNull()
    {
        // A class that grants nothing should carry no key at all — the Knight's whole book.
        var cls = new ClassRecord { Name = "Knight", StartingItems = [], StartingSpells = [] };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems, Is.Null);
            Assert.That(cls.StartingSpells, Is.Null);
        });
    }

    [Test]
    public void Normalize_CapsToWhatACharacterCanHold()
    {
        var cls = new ClassRecord { Name = "Hoarder", StartingItems = [], StartingSpells = [] };
        for (int i = 1; i <= Constants.MaxInv + 10; i++) cls.StartingItems.Add(new ClassStartingItem { ItemNum = i });
        for (int i = 1; i <= Constants.MaxPlayerSpells + 10; i++) cls.StartingSpells.Add(i);

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(Constants.MaxInv));
            Assert.That(cls.StartingSpells!, Has.Count.EqualTo(Constants.MaxPlayerSpells));
        });
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        var cls = new ClassRecord { Name = "Sage", StartingItems = [new ClassStartingItem { ItemNum = 4 }], StartingSpells = [3, 3] };

        cls.Normalize();
        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(1));
            Assert.That(cls.StartingSpells!, Has.Count.EqualTo(1));
        });
    }

    // ── Per-sex class art ────────────────────────────────────────────────────

    [Test]
    public void SpriteFor_PicksThePerSexArt()
    {
        var cls = new ClassRecord { Name = "Warrior", SpriteMale = 3, SpriteFemale = 13 };

        Assert.Multiple(() =>
        {
            Assert.That(cls.SpriteFor(Sex.Male), Is.EqualTo(3));
            Assert.That(cls.SpriteFor(Sex.Female), Is.EqualTo(13));
        });
    }

    [Test]
    public void Normalize_MigratesALegacySingleSpriteToBothSexes()
    {
        // A world authored before the split looked exactly one way; both sexes keep looking that way
        // rather than falling to sprite 0, which draws nothing at all.
        var cls = new ClassRecord { Name = "Warrior", Sprite = 4 };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.SpriteMale, Is.EqualTo(4));
            Assert.That(cls.SpriteFemale, Is.EqualTo(4));
            Assert.That(cls.Sprite, Is.Zero, "and the legacy field stops being written");
        });
    }

    [Test]
    public void Normalize_NeverLetsALegacySpriteOverwriteAuthoredArt()
    {
        var cls = new ClassRecord { Name = "Warrior", Sprite = 4, SpriteMale = 1, SpriteFemale = 11 };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.SpriteMale, Is.EqualTo(1));
            Assert.That(cls.SpriteFemale, Is.EqualTo(11));
        });
    }

    // ── The gate the grant path applies ──────────────────────────────────────
    // A brand-new character has EXACTLY its class's base stats, so "can this class start with it?" is
    // GearStatRequirement(power, classStat) <= classStat. These pin the cases the ten-class roster
    // actually lands on, because each one reads as a design statement rather than an accident.

    [TestCase(15, 10, true, TestName = "Warrior STR 15 lifts a heavy tier-1 weapon (Power 10)")]
    [TestCase(5, 10, false, TestName = "Wanderer STR 5 cannot lift a heavy tier-1 weapon")]
    [TestCase(5, 6, true, TestName = "Wanderer STR 5 lifts a LIGHT tier-1 weapon (Power 6)")]
    [TestCase(0, 6, false, TestName = "A 0-STR class lifts nothing at all")]
    public void GearGate_AtStartingStats(int classStat, int power, bool expected)
    {
        bool canUse = CombatFormulas.GearStatRequirement(power, classStat) <= classStat;

        Assert.That(canUse, Is.EqualTo(expected));
    }

    [Test]
    public void GearGate_ACapIsACeilingNotAFloor()
    {
        // The Warrior's chart says MEDIUM armor, but DEF 5 cannot meet a Power-8 medium piece at level 1
        // (requirement 7). It starts in light instead. That is the cap working as designed — a class
        // falls back rather than being locked out — and it is why the generator picks the best PASSING
        // piece rather than the piece its chart names.
        const int warriorDef = 5, mediumPower = 8, lightPower = 6;

        Assert.Multiple(() =>
        {
            Assert.That(CombatFormulas.GearStatRequirement(mediumPower, warriorDef), Is.GreaterThan(warriorDef));
            Assert.That(CombatFormulas.GearStatRequirement(lightPower, warriorDef), Is.LessThanOrEqualTo(warriorDef));
        });
    }

    [TestCase(15, 10, true, TestName = "Sorcerer INT 15 casts a high tier-1 spell (VitalAmount 10)")]
    [TestCase(5, 10, false, TestName = "Wanderer INT 5 cannot cast a high tier-1 spell")]
    [TestCase(5, 6, true, TestName = "Wanderer INT 5 casts a LOW tier-1 spell (VitalAmount 6)")]
    [TestCase(0, 6, false, TestName = "A 0-INT class starts with no spell at all")]
    public void SpellGate_AtStartingStats(int classInt, int vitalAmount, bool expected)
    {
        var spell = new SpellRecord { Name = "S", Type = SpellType.SubHp, VitalAmount = (short)vitalAmount };

        bool canLearn = CombatFormulas.GetSpellIntRequirement(spell, classInt) <= classInt;

        Assert.That(canLearn, Is.EqualTo(expected));
    }

    // ── The resolver both the grant and the preview go through ───────────────

    // Item table: 1 gold, 2 a light sword any class can lift, 3 a heavy one only a strong class can,
    // 4 a potion, 5 a sword gated to class 2 alone, 6 a sword nobody reaches until level 5.
    private static ItemRecord[] Items() =>
    [
        new(),
        new() { Name = "Gold", Type = ItemType.Currency },
        new() { Name = "Light Sword", Type = ItemType.Weapon, Power = 6, Durability = 40 },
        new() { Name = "Heavy Sword", Type = ItemType.Weapon, Power = 10, Durability = 60 },
        new() { Name = "Elixir", Type = ItemType.PotionAddHp, VitalAmount = 20 },
        new() { Name = "Guild Blade", Type = ItemType.Weapon, Power = 6, AllowedClasses = [2] },
        new() { Name = "Veteran Blade", Type = ItemType.Weapon, Power = 6, LevelReq = 5 },
    ];

    private static SpellRecord[] Spells() =>
    [
        new(),
        new() { Name = "Spark", Type = SpellType.SubHp, VitalAmount = 6 },
        new() { Name = "Firestorm", Type = SpellType.SubHp, VitalAmount = 60 },
        new() { Name = "Guild Ward", Type = SpellType.AddHp, VitalAmount = 6, AllowedClasses = [2] },
    ];

    private static ClassRecord ClassWith(int str, int @int, params int[] itemNums) => new()
    {
        Name = "T",
        Str = str,
        Int = @int,
        Def = str,   // irrelevant to the weapon gate; kept equal so nothing else silently fails
        StartingItems = [.. itemNums.Select(n => new ClassStartingItem { ItemNum = (short)n, Quantity = 200 })],
    };

    [Test]
    public void Resolve_SkipsWhatTheClassCannotUseAndLeavesNoGapInTheBag()
    {
        // Heavy sword (2nd line) fails the STR gate. The potion behind it must still land in slot 2 —
        // a hole would be a bag that looks half-empty for no reason the player can see.
        var granted = StartingLoadout.ResolveItems(ClassWith(str: 5, @int: 0, 2, 3, 4), classNum: 1, Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted.Select(g => g.Num), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(granted.Select(g => g.Slot), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void Resolve_EquipmentIsWornAndCarriesFullDurability()
    {
        var granted = StartingLoadout.ResolveItems(ClassWith(str: 15, @int: 0, 2, 4), classNum: 1, Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted[0].Worn, Is.True, "a weapon that passes its gates arrives equipped");
            Assert.That(granted[0].Durability, Is.EqualTo(40), "and pristine");
            Assert.That(granted[1].Worn, Is.False, "a potion is carried");
        });
    }

    [Test]
    public void Resolve_CurrencyKeepsItsStackAndEverythingElseIsExactlyOne()
    {
        var granted = StartingLoadout.ResolveItems(ClassWith(str: 15, @int: 0, 1, 4), classNum: 1, Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted[0].Value, Is.EqualTo(200));
            Assert.That(granted[1].Value, Is.EqualTo(0), "the engine reads Value only for currency");
        });
    }

    [Test]
    public void Resolve_HonorsTheClassAndLevelGatesToo()
    {
        // Item 5 is another class's blade; item 6 needs a level this character does not have yet.
        var granted = StartingLoadout.ResolveItems(ClassWith(str: 15, @int: 0, 5, 6, 2), classNum: 1, Items());

        Assert.That(granted.Select(g => g.Num), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void ResolveSpells_SkipsWhatTheClassCannotCast()
    {
        var cls = new ClassRecord { Name = "T", Int = 6, StartingSpells = [1, 2, 3] };

        // Firestorm is out of INT reach; the Guild Ward belongs to class 2.
        Assert.That(StartingLoadout.ResolveSpells(cls, classNum: 1, Spells()), Is.EqualTo(new[] { 1 }));
    }

    // A caster arrives with its attack spell already prepared, so its first fight needs no trip through
    // the spell panel. The prepared slot is the caster's WEAPON and only ever holds SubHp, which is what
    // decides both of these — the slot is an index into the book that was actually granted.
    [Test]
    public void ResolvePreparedSlot_PicksTheFirstSubHpSpellInTheBook()
    {
        // Guild Ward (AddHp) sits ahead of Spark (SubHp), so the slot must be 2 and not 1.
        Assert.That(StartingLoadout.ResolvePreparedSlot([3, 1], Spells()), Is.EqualTo(2));
        Assert.That(StartingLoadout.ResolvePreparedSlot([1, 2], Spells()), Is.EqualTo(1));
    }

    [Test]
    public void ResolvePreparedSlot_LeavesAMeleeBookUnprepared()
    {
        // Nothing preparable, which is every melee class: 0 is the same value clearing the slot gives.
        Assert.That(StartingLoadout.ResolvePreparedSlot([3], Spells()), Is.EqualTo(0));
        Assert.That(StartingLoadout.ResolvePreparedSlot([], Spells()), Is.EqualTo(0));
    }
}

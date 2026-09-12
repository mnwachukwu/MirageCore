using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// Wearing something, without the engine knowing what it is.
///
/// <para>Core's whole model of equipment is four rules: a game declares the slots, one item goes in each,
/// the item stays in the bag, and a key nobody declared cannot be worn. Everything a game means by a slot
/// — what it protects, what it looks like, whether a two-handed thing takes two — is above this.</para>
/// </summary>
[TestFixture]
public class EquipSlotTests
{
    private static EquipSlotSet Set() => new(
    [
        new EquipSlot { Key = "offhand", Ordinal = 3 },
        new EquipSlot { Key = "head", Ordinal = 0 },
        new EquipSlot { Key = "body", Ordinal = 1 },
    ]);

    // Display order is the game's, not the order a module happened to call AddEquipSlot in.
    [Test]
    public void Slots_AreOrderedByOrdinal()
        => Assert.That(Set().Slots.Select(s => s.Key), Is.EqualTo(new[] { "head", "body", "offhand" }));

    [Test]
    public void Has_AnswersForDeclaredAndUndeclaredKeys()
    {
        var slots = Set();

        Assert.Multiple(() =>
        {
            Assert.That(slots.Has("head"), Is.True);
            Assert.That(slots.Has("saddle"), Is.False, "a key from another game is simply not here");
            Assert.That(slots.Has(null), Is.False);
            Assert.That(slots.Find("body")?.Ordinal, Is.EqualTo(1));
        });
    }

    /// <summary>An engine with no module loaded declares no slots, so nothing in it can be worn. The same
    /// answer <c>DecalSystem.Deposit</c> gives: the machinery is present and nothing drives it.</summary>
    [Test]
    public void CoreAlone_DeclaresNoSlots()
        => Assert.That(CoreRegistry.CoreOnly.EquipSlots.Count, Is.Zero);

    // ── What a module may declare ─────────────────────────────────────────────

    private sealed class SlotModule(string name, params EquipSlot[] slots) : ICoreModule
    {
        public string Name => name;

        public void Configure(ICoreBuilder builder)
        {
            foreach (var slot in slots) builder.AddEquipSlot(slot);
        }
    }

    [Test]
    public void AModule_DeclaresTheSlotsItsGameHas()
    {
        var registry = CoreRegistry.Build([new SlotModule("Pocket",
            new EquipSlot { Key = "held", LabelKey = "Pocket_Slot_Held" })]);

        Assert.That(registry.EquipSlots.Find("held")?.LabelKey, Is.EqualTo("Pocket_Slot_Held"));
    }

    // Two modules claiming one slot key is the same class of collision as two claiming a family id: the
    // second one is named and the server stops, rather than one of them silently winning.
    [Test]
    public void TwoModulesClaimingOneKey_StopsTheServerAndNamesTheSecond()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
        [
            new SlotModule("First", new EquipSlot { Key = "held" }),
            new SlotModule("Second", new EquipSlot { Key = "held" }),
        ]));

        Assert.That(ex!.Message, Does.Contain("Second").And.Contain("held"));
    }

    [Test]
    public void ASlotWithNoKey_IsRefused()
        => Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            [new SlotModule("Blank", new EquipSlot { Key = "  " })]));

    // ── What a character wears ────────────────────────────────────────────────

    [Test]
    public void Wearing_RecordsWhereTheBagSlotIsWorn()
    {
        var p = new PlayerRecord();
        p.SetEquipped("head", 4);

        Assert.Multiple(() =>
        {
            Assert.That(p.EquippedIn("head"), Is.EqualTo(4));
            Assert.That(p.EquippedIn("body"), Is.Zero, "an absent key is an empty slot");
            Assert.That(p.IsEquipped(4), Is.True);
            Assert.That(p.IsEquipped(5), Is.False);
            Assert.That(p.IsEquipped(0), Is.False, "0 is the not-worn sentinel, not a bag slot");
        });
    }

    [Test]
    public void SettingASlotToZero_TakesItOff()
    {
        var p = new PlayerRecord();
        p.SetEquipped("head", 4);

        p.SetEquipped("head", 0);

        Assert.That(p.Equipped, Is.Empty, "an empty slot carries no entry at all");
    }

    /// <summary>Every path that empties a bag slot — dropping, selling, banking, mailing — calls this, so
    /// a worn slot cannot go on pointing at an item that has left.</summary>
    [Test]
    public void ClearEquipped_TakesTheItemOffWhereverItIsWorn()
    {
        var p = new PlayerRecord();
        p.SetEquipped("head", 4);
        p.SetEquipped("body", 7);

        p.ClearEquipped(4);

        Assert.Multiple(() =>
        {
            Assert.That(p.EquippedIn("head"), Is.Zero);
            Assert.That(p.EquippedIn("body"), Is.EqualTo(7), "and leaves the others alone");
        });
    }

    // A clone that shared the map would have one character's changes of clothes show up on another.
    [Test]
    public void Clone_TakesItsOwnCopyOfWhatIsWorn()
    {
        var p = new PlayerRecord();
        p.SetEquipped("head", 4);

        var copy = p.Clone();
        copy.SetEquipped("head", 9);

        Assert.That(p.EquippedIn("head"), Is.EqualTo(4));
    }

    // ── The item's half of it ─────────────────────────────────────────────────

    [Test]
    public void Normalize_ClearsTheSlotOnAnythingNotWorn()
    {
        var item = new ItemRecord { Type = ItemType.Consumable, EquipSlot = "head" };

        item.Normalize();

        Assert.That(item.EquipSlot, Is.Empty);
    }

    [Test]
    public void Normalize_KeepsTheSlotOnEquipment()
    {
        var item = new ItemRecord { Type = ItemType.Equipment, EquipSlot = "head" };

        item.Normalize();

        Assert.That(item.EquipSlot, Is.EqualTo("head"));
    }
}

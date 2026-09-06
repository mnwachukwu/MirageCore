using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>
/// The one gate every class restriction routes through — items, spells and quests alike. Pins the two
/// things the rest of the engine assumes: that an absent gate means EVERYONE (the common case, and the
/// one that would silently lock the world if it were ever inverted), and that a saved gate is
/// canonical, so two rows allowing the same classes are byte-identical on disk.
/// </summary>
[TestFixture]
public class ClassGateTests
{
    [Test]
    public void NoGate_AllowsEveryClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClassGate.Allows(null, 1), Is.True, "absent = unrestricted");
            Assert.That(ClassGate.Allows(new List<short>(), 1), Is.True, "empty = unrestricted");
            Assert.That(ClassGate.Allows(null, 0), Is.True, "even a player with no class set");
            Assert.That(ClassGate.IsRestricted(null), Is.False);
            Assert.That(ClassGate.IsRestricted(new List<short>()), Is.False);
        });
    }

    [Test]
    public void AGate_AllowsExactlyItsMembers()
    {
        var gate = new List<short> { 2, 5, 9 };
        Assert.Multiple(() =>
        {
            Assert.That(ClassGate.Allows(gate, 2), Is.True);
            Assert.That(ClassGate.Allows(gate, 5), Is.True);
            Assert.That(ClassGate.Allows(gate, 9), Is.True);
            Assert.That(ClassGate.Allows(gate, 1), Is.False);
            Assert.That(ClassGate.Allows(gate, 3), Is.False);
            Assert.That(ClassGate.IsRestricted(gate), Is.True);
        });
    }

    // The whole point of the multi-class change: one item shared by a group of classes, which the old
    // single-id field could not express at all.
    [Test]
    public void AGate_CanNameAWholeGroup()
    {
        var strengthClasses = new List<short> { 1, 2, 3 };
        Assert.Multiple(() =>
        {
            foreach (short c in strengthClasses)
                Assert.That(ClassGate.Allows(strengthClasses, c), Is.True, $"class {c} is in the group");
            Assert.That(ClassGate.Allows(strengthClasses, 4), Is.False, "and nobody else is");
        });
    }

    [Test]
    public void Normalize_SortsDedupesAndDropsInvalid()
    {
        var messy = new List<short> { 9, 2, 9, 0, 2, (short)(Constants.MaxClasses + 1), -3, 5 };

        var clean = ClassGate.Normalize(messy);

        Assert.That(clean, Is.EqualTo(new short[] { 2, 5, 9 }),
            "sorted, deduped, and with 0 / negative / past-MaxClasses ids dropped");
    }

    [Test]
    public void Normalize_CollapsesEmptyToNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClassGate.Normalize(null), Is.Null);
            Assert.That(ClassGate.Normalize(new List<short>()), Is.Null);
            // Every id invalid is the same as no gate — and must not persist as "[]", or an unrestricted
            // row would have two spellings.
            Assert.That(ClassGate.Normalize(new List<short> { 0, -1 }), Is.Null);
        });
    }

    [Test]
    public void Describe_JoinsTheAllowedClassNames()
    {
        var classes = new ClassRecord?[]
        {
            null,
            new() { Name = "Warrior" },
            new() { Name = "Soldier  " },   // names are stored padded; the readout trims
            new() { Name = "Knight" },
        };

        Assert.Multiple(() =>
        {
            Assert.That(ClassGate.Describe(new List<short> { 1, 2, 3 }, classes), Is.EqualTo("Warrior, Soldier, Knight"));
            Assert.That(ClassGate.Describe(new List<short> { 2 }, classes), Is.EqualTo("Soldier"));
            Assert.That(ClassGate.Describe(null, classes), Is.Empty, "nothing to say when unrestricted");
            // An id past the end of the table is skipped rather than rendered as "?", so a stale gate
            // degrades to naming the classes that do exist.
            Assert.That(ClassGate.Describe(new List<short> { 1, 99 }, classes), Is.EqualTo("Warrior"));
        });
    }

    // The records delegate to the gate, so a save canonicalizes without each record re-implementing it.
    [Test]
    public void RecordNormalize_CanonicalizesTheGate()
    {
        var item = new ItemRecord { Type = ItemType.Weapon, AllowedClasses = [3, 1, 3] };
        item.Normalize();

        var spell = new SpellRecord { Type = SpellType.SubHp, AllowedClasses = [] };
        spell.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(item.AllowedClasses, Is.EqualTo(new short[] { 1, 3 }));
            Assert.That(spell.AllowedClasses, Is.Null, "an empty gate is stored as absent");
        });
    }

    // A gate only applies where the type uses one: a potion is not equipment, so it carries no class
    // restriction however it was authored.
    [Test]
    public void ItemNormalize_DropsTheGateOnNonEquipment()
    {
        var potion = new ItemRecord { Type = ItemType.PotionAddHp, AllowedClasses = [1, 2] };
        potion.Normalize();
        Assert.That(potion.AllowedClasses, Is.Null);
    }
}

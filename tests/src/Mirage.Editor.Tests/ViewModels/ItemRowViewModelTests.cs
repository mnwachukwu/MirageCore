using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The item-editor row view-model: faithful record round-trip, the dirty-flag lifecycle, the
/// load-clobbers-save guard (applying a server packet must NOT flip a clean row to "edited"), the
/// type-driven field visibility, and the save-time normalization that keeps a retyped item from carrying
/// its previous type's numbers.</summary>
[TestFixture]
public class ItemRowViewModelTests
{
    static ItemRecord Sword() => new()
    {
        Name = "Rusty Sword", Pic = 12, Type = ItemType.Weapon, Durability = 100, Power = 8, AllowedClasses = [2, 3],
    };

    static ItemRowViewModel Row(ItemType type) => new(1, new ItemRecord { Type = type });

    [Test]
    public void Ctor_FromRecord_RoundTrips_AndIsClean()
    {
        var vm = new ItemRowViewModel(3, Sword());
        var r = vm.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Rusty Sword"));
            Assert.That(r.Pic, Is.EqualTo((short)12));
            Assert.That(r.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(r.Durability, Is.EqualTo((short)100));
            Assert.That(r.Power, Is.EqualTo((short)8));
            Assert.That(r.AllowedClasses, Is.EqualTo(new short[] { 2, 3 }));
            Assert.That(vm.IsDirty, Is.False, "a freshly loaded row is not dirty");
            Assert.That(vm.IsLoaded, Is.True);
        });
    }

    [Test]
    public void EditingAField_MarksDirty()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Durability = 55;
        Assert.That(vm.IsDirty, Is.True);
    }

    [Test]
    public void ClearDirty_Resets()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Name = "Edited";
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    // Loading from a record must not read as an edit (the _loading guard), and it clears any prior dirt.
    [Test]
    public void LoadFromRecord_UpdatesFields_WithoutMarkingDirty()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Name = "Edited";   // now dirty

        vm.LoadFromRecord(new ItemRecord { Name = "Shield", Type = ItemType.Shield, Durability = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a load is not an edit");
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.Shield));
            Assert.That(vm.ToRecord().Durability, Is.EqualTo((short)50));
        });
    }

    // The reported failure shape (cf. NpcSizeRoundTrip): an online packet load must NOT dirty a clean row,
    // else an open-then-save would silently rewrite the item.
    [Test]
    public void ApplyPacket_SeedsFields_MarksLoaded_ButNotDirty()
    {
        var vm = new ItemRowViewModel(1, new ItemRecord { Name = "" }, isLoaded: false);

        vm.ApplyPacket(new UpdateItemPacket { Name = "Potion", Type = ItemType.PotionAddHp, VitalAmount = 25 });

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "loading from the wire is not an edit");
            Assert.That(vm.IsLoaded, Is.True, "the row is now loaded");
            Assert.That(vm.ToRecord().Name, Is.EqualTo("Potion"));
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.PotionAddHp));
            Assert.That(vm.ToRecord().VitalAmount, Is.EqualTo((short)25));
        });
    }

    // Visibility follows the item type: only equipment exposes durability, power and a class requirement;
    // only potions expose an amount; only a scroll picks a spell; keys and currency expose nothing.
    [Test]
    public void FieldVisibility_FollowsItemType()
    {
        Assert.Multiple(() =>
        {
            var weapon = Row(ItemType.Weapon);
            Assert.That(weapon.DurabilityVisible, Is.True);
            Assert.That(weapon.PowerVisible, Is.True);
            Assert.That(weapon.AllowedClassesVisible, Is.True);
            Assert.That(weapon.VitalAmountVisible, Is.False);
            Assert.That(weapon.SpellNumVisible, Is.False);

            var potion = Row(ItemType.PotionAddHp);
            Assert.That(potion.VitalAmountVisible, Is.True, "potion amount is editable");
            Assert.That(potion.DurabilityVisible, Is.False, "potions do not wear");
            Assert.That(potion.PowerVisible, Is.False);
            Assert.That(potion.AllowedClassesVisible, Is.False);

            var scroll = Row(ItemType.Spell);
            Assert.That(scroll.SpellNumVisible, Is.True, "a scroll picks the spell it teaches");
            Assert.That(scroll.VitalAmountVisible, Is.False);
            Assert.That(scroll.PowerVisible, Is.False);

            foreach (var bare in new[] { ItemType.Key, ItemType.Currency, ItemType.None })
            {
                var row = Row(bare);
                Assert.That(row.DurabilityVisible, Is.False, $"{bare} carries no editable fields");
                Assert.That(row.VitalAmountVisible, Is.False, $"{bare} carries no editable fields");
                Assert.That(row.SpellNumVisible, Is.False, $"{bare} carries no editable fields");
                Assert.That(row.PowerVisible, Is.False, $"{bare} carries no editable fields");
                Assert.That(row.AllowedClassesVisible, Is.False, $"{bare} carries no editable fields");
            }
        });
    }

    // The hazard the named fields alone don't fix: retype a weapon as a potion and its Power/ClassReq are
    // hidden but still set. Saving has to drop them, or the file keeps numbers the item no longer has.
    [Test]
    public void ToRecord_ZeroesFieldsTheTypeDoesNotUse()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.PotionAddHp;
        vm.VitalAmount = 25;

        var r = vm.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(r.VitalAmount, Is.EqualTo((short)25));
            Assert.That(r.Durability, Is.EqualTo((short)0), "a potion does not wear");
            Assert.That(r.Power, Is.EqualTo((short)0), "the weapon's power must not survive the retype");
            Assert.That(r.AllowedClasses, Is.Null, "nor its class gate");
        });
    }

    // The row itself keeps the values, so flipping type by accident and back does not destroy authoring
    // work — only what reaches disk is normalized.
    [Test]
    public void ToRecord_DoesNotMutateTheRow()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.PotionAddHp;

        vm.ToRecord();
        vm.Type = ItemType.Weapon;

        Assert.Multiple(() =>
        {
            Assert.That(vm.Power, Is.EqualTo((short)8));
            Assert.That(vm.ToRecord().Power, Is.EqualTo((short)8));
        });
    }

    // The online save must store exactly what the offline save would; the packet is built from the same
    // normalized record, so a retyped item cannot reach the server carrying stale fields.
    [Test]
    public void BuildSavePacket_IsNormalizedLikeToRecord()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.Spell;
        vm.SpellNum = 7;

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.ItemNum, Is.EqualTo(3));
            Assert.That(pkt.SpellNum, Is.EqualTo((short)7));
            Assert.That(pkt.Durability, Is.EqualTo((short)0));
            Assert.That(pkt.Power, Is.EqualTo((short)0));
            Assert.That(pkt.AllowedClasses, Is.Null);
        });
    }
}

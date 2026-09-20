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
        Name = "Rusty Sword", Pic = 12, Type = ItemType.Equipment, Durability = 100,
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
            Assert.That(r.Type, Is.EqualTo(ItemType.Equipment));
            Assert.That(r.Durability, Is.EqualTo((short)100));
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

        vm.LoadFromRecord(new ItemRecord { Name = "Shield", Type = ItemType.Equipment, Durability = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a load is not an edit");
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.Equipment));
            Assert.That(vm.ToRecord().Durability, Is.EqualTo((short)50));
        });
    }

    // The reported failure shape (cf. NpcSizeRoundTrip): an online packet load must NOT dirty a clean row,
    // else an open-then-save would silently rewrite the item.
    [Test]
    public void ApplyPacket_SeedsFields_MarksLoaded_ButNotDirty()
    {
        var vm = new ItemRowViewModel(1, new ItemRecord { Name = "" }, isLoaded: false);

        vm.ApplyPacket(new UpdateItemPacket { Name = "Potion", Type = ItemType.Consumable});

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "loading from the wire is not an edit");
            Assert.That(vm.IsLoaded, Is.True, "the row is now loaded");
            Assert.That(vm.ToRecord().Name, Is.EqualTo("Potion"));
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.Consumable));
        });
    }

    // Visibility follows the item type: only what is worn carries durability, and nothing else the
    // engine reads is type-specific. Everything a game keeps about an item lives on the record's own
    // attributes, which the game-fields panel authors.
    [Test]
    public void FieldVisibility_FollowsItemType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row(ItemType.Equipment).DurabilityVisible, Is.True);
            Assert.That(Row(ItemType.Consumable).DurabilityVisible, Is.False, "potions do not wear");

            foreach (var bare in new[] { ItemType.Key, ItemType.Currency, ItemType.None })
            {
                Assert.That(Row(bare).DurabilityVisible, Is.False, $"{bare} carries no editable fields");
            }
        });
    }

    // The hazard the named fields alone don't fix: retype a weapon as a potion and its durability is
    // hidden but still set. Saving has to drop it, or the file keeps numbers the item no longer has.
    [Test]
    public void ToRecord_ZeroesFieldsTheTypeDoesNotUse()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.Consumable;

        var r = vm.ToRecord();

        Assert.That(r.Durability, Is.EqualTo((short)0), "a potion does not wear");
    }

    // The row itself keeps the values, so flipping type by accident and back does not destroy authoring
    // work — only what reaches disk is normalized.
    [Test]
    public void ToRecord_DoesNotMutateTheRow()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.Consumable;

        vm.ToRecord();
        vm.Type = ItemType.Equipment;

        Assert.Multiple(() =>
        {
            Assert.That(vm.Durability, Is.EqualTo((short)100));
            Assert.That(vm.ToRecord().Durability, Is.EqualTo((short)100));
        });
    }

    // The online save must store exactly what the offline save would; the packet is built from the same
    // normalized record, so a retyped item cannot reach the server carrying stale fields.
    [Test]
    public void BuildSavePacket_IsNormalizedLikeToRecord()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Type = ItemType.Consumable;

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.ItemNum, Is.EqualTo(3));
            Assert.That(pkt.Durability, Is.EqualTo((short)0));
        });
    }
}

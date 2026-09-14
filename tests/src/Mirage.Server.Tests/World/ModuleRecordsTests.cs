using Mirage.Server.Core.World;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Where a game's own records live on the server.
///
/// <para>Core has no type for them, so the store's whole job is slots: how many there are, which numbers
/// are real, and handing back what it was given. Nothing in it reads a key.</para>
/// </summary>
[TestFixture]
public class ModuleRecordsTests
{
    private static RecordFamily Species() => new() { Id = "Species", DefaultLimit = 3 };

    private static ModuleRecords WithSpecies(int limit = 3)
    {
        var store = new ModuleRecords();
        store.Declare(Species(), limit);
        return store;
    }

    [Test]
    public void ADeclaredFamily_HasItsSlotsReadyAndBlank()
    {
        var store = WithSpecies();

        Assert.Multiple(() =>
        {
            Assert.That(store.Has("Species"), Is.True);
            Assert.That(store.Limit("Species"), Is.EqualTo(3));
            Assert.That(store.Get("Species", 1)!.IsEmpty, Is.True, "an unauthored slot is a blank record");
            Assert.That(store.All("Species"), Has.Count.EqualTo(3));
        });
    }

    /// <summary>0 rather than "unlimited": a family this world does not hold reads the same as one with no
    /// room, and a caller stops on either.</summary>
    [Test]
    public void AFamilyThisWorldDoesNotHold_HasNoRoomAndNoRecords()
    {
        var store = WithSpecies();

        Assert.Multiple(() =>
        {
            Assert.That(store.Has("Moves"), Is.False);
            Assert.That(store.Limit("Moves"), Is.Zero);
            Assert.That(store.Get("Moves", 1), Is.Null);
            Assert.That(store.All("Moves"), Is.Empty);
            Assert.That(store.Has(null), Is.False);
        });
    }

    // Slot numbers arrive off the wire, so out of range answers rather than throws.
    [Test]
    public void ASlotOutsideTheFamilysRange_IsNotThere()
    {
        var store = WithSpecies();

        Assert.Multiple(() =>
        {
            Assert.That(store.Get("Species", 0), Is.Null, "slots are 1-based");
            Assert.That(store.Get("Species", 4), Is.Null);
            Assert.That(store.Get("Species", -1), Is.Null);
        });
    }

    [Test]
    public void SettingASlot_ReplacesWhatItHeld()
    {
        var store = WithSpecies();

        store.Set("Species", 2, new AttributeBag().Set("name", "Vulpine"));

        Assert.Multiple(() =>
        {
            Assert.That(store.Get("Species", 2)!["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(store.Get("Species", 1)!.IsEmpty, Is.True, "and leaves its neighbors alone");
        });
    }

    [Test]
    public void SettingASlotThatDoesNotExist_ChangesNothing()
    {
        var store = WithSpecies();

        store.Set("Species", 9, new AttributeBag().Set("name", "Nowhere"));
        store.Set("Moves", 1, new AttributeBag().Set("name", "Nowhere"));

        Assert.That(store.All("Species").All(r => r.IsEmpty), Is.True);
    }

    // Slot n is at index n-1, which is what a list rendered against slot numbers depends on.
    [Test]
    public void All_ReturnsTheSlotsInOrderFromOne()
    {
        var store = WithSpecies();
        store.Set("Species", 3, new AttributeBag().Set("name", "Third"));

        Assert.That(store.All("Species")[2]["name"].AsText(), Is.EqualTo("Third"));
    }

    [Test]
    public void Adopt_ReplacesTheFamilysRecordsWholesale()
    {
        var store = WithSpecies();
        var loaded = new[] { new AttributeBag(), new AttributeBag().Set("name", "FromDisk"), new AttributeBag() };

        store.Adopt(Species(), loaded);

        Assert.Multiple(() =>
        {
            Assert.That(store.Limit("Species"), Is.EqualTo(2), "the array it was handed sets the range");
            Assert.That(store.Get("Species", 1)!["name"].AsText(), Is.EqualTo("FromDisk"));
        });
    }
}

using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// A row over a record of a family this build was never compiled against.
///
/// <para>Everything a compiled row gets from having a property per field — a name to list it by, a dirty
/// flag, a save projection — this one has to derive from the bag and the family's own declaration.</para>
/// </summary>
[TestFixture]
public class SchemaRecordRowTests
{
    private static RecordFamily Species(string nameFieldKey = "name") => new()
    {
        Id = "Species",
        NameFieldKey = nameFieldKey,
        Fields =
        [
            new FieldDescriptor { Key = "name", Kind = FieldKind.Text },
            new FieldDescriptor { Key = "baseSpeed", Kind = FieldKind.Integer, Min = 0, Max = 255 },
        ],
    };

    private static SchemaRecordRowViewModel Row(AttributeBag? record = null, RecordFamily? family = null)
        => new(1, family ?? Species(), record ?? new AttributeBag(), RecordSchema.Empty);

    [Test]
    public void TheListCaption_ComesFromTheFieldTheFamilyNominated()
    {
        var row = Row(new AttributeBag().Set("name", "Vulpine"));

        Assert.That(row.DisplayName, Is.EqualTo("1: Vulpine"));
    }

    /// <summary>A family whose records have no name is a real shape — a lookup table keyed by number has
    /// nothing to call a row — and it reads as its slot number rather than as a crash.</summary>
    [Test]
    public void AFamilyThatNominatesNoNameField_ListsBySlotAlone()
    {
        var row = Row(new AttributeBag().Set("name", "Vulpine"), Species(nameFieldKey: ""));

        Assert.That(row.Name, Is.Empty);
    }

    // ── Dirty ─────────────────────────────────────────────────────────────────

    [Test]
    public void AFreshRow_IsClean()
        => Assert.That(Row(new AttributeBag().Set("name", "Vulpine")).IsDirty, Is.False);

    /// <summary>The form writes straight into the bag, so dirty is a comparison against what was last
    /// saved rather than a flag any setter raises.</summary>
    [Test]
    public void EditingThroughTheForm_MarksTheRowDirty()
    {
        var row = Row();

        row.Form.Fields.Single(f => f.Key == "name").TextValue = "Vulpine";

        Assert.Multiple(() =>
        {
            Assert.That(row.IsDirty, Is.True);
            Assert.That(row.DisplayName, Is.EqualTo("1: Vulpine"), "and the list caption follows it");
        });
    }

    [Test]
    public void ClearDirty_TakesWhatTheRowHoldsAsSaved()
    {
        var row = Row();
        row.Form.Fields.Single(f => f.Key == "name").TextValue = "Vulpine";

        row.ClearDirty();

        Assert.That(row.IsDirty, Is.False);
    }

    [Test]
    public void LoadFromRecord_ReplacesTheRecordAndItsForm()
    {
        var row = Row();
        row.Form.Fields.Single(f => f.Key == "name").TextValue = "Typed but not saved";

        row.LoadFromRecord(new AttributeBag().Set("name", "FromTheServer"));

        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo("FromTheServer"));
            Assert.That(row.IsDirty, Is.False, "what arrived is what is saved");
            Assert.That(row.Form.Fields.Single(f => f.Key == "name").TextValue, Is.EqualTo("FromTheServer"));
        });
    }

    /// <summary>The row takes its own copy, so a bag the caller keeps editing cannot reach the record on
    /// screen — and the record on screen cannot reach the one the caller passed in.</summary>
    [Test]
    public void TheRowDoesNotShareTheBagItWasGiven()
    {
        var source = new AttributeBag().Set("name", "Vulpine");
        var row = Row(source);

        source.Set("name", "Changed behind its back");

        Assert.That(row.Name, Is.EqualTo("Vulpine"));
    }

    // ── Copy and save ─────────────────────────────────────────────────────────

    [Test]
    public void CopyFrom_TakesEveryKeyAndRenamesTheCopy()
    {
        var source = Row(new AttributeBag().Set("name", "Vulpine").Set("baseSpeed", 65));
        var target = new SchemaRecordRowViewModel(2, Species(), new AttributeBag(), RecordSchema.Empty);

        target.CopyFrom(source);

        Assert.Multiple(() =>
        {
            Assert.That(target.Name, Is.EqualTo("Vulpine (Copy)"));
            Assert.That(target.Record["baseSpeed"].AsLong(), Is.EqualTo(65));
            Assert.That(target.IsDirty, Is.True, "a copy is unsaved until it is saved");
            Assert.That(source.Name, Is.EqualTo("Vulpine"), "and the original is untouched");
        });
    }

    [Test]
    public void TheSavePacket_NamesTheFamilyTheSlotAndEveryKey()
    {
        var row = Row(new AttributeBag().Set("name", "Vulpine").Set("hiddenAbility", "Drizzle"));

        var pkt = row.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.Family, Is.EqualTo("Species"));
            Assert.That(pkt.Num, Is.EqualTo(1));
            Assert.That(pkt.Fields["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(pkt.Fields["hiddenAbility"].AsText(), Is.EqualTo("Drizzle"),
                        "a key no field describes still travels");
        });
    }

    /// <summary>The packet carries a copy, so an edit made while a save is in flight cannot change what
    /// was sent.</summary>
    [Test]
    public void TheSavePacket_DoesNotShareTheRowsRecord()
    {
        var row = Row(new AttributeBag().Set("name", "Vulpine"));

        var pkt = row.BuildSavePacket();
        row.Form.Fields.Single(f => f.Key == "name").TextValue = "Edited after";

        Assert.That(pkt.Fields["name"].AsText(), Is.EqualTo("Vulpine"));
    }
}

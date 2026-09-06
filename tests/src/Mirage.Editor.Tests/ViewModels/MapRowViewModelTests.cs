using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The map-list row (wraps a MapRecord directly): LoadRecord is a lazy fetch that must NOT mark the
/// row dirty, while UpdateRecord (an edit) does; BumpRevision advances the save-revision counter; and the
/// list label appends the player-facing DisplayName in parens only when one is authored.</summary>
[TestFixture]
public class MapRowViewModelTests
{
    static MapRowViewModel Row(MapRecord? r = null, bool isLoaded = false) =>
        new(3, r ?? new MapRecord { Name = "Town" }, isLoaded);

    // A lazy record fetch fills the row without reading as an edit (else a later save would rewrite the map).
    [Test]
    public void LoadRecord_FillsAndMarksLoaded_WithoutDirtying()
    {
        var vm = Row();
        vm.LoadRecord(new MapRecord { Name = "Kordavan", Revision = 4 });
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsLoaded, Is.True);
            Assert.That(vm.IsDirty, Is.False, "a lazy load is not an edit");
            Assert.That(vm.Record.Name, Is.EqualTo("Kordavan"));
        });
    }

    [Test]
    public void UpdateRecord_MarksDirty()
    {
        var vm = Row();
        vm.UpdateRecord(new MapRecord { Name = "Edited" });
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.True);
            Assert.That(vm.Record.Name, Is.EqualTo("Edited"));
        });
    }

    [Test]
    public void BumpRevision_IncrementsTheRecordRevision()
    {
        var vm = Row(new MapRecord { Name = "Town", Revision = 7 });
        vm.BumpRevision();
        Assert.That(vm.Record.Revision, Is.EqualTo(8));
    }

    [Test]
    public void ClearDirty_Resets()
    {
        var vm = Row();
        vm.MarkDirty();
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    // The label shows the player-facing name in parens only when it's authored.
    [Test]
    public void DisplayName_AppendsPlayerFacingNameWhenAuthored()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row(new MapRecord { Name = "Town", DisplayName = "Greenwood" }).DisplayName,
                Does.Contain("(Greenwood)"));
            Assert.That(Row(new MapRecord { Name = "Town", DisplayName = "" }).DisplayName,
                Is.EqualTo("3: Town"), "no parens when there is no player-facing name");
        });
    }
}

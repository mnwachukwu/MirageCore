using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Save follows the map you are looking at.
///
/// <para>Dirtiness belongs to the SELECTED map, so switching maps changes the answer without any row
/// raising anything of its own. Save's IsEnabled binds to <c>IsSelectedMapDirty</c>, so a selection change
/// that announces nothing leaves the button showing the previous map's state — enabled over a clean map,
/// and then disabled over the dirty one you came back to, with Save All the only way out.</para>
///
/// <para>The command carries the same rule itself: a save with nothing to save would still bump the
/// revision, and the revision is what tells a connected client its cached copy is stale.</para>
/// </summary>
[TestFixture]
public class SaveButtonFollowsSelectionTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    private static MapEditorViewModel Build()
    {
        var data = new EditorDataService();
        Set(data, nameof(EditorDataService.OfflineMaps), new[]
        {
            new MapRecord { Name = "(none)" },
            new MapRecord { Name = "First" },
            new MapRecord { Name = "Second" },
        });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    private static MapRowViewModel Row(MapEditorViewModel vm, int index) => vm.Maps.First(m => m.Index == index);

    /// <summary>Paint something, the way dirtying a map actually happens.</summary>
    private static void Dirty(MapEditorViewModel vm, MapRowViewModel row)
    {
        vm.SelectedMap = row;
        vm.SelectedMode = EditorMode.Attribute;
        row.Record.EditTile(1, 1, t => t with { Type = TileType.Blocked });
        row.MarkDirty();
    }

    [Test]
    public void LeavingADirtyMapForACleanOne_DisablesSave()
    {
        var vm = Build();
        Dirty(vm, Row(vm, 1));
        Assert.That(vm.IsSelectedMapDirty, Is.True, "the map that was just painted is dirty");

        vm.SelectedMap = Row(vm, 2);

        Assert.That(vm.IsSelectedMapDirty, Is.False,
            "Save stayed enabled over a clean map — the selection change announced nothing");
    }

    [Test]
    public void ReturningToADirtyMap_ReEnablesSave()
    {
        var vm = Build();
        Dirty(vm, Row(vm, 1));
        vm.SelectedMap = Row(vm, 2);

        vm.SelectedMap = Row(vm, 1);

        Assert.That(vm.IsSelectedMapDirty, Is.True,
            "the dirty map came back showing Save disabled, leaving Save All as the only way to save it");
    }

    /// <summary>The binding is only as good as the notification behind it, so pin the notification.</summary>
    [Test]
    public void ChangingSelection_AnnouncesTheDirtyState()
    {
        var vm = Build();
        Dirty(vm, Row(vm, 1));

        var seen = new List<string>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) seen.Add(e.PropertyName);
        };
        vm.SelectedMap = Row(vm, 2);

        Assert.That(seen, Does.Contain(nameof(MapEditorViewModel.IsSelectedMapDirty)),
            "nothing told the Save button its answer had changed");
    }

    [Test]
    public void SavingACleanMap_DoesNotBumpItsRevision()
    {
        var vm = Build();
        var clean = Row(vm, 2);
        vm.SelectedMap = clean;
        int before = clean.Record.Revision;

        vm.SaveMapCommand.Execute(null);

        Assert.That(clean.Record.Revision, Is.EqualTo(before),
            "a save with nothing to save bumped the revision, which invalidates every client's cached copy");
    }

    [Test]
    public void SavingADirtyMap_StillBumpsItsRevision()
    {
        var vm = Build();
        var row = Row(vm, 1);
        Dirty(vm, row);
        int before = row.Record.Revision;

        vm.SaveMapCommand.Execute(null);

        Assert.That(row.Record.Revision, Is.GreaterThan(before), "a real save must still bump the revision");
    }
}

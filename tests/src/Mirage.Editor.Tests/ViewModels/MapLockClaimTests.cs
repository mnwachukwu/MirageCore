using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The map editor claims a map the moment it is dirtied — from the same row subscription that raises the
/// dirty flags, and from nowhere else.
///
/// <para>🔴 Every loader throws the rows away and builds new ones: connecting, disconnecting, reopening a
/// world, rereading one. A claim hung off a second subscription has to be rebuilt with them, and a second
/// subscription that tracks rows by anything other than the row itself — a slot number, a "seen" set —
/// silently stops firing on the first reload. Nothing announces that: the map still dirties, still shows
/// its unsaved dot, still saves. It is simply edited with no lock behind it, so the other session is
/// never told and both people write.</para>
///
/// <para>There is no seam on the connection to watch the packet go, so what is pinned is the shape that
/// made it possible: one subscription, maintained by the collection itself.</para>
/// </summary>
[TestFixture]
public class MapLockClaimTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    private static MapEditorViewModel Build()
    {
        var data = new EditorDataService();
        Set(data, nameof(EditorDataService.OfflineMaps),
            new[] { new MapRecord { Name = "(none)" }, new MapRecord { Name = "Yard" }, new MapRecord { Name = "Road" } });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });
        return new MapEditorViewModel(data, new EditorConnection());
    }

    private static List<MapRowViewModel> SubscribedRows(MapEditorViewModel vm) =>
        (List<MapRowViewModel>)typeof(MapEditorViewModel)
            .GetField("_subscribedMapRows", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(vm)!;

    /// <summary>What every reload has to leave true: the rows being watched are the rows on screen, by
    /// identity. A reload builds new row objects, so tracking that survives one is tracking the wrong
    /// thing.</summary>
    [Test]
    public void AfterAReload_TheWatchedRowsAreTheRowsOnScreen()
    {
        var vm = Build();
        vm.LoadOffline();
        var first = vm.Maps.ToList();

        vm.LoadOffline();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Maps, Is.Not.EquivalentTo(first), "a reload builds new rows — otherwise this proves nothing");
            Assert.That(SubscribedRows(vm), Is.EquivalentTo(vm.Maps),
                "the rows being watched are not the rows on screen, so dirtying one reaches nothing");
        });
    }

    private static string SourceRoot()
    {
        string dir = typeof(MapLockClaimTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(dir), Is.True, $"Editor source root not found: {dir}");
        return dir;
    }

    private static string[] ViewModelSources() =>
        [.. Directory.GetFiles(Path.Combine(SourceRoot(), "ViewModels"), "MapEditorViewModel*.cs")
            .OrderBy(p => p, StringComparer.Ordinal)];

    /// <summary>One subscription to a map row's changes, so the claim cannot outlive the rows it was made
    /// against. <c>HookMaps</c> owns it and the collection maintains it.</summary>
    [Test]
    public void OnlyOnePlace_SubscribesToAMapRow()
    {
        var registrations = ViewModelSources()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^\s*row\.PropertyChanged\s*\+=", RegexOptions.Multiline)
                                  .Select(_ => Path.GetFileName(f)))
            .ToList();

        Assert.That(registrations, Has.Count.EqualTo(1),
            "A second subscription to the map rows has to be rebuilt by every loader, and one that is not "
            + "goes quiet after the first load: " + string.Join(", ", registrations));
        Assert.That(registrations[0], Is.EqualTo("MapEditorViewModel.MapList.cs"),
            "the row subscription belongs with the collection hook that maintains it");
    }

    /// <summary>The claim hangs off that subscription rather than one of its own.</summary>
    [Test]
    public void TheClaim_HangsOffTheDirtyHandler()
    {
        string mapList = File.ReadAllText(Path.Combine(SourceRoot(), "ViewModels", "MapEditorViewModel.MapList.cs"));
        string handler = mapList[mapList.IndexOf("private void OnMapItemPropertyChanged", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.That(handler, Does.Contain("ClaimOrReleaseLock"),
            "the dirty handler no longer claims the lock, so dirtying a map tells the server nothing");
    }
}

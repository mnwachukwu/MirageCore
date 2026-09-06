using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// "Which maps are in this group", and following one of them.
///
/// <para>Membership is a field on the MAP — a group record holds no roster — so the group editor cannot answer
/// this from its own data. <c>MainWindowViewModel</c> hands it a resolver over the map list; these pin the two
/// halves of that arrangement: the group side asks and re-asks at the right moments, and the map side opens a
/// map as an ordinary selection so it lands on the back/forward trail.</para>
/// </summary>
[TestFixture]
public class MapGroupMembershipTests
{
    private static MapEditorViewModel MapsNamed(params (int Num, string Name, int Group)[] maps)
    {
        var vm = new MapEditorViewModel(new EditorDataService(), new EditorConnection());
        foreach (var (num, name, group) in maps)
            vm.Maps.Add(new MapRowViewModel(num, new MapRecord { Name = name, MapGroup = group }));
        return vm;
    }

    // The wiring MainWindowViewModel installs, kept in one place so these tests exercise the real shape.
    private static void Wire(MapGroupEditorViewModel groups, MapEditorViewModel maps, List<int> opened) =>
        groups.ResolveGroupMaps = id => maps.Maps
            .Where(m => m.Record.MapGroup == id)
            .Select(m => new ReferenceLinkViewModel(m.DisplayName, () => opened.Add(m.Index)))
            .ToList();

    private static MapGroupEditorViewModel Groups(params int[] indices)
    {
        var vm = new MapGroupEditorViewModel(new EditorDataService(), new EditorConnection());
        foreach (int i in indices)
            vm.MapGroups.Add(new MapGroupRowViewModel(i, new MapGroupRecord { Index = i, Name = $"Group {i}" }, () => []));
        return vm;
    }

    [Test]
    public void OnlyTheMapsNamingThisGroupAreListed()
    {
        var maps = MapsNamed((1, "Clearing", 1), (2, "Deck", 2), (3, "Causeway", 1), (4, "Nowhere", 0));
        var groups = Groups(1, 2);
        Wire(groups, maps, []);

        groups.SelectedMapGroup = groups.MapGroups.First(g => g.Index == 1);

        Assert.Multiple(() =>
        {
            Assert.That(groups.GroupMaps.Select(l => l.DisplayName),
                Is.EquivalentTo(new[] { maps.Maps[0].DisplayName, maps.Maps[2].DisplayName }));
            Assert.That(groups.HasGroupMaps, Is.True);
        });
    }

    [Test]
    public void AGroupNoMapNames_ListsNothing()
    {
        var maps = MapsNamed((1, "Clearing", 1));
        var groups = Groups(9);
        Wire(groups, maps, []);

        groups.SelectedMapGroup = groups.MapGroups.First();

        Assert.Multiple(() =>
        {
            Assert.That(groups.GroupMaps, Is.Empty);
            Assert.That(groups.HasGroupMaps, Is.False, "so the panel can say so rather than showing a blank column");
        });
    }

    /// <summary>Online, a map row is a name with an empty record until the eager load fills it, so membership
    /// reads as zero for every map. Anything already on screen is stale until it is told to look again.</summary>
    [Test]
    public void MembershipIsRereadWhenTold()
    {
        var maps = MapsNamed((1, "Clearing", 0));   // not yet loaded: group unknown
        var groups = Groups(1);
        Wire(groups, maps, []);
        groups.SelectedMapGroup = groups.MapGroups.First();
        Assert.That(groups.GroupMaps, Is.Empty, "precondition: nothing claims the group yet");

        maps.Maps[0].UpdateRecord(new MapRecord { Name = "Clearing", MapGroup = 1 });   // the fetch lands
        groups.NotifyGroupMapsChanged();

        Assert.That(groups.GroupMaps, Has.Count.EqualTo(1), "the panel catches up rather than staying empty");
    }

    [Test]
    public void FollowingALinkReportsTheMapItNames()
    {
        var maps = MapsNamed((4, "Causeway", 1), (7, "Reeds", 1));
        var groups = Groups(1);
        var opened = new List<int>();
        Wire(groups, maps, opened);
        groups.SelectedMapGroup = groups.MapGroups.First();

        groups.GroupMaps.Single(l => l.DisplayName.StartsWith("7:")).OpenCommand.Execute(null);

        Assert.That(opened, Is.EqualTo(new[] { 7 }));
    }

    // ── The map side: a followed link is an ordinary selection ────────────────

    /// <summary>Matt's requirement, and the reason SelectByIndex does NOT suppress history: arriving from a
    /// group link must leave Back pointing at the map you were on, and Forward at the one you landed on.</summary>
    [Test]
    public void OpeningAMapByNumber_LandsOnTheBackForwardTrail()
    {
        var maps = MapsNamed((1, "Clearing", 1), (2, "Deck", 2), (3, "Causeway", 1));
        maps.SelectedMap = maps.Maps[0];                       // reading map 1

        Assert.That(maps.SelectByIndex(3), Is.True, "the link opens map 3");
        Assert.That(maps.SelectedMap!.Index, Is.EqualTo(3));
        Assert.That(maps.CanNavigateBack, Is.True, "which pushed map 1 onto the trail");

        maps.NavigateBackCommand.Execute(null);
        Assert.That(maps.SelectedMap!.Index, Is.EqualTo(1), "Back returns to the map we came from");

        maps.NavigateForwardCommand.Execute(null);
        Assert.That(maps.SelectedMap!.Index, Is.EqualTo(3), "Forward returns to the one the link opened");
    }

    /// <summary>A number naming no row changes nothing, so the caller can decline to switch sections rather
    /// than showing the Maps pane with the wrong map — or none — selected.</summary>
    [Test]
    public void OpeningAMapThatIsNotThere_ChangesNothing()
    {
        var maps = MapsNamed((1, "Clearing", 1));
        maps.SelectedMap = maps.Maps[0];

        Assert.Multiple(() =>
        {
            Assert.That(maps.SelectByIndex(999), Is.False);
            Assert.That(maps.SelectedMap!.Index, Is.EqualTo(1), "the selection is left alone");
        });
    }
}

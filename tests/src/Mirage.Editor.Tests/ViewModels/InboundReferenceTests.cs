using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The plumbing behind every "Referenced by" panel.
///
/// <para>The scans themselves live on <c>MainWindowViewModel</c>, which needs a bitmap cache and a live
/// window to build. What every editor shares — and what actually breaks — is the contract underneath: a
/// resolver hands the panel its groups, the panel re-reads them when the selection moves, and following a
/// link selects the record by number through the editor's ordinary selection path.</para>
///
/// <para><c>TrySelect</c> is the piece worth pinning hardest: it exists so a link can open a record in
/// another section, and it must refuse a number that names no row rather than leaving the pane showing the
/// wrong record after the section has already switched.</para>
/// </summary>
[TestFixture]
public class InboundReferenceTests
{
    private static ItemEditorViewModel Items(params (int Num, string Name)[] items)
    {
        var vm = new ItemEditorViewModel(new EditorDataService(), new EditorConnection());
        foreach (var (num, name) in items)
            vm.Items.Add(new ItemRowViewModel(num, new ItemRecord { Name = name }));
        return vm;
    }

    // ── TrySelect: how a followed link lands ─────────────────────────────────

    [Test]
    public void TrySelect_SelectsTheRowWithThatNumber()
    {
        var vm = Items((3, "Bread"), (9, "Iron Sword"));

        Assert.Multiple(() =>
        {
            Assert.That(vm.TrySelect(9), Is.True);
            Assert.That(vm.SelectedItem!.Index, Is.EqualTo(9));
        });
    }

    /// <summary>A number naming no row must change nothing, so the caller can decline to switch sections
    /// rather than showing a pane with the wrong record — or none — selected.</summary>
    [Test]
    public void TrySelect_LeavesTheSelectionAloneWhenTheNumberNamesNoRow()
    {
        var vm = Items((3, "Bread"));
        vm.TrySelect(3);

        Assert.Multiple(() =>
        {
            Assert.That(vm.TrySelect(999), Is.False);
            Assert.That(vm.SelectedItem!.Index, Is.EqualTo(3), "still on the row we were on");
        });
    }

    // ── The panel contract ───────────────────────────────────────────────────

    [Test]
    public void WithNoResolverWired_NothingIsClaimedToReferenceIt()
    {
        var vm = Items((3, "Bread"));
        vm.TrySelect(3);

        Assert.Multiple(() =>
        {
            Assert.That(vm.InboundRefs, Is.Empty);
            Assert.That(vm.HasInboundRefs, Is.False, "so the panel stays hidden rather than showing a heading");
        });
    }

    [Test]
    public void TheResolverIsAskedForTheSelectedRecord()
    {
        var vm = Items((3, "Bread"), (9, "Iron Sword"));
        var asked = new List<int>();
        vm.ResolveInboundRefs = num =>
        {
            asked.Add(num);
            return [new ReferenceGroupViewModel("Dropped by", [new ReferenceLinkViewModel($"npc for {num}", () => { })])];
        };

        vm.TrySelect(9);
        var groups = vm.InboundRefs;

        Assert.Multiple(() =>
        {
            Assert.That(asked, Does.Contain(9), "asked about the record actually selected");
            Assert.That(groups.Single().Links.Single().DisplayName, Is.EqualTo("npc for 9"));
            Assert.That(vm.HasInboundRefs, Is.True);
        });
    }

    /// <summary>The referring records live in other editors, so this one cannot see them arrive. Online they
    /// do not exist at all until the eager load lands, which is long after the panel first reads them.</summary>
    [Test]
    public void ThePanelRereadsWhenTold()
    {
        var vm = Items((3, "Bread"));
        var world = new List<string>();
        vm.ResolveInboundRefs = _ => world.Select(n => new ReferenceLinkViewModel(n, () => { }))
                                          .ToList() is { Count: > 0 } links
            ? [new ReferenceGroupViewModel("Dropped by", links)]
            : [];
        vm.TrySelect(3);
        Assert.That(vm.HasInboundRefs, Is.False, "precondition: nothing refers to it yet");

        world.Add("Marsh Rat");             // the eager load lands
        vm.NotifyInboundRefsChanged();

        Assert.That(vm.InboundRefs.Single().Links, Has.Count.EqualTo(1), "the panel catches up");
    }

    /// <summary>Selecting a different record must re-ask. The panel is a property of the SELECTION, and a
    /// stale answer here would describe the record you just navigated away from.</summary>
    [Test]
    public void MovingTheSelectionReAsks()
    {
        var vm = Items((3, "Bread"), (9, "Iron Sword"));
        vm.ResolveInboundRefs = num =>
            [new ReferenceGroupViewModel("Dropped by", [new ReferenceLinkViewModel($"for {num}", () => { })])];

        vm.TrySelect(3);
        Assert.That(vm.InboundRefs.Single().Links.Single().DisplayName, Is.EqualTo("for 3"));

        vm.TrySelect(9);
        Assert.That(vm.InboundRefs.Single().Links.Single().DisplayName, Is.EqualTo("for 9"));
    }

    [Test]
    public void FollowingALinkRunsWhatTheScanGaveIt()
    {
        int opened = 0;
        var link = new ReferenceLinkViewModel("Marsh Rat", () => opened++);

        link.OpenCommand.Execute(null);

        Assert.That(opened, Is.EqualTo(1));
    }
}

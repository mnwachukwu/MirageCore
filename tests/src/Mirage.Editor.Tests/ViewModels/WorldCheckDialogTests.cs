using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The world check's results window.
///
/// <para>The sweep itself is proven in <c>WorldCheckTests</c>; what matters here is that a finding arrives
/// worded, carries the record it is about, and gets the author there. A list of faults nobody can navigate
/// to is a list nobody acts on.</para>
/// </summary>
[TestFixture]
public class WorldCheckDialogTests
{
    private static WorldCheckDialogViewModel Vm(params WorldIssue[] issues) =>
        new(issues, (kind, n) => $"{kind} {n} name");

    private static WorldIssue OnRecord(WorldIssueKind kind, WorldRecordKind owner, int num) =>
        new(kind, owner, num, -1, -1, "detail");

    [Test]
    public void NoFindings_SaysSoRatherThanShowingAnEmptyList()
    {
        var vm = Vm();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsClean, Is.True);
            Assert.That(vm.Rows, Is.Empty);
            Assert.That(vm.Summary, Is.Not.Empty);
        });
    }

    /// <summary>Every kind and every record family has to resolve to a sentence. A missing entry in either
    /// switch shows the reader an enum name, which is exactly the case a hand-written lookup drops.</summary>
    [Test]
    public void EveryKind_HasWording()
    {
        foreach (var kind in Enum.GetValues<WorldIssueKind>())
        {
            var vm = Vm(OnRecord(kind, WorldRecordKind.Map, 3));

            Assert.That(vm.Rows[0].What, Is.Not.Empty.And.Not.EqualTo(kind.ToString()),
                        $"{kind} must resolve to a sentence, not to its enum name");
        }
    }

    [Test]
    public void EveryRecordFamily_HasALabel()
    {
        foreach (var owner in Enum.GetValues<WorldRecordKind>())
        {
            var vm = Vm(OnRecord(WorldIssueKind.NpcMissing, owner, 2));

            Assert.That(vm.Rows[0].Where, Is.Not.Empty.And.Not.Contain(owner.ToString() + "_"),
                        $"{owner} must resolve to a label");
        }
    }

    [Test]
    public void ATileScopedFinding_NamesItsTile()
    {
        var vm = Vm(new WorldIssue(WorldIssueKind.WarpTileOutside, WorldRecordKind.Map, 4, 7, 9, "x"));

        Assert.That(vm.Rows[0].Where, Does.Contain("7").And.Contain("9"));
    }

    [Test]
    public void ARecordScopedFinding_NamesOnlyTheRecord()
    {
        var vm = Vm(OnRecord(WorldIssueKind.ShopHasNoKeeper, WorldRecordKind.Shop, 4));

        Assert.That(vm.Rows[0].Where, Does.Contain("Shop 4 name"));
    }

    /// <summary>Following a row goes to its record and closes the window, since the record behind it is what
    /// the author needs to see.</summary>
    [Test]
    public void FollowingARow_NavigatesAndCloses()
    {
        var vm = Vm(OnRecord(WorldIssueKind.QuestPrereqCycle, WorldRecordKind.Quest, 12));
        (WorldRecordKind Kind, int Num)? went = null;
        bool closed = false;
        vm.Navigate += (k, n) => went = (k, n);
        vm.Closed += () => closed = true;

        vm.Rows[0].GoCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(went, Is.EqualTo((WorldRecordKind.Quest, 12)));
            Assert.That(closed, Is.True);
        });
    }

    /// <summary>Findings group by the record they are on, so everything wrong with one map reads together
    /// instead of being scattered through the list.</summary>
    [Test]
    public void Findings_GroupByTheirRecord()
    {
        var vm = Vm(
            OnRecord(WorldIssueKind.ShopHasNoKeeper, WorldRecordKind.Shop, 2),
            OnRecord(WorldIssueKind.MapGroupMissing, WorldRecordKind.Map, 5),
            OnRecord(WorldIssueKind.NpcMissing, WorldRecordKind.Shop, 1),
            OnRecord(WorldIssueKind.LinkOutOfRange, WorldRecordKind.Map, 1));

        Assert.That(vm.Rows.Select(r => r.Where), Is.EqualTo(new[]
        {
            "Map 1: Map 1 name", "Map 5: Map 5 name", "Shop 1: Shop 1 name", "Shop 2: Shop 2 name",
        }));
    }

    [Test]
    public void EveryRow_CarriesItsButtonCaption()
    {
        var vm = Vm(OnRecord(WorldIssueKind.LinkOutOfRange, WorldRecordKind.Map, 1));

        Assert.That(vm.Rows[0].GoLabel, Is.Not.Empty);
    }
}

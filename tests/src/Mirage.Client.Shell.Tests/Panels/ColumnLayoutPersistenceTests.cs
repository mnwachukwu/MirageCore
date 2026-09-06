using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>Pins the full column-layout persistence round-trip a Table goes through — user resize/reorder →
/// host saves via <see cref="AccountConfig"/> (keyed by table id) → a new session loads +
/// <see cref="Table{T}.ApplyColumnLayout"/> re-applies it. Reorderable tables persist order; fixed tables
/// persist only widths + sort (a null order, omitted on disk). Regression guard for "my column widths /
/// arrangement don't persist across sessions."</summary>
[TestFixture]
public class ColumnLayoutPersistenceTests
{
    private sealed record Row(int V);

    // A stand-in for a real 5-column table (the territory table's default widths).
    private static Table<Row> FiveColTable() => new Table<Row>()
        .Column("Territory", r => r.V, width: 104, minWidth: 70)
        .Column("Owner", r => r.V, width: 96, minWidth: 60)
        .Column("WeeksHeld", r => r.V, width: 56, minWidth: 40)
        .Column("PrevIncome", r => r.V, width: 72, minWidth: 50)
        .Column("Contesting", r => r.V, width: 100, minWidth: 60);

    [Test]
    public void ApplyColumnLayout_AppliesSavedWidths_ToTheModelTheGetterReads()
    {
        var t = FiveColTable();
        t.ApplyColumnLayout(new[] { 0, 1, 2, 3, 4 }, new[] { 104, 96, 83, 91, 100 }, sortColumn: 0, sortAscending: true);
        // The host getter reads exactly this: ColumnWidths → Model.Columns[i].Width.
        Assert.That(t.ColumnWidths, Is.EqualTo(new[] { 104, 96, 83, 91, 100 }));
    }

    [Test]
    public void AccountConfig_RoundTripsColumns_Reorderable()
    {
        var cfg = new AccountConfig();
        cfg.SetTableColumns("Thragtar", "social.roster", new[] { 0, 2, 1, 3, 4 }, new[] { 104, 96, 83, 91, 100 }, 0, true);
        var got = cfg.GetTableColumns("Thragtar", "social.roster");
        Assert.That(got, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(got!.Widths, Is.EqualTo(new[] { 104, 96, 83, 91, 100 }));
            Assert.That(got.Order, Is.EqualTo(new[] { 0, 2, 1, 3, 4 }), "a reorderable table persists its column order");
        });
    }

    [Test]
    public void AccountConfig_FixedTable_PersistsWidthsAndSort_ButNoOrder()
    {
        var cfg = new AccountConfig();
        // A fixed table saves with a null order (the host passes null when AllowReorder is false).
        cfg.SetTableColumns("Thragtar", "social.territory", order: null, new[] { 104, 96, 83, 91, 100 }, 2, false);
        var got = cfg.GetTableColumns("Thragtar", "social.territory")!;
        Assert.Multiple(() =>
        {
            Assert.That(got.Order, Is.Null, "a fixed table persists no order");
            Assert.That(got.Widths, Is.EqualTo(new[] { 104, 96, 83, 91, 100 }));
            Assert.That(got.SortColumn, Is.EqualTo(2));
            Assert.That(got.SortAscending, Is.False);
        });
    }

    [Test]
    public void FullRoundTrip_UserResize_SaveToConfig_NewSessionLoad_PreservesCustomWidths()
    {
        // Session 1: the user drags WeeksHeld (56 → 83) and PrevIncome (72 → 91), then the host persists.
        var session1 = FiveColTable();
        session1.Model.ResizeColumn(2, 83);
        session1.Model.ResizeColumn(3, 91);
        var cfg = new AccountConfig();
        cfg.SetTableColumns("Thragtar", "social.roster",
            session1.ColumnOrder, session1.ColumnWidths, session1.SortColumn, session1.SortAscending);

        // Session 2: a fresh table (default widths) loads the saved layout.
        var session2 = FiveColTable();
        var saved = cfg.GetTableColumns("Thragtar", "social.roster")!;
        session2.ApplyColumnLayout(saved.Order ?? new List<int>(), saved.Widths, saved.SortColumn, saved.SortAscending);

        // The custom widths survived the round-trip; the untouched columns keep their defaults.
        Assert.That(session2.ColumnWidths, Is.EqualTo(new[] { 104, 96, 83, 91, 100 }));
    }
}

using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>The pure state behind the <see cref="Table{T}"/> control (<see cref="TableModel"/>): column
/// reorder, resize-with-min-clamp, the asc/desc sort toggle, and the stable row-order permutation. The
/// view (header input + rendering) rides on these, so locking the math down here is what guarantees the
/// control's correctness.</summary>
[TestFixture]
public class TableModelTests
{
    private static TableModel Make() => new(new[]
    {
        new TableColumn("Rank", 60, 40),
        new TableColumn("Name", 100, 50),
        new TableColumn("Level", 50, 30),
    });

    [Test]
    public void NewModel_IdentityOrder_Unsorted()
    {
        var m = Make();
        Assert.That(m.Order, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(m.SortColumn, Is.EqualTo(-1));
        Assert.That(m.SortAscending, Is.True);
        Assert.That(m.ColumnCount, Is.EqualTo(3));
    }

    [Test]
    public void Ctor_WidthBelowMin_ClampsUp()
    {
        var m = new TableModel(new[] { new TableColumn("A", 10, 40) });
        Assert.That(m.ColumnAt(0).Width, Is.EqualTo(40));
    }

    [Test]
    public void Ctor_NoColumns_Throws()
        => Assert.Throws<ArgumentException>(() => new TableModel(Array.Empty<TableColumn>()));

    // ── Reorder ────────────────────────────────────────────────────────────────
    [Test]
    public void MoveColumn_ForwardThenBack_RoundTrips()
    {
        var m = Make();
        m.MoveColumn(0, 2);
        Assert.That(m.Order, Is.EqualTo(new[] { 1, 2, 0 }));   // Rank pushed to the end
        m.MoveColumn(2, 0);
        Assert.That(m.Order, Is.EqualTo(new[] { 0, 1, 2 }));   // and back
    }

    [Test]
    public void MoveColumn_ClampsTarget_AndIgnoresNoOpAndBadSource()
    {
        var m = Make();
        m.MoveColumn(0, 99);                                    // target clamped to last
        Assert.That(m.Order, Is.EqualTo(new[] { 1, 2, 0 }));
        var snapshot = m.Order.ToArray();
        m.MoveColumn(1, 1);                                     // no-op
        Assert.That(m.Order, Is.EqualTo(snapshot));
        m.MoveColumn(-1, 0);                                    // invalid source
        Assert.That(m.Order, Is.EqualTo(snapshot));
    }

    // ── Resize ─────────────────────────────────────────────────────────────────
    [Test]
    public void ResizeColumn_GrowsFreely_ButClampsAtMin()
    {
        var m = Make();
        m.ResizeColumn(0, 200);
        Assert.That(m.ColumnAt(0).Width, Is.EqualTo(200));
        m.ResizeColumn(0, 5);                                   // below the Rank column's min of 40
        Assert.That(m.ColumnAt(0).Width, Is.EqualTo(40));
    }

    // ── Sort toggle ──────────────────────────────────────────────────────────────
    [Test]
    public void ToggleSort_AscThenDesc_ResetsAscOnNewColumn()
    {
        var m = Make();
        m.ToggleSort(1);
        Assert.That(m.SortColumn, Is.EqualTo(1));
        Assert.That(m.SortAscending, Is.True);
        m.ToggleSort(1);
        Assert.That(m.SortAscending, Is.False);                 // second click on the same column flips
        m.ToggleSort(2);
        Assert.That(m.SortColumn, Is.EqualTo(2));
        Assert.That(m.SortAscending, Is.True);                  // a new column starts ascending again
    }

    [Test]
    public void SetSort_SetsColumnAndDirection_Deterministically()
    {
        var m = Make();
        m.SetSort(2, ascending: false);   // unlike ToggleSort, sets the direction outright
        Assert.That(m.SortColumn, Is.EqualTo(2));
        Assert.That(m.SortAscending, Is.False);
        m.SetSort(0, ascending: true);
        Assert.That(m.SortColumn, Is.EqualTo(0));
        Assert.That(m.SortAscending, Is.True);
    }

    // ── Row-order permutation ────────────────────────────────────────────────────
    [Test]
    public void SortRowOrder_Unsorted_IsIdentity()
    {
        var m = Make();
        Assert.That(m.SortRowOrder(new IComparable?[] { 3, 1, 2 }), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void SortRowOrder_AscendingThenDescending()
    {
        var m = Make();
        var keys = new IComparable?[] { 30, 10, 20 };
        m.ToggleSort(0);
        Assert.That(m.SortRowOrder(keys), Is.EqualTo(new[] { 1, 2, 0 }));
        m.ToggleSort(0);
        Assert.That(m.SortRowOrder(keys), Is.EqualTo(new[] { 0, 2, 1 }));
    }

    [Test]
    public void SortRowOrder_StableForEqualKeys_InBothDirections()
    {
        var m = Make();
        var keys = new IComparable?[] { 5, 5, 5, 1 };
        m.ToggleSort(0);                                        // asc: the 1 leads, the 5s keep source order
        Assert.That(m.SortRowOrder(keys), Is.EqualTo(new[] { 3, 0, 1, 2 }));
        m.ToggleSort(0);                                        // desc: the 5s STILL keep source order, then the 1
        Assert.That(m.SortRowOrder(keys), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void SortRowOrder_StringsAndNulls_NullSortsFirstAscending()
    {
        var m = Make();
        m.ToggleSort(1);
        var keys = new IComparable?[] { "b", null, "a" };
        Assert.That(m.SortRowOrder(keys), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    // ── Layout geometry ──────────────────────────────────────────────────────────
    [Test]
    public void Layout_XOffsetsFollowWidthsInDisplayOrder()
    {
        var m = Make();
        var l = m.Layout();
        Assert.That(l[0].X, Is.EqualTo(0));
        Assert.That(l[1].X, Is.EqualTo(60));                    // after Rank(60)
        Assert.That(l[2].X, Is.EqualTo(160));                   // after Rank(60) + Name(100)
    }

    [Test]
    public void Layout_ReflectsReorder()
    {
        var m = Make();
        m.MoveColumn(2, 0);                                     // Level(50) to the front
        var l = m.Layout();
        Assert.That(l[0].Logical, Is.EqualTo(2));
        Assert.That(l[0].X, Is.EqualTo(0));
        Assert.That(l[1].Logical, Is.EqualTo(0));
        Assert.That(l[1].X, Is.EqualTo(50));                    // after Level(50)
    }

    [Test]
    public void TotalWidth_SumsColumnWidths()
    {
        var m = Make();
        Assert.That(m.TotalWidth, Is.EqualTo(210));
        m.ResizeColumn(1, 140);
        Assert.That(m.TotalWidth, Is.EqualTo(250));
    }

    // ── Layout persistence (ApplyLayout) ─────────────────────────────────────────
    [Test]
    public void ApplyLayout_RestoresOrderAndWidths_ClampingToMin()
    {
        var m = Make();   // logical mins: Rank 40, Name 50, Level 30
        m.ApplyLayout(new[] { 2, 0, 1 }, new[] { 5, 200, 90 });   // width 5 is below Rank's min of 40
        Assert.That(m.Order, Is.EqualTo(new[] { 2, 0, 1 }));
        Assert.That(m.ColumnAt(0).Width, Is.EqualTo(40));         // clamped up to Rank's min
        Assert.That(m.ColumnAt(1).Width, Is.EqualTo(200));
        Assert.That(m.ColumnAt(2).Width, Is.EqualTo(90));
    }

    [Test]
    public void ApplyLayout_IgnoresMismatchedShapes()
    {
        var m = Make();
        var order = m.Order.ToArray();
        m.ApplyLayout(new[] { 0, 1 }, new[] { 10, 20 });          // wrong count → ignored (stale/foreign config)
        Assert.That(m.Order, Is.EqualTo(order));
        Assert.That(m.ColumnAt(0).Width, Is.EqualTo(60));         // unchanged default
        m.ApplyLayout(new[] { 0, 0, 1 }, new[] { 60, 100, 50 });  // not a permutation → order ignored
        Assert.That(m.Order, Is.EqualTo(order));
    }
}

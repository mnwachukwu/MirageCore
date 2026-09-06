using Mirage.Client.Shell.Ui;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>The data-bound generic table (<see cref="Table{T}"/>): feed it a collection, declare columns
/// from a row's properties, and it renders/sorts as asked. These cover the pure data path — column
/// value/sort-key extraction, the declared default order, programmatic sort, and key-based selection that
/// survives a list swap. (The header input + rendering are hardware/graphics and stay a manual playtest;
/// the column math itself lives in the separately-tested <see cref="TableModel"/>.)</summary>
[TestFixture]
public class TableTests
{
    private sealed record Person(string Name, int Level, bool Online);

    private static readonly Person[] Sample =
    {
        new("Cara", 30, true),
        new("Alba", 5, false),
        new("Bram", 12, true),
    };

    // Two typical columns: a string field and an int field (the int sorts numerically for free).
    private static Table<Person> People() => new Table<Person>()
        .Column("Name", p => p.Name)
        .Column("Level", p => p.Level);

    [Test]
    public void Items_DefaultOrder_IsDeclarationOrderOfTheCollection()
    {
        var t = People();
        t.Items = Sample;
        Assert.That(t.RowsInDisplayOrder().Select(p => p.Name), Is.EqualTo(new[] { "Cara", "Alba", "Bram" }));
    }

    [Test]
    public void ToggleSort_StringColumn_SortsAscThenDesc()
    {
        var t = People();
        t.Items = Sample;
        t.ToggleSort(0);
        Assert.That(t.RowsInDisplayOrder().Select(p => p.Name), Is.EqualTo(new[] { "Alba", "Bram", "Cara" }));
        t.ToggleSort(0);
        Assert.That(t.RowsInDisplayOrder().Select(p => p.Name), Is.EqualTo(new[] { "Cara", "Bram", "Alba" }));
    }

    [Test]
    public void ToggleSort_IntColumn_SortsNumerically()
    {
        var t = People();
        t.Items = Sample;
        t.ToggleSort(1);
        Assert.That(t.RowsInDisplayOrder().Select(p => p.Level), Is.EqualTo(new[] { 5, 12, 30 }));
    }

    [Test]
    public void Column_SeparateDisplayText_StillSortsByTheKey()
    {
        // Sort key is the numeric level; the display would render "Lv5" etc. Sorting must use the key.
        var t = new Table<Person>().Column("Lvl", p => p.Level, text: p => $"Lv{p.Level}");
        t.Items = Sample;
        t.ToggleSort(0);
        Assert.That(t.RowsInDisplayOrder().Select(p => p.Level), Is.EqualTo(new[] { 5, 12, 30 }));
    }

    [Test]
    public void Selection_FollowsRowKey_AcrossAnItemsSwap()
    {
        var t = new Table<Person>().Column("Name", p => p.Name).WithRowKey(p => p.Name);
        t.Items = Sample;
        t.SelectedIndex = 2;   // Bram
        Assert.That(t.SelectedItem!.Name, Is.EqualTo("Bram"));

        // A server push: a fresh, reordered list where Bram's other fields changed.
        t.Items = new[] { new Person("Bram", 99, false), new Person("Alba", 5, false) };
        Assert.That(t.SelectedItem!.Name, Is.EqualTo("Bram"));   // still selected, matched by key
    }

    [Test]
    public void Selection_WithoutRowKey_ClearsOnAnyItemsSwap()
    {
        var t = People();   // no WithRowKey
        t.Items = Sample;
        t.SelectedIndex = 1;
        Assert.That(t.SelectedItem!.Name, Is.EqualTo("Alba"));
        // A fresh list of the SAME size — index 1 is still in range, but without a key we can't know it's the
        // same logical row, so the selection is cleared (safer than highlighting a different row).
        t.Items = new[] { new Person("X", 1, true), new Person("Y", 2, false), new Person("Z", 3, true) };
        Assert.That(t.SelectedItem, Is.Null);
    }

    [Test]
    public void EmptyItems_ProduceNoRowsAndNoSelection()
    {
        var t = People();
        t.Items = System.Array.Empty<Person>();
        Assert.That(t.RowsInDisplayOrder(), Is.Empty);
        Assert.That(t.SelectedItem, Is.Null);
    }

    [Test]
    public void UsingATableWithNoColumns_Throws()
    {
        var t = new Table<Person>();
        Assert.Throws<InvalidOperationException>(() => t.ToggleSort(0));
    }

    // Reordering is opt-in now: a fresh table is fixed-order until a host sets AllowReorder = true.
    [Test]
    public void AllowReorder_DefaultsToFalse()
    {
        Assert.That(new Table<Person>().AllowReorder, Is.False);
    }
}

using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Mirage.Editor.Tests.RoundTrip;

/// <summary>
/// Every row view model fills its fields TWICE, and nothing makes the two agree.
///
/// <para>🔴 A row is built from a record by its constructor, and refilled from one by
/// <c>LoadFromRecord</c>. Both copy the same fields, both are written by hand, and a field added to one
/// is not added to the other — which is invisible: the editor opens, the form is populated, and the one
/// field that was missed reads as whatever the row happened to start with. Adding
/// <c>ItemRecord.EquipSlot</c> to only the constructor cost a debugging cycle.</para>
///
/// <para><b>The comparison is the ROW's own properties, not the record it saves.</b> Comparing
/// <c>ToRecord()</c> reads better and is wrong: it normalizes, so a row that dropped
/// <c>ItemRecord.EquipSlot</c> still matched — the fixture's item type was not Equipment, and
/// <c>ToRecord</c> blanks that field for every other type. What has to agree is the state the two fill
/// paths leave behind, which is the row itself.</para>
///
/// <para><b>The input record is filled by reflection, not by hand.</b> A hand-built record goes stale
/// exactly the way the constructor does — the field nobody remembered to copy is the field nobody
/// remembers to put in the fixture either. Every writable scalar gets a value distinct from its
/// default, so a field arriving tomorrow is covered without anyone deciding to cover it.</para>
///
/// <para><c>SchemaRecordRowViewModel</c> is not here and does not need to be: its record is an
/// <c>AttributeBag</c>, so it copies a dictionary rather than a list of named fields, and there is
/// nothing to fall out of step.</para>
/// </summary>
[TestFixture]
public class RowFillPathsAgreeTests
{
    private static readonly JsonSerializerOptions Readable = new() { WriteIndented = true };

    /// <summary>A value for a property that is not what an empty record would hold, so a field the
    /// second path never writes shows up as a difference rather than as a coincidental match.</summary>
    private static object? Distinct(Type type, int seed)
    {
        Type bare = Nullable.GetUnderlyingType(type) ?? type;

        if (bare == typeof(string)) return "r" + seed;
        if (bare.IsEnum)
        {
            // The LAST member rather than the first: the first is usually the zero an empty record
            // already has, and a fixture that happens to match the default checks nothing.
            Array members = Enum.GetValues(bare);
            return members.GetValue(members.Length - 1);
        }

        if (bare == typeof(bool)) return true;
        if (bare == typeof(short)) return (short)(seed + 1);
        if (bare == typeof(byte)) return (byte)(seed % 200 + 1);
        if (bare == typeof(int)) return seed + 1;
        if (bare == typeof(long)) return (long)(seed + 1);
        if (bare == typeof(double)) return seed + 1.5d;
        if (bare == typeof(float)) return seed + 1.5f;

        return null;   // collections and anything else: left as the record built it
    }

    /// <summary>Give every writable property on <paramref name="target"/> a value, walking into the
    /// lists a record carries so a row's CHILD rows are covered too. Depth-capped, because a record
    /// pointing at its own type would otherwise never finish.</summary>
    private static void Fill(object target, int depth = 0)
    {
        int seed = 0;

        foreach (PropertyInfo property in target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            seed++;
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;

            if (Distinct(property.PropertyType, seed) is { } scalar)
            {
                property.SetValue(target, scalar);
                continue;
            }

            if (depth < 3 && ListElement(property.PropertyType) is { } element)
                property.SetValue(target, OneOf(element, depth, seed));
        }
    }

    /// <summary>The T of a settable <c>List&lt;T&gt;</c>, or null for anything else.</summary>
    private static Type? ListElement(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;

    /// <summary>A one-element list, its element filled the same way. One element rather than several:
    /// what is being checked is whether the second path builds the collection at all, and a single
    /// entry answers that as well as ten would.</summary>
    private static object OneOf(Type element, int depth, int seed)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;

        if (Distinct(element, seed) is { } scalar)
        {
            list.Add(scalar);
            return list;
        }

        if (element.GetConstructor(Type.EmptyTypes) is null) return list;

        object item = Activator.CreateInstance(element)!;
        Fill(item, depth + 1);
        list.Add(item);
        return list;
    }

    private static T Filled<T>() where T : new()
    {
        var record = new T();
        Fill(record);

        // The guard that makes the comparison mean something: a fixture indistinguishable from an empty
        // record would compare two empty rows and pass whatever the two paths do.
        Assert.That(JsonSerializer.Serialize(record, Readable),
            Is.Not.EqualTo(JsonSerializer.Serialize(new T(), Readable)),
            $"{typeof(T).Name}: the fixture is an empty record, so this checks nothing");

        return record;
    }

    /// <summary>Build the row both ways and hold the two against each other, property by property.
    ///
    /// <para>Scalars are compared by value; a collection by how many rows it holds, which answers
    /// "did the second path build the child rows at all". Anything else — a command, a service — is
    /// skipped, since it is not filled from the record.</para></summary>
    private static void BothPathsAgree<TRecord, TRow>(
        Func<TRecord, TRow> construct, Action<TRow, TRecord> load)
        where TRecord : new()
    {
        TRecord record = Filled<TRecord>();

        TRow built = construct(record);

        TRow loaded = construct(new TRecord());
        load(loaded, record);

        var differences = new List<string>();
        int compared = 0;

        foreach (PropertyInfo property in typeof(TRow)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;

            object? a = property.GetValue(built), b = property.GetValue(loaded);
            if (!Comparable(a) && !Comparable(b)) continue;

            compared++;
            if (!Equals(Comparison(a), Comparison(b)))
                differences.Add($"{property.Name}: constructor {Comparison(a)}, LoadFromRecord {Comparison(b)}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(compared, Is.GreaterThan(3), $"{typeof(TRow).Name}: nothing was compared");
            Assert.That(differences, Is.Empty,
                $"{typeof(TRow).Name} fills its fields differently in its constructor and in LoadFromRecord");
        });
    }

    private static bool Comparable(object? value)
        => value is string or bool or Enum || (value is not null && value.GetType().IsPrimitive)
           || value is IEnumerable;

    /// <summary>What a property is worth for the comparison: a scalar as itself, a collection as its
    /// length. Comparing a collection's CONTENTS would mean reaching into child rows, which have their
    /// own two fill paths and belong in their own case.</summary>
    private static object? Comparison(object? value) => value switch
    {
        null => null,
        string text => text,
        IEnumerable items => items.Cast<object?>().Count(),
        _ => value,
    };

    [Test]
    public void AnItemRow_FillsTheSameWayBothWays() => BothPathsAgree<ItemRecord, ItemRowViewModel>(
        r => new ItemRowViewModel(1, r), (row, r) => row.LoadFromRecord(r));

    [Test]
    public void AnNpcRow_FillsTheSameWayBothWays() => BothPathsAgree<NpcRecord, NpcRowViewModel>(
        r => new NpcRowViewModel(1, r), (row, r) => row.LoadFromRecord(r));

    [Test]
    public void AConversationRow_FillsTheSameWayBothWays()
        => BothPathsAgree<ConversationRecord, ConversationRowViewModel>(
            r => new ConversationRowViewModel(1, r, () => []), (row, r) => row.LoadFromRecord(r));

    [Test]
    public void AMapGroupRow_FillsTheSameWayBothWays()
        => BothPathsAgree<MapGroupRecord, MapGroupRowViewModel>(
            r => new MapGroupRowViewModel(1, r, () => []), (row, r) => row.LoadFromRecord(r));

    [Test]
    public void AShopRow_FillsTheSameWayBothWays() => BothPathsAgree<ShopRecord, ShopRowViewModel>(
        r => new ShopRowViewModel(1, r, () => [], () => [], _ => false), (row, r) => row.LoadFromRecord(r));

    /// <summary>Every row view model that has both fill paths is in the list above.
    ///
    /// <para>The test that matters most is the one nobody wrote: a row added later gets neither of
    /// these, and its two paths drift with nothing watching. So the list checks itself.</para></summary>
    [Test]
    public void EveryRowWithTwoFillPaths_IsCovered()
    {
        var covered = new[]
        {
            typeof(ItemRowViewModel), typeof(NpcRowViewModel), typeof(ConversationRowViewModel),
            typeof(MapGroupRowViewModel), typeof(ShopRowViewModel), typeof(SchemaRecordRowViewModel),
        };

        var withBoth = typeof(ItemRowViewModel).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("RowViewModel", StringComparison.Ordinal))
            .Where(t => t.GetMethod("LoadFromRecord") is not null)
            .ToArray();

        Assert.That(withBoth, Is.SubsetOf(covered));
    }
}

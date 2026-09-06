using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections;
using System.Reflection;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// A definition packet carries a record's fields to the client and the editor. Any field the builder
/// forgets arrives as a default, and a reader gated on it renders nothing — no exception, no log line.
///
/// <para>These fill a record with distinctive values, run the builder, and compare every property the
/// packet shares with the record by name. A field added to both without a line in the builder fails here
/// rather than in a tooltip nobody thinks to re-check.</para>
/// </summary>
[TestFixture]
public class DefinitionPacketFieldTests
{
    [Test]
    public void UpdateSpell_CarriesEveryFieldItSharesWithTheRecord()
    {
        var spell = Populated<SpellRecord>();
        var packet = PacketBuilder.UpdateSpell(12, spell);

        Assert.That(packet.SpellNum, Is.EqualTo(12));
        Assert.That(Mismatches(spell, packet), Is.Empty);
    }

    [Test]
    public void UpdateItem_CarriesEveryFieldItSharesWithTheRecord()
    {
        var item = Populated<ItemRecord>();
        var packet = PacketBuilder.UpdateItem(12, item);

        Assert.That(packet.ItemNum, Is.EqualTo(12));
        Assert.That(Mismatches(item, packet), Is.Empty);
    }

    [Test]
    public void UpdateClass_CarriesEveryFieldItSharesWithTheRecord()
    {
        var cls = Populated<ClassRecord>();
        var packet = PacketBuilder.UpdateClass(12, cls);

        Assert.That(packet.ClassNum, Is.EqualTo(12));
        Assert.That(Mismatches(cls, packet), Is.Empty);
    }

    /// <summary><c>Sales</c> is <c>SalesItem</c> on the record, so the name-matched sweep skips it and it
    /// is asserted here — it is also the field the bulk editor list used to omit, which emptied every
    /// shop's sales list on connect.</summary>
    [Test]
    public void UpdateShop_CarriesEveryFieldItSharesWithTheRecord()
    {
        var shop = Populated<ShopRecord>();
        shop.SalesItem = [4, 8, 15];
        var packet = PacketBuilder.UpdateShop(12, shop);

        Assert.That(packet.ShopNum, Is.EqualTo(12));
        Assert.That(packet.Sales, Is.EqualTo(new[] { 4, 8, 15 }));
        Assert.That(Mismatches(shop, packet), Is.Empty);
    }

    /// <summary><c>Size</c> comes from <c>EffectiveSize</c>, not the raw field, so it is asserted against
    /// that rather than through the name-matched sweep.</summary>
    [Test]
    public void UpdateNpc_CarriesEveryFieldItSharesWithTheRecord()
    {
        var npc = Populated<NpcRecord>();
        var packet = PacketBuilder.UpdateNpc(12, npc, keeperShopKind: 2);

        Assert.Multiple(() =>
        {
            Assert.That(packet.NpcNum, Is.EqualTo(12));
            Assert.That(packet.Size, Is.EqualTo(npc.EffectiveSize));
            Assert.That(packet.KeeperShop, Is.EqualTo(2));
            Assert.That(Mismatches(npc, packet, except: ["Size"]), Is.Empty);
        });
    }

    /// <summary>The live-update packet and the bulk join packet describe the same record, so a field on one
    /// and not the other means one of the two paths shows stale data until the player reconnects.</summary>
    [Test]
    public void UpdateAndBulkPackets_DescribeTheSameFields()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SharedNames(typeof(SpellRecord), typeof(Mirage.Shared.Protocol.Packets.UpdateSpellPacket)),
                Is.EquivalentTo(SharedNames(typeof(SpellRecord), typeof(Mirage.Shared.Protocol.Packets.SendSpellsPacket.SpellData))),
                "spell: the update packet and the join packet carry different fields");
            Assert.That(SharedNames(typeof(ItemRecord), typeof(Mirage.Shared.Protocol.Packets.UpdateItemPacket)),
                Is.EquivalentTo(SharedNames(typeof(ItemRecord), typeof(Mirage.Shared.Protocol.Packets.SendItemsPacket.ItemData))),
                "item: the update packet and the join packet carry different fields");
        });
    }

    private static List<string> SharedNames(Type record, Type packet) =>
        [.. packet.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => record.GetProperty(n) is not null)];

    /// <summary>Every property the packet shares with the record, where the two disagree.</summary>
    private static List<string> Mismatches(object record, object packet, string[]? except = null)
    {
        var problems = new List<string>();
        foreach (var pp in packet.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (except is not null && except.Contains(pp.Name)) continue;
            var rp = record.GetType().GetProperty(pp.Name);
            if (rp is null) continue;

            object? expected = rp.GetValue(record), actual = pp.GetValue(packet);
            if (!Same(expected, actual))
                problems.Add($"{pp.Name}: record={Show(expected)} packet={Show(actual)}");
        }
        return problems;
    }

    private static bool Same(object? a, object? b)
    {
        if (a is IEnumerable ea and not string && b is IEnumerable eb and not string)
            return ea.Cast<object>().SequenceEqual(eb.Cast<object>());
        return Equals(a, b);
    }

    private static string Show(object? v) => v switch
    {
        null => "null",
        IEnumerable e and not string => "[" + string.Join(",", e.Cast<object>()) + "]",
        _ => v.ToString() ?? "",
    };

    /// <summary>A record with every settable property set to something no default would produce, so a
    /// field the builder skips reads back as the default and shows up as a mismatch.</summary>
    private static T Populated<T>() where T : new()
    {
        var made = new T();
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite) continue;
            object? value = SampleFor(p.PropertyType);
            if (value is not null) p.SetValue(made, value);
        }
        return made;
    }

    private static object? SampleFor(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;

        if (underlying.IsEnum)
            return Enum.GetValues(underlying).Cast<object>()
                .FirstOrDefault(v => Convert.ToInt64(v) != 0) ?? Enum.GetValues(underlying).GetValue(0);
        if (underlying == typeof(bool)) return true;
        if (underlying == typeof(string)) return "sample";
        if (underlying == typeof(short)) return (short)23;
        if (underlying == typeof(int)) return 23;
        if (underlying == typeof(long)) return 23L;
        if (underlying == typeof(byte)) return (byte)23;
        if (underlying == typeof(List<short>)) return new List<short> { 3, 9 };
        return null;   // arrays, nested records and anything else no definition packet carries
    }
}

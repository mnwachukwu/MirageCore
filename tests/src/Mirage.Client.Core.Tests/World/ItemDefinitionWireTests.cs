using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Linq;
using System.Reflection;

namespace Mirage.Client.Core.Tests.World;

/// <summary>
/// Item definitions surviving the trip onto the client.
///
/// <para>Both handlers rebuild an <c>ItemRecord</c> field by field from the packet. A field the server sends
/// and the handler forgets to assign is invisible: the record is still valid, the client still runs, and the
/// only symptom is a number quietly reading zero somewhere far away — a tooltip line that never renders, a
/// requirement never shown before a purchase. The gate fields below are the ones whose absence is silent, so
/// the reflection sweep at the end holds the whole shape rather than only the ones remembered today.</para>
/// </summary>
[TestFixture]
public class ItemDefinitionWireTests
{
    private static readonly MethodInfo HandleSendItems = typeof(ClientPacketHandler)
        .GetMethod("HandleSendItems", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo HandleUpdateItem = typeof(ClientPacketHandler)
        .GetMethod("HandleUpdateItem", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The handlers touch neither sender nor mapCache, so both are null! (per the client-test convention).
    private static ClientState Apply(MethodInfo handler, object packet)
    {
        var state = new ClientState();
        handler.Invoke(new ClientPacketHandler(state, null!, null!), new[] { packet });
        return state;
    }

    // The class gate carries a real list: the sweep below can only prove a field that was sent as something
    // other than its blank value, so a null here would exempt the one gate whose absence reads as
    // "anybody may wear this" rather than as a zero.
    private static SendItemsPacket.ItemData Sword(short levelReq = 40) => new(
        Num: 7, Name: "Iron Sword", Pic: 3, Type: ItemType.Weapon, Durability: 50, VitalAmount: 0,
        SpellNum: 0, Power: 12, LevelReq: levelReq, AllowedClasses: [2, 3], NonTradeable: false,
        NonListable: false, NonMailable: false, DestroyOnDrop: false, NonJunkable: false, Price: 250);

    /// <summary>The reported bug: gear is level-gated server-side, but the client dropped the number on
    /// receive, so every item read as level 0 and no requirement could be shown before you tried to wear it.</summary>
    [Test]
    public void TheJoinTimeBulk_CarriesTheLevelGate()
    {
        var state = Apply(HandleSendItems, new SendItemsPacket { Items = [Sword()] });

        Assert.That(state.Items[7]!.LevelReq, Is.EqualTo(40));
    }

    [Test]
    public void ALiveEditorSave_CarriesTheLevelGateToo()
    {
        var state = Apply(HandleUpdateItem, new UpdateItemPacket
        {
            ItemNum = 7, Name = "Iron Sword", Pic = 3, Type = ItemType.Weapon,
            Durability = 50, Power = 12, LevelReq = 40, Price = 250,
        });

        Assert.That(state.Items[7]!.LevelReq, Is.EqualTo(40));
    }

    /// <summary>The other gate fields, which fail the same silent way.</summary>
    [Test]
    public void TheGateFieldsAllSurvive()
    {
        var state = Apply(HandleSendItems, new SendItemsPacket { Items = [Sword()] });
        var it = state.Items[7]!;

        Assert.Multiple(() =>
        {
            Assert.That(it.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(it.Power, Is.EqualTo(12), "drives the STR requirement line");
            Assert.That(it.LevelReq, Is.EqualTo(40));
            Assert.That(it.Durability, Is.EqualTo(50));
            Assert.That(it.Price, Is.EqualTo(250));
            Assert.That(it.AllowedClasses, Is.EqualTo(new short[] { 2, 3 }),
                "the class gate drives the tooltip line and the purchase confirm");
        });
    }

    [Test]
    public void ALiveEditorSave_CarriesTheClassGateToo()
    {
        var state = Apply(HandleUpdateItem, new UpdateItemPacket
        {
            ItemNum = 7, Name = "Iron Sword", Pic = 3, Type = ItemType.Weapon,
            Durability = 50, Power = 12, LevelReq = 40, Price = 250, AllowedClasses = [2, 3],
        });

        Assert.That(state.Items[7]!.AllowedClasses, Is.EqualTo(new short[] { 2, 3 }));
    }

    /// <summary>Every property the wire and the record share by name must actually be copied. Written as a
    /// sweep so a field added to both later cannot be left unassigned in the handler and go unnoticed —
    /// which is exactly how the level gate went missing.</summary>
    [Test]
    public void EveryFieldTheWireAndTheRecordShare_IsCopied()
    {
        var wire = Sword();
        var state = Apply(HandleSendItems, new SendItemsPacket { Items = [wire] });
        var record = state.Items[7]!;

        var recordProps = typeof(Mirage.Shared.Records.ItemRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name);

        var missed = new List<string>();
        foreach (var wireProp in wire.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Num addresses the record rather than living in it, and Name is set from a trimmed copy.
            if (wireProp.Name is "Num" or "Name") continue;
            if (!recordProps.TryGetValue(wireProp.Name, out var recordProp)) continue;
            if (recordProp.PropertyType != wireProp.PropertyType) continue;

            object? sent = wireProp.GetValue(wire);
            object? landed = recordProp.GetValue(record);
            // Only a field the fixture gave a non-default value can prove anything.
            object? blank = wireProp.PropertyType.IsValueType ? Activator.CreateInstance(wireProp.PropertyType) : null;
            if (Equals(sent, blank)) continue;
            if (!Equals(sent, landed)) missed.Add($"{wireProp.Name} (sent {sent}, landed {landed})");
        }

        Assert.That(missed, Is.Empty, "the handler builds the record field by field, and these were not assigned");
    }
}

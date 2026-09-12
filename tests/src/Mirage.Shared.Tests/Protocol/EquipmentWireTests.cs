using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The two packets that carry equipment to a client: what slots exist, and what is in them.
///
/// <para>The client is compiled against no slots at all, so both of these have to survive the trip
/// intact or a character sheet renders empty for a game that has one.</para>
/// </summary>
[TestFixture]
public class EquipmentWireTests
{
    // The production path exactly: written as a line by the dispatcher, read back through the registry.
    private static T RoundTrip<T>(T sent) where T : class, IPacket
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<T>(), "the packet did not survive the wire at all");
        return (T)back!;
    }

    [Test]
    public void TheSlotList_SurvivesTheTripInDisplayOrder()
    {
        var slots = new EquipSlotSet(
        [
            new EquipSlot { Key = "offhand", LabelKey = "Game_Slot_Offhand", Ordinal = 1 },
            new EquipSlot { Key = "head", LabelKey = "Game_Slot_Head", Ordinal = 0 },
        ]);

        var back = RoundTrip(PacketBuilder.EquipSlots(slots));

        Assert.Multiple(() =>
        {
            Assert.That(back.Slots.Select(s => s.Key), Is.EqualTo(new[] { "head", "offhand" }));
            Assert.That(back.Slots[0].LabelKey, Is.EqualTo("Game_Slot_Head"), "the caption key the client looks up");
            Assert.That(back.Slots[1].Ordinal, Is.EqualTo(1));
        });
    }

    [Test]
    public void AGameWithNothingWorn_SendsAnEmptyList()
        => Assert.That(RoundTrip(PacketBuilder.EquipSlots(EquipSlotSet.Empty)).Slots, Is.Empty);

    /// <summary>Only the occupied slots travel, so a game with twenty slots and one worn item sends one
    /// entry rather than nineteen zeroes.</summary>
    [Test]
    public void TheWornSet_CarriesOnlyTheOccupiedSlots()
    {
        var p = new PlayerRecord();
        p.SetEquipped("head", 4);
        p.SetEquipped("offhand", 9);

        var back = RoundTrip(PacketBuilder.EquippedGear(3, p));

        Assert.Multiple(() =>
        {
            Assert.That(back.Index, Is.EqualTo(3));
            Assert.That(back.Worn, Has.Length.EqualTo(2));
            Assert.That(back.Worn.Single(e => e.Slot == "head").InvSlot, Is.EqualTo(4));
            Assert.That(back.Worn.Single(e => e.Slot == "offhand").InvSlot, Is.EqualTo(9));
        });
    }

    [Test]
    public void ACharacterWearingNothing_SendsNoEntries()
        => Assert.That(RoundTrip(PacketBuilder.EquippedGear(1, new PlayerRecord())).Worn, Is.Empty);

    // The item's slot key rides both item packets, because the client needs it to say which piece a
    // paper-doll slot would take and the editor needs it to show what an author chose.
    [Test]
    public void AnItemsSlotKey_RidesBothItemPackets()
    {
        var item = new ItemRecord { Name = "Cap", Type = ItemType.Equipment, EquipSlot = "head" };

        var update = RoundTrip(PacketBuilder.UpdateItem(7, item));
        var bulk = RoundTrip(PacketBuilder.SendItems([(7, item)]));

        Assert.Multiple(() =>
        {
            Assert.That(update.EquipSlot, Is.EqualTo("head"));
            Assert.That(bulk.Items.Single().EquipSlot, Is.EqualTo("head"));
        });
    }
}

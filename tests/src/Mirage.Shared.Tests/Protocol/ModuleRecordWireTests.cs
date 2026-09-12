using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// A record of a family neither side was compiled against, crossing the wire.
///
/// <para>Core's own records travel as a property per field, so a dropped one is a compile error on the
/// far side. These carry a bag instead, and nothing in the type system says what belongs in it — which
/// makes the round trip the only thing standing between an author's edit and a silently emptied record.</para>
/// </summary>
[TestFixture]
public class ModuleRecordWireTests
{
    // The production path exactly: written as a line by the dispatcher, read back through the registry.
    private static T RoundTrip<T>(T sent) where T : class, IPacket
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<T>(), "the packet did not survive the wire at all");
        return (T)back!;
    }

    private static AttributeBag Vulpine() => new AttributeBag()
        .Set("name", "Vulpine")
        .Set("baseSpeed", 65)
        .Set("catchRate", 0.45)
        .Set("evolves", true);

    [Test]
    public void ARequest_NamesItsFamilyAndSlot()
    {
        var back = RoundTrip(new EditorRequestRecordPacket { Family = "Species", Num = 7 });

        Assert.Multiple(() =>
        {
            Assert.That(back.Family, Is.EqualTo("Species"));
            Assert.That(back.Num, Is.EqualTo(7));
        });
    }

    [Test]
    public void ABulkRequest_NamesItsFamily()
        => Assert.That(RoundTrip(new EditorRequestAllRecordsPacket { Family = "Species" }).Family,
                       Is.EqualTo("Species"));

    /// <summary>Every value kind a game can put in a record survives, with its kind intact. A number that
    /// came back as text would compare unequal to what the editor sent and mark a clean record dirty.</summary>
    [Test]
    public void EveryValueKind_SurvivesASave()
    {
        var back = RoundTrip(new EditorSaveRecordPacket { Family = "Species", Num = 3, Fields = Vulpine() });

        Assert.Multiple(() =>
        {
            Assert.That(back.Family, Is.EqualTo("Species"));
            Assert.That(back.Num, Is.EqualTo(3));
            Assert.That(back.Fields["name"].Kind, Is.EqualTo(AttributeKind.Text));
            Assert.That(back.Fields["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(back.Fields["baseSpeed"].Kind, Is.EqualTo(AttributeKind.Integer));
            Assert.That(back.Fields["baseSpeed"].AsLong(), Is.EqualTo(65));
            Assert.That(back.Fields["catchRate"].Kind, Is.EqualTo(AttributeKind.Real));
            Assert.That(back.Fields["catchRate"].AsDouble(), Is.EqualTo(0.45).Within(1e-9));
            Assert.That(back.Fields["evolves"].Kind, Is.EqualTo(AttributeKind.Flag));
            Assert.That(back.Fields["evolves"].AsBool(), Is.True);
        });
    }

    /// <summary>The bag compares by value, which is how the editor decides a record is dirty. A round
    /// trip that changed nothing has to come back equal, or every record an author opened reads as
    /// edited.</summary>
    [Test]
    public void ARoundTrippedBag_EqualsTheOneThatWasSent()
        => Assert.That(RoundTrip(new EditorSaveRecordPacket { Fields = Vulpine() }).Fields, Is.EqualTo(Vulpine()));

    [Test]
    public void AnEmptyRecord_SurvivesAsEmpty()
        => Assert.That(RoundTrip(new UpdateRecordPacket { Family = "Species", Num = 1 }).Fields.IsEmpty, Is.True);

    [Test]
    public void TheBulkReply_CarriesEverySlotWithItsNumber()
    {
        var back = RoundTrip(new EditorAllRecordsPacket
        {
            Family = "Species",
            Records =
            [
                new EditorAllRecordsPacket.Entry(1, Vulpine()),
                new EditorAllRecordsPacket.Entry(2, new AttributeBag()),
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(back.Family, Is.EqualTo("Species"));
            Assert.That(back.Records.Select(r => r.Num), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(back.Records[0].Fields["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(back.Records[1].Fields.IsEmpty, Is.True);
        });
    }
}

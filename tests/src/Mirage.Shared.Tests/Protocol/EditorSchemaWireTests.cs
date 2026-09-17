using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The record schema crossing the wire on the editor's login response.
///
/// <para>This is how an editor learns about a family it was not compiled against, so every part of a
/// family that the editor acts on has to survive the trip. A field silently defaulting here presents as
/// a family that loads from the wrong folder or refuses to save.</para>
/// </summary>
[TestFixture]
public class EditorSchemaWireTests
{
    // The production path exactly: written as a line by the dispatcher, read back through the registry.
    // RecordJson is for records on DISK and configures different options, so round-tripping through it
    // would prove something no packet ever does.
    private static EditorLoginResponsePacket RoundTrip(EditorLoginResponsePacket sent)
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<EditorLoginResponsePacket>(), "the response did not survive the wire at all");
        return (EditorLoginResponsePacket)back!;
    }

    [Test]
    public void CoresOwnSchema_SurvivesTheTrip()
    {
        var back = RoundTrip(new EditorLoginResponsePacket { Success = true, Schema = CoreRegistry.CoreOnly.Schema });

        Assert.That(back.Schema.Families.Select(f => f.Id),
                    Is.EqualTo(CoreRecordFamilies.World.Select(f => f.Id)));
    }

    // Every one of these drives behavior on the editor side: the folder it reads, the file each record is
    // written to, how many slots it shows, and whether it offers the family a section at all.
    [Test]
    public void AFamilysActionableFields_AllSurvive()
    {
        var sent = new RecordSchema
        {
            Families =
            [
                new RecordFamily
                {
                    Id = "Species",
                    Directory = "species",
                    FilePrefix = "mon",
                    LabelKey = "Pocket_Section_Species",
                    SingularLabelKey = "Pocket_Species_One",
                    DefaultLimit = 386,
                    LimitIsFixed = true,
                    LoadsIndividually = true,
                    Authorable = false,
                    KindFieldKey = "kind",
                    NameFieldKey = "speciesName",
                },
            ],
        };

        var family = RoundTrip(new EditorLoginResponsePacket { Schema = sent }).Schema.Family("Species")!;

        Assert.Multiple(() =>
        {
            Assert.That(family.Directory, Is.EqualTo("species"));
            Assert.That(family.FilePrefix, Is.EqualTo("mon"));
            Assert.That(family.LabelKey, Is.EqualTo("Pocket_Section_Species"));
            Assert.That(family.SingularLabelKey, Is.EqualTo("Pocket_Species_One"));
            Assert.That(family.DefaultLimit, Is.EqualTo(386));
            Assert.That(family.LimitIsFixed, Is.True);
            Assert.That(family.LoadsIndividually, Is.True);
            Assert.That(family.Authorable, Is.False);
            Assert.That(family.KindFieldKey, Is.EqualTo("kind"));
            Assert.That(family.NameFieldKey, Is.EqualTo("speciesName"));
            Assert.That(family.FileNameFor(7), Is.EqualTo("mon7.json"), "the derived name follows the prefix that traveled");
        });
    }

    [Test]
    public void ChoiceSetsTravelWithTheFamiliesThatDrawOnThem()
    {
        var sent = new RecordSchema
        {
            Families = [new RecordFamily { Id = "Species", Directory = "species" }],
            ChoiceSets = [new ChoiceSet { Id = "types", Members = [new KindDescriptor { Id = "fire" }] }],
        };

        var back = RoundTrip(new EditorLoginResponsePacket { Schema = sent }).Schema;

        Assert.That(back.Choices("types")?.Find("fire"), Is.Not.Null);
    }

    /// <summary>A refused login carries no schema, and an editor reading one must not see a world with no
    /// families, so the receiving side treats empty as "said nothing" rather than as an answer.</summary>
    [Test]
    public void ARefusedLogin_CarriesAnEmptySchemaRatherThanNull()
    {
        var back = RoundTrip(new EditorLoginResponsePacket { Success = false, Message = "no" });

        Assert.That(back.Schema, Is.Not.Null);
        Assert.That(back.Schema.Families, Is.Empty);
    }

    // An older server does not write the key at all, which has to read as "nothing said" rather than throw.
    [Test]
    public void AResponseWithNoSchemaKey_ReadsAsEmpty()
    {
        string json = """{ "cmd": "editorloginresp", "success": true, "message": "ok", "session": "abc" }""";

        var back = (EditorLoginResponsePacket)PacketSerializer.TryDeserialize(json)!;

        Assert.That(back.Schema.Families, Is.Empty);
        Assert.That(back.SessionId, Is.EqualTo("abc"));
    }

    [Test]
    public void TheResponseCarriesItsCommandAndDecodesThroughTheRegistry()
    {
        string line = PacketSerializer.Serialize(
            new EditorLoginResponsePacket { Success = true, Schema = CoreRegistry.CoreOnly.Schema });

        Assert.That(line, Does.Contain(PacketNames.EditorLoginResponse));
        Assert.That(((EditorLoginResponsePacket)PacketSerializer.TryDeserialize(line)!).Schema.Families, Is.Not.Empty);
    }
}

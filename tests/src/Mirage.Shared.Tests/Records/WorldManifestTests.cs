using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Linq;
using System.Text.Json;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// A world's manifest says only what the stock answers do not.
///
/// <para>Every setting has a default and a folder with no file runs on all of them, so a key repeating its
/// default states nothing. What an operator has actually chosen should be the whole content of the file
/// rather than three lines buried in forty.</para>
/// </summary>
[TestFixture]
public class WorldManifestTests
{
    private static string Write(WorldManifest m) => JsonSerializer.Serialize(m, RecordJson.Options);
    private static WorldManifest Read(string json) => JsonSerializer.Deserialize<WorldManifest>(json, RecordJson.Options)!;

    [Test]
    public void AWorldThatChoosesNothing_WritesAnEmptyObject()
    {
        Assert.That(Write(new WorldManifest()).Replace(" ", "").Replace("\r", "").Replace("\n", ""),
                    Is.EqualTo("{}"));
    }

    [Test]
    public void AWorldWithOnlyAName_WritesOnlyThatName()
    {
        string json = Write(new WorldManifest { Name = "Demo Landia" });

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("Demo Landia"));
            Assert.That(json, Does.Not.Contain("records"));
            Assert.That(json, Does.Not.Contain("defaultMapSize"));
        });
    }

    [Test]
    public void ASettingThatDiffers_IsWritten()
    {
        string size = Write(new WorldManifest { DefaultMapSize = new MapSize(24, 20) });
        string limits = Write(new WorldManifest { Records = new RecordLimits { Items = 2000 } });

        Assert.Multiple(() =>
        {
            Assert.That(size, Does.Contain("defaultMapSize").And.Not.Contain("records"));
            Assert.That(limits, Does.Contain("records").And.Not.Contain("defaultMapSize"));
        });
    }

    /// <summary>A manifest whose every setting differs from stock, written and read back.
    ///
    /// <para>🔴 The converter is hand-written, so a setting added to the record without a matching arm in
    /// BOTH halves of it is silently dropped on save and silently defaulted on load. Nothing about that
    /// fails, warns, or looks wrong until an author notices their work has gone. This test and the coverage
    /// check below it are the only things that catch it, which is why the check exists rather than trusting
    /// this list to be kept current by hand.</para></summary>
    [Test]
    public void EverySetting_SurvivesARoundTrip()
    {
        var back = Read(Write(FullyAuthored()));

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Demo Landia"));
            Assert.That(back.DefaultMapSize, Is.EqualTo(new MapSize(24, 20)));
            Assert.That(back.Records.Items, Is.EqualTo(2000));
            Assert.That(back.Records.Maps, Is.EqualTo(300));
            Assert.That(back.Records.Npcs, Is.EqualTo(RecordLimits.Default.Npcs), "an untouched family keeps its default");
            Assert.That(back.Appearances, Has.Count.EqualTo(2));
            Assert.That(back.Appearances[0].Name, Is.EqualTo("Villager"));
            Assert.That(back.Appearances[1].Sprite, Is.EqualTo(17));
            Assert.That(back.Appearances[1].SpriteSheet, Is.EqualTo(2));
            Assert.That(back.StartingItems, Has.Count.EqualTo(2));
            Assert.That(back.StartingItems[0].ItemNum, Is.EqualTo(4));
            Assert.That(back.StartingItems[1].Quantity, Is.EqualTo((short)75));
            Assert.That(back.DecalColor, Is.EqualTo(0x1A3C0Bu));
            Assert.That(back.Families.Single().Id, Is.EqualTo("Species"));
            Assert.That(back.Families.Single().DefaultLimit, Is.EqualTo(386));
            Assert.That(back.ChoiceSets.Single().Find("fire"), Is.Not.Null);
            Assert.That(back.EquipSlots.Single().Key, Is.EqualTo("paw"));
            Assert.That(back.EquipSlots.Single().Ordinal, Is.EqualTo(2));
        });
    }

    /// <summary>Every setting the manifest declares must actually differ from stock in the fixture above —
    /// otherwise a property could be added, left out of the converter, and still "pass" a round trip that
    /// never set it in the first place.</summary>
    [Test]
    public void TheRoundTripFixture_CoversEverySettingTheManifestDeclares()
    {
        var stock = new WorldManifest();
        var authored = FullyAuthored();

        var uncovered = typeof(WorldManifest).GetProperties()
            .Where(pi => pi.CanRead && pi.GetIndexParameters().Length == 0)
            .Where(pi => pi.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length == 0)
            .Where(pi => pi.SetMethod is not null)
            .Where(pi => Equals(Describe(pi.GetValue(authored)), Describe(pi.GetValue(stock))))
            .Select(pi => pi.Name)
            .ToList();

        Assert.That(uncovered, Is.Empty,
            "these settings are left at their default by the round-trip fixture, so it proves nothing about them");
    }

    // Value equality for the comparison above: a list has to be compared by content, not by reference.
    private static string Describe(object? v) => v switch
    {
        null => "<null>",
        System.Collections.IEnumerable e and not string => string.Join("|", e.Cast<object>().Select(x => x?.ToString())),
        _ => v.ToString() ?? "",
    };

    private static WorldManifest FullyAuthored() => new()
    {
        Name = "Demo Landia",
        DefaultMapSize = new MapSize(24, 20),
        Records = new RecordLimits { Items = 2000, Maps = 300 },
        Appearances =
        [
            new CharacterAppearance { Name = "Villager", Sprite = 3, SpriteSheet = 0 },
            new CharacterAppearance { Name = "Sailor", Sprite = 17, SpriteSheet = 2 },
        ],
        StartingItems = [new StartingItem { ItemNum = 4 }, new StartingItem { ItemNum = 1, Quantity = 75 }],
        DecalColor = 0x1A3C0B,
        Families = [new RecordFamily { Id = "Species", Directory = "species", DefaultLimit = 386 }],
        ChoiceSets = [new ChoiceSet { Id = "types", Members = [new KindDescriptor { Id = "fire" }] }],
        EquipSlots = [new EquipSlot { Key = "paw", LabelKey = "Demo_Slot_Paw", Ordinal = 2 }],
    };

    /// <summary>An absent key has to mean what an absent FILE means, or a partial manifest would answer
    /// differently from no manifest at all.</summary>
    [Test]
    public void AnAbsentKey_MeansTheSameAsAnAbsentFile()
    {
        var fromPartial = Read("""{ "name": "Demo Landia" }""");
        var fromNothing = new WorldManifest();

        Assert.Multiple(() =>
        {
            Assert.That(fromPartial.DefaultMapSize, Is.EqualTo(fromNothing.DefaultMapSize));
            Assert.That(fromPartial.Records, Is.EqualTo(fromNothing.Records));
        });
    }

    [Test]
    public void AnEmptyObject_ReadsAsEveryDefault()
    {
        Assert.That(Read("{}"), Is.EqualTo(new WorldManifest()));
    }

    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("Demo Landia", true)]
    public void AWorldKnows_WhetherItIsNamed(string name, bool named)
    {
        Assert.That(new WorldManifest { Name = name }.IsNamed, Is.EqualTo(named));
    }

    /// <summary>The stain color is written for a human to read and edit, and read back in whichever of the
    /// three notations a file happens to carry.</summary>
    [Test]
    public void TheStainColor_IsWrittenAsHexAndReadInEitherNotation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Write(new WorldManifest { DecalColor = 0x1A3C0B }), Does.Contain("#1A3C0B"));
            Assert.That(Read("""{ "decalColor": "#1A3C0B" }""").DecalColor, Is.EqualTo(0x1A3C0Bu));
            Assert.That(Read("""{ "decalColor": "1A3C0B" }""").DecalColor, Is.EqualTo(0x1A3C0Bu));
            Assert.That(Read("""{ "decalColor": 1719307 }""").DecalColor, Is.EqualTo(0x1A3C0Bu));
            Assert.That(Read("""{ "decalColor": "puce" }""").DecalColor, Is.EqualTo(new WorldManifest().DecalColor),
                        "a color nobody can parse leaves the world on its default rather than turning it black");
        });
    }

    /// <summary>A world folder is opened by editors that do not have the game that wrote it, so what
    /// families it holds has to be in the folder. Core's are always there; the manifest carries the
    /// rest.</summary>
    [Test]
    public void TheSchema_IsCoresFamiliesThenTheWorldsOwn()
    {
        var world = new WorldManifest
        {
            Families = [new RecordFamily { Id = "Species", Directory = "species" }],
        };

        var schema = world.Schema;

        Assert.Multiple(() =>
        {
            Assert.That(schema.Family(CoreRecordFamilies.Items), Is.Not.Null, "Core's are always present");
            Assert.That(schema.Family("Species"), Is.Not.Null);
            Assert.That(schema.Families[^1].Id, Is.EqualTo("Species"), "the world's own follow Core's");
        });
    }

    [Test]
    public void AWorldWithNoFamiliesOfItsOwn_StillHasCores()
    {
        Assert.That(new WorldManifest().Schema.Families.Select(f => f.Id),
                    Is.EqualTo(CoreRecordFamilies.World.Select(f => f.Id)));
    }

        /// <summary>A hand-edited file cannot ask for a zero-width map or an allocation measured in gigabytes,
    /// and that clamp has to survive the converter.</summary>
    [Test]
    public void AHandEditedFile_IsStillClamped()
    {
        var m = Read("""{ "defaultMapSize": { "width": 0, "height": 999999 }, "records": { "items": 0 } }""");

        Assert.Multiple(() =>
        {
            Assert.That(m.DefaultMapSize.Width, Is.EqualTo(1));
            Assert.That(m.DefaultMapSize.Height, Is.EqualTo(MapSize.HardMax));
            Assert.That(m.Records.Items, Is.EqualTo(1));
        });
    }
}

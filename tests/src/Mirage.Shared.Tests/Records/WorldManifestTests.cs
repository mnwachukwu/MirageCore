using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
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

    [Test]
    public void EverySetting_SurvivesARoundTrip()
    {
        var original = new WorldManifest
        {
            Name = "Demo Landia",
            DefaultMapSize = new MapSize(24, 20),
            Records = new RecordLimits { Items = 2000, Maps = 300 },
        };

        var back = Read(Write(original));

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Demo Landia"));
            Assert.That(back.DefaultMapSize, Is.EqualTo(new MapSize(24, 20)));
            Assert.That(back.Records.Items, Is.EqualTo(2000));
            Assert.That(back.Records.Maps, Is.EqualTo(300));
            Assert.That(back.Records.Npcs, Is.EqualTo(RecordLimits.Default.Npcs), "an untouched family keeps its default");
        });
    }

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

using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The bag as it sits on a body: persisted with the character, authored on the NPC template, and kept
/// separately by each running copy.
/// </summary>
[TestFixture]
public class EntityAttributeBagTests
{
    private static string Write<T>(T value) => JsonSerializer.Serialize(value, RecordJson.Options);
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, RecordJson.Options)!;

    [Test]
    public void ACharactersAttributes_SurviveASaveAndLoad()
    {
        var p = new PlayerRecord { Name = "Matt" };
        p.Attributes.Set("gold", 250).Set("species", "ember").Set("shiny", true).Set("weight", 61.5);

        var back = Read<PlayerRecord>(Write(p));

        Assert.Multiple(() =>
        {
            Assert.That(back.Attributes["gold"].AsLong(), Is.EqualTo(250));
            Assert.That(back.Attributes["species"].AsText(), Is.EqualTo("ember"));
            Assert.That(back.Attributes["shiny"].AsBool(), Is.True);
            Assert.That(back.Attributes["weight"].AsDouble(), Is.EqualTo(61.5));
            Assert.That(back.Attributes["gold"].Kind, Is.EqualTo(AttributeKind.Integer),
                "a whole number must come back whole, or a game reading AsLong gets a truncation");
        });
    }

    /// <summary>An empty bag writes no key at all. Every record has one and almost none are used, so a
    /// bag that wrote `"attributes": {}` would add a line to every file in a world for nothing.</summary>
    [Test]
    public void AnEmptyBag_IsNotWrittenAtAll()
    {
        Assert.That(Write(new NpcRecord { Name = "Rat" }), Does.Not.Contain("attributes"));
    }

    /// <summary>🔴 The character save runs off the game thread against a snapshot while the game thread
    /// keeps playing. A bag shared between the two would be written mid-change — and the symptom is a
    /// save that is occasionally, unreproducibly wrong rather than an error.</summary>
    [Test]
    public void TheSaveSnapshot_DoesNotShareTheBagWithTheLivePlayer()
    {
        var live = new PlayerRecord { Name = "Matt" };
        live.Attributes.Set("gold", 250);

        var snapshot = live.Clone();
        live.Attributes.Set("gold", 0);

        Assert.That(snapshot.Attributes["gold"].AsLong(), Is.EqualTo(250));
    }

    /// <summary>A template's values are what every copy STARTS with, not what they share. One wolf
    /// taking damage must not wound the species.</summary>
    [Test]
    public void ARunningCopy_DoesNotShareTheTemplatesBag()
    {
        var template = new NpcRecord { Name = "Wolf" };
        template.Attributes.Set("hp", 40);

        var copy = new MapNpcRecord { Attributes = template.Attributes.Clone() };
        copy.Attributes.Set("hp", 3);

        Assert.That(template.Attributes["hp"].AsLong(), Is.EqualTo(40));
    }

    /// <summary>A hand-edited file naming a value the bag cannot hold loses that key and keeps the
    /// record. Core never reads these, so nothing it does should be able to fail a boot over one.</summary>
    [Test]
    public void AValueTheBagCannotHold_LosesTheKeyRatherThanTheRecord()
    {
        var back = Read<NpcRecord>("""{"name":"Rat","attributes":{"good":1,"bad":[1,2],"alsoGood":"x"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Rat"));
            Assert.That(back.Attributes["good"].AsLong(), Is.EqualTo(1));
            Assert.That(back.Attributes["alsoGood"].AsText(), Is.EqualTo("x"));
            Assert.That(back.Attributes.Has("bad"), Is.False);
        });
    }
}

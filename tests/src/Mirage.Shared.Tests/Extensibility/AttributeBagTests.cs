using Mirage.Shared.Extensibility;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The bag is the type the most seams name, and three of its properties are load-bearing somewhere
/// that would fail quietly without them: it compares by value (or the editor marks every loaded record
/// unsaved), it round-trips through the record serializer (or authored game data does not survive a
/// save), and a value it cannot represent costs one key rather than the file.
/// </summary>
[TestFixture]
public class AttributeBagTests
{
    [Test]
    public void AnAbsentKeyReadsAsZeroRatherThanThrowing()
    {
        var bag = new AttributeBag();

        Assert.Multiple(() =>
        {
            Assert.That(bag["nothing"].AsLong(), Is.Zero);
            Assert.That(bag.Has("nothing"), Is.False);
            Assert.That(bag.IsEmpty, Is.True);
        });
    }

    [Test]
    public void KeysAreCaseSensitive()
    {
        var bag = new AttributeBag().Set("hp", 10).Set("Hp", 20);

        Assert.Multiple(() =>
        {
            Assert.That(bag.Count, Is.EqualTo(2));
            Assert.That(bag["hp"].AsLong(), Is.EqualTo(10));
            Assert.That(bag["Hp"].AsLong(), Is.EqualTo(20));
        });
    }

    [Test]
    public void TwoBagsWithTheSameEntriesAreEqualWhateverOrderTheyWereBuiltIn()
    {
        var first = new AttributeBag().Set("a", 1).Set("b", "two").Set("c", true);
        var second = new AttributeBag().Set("c", true).Set("b", "two").Set("a", 1);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first == second, Is.True);
        });
    }

    [Test]
    public void ABagThatDiffersInOneValueIsNotEqual()
    {
        var first = new AttributeBag().Set("a", 1);
        var second = new AttributeBag().Set("a", 2);

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void ACloneIsIndependentOfWhatItWasClonedFrom()
    {
        var original = new AttributeBag().Set("a", 1);
        var copy = original.Clone();

        copy.Set("a", 99);

        Assert.Multiple(() =>
        {
            Assert.That(original["a"].AsLong(), Is.EqualTo(1));
            Assert.That(copy["a"].AsLong(), Is.EqualTo(99));
        });
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    [Test]
    public void ItRoundTripsThroughTheRecordSerializer()
    {
        var bag = new AttributeBag()
            .Set("whole", 45)
            .Set("fraction", 1.5)
            .Set("flag", true)
            .Set("word", "ember");

        string json = JsonSerializer.Serialize(bag, RecordJson.Options);
        var back = JsonSerializer.Deserialize<AttributeBag>(json, RecordJson.Options);

        Assert.That(back, Is.EqualTo(bag));
    }

    [Test]
    public void ItWritesPlainJsonValuesSoAnAuthoredFileStaysReadable()
    {
        var bag = new AttributeBag().Set("baseHp", 45).Set("shiny", false).Set("species", "ember");

        string json = JsonSerializer.Serialize(bag, RecordJson.Options);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"baseHp\": 45"));
            Assert.That(json, Does.Contain("\"shiny\": false"));
            Assert.That(json, Does.Contain("\"species\": \"ember\""));
            Assert.That(json, Does.Not.Contain("kind"));
        });
    }

    [Test]
    public void KeysAreWrittenSortedSoTheSameBagProducesTheSameBytes()
    {
        var forwards = new AttributeBag().Set("a", 1).Set("b", 2).Set("c", 3);
        var backwards = new AttributeBag().Set("c", 3).Set("b", 2).Set("a", 1);

        Assert.That(JsonSerializer.Serialize(forwards, RecordJson.Options),
                    Is.EqualTo(JsonSerializer.Serialize(backwards, RecordJson.Options)));
    }

    [Test]
    public void AWholeNumberComesBackWhole()
    {
        var back = JsonSerializer.Deserialize<AttributeBag>("""{"n": 45}""", RecordJson.Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(back["n"].Kind, Is.EqualTo(AttributeKind.Integer));
            Assert.That(back["n"].AsLong(), Is.EqualTo(45));
        });
    }

    [Test]
    public void ANumberWithAFractionComesBackReal()
    {
        var back = JsonSerializer.Deserialize<AttributeBag>("""{"n": 1.5}""", RecordJson.Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(back["n"].Kind, Is.EqualTo(AttributeKind.Real));
            Assert.That(back["n"].AsDouble(), Is.EqualTo(1.5));
        });
    }

    /// <summary>The startup loaders have no guard around them, so a value this type cannot hold must
    /// cost that one key rather than the record — and with it, the boot.
    ///
    /// <para>An ARRAY is not one of those: a bag holds a set. What it still cannot hold is a nested
    /// object or a null.</para></summary>
    [Test]
    public void AValueTheBagCannotHoldIsSkippedAndTheRestOfTheObjectSurvives()
    {
        const string json = """
            { "before": 1, "nested": { "x": 1 }, "list": [1, 2], "nothing": null, "after": 2 }
            """;

        var bag = JsonSerializer.Deserialize<AttributeBag>(json, RecordJson.Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(bag["before"].AsLong(), Is.EqualTo(1));
            Assert.That(bag["after"].AsLong(), Is.EqualTo(2));
            Assert.That(bag.Has("nested"), Is.False);
            Assert.That(bag.Has("nothing"), Is.False);
            Assert.That(bag["list"].AsLongs(), Is.EqualTo(new long[] { 1, 2 }), "an array is a set");
            Assert.That(bag.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void ABagThatIsNotAnObjectIsRefused()
    {
        Assert.That(() => JsonSerializer.Deserialize<AttributeBag>("[1,2]", RecordJson.Options),
                    Throws.InstanceOf<JsonException>());
    }
}

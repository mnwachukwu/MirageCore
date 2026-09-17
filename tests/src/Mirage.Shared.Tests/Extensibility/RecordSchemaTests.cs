using Mirage.Shared.Extensibility;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The schema is how a server describes a world the editor was not compiled against, so
/// the property that matters most is that it survives the trip: every descriptor has to round-trip
/// through the serializer with nothing silently dropped.
/// </summary>
[TestFixture]
public class RecordSchemaTests
{
    private static RecordFamily Items() => new()
    {
        Id = "items",
        LabelKey = "family.items",
        SingularLabelKey = "family.item",
        DefaultLimit = 1000,
        KindFieldKey = "type",
        Fields =
        [
            new FieldDescriptor { Key = "name", LabelKey = "field.name", Kind = FieldKind.Text, MaxLength = 40 },
            new FieldDescriptor { Key = "type", LabelKey = "field.type", Kind = FieldKind.Choice, ChoiceSetId = "itemTypes" },
        ],
    };

    [Test]
    public void AFamilyNamesItsFilesFromItsIdWhenItGivesNoPrefix()
    {
        var family = Items();

        Assert.Multiple(() =>
        {
            Assert.That(family.EffectiveFilePrefix, Is.EqualTo("item"));
            Assert.That(family.FileNameFor(214), Is.EqualTo("item214.json"));
        });
    }

    [Test]
    public void AnExplicitPrefixWins()
    {
        var family = Items() with { Id = "creatures", FilePrefix = "mon" };

        Assert.That(family.FileNameFor(3), Is.EqualTo("mon3.json"));
    }

    [Test]
    public void AnIdThatIsAlreadySingularIsLeftAlone()
    {
        var family = Items() with { Id = "terrain", FilePrefix = "" };

        Assert.That(family.EffectiveFilePrefix, Is.EqualTo("terrain"));
    }

    [Test]
    public void AFieldIsFoundByKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Items().FindField("name")?.Kind, Is.EqualTo(FieldKind.Text));
            Assert.That(Items().FindField("nothing"), Is.Null);
        });
    }

    /// <summary>Choosing a kind brings its extra rows into the form, so a field that belongs to one
    /// kind has to be findable through the set that kind lives in.</summary>
    [Test]
    public void AFieldBelongingToOneKindIsFoundThroughItsChoiceSet()
    {
        var kinds = new ChoiceSet
        {
            Id = "itemTypes",
            Members =
            [
                new KindDescriptor
                {
                    Id = "currency",
                    LabelKey = "itemType.currency",
                    Fields = [new FieldDescriptor { Key = "denomination", LabelKey = "field.denomination", Kind = FieldKind.Integer }],
                },
            ],
        };

        Assert.Multiple(() =>
        {
            Assert.That(Items().FindField("denomination"), Is.Null, "not one of the family's own fields");
            Assert.That(Items().FindField("denomination", kinds)?.Kind, Is.EqualTo(FieldKind.Integer));
            Assert.That(kinds.Find("currency"), Is.Not.Null);
            Assert.That(kinds.Find("absent"), Is.Null);
        });
    }

    [Test]
    public void AWholeSchemaRoundTripsThroughTheSerializer()
    {
        var schema = new RecordSchema
        {
            Families = [Items()],
            ChoiceSets =
            [
                new ChoiceSet
                {
                    Id = "itemTypes",
                    Members = [new KindDescriptor { Id = "currency", LabelKey = "itemType.currency" }],
                },
            ],
        };

        string json = JsonSerializer.Serialize(schema, RecordJson.Options);
        var back = JsonSerializer.Deserialize<RecordSchema>(json, RecordJson.Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Family("items")?.LabelKey, Is.EqualTo("family.items"));
            Assert.That(back.Family("items")?.Fields, Has.Count.EqualTo(2));
            Assert.That(back.Family("items")?.KindFieldKey, Is.EqualTo("type"));
            Assert.That(back.Choices("itemTypes")?.Members, Has.Count.EqualTo(1));
            Assert.That(back.Family("absent"), Is.Null);
            Assert.That(back.Choices(null), Is.Null);
        });
    }

    [Test]
    public void AnEmptySchemaDescribesAWorldWithNoFamilies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RecordSchema.Empty.Families, Is.Empty);
            Assert.That(RecordSchema.Empty.Family("items"), Is.Null);
        });
    }

    [Test]
    public void BoundsClampOnlyWhereTheyWereGiven()
    {
        var bounded = new FieldDescriptor { Key = "n", Kind = FieldKind.Integer, Min = 1, Max = 10 };
        var unbounded = new FieldDescriptor { Key = "n", Kind = FieldKind.Integer };
        var text = new FieldDescriptor { Key = "s", Kind = FieldKind.Text, Min = 1, Max = 10 };

        Assert.Multiple(() =>
        {
            Assert.That(bounded.IsBounded, Is.True);
            Assert.That(bounded.Clamp(50), Is.EqualTo(10));
            Assert.That(bounded.Clamp(-50), Is.EqualTo(1));
            Assert.That(bounded.Clamp(5), Is.EqualTo(5));

            Assert.That(unbounded.IsBounded, Is.False);
            Assert.That(unbounded.Clamp(50), Is.EqualTo(50));

            Assert.That(text.Clamp(50), Is.EqualTo(50), "bounds mean nothing to a non-numeric field");
        });
    }
}

[TestFixture]
public class DisplayProjectionTests
{
    [Test]
    public void AMeterReportsHowFullItIs()
    {
        var row = DisplayRow.OfMeter("hud.hp", 30, 120, GameColor.BrightRed);

        Assert.Multiple(() =>
        {
            Assert.That(row.Style, Is.EqualTo(DisplayStyle.Meter));
            Assert.That(row.Fill, Is.EqualTo(0.25));
            Assert.That(row.Text, Is.EqualTo("30/120"));
        });
    }

    /// <summary>A game may well hand over a maximum of zero — a value nobody has earned yet. Drawing an
    /// empty bar is the only answer that is not a divide by zero.</summary>
    [Test]
    public void AMeterWithNothingToFillIsEmptyRatherThanUndefined()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DisplayRow.OfMeter("x", 5, 0, 0).Fill, Is.Zero);
            Assert.That(DisplayRow.OfMeter("x", 5, -1, 0).Fill, Is.Zero);
        });
    }

    [Test]
    public void AMeterCannotOverflowItsBar()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DisplayRow.OfMeter("x", 500, 100, 0).Fill, Is.EqualTo(1));
            Assert.That(DisplayRow.OfMeter("x", -5, 100, 0).Fill, Is.Zero);
        });
    }

    [Test]
    public void ARowWithNothingToSayIsStillARow()
    {
        var heading = DisplayRow.OfHeading("hud.section");

        Assert.Multiple(() =>
        {
            Assert.That(heading.Style, Is.EqualTo(DisplayStyle.Heading));
            Assert.That(heading.Text, Is.Empty);
            Assert.That(heading.LabelKey, Is.EqualTo("hud.section"));
        });
    }

    [Test]
    public void ABadgeCarriesTextWithoutACaption()
    {
        var badge = DisplayRow.OfBadge("asleep", GameColor.White);

        Assert.Multiple(() =>
        {
            Assert.That(badge.LabelKey, Is.Null);
            Assert.That(badge.Text, Is.EqualTo("asleep"));
        });
    }

    [Test]
    public void AProjectionWithNoRowsIsEmptyRatherThanNull()
    {
        var nothing = DisplayProjection.Nothing("hud");

        Assert.Multiple(() =>
        {
            Assert.That(nothing.IsEmpty, Is.True);
            Assert.That(nothing.Rows, Is.Not.Null);
            Assert.That(nothing.Surface, Is.EqualTo("hud"));
        });
    }
}

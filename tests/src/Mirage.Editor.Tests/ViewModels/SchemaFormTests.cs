using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// A form the editor built from a schema rather than one it was compiled with.
///
/// <para>This is how a game that declares its records gets an authoring page without shipping a view, so
/// what these pin is that a descriptor is enough: the right control, the declared bounds, the kind field
/// swapping the rows under it, and every edit landing in the record.</para>
/// </summary>
[TestFixture]
public class SchemaFormTests
{
    [OneTimeSetUp]
    public void LoadStrings() => EditorStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));

    private static FieldDescriptor Field(string key, FieldKind kind = FieldKind.Text) =>
        new() { Key = key, Kind = kind, LabelKey = $"Pocket_{key}" };

    private static SchemaFormViewModel Form(RecordFamily family, AttributeBag? record = null,
                                            RecordSchema? schema = null,
                                            Func<string, IReadOnlyList<NamedEntry>>? refs = null)
        => new(family, record ?? new AttributeBag(), schema ?? new RecordSchema { Families = [family] }, refs);

    // ── The rows a descriptor produces ────────────────────────────────────────

    [Test]
    public void EveryDeclaredField_BecomesARowInOrder()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [Field("name"), Field("baseHp", FieldKind.Integer), Field("catchable", FieldKind.Flag)],
        };

        Assert.That(Form(family).Fields.Select(f => f.Key), Is.EqualTo(new[] { "name", "baseHp", "catchable" }));
    }

    [Test]
    public void AFieldsKind_PicksExactlyOneControl()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields =
            [
                Field("n", FieldKind.Integer), Field("r", FieldKind.Real), Field("f", FieldKind.Flag),
                Field("t", FieldKind.Text), Field("c", FieldKind.Choice), Field("x", FieldKind.RecordRef),
            ],
        };

        Assert.Multiple(() =>
        {
            foreach (var row in Form(family).Fields)
            {
                bool[] flags = [row.IsInteger, row.IsReal, row.IsFlag, row.IsText, row.IsChoice, row.IsRecordRef];
                Assert.That(flags.Count(x => x), Is.EqualTo(1), $"{row.Key} lit {flags.Count(x => x)} controls");
            }
        });
    }

    /// <summary>🔴 A module's label is in no language file this build ships, and never can be — so it
    /// is shown AS WRITTEN rather than replaced by the field's id.
    ///
    /// <para>This used to fall back to the id, and the first module ever connected to a running editor
    /// made that visible: Survey declares "Common name", "Habitat" and "Field notes", and the form drew
    /// "name", "habitat", "notes". The captions were declared, they travelled, and the editor threw them
    /// away at the last step.</para>
    ///
    /// <para>It is the bargain the whole engine already takes — the client shows a game's caption as
    /// written too. A game whose players have its language ships loc keys and gets translations, because
    /// the lookup is still tried first.</para></summary>
    [Test]
    public void AModulesLabel_IsShownAsWritten()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields =
            [
                new FieldDescriptor { Key = "name", Kind = FieldKind.Text, LabelKey = "Common name" },
                new FieldDescriptor { Key = "baseHp", Kind = FieldKind.Integer },
            ],
        };

        var form = Form(family);

        Assert.Multiple(() =>
        {
            Assert.That(form.Fields[0].Label, Is.EqualTo("Common name"));
            Assert.That(form.Fields[1].Label, Is.EqualTo("baseHp"),
                "a field that declares no label at all still has to say something, and its key is the "
                + "only thing left");
        });
    }

    // ── Edits land in the record ──────────────────────────────────────────────

    [Test]
    public void EditingARow_WritesStraightIntoTheRecord()
    {
        var record = new AttributeBag();
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [Field("name"), Field("baseHp", FieldKind.Integer), Field("catchable", FieldKind.Flag)],
        };
        var form = Form(family, record);

        form.Fields[0].TextValue = "Bulbasaur";
        form.Fields[1].NumberValue = 45;
        form.Fields[2].FlagValue = true;

        Assert.Multiple(() =>
        {
            Assert.That(record["name"].AsText(), Is.EqualTo("Bulbasaur"));
            Assert.That(record["baseHp"].AsLong(), Is.EqualTo(45));
            Assert.That(record["catchable"].AsBool(), Is.True);
        });
    }

    [Test]
    public void AnExistingRecord_ShowsItsValues()
    {
        var record = new AttributeBag().Set("name", "Pikachu").Set("baseHp", 35);
        var family = new RecordFamily { Id = "Species", Fields = [Field("name"), Field("baseHp", FieldKind.Integer)] };

        var form = Form(family, record);

        Assert.That(form.Fields[0].TextValue, Is.EqualTo("Pikachu"));
        Assert.That(form.Fields[1].NumberValue, Is.EqualTo(35));
    }

    [Test]
    public void AnEditRaisesChanged_SoTheOwnerCanMarkTheRecordDirty()
    {
        var family = new RecordFamily { Id = "Species", Fields = [Field("name")] };
        var form = Form(family);
        int changes = 0;
        form.Changed += () => changes++;

        form.Fields[0].TextValue = "Eevee";
        form.Fields[0].TextValue = "Eevee";   // same value, so nothing happened

        Assert.That(changes, Is.EqualTo(1), "writing the value it already held is not an edit");
    }

    // ── What the declaration constrains ───────────────────────────────────────

    [Test]
    public void ABoundedNumber_IsClampedOnTheWayIn()
    {
        var record = new AttributeBag();
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [new FieldDescriptor { Key = "baseHp", Kind = FieldKind.Integer, Min = 1, Max = 255 }],
        };
        var form = Form(family, record);

        form.Fields[0].NumberValue = 999;
        Assert.That(record["baseHp"].AsLong(), Is.EqualTo(255));

        form.Fields[0].NumberValue = -40;
        Assert.That(record["baseHp"].AsLong(), Is.EqualTo(1));
    }

    [Test]
    public void AnIntegerField_StoresAWholeNumber()
    {
        var record = new AttributeBag();
        var family = new RecordFamily { Id = "Species", Fields = [Field("baseHp", FieldKind.Integer)] };

        Form(family, record).Fields[0].NumberValue = 45.7;

        Assert.That(record["baseHp"].AsLong(), Is.EqualTo(46));
    }

    [Test]
    public void ARealField_KeepsItsFraction()
    {
        var record = new AttributeBag();
        var family = new RecordFamily { Id = "Species", Fields = [Field("weight", FieldKind.Real)] };

        Form(family, record).Fields[0].NumberValue = 6.9;

        Assert.That(record["weight"].AsDouble(), Is.EqualTo(6.9).Within(1e-9));
    }

    [Test]
    public void TextPastItsDeclaredLimit_IsTrimmedWhereItIsTyped()
    {
        var record = new AttributeBag();
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [new FieldDescriptor { Key = "name", Kind = FieldKind.Text, MaxLength = 8 }],
        };

        Form(family, record).Fields[0].TextValue = "Bulbasaurus Rex";

        Assert.That(record["name"].AsText(), Is.EqualTo("Bulbasau"));
    }

    [Test]
    public void ARequiredFieldLeftEmpty_IsReported()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [new FieldDescriptor { Key = "name", Kind = FieldKind.Text, Required = true }],
        };
        var form = Form(family);

        Assert.That(form.IsComplete, Is.False);
        Assert.That(form.MissingRequired, Is.EqualTo(new[] { "name" }));

        form.Fields[0].TextValue = "Snorlax";

        Assert.That(form.IsComplete, Is.True);
    }

    // ── Choices and references ────────────────────────────────────────────────

    [Test]
    public void AChoiceField_OffersTheSetItNames()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [new FieldDescriptor { Key = "type", Kind = FieldKind.Choice, ChoiceSetId = "types" }],
        };
        var schema = new RecordSchema
        {
            Families = [family],
            ChoiceSets =
            [
                new ChoiceSet
                {
                    Id = "types",
                    Members = [new KindDescriptor { Id = "fire" }, new KindDescriptor { Id = "water" }],
                },
            ],
        };

        Assert.That(Form(family, schema: schema).Fields[0].Options.Select(o => o.Id),
                    Is.EqualTo(new[] { "fire", "water" }));
    }

    [Test]
    public void ARecordRefField_OffersTheFamilyItPointsAt()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            Fields = [new FieldDescriptor { Key = "drop", Kind = FieldKind.RecordRef, RecordFamilyId = "Items" }],
        };

        var form = Form(family, refs: id => id == "Items" ? [new NamedEntry(4, "Potion")] : []);

        Assert.That(form.Fields[0].References.Single().Name, Is.EqualTo("Potion"));
    }

    // ── The kind field ────────────────────────────────────────────────────────

    [Test]
    public void ChoosingAKind_AddsTheRowsThatKindActivates()
    {
        var (family, schema) = KindedFamily();
        var form = Form(family, schema: schema);

        Assert.That(form.Fields.Select(f => f.Key), Is.EqualTo(new[] { "kind" }), "nothing is chosen yet");

        form.Fields[0].ChoiceValue = "starter";

        Assert.That(form.Fields.Select(f => f.Key), Is.EqualTo(new[] { "kind", "evolvesAt" }));
    }

    [Test]
    public void ChangingTheKind_SwapsTheRowsUnderIt()
    {
        var (family, schema) = KindedFamily();
        var form = Form(family, schema: schema);

        form.Fields[0].ChoiceValue = "starter";
        form.Fields[0].ChoiceValue = "legendary";

        Assert.That(form.Fields.Select(f => f.Key), Is.EqualTo(new[] { "kind", "roamRadius" }));
    }

    /// <summary>The form writes keys and never removes one, so a value typed under one kind is still in
    /// the record after looking at another — and comes back when the author returns to it.</summary>
    [Test]
    public void AValueTypedUnderOneKind_SurvivesLookingAtAnother()
    {
        var (family, schema) = KindedFamily();
        var record = new AttributeBag();
        var form = Form(family, record, schema);

        form.Fields[0].ChoiceValue = "starter";
        form.Fields[1].NumberValue = 16;
        form.Fields[0].ChoiceValue = "legendary";

        Assert.That(record["evolvesAt"].AsLong(), Is.EqualTo(16), "still in the record while off screen");

        form.Fields[0].ChoiceValue = "starter";

        Assert.That(form.Fields[1].NumberValue, Is.EqualTo(16), "and back on screen holding it");
    }

    // Authored data may name a kind a newer build of the game declares. Showing the family's own rows and
    // nothing else beats refusing to open the record.
    [Test]
    public void AKindThisSchemaDoesNotDeclare_ActivatesNothing()
    {
        var (family, schema) = KindedFamily();
        var record = new AttributeBag().Set("kind", "mythical");

        var form = Form(family, record, schema);

        Assert.That(form.Fields.Select(f => f.Key), Is.EqualTo(new[] { "kind" }));
    }

    [Test]
    public void AFamilyWithNoKindField_JustShowsItsFields()
    {
        var family = new RecordFamily { Id = "Species", Fields = [Field("name")] };
        var form = Form(family);

        form.Fields[0].TextValue = "Ditto";

        Assert.That(form.Fields, Has.Count.EqualTo(1));
    }

    private static (RecordFamily, RecordSchema) KindedFamily()
    {
        var family = new RecordFamily
        {
            Id = "Species",
            KindFieldKey = "kind",
            Fields = [new FieldDescriptor { Key = "kind", Kind = FieldKind.Choice, ChoiceSetId = "kinds" }],
        };

        var schema = new RecordSchema
        {
            Families = [family],
            ChoiceSets =
            [
                new ChoiceSet
                {
                    Id = "kinds",
                    Members =
                    [
                        new KindDescriptor
                        {
                            Id = "starter",
                            Fields = [new FieldDescriptor { Key = "evolvesAt", Kind = FieldKind.Integer }],
                        },
                        new KindDescriptor
                        {
                            Id = "legendary",
                            Fields = [new FieldDescriptor { Key = "roamRadius", Kind = FieldKind.Integer }],
                        },
                    ],
                },
            ],
        };

        return (family, schema);
    }
}

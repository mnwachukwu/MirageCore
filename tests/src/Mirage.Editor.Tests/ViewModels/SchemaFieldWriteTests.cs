using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// What a schema form row does NOT write.
///
/// <para>The view stacks one control per field kind and shows the one that applies, so all six are
/// constructed and all six bind — and a two-way control writes its value back as it initializes. Every
/// one of those writes lands on the record being edited, which decides whether it reads as unsaved.
/// Opening a record must change nothing.</para>
/// </summary>
[TestFixture]
public class SchemaFieldWriteTests
{
    private static SchemaFieldViewModel Field(FieldKind kind, AttributeBag bag, ChoiceSet? choices = null)
        => new(new FieldDescriptor { Key = "field", Kind = kind, Min = 0, Max = 255 }, bag, choices);

    private static readonly ChoiceSet Elements = new()
    {
        Id = "elements",
        Members = [new KindDescriptor { Id = "fire" }],
    };

    /// <summary>Each row is one kind, and only that kind's control may write. Every other property is a
    /// read-only view of the same value as far as the bag is concerned.</summary>
    [TestCase(FieldKind.Integer)]
    [TestCase(FieldKind.Real)]
    [TestCase(FieldKind.Flag)]
    [TestCase(FieldKind.Text)]
    [TestCase(FieldKind.Choice)]
    [TestCase(FieldKind.RecordRef)]
    public void EveryOtherKindsControl_WritesNothing(FieldKind kind)
    {
        var bag = new AttributeBag();
        var row = Field(kind, bag, Elements);

        // What Avalonia does as the six stacked controls come up: each writes back what it is showing.
        row.NumberValue = row.NumberValue;
        row.FlagValue = row.FlagValue;
        row.TextValue = row.TextValue;
        row.ChoiceValue = row.ChoiceValue;
        row.ReferenceValue = row.ReferenceValue;

        Assert.That(bag.IsEmpty, Is.True, "opening a record must not author anything into it");
    }

    /// <summary>Even the row's OWN control writing back the blank it is showing is not an edit: absent and
    /// empty read the same, so creating the key would change what the record holds for no reason.</summary>
    [Test]
    public void WritingTheValueTheRowAlreadyShows_IsNotAnEdit()
    {
        var bag = new AttributeBag();
        var row = Field(FieldKind.Text, bag);
        bool edited = false;
        row.Edited += _ => edited = true;

        row.TextValue = "";

        Assert.Multiple(() =>
        {
            Assert.That(bag.IsEmpty, Is.True);
            Assert.That(edited, Is.False);
        });
    }

    [Test]
    public void TheRowsOwnControl_WritesARealEdit()
    {
        var bag = new AttributeBag();
        var row = Field(FieldKind.Integer, bag);

        row.NumberValue = 65;

        Assert.Multiple(() =>
        {
            Assert.That(bag["field"].Kind, Is.EqualTo(AttributeKind.Integer));
            Assert.That(bag["field"].AsLong(), Is.EqualTo(65));
        });
    }

    // A declared range is the record's rule, so a value past it is brought back rather than stored.
    [Test]
    public void AnIntegerOutsideItsDeclaredRange_IsClamped()
    {
        var bag = new AttributeBag();

        Field(FieldKind.Integer, bag).NumberValue = 900;

        Assert.That(bag["field"].AsLong(), Is.EqualTo(255));
    }

    [Test]
    public void ARealKeepsItsFraction()
    {
        var bag = new AttributeBag();
        var row = new SchemaFieldViewModel(
            new FieldDescriptor { Key = "field", Kind = FieldKind.Real, Min = 0, Max = 1 }, bag);

        row.NumberValue = 0.45;

        Assert.Multiple(() =>
        {
            Assert.That(bag["field"].Kind, Is.EqualTo(AttributeKind.Real));
            Assert.That(bag["field"].AsDouble(), Is.EqualTo(0.45).Within(1e-9));
        });
    }
}

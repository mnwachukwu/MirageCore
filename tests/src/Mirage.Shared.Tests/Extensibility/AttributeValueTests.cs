using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The accessors coerce on purpose, so the thing worth pinning is that each of them reads every kind
/// and that <see cref="AttributeValue.Kind"/> stays the honest answer about what was written.
/// </summary>
[TestFixture]
public class AttributeValueTests
{
    [Test]
    public void EachKindRemembersWhatItWas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From(1L).Kind, Is.EqualTo(AttributeKind.Integer));
            Assert.That(AttributeValue.From(1.5).Kind, Is.EqualTo(AttributeKind.Real));
            Assert.That(AttributeValue.From(true).Kind, Is.EqualTo(AttributeKind.Flag));
            Assert.That(AttributeValue.From("x").Kind, Is.EqualTo(AttributeKind.Text));
        });
    }

    [Test]
    public void AWholeNumberReadsAsEveryKind()
    {
        AttributeValue value = 45;

        Assert.Multiple(() =>
        {
            Assert.That(value.AsLong(), Is.EqualTo(45));
            Assert.That(value.AsDouble(), Is.EqualTo(45d));
            Assert.That(value.AsBool(), Is.True);
            Assert.That(value.AsText(), Is.EqualTo("45"));
        });
    }

    [Test]
    public void ARealTruncatesRatherThanRoundsWhenReadWhole()
    {
        Assert.That(AttributeValue.From(1.9).AsLong(), Is.EqualTo(1));
    }

    [Test]
    public void AFlagIsOneOrZeroToEveryNumericReader()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From(true).AsLong(), Is.EqualTo(1));
            Assert.That(AttributeValue.From(false).AsLong(), Is.Zero);
            Assert.That(AttributeValue.From(true).AsDouble(), Is.EqualTo(1d));
        });
    }

    [Test]
    public void ZeroIsTheOnlyFalseNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From(0L).AsBool(), Is.False);
            Assert.That(AttributeValue.From(0.0).AsBool(), Is.False);
            Assert.That(AttributeValue.From(-1L).AsBool(), Is.True);
        });
    }

    [TestCase("false", false)]
    [TestCase("FALSE", false)]
    [TestCase("no", false)]
    [TestCase("0", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("yes", true)]
    [TestCase("anything", true)]
    public void TextSpellsOutItsOwnTruth(string text, bool expected)
    {
        Assert.That(AttributeValue.From(text).AsBool(), Is.EqualTo(expected));
    }

    [Test]
    public void TextThatSpellsANumberReadsAsOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From("42").AsLong(), Is.EqualTo(42));
            Assert.That(AttributeValue.From("1.5").AsDouble(), Is.EqualTo(1.5));
        });
    }

    [Test]
    public void TextThatSpellsNothingNumericReadsAsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From("ember").AsLong(), Is.Zero);
            Assert.That(AttributeValue.From("ember").AsDouble(), Is.Zero);
        });
    }

    /// <summary>A record saved on one machine and read on another must produce the same text, so the
    /// number formatting cannot follow the current culture.</summary>
    [Test]
    public void NumbersFormatTheSameWhateverTheMachineSaysTheDecimalPointIs()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE"); // writes 1,5 for one and a half

            Assert.That(AttributeValue.From(1.5).AsText(), Is.EqualTo("1.5"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void ANullStringIsEmptyTextRatherThanNothing()
    {
        var value = AttributeValue.From((string?)null);

        Assert.Multiple(() =>
        {
            Assert.That(value.Kind, Is.EqualTo(AttributeKind.Text));
            Assert.That(value.AsText(), Is.Empty);
        });
    }

    [Test]
    public void TwoValuesOfTheSameKindAndContentAreEqual()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From(5L), Is.EqualTo(AttributeValue.From(5L)));
            Assert.That(AttributeValue.From("x"), Is.EqualTo(AttributeValue.From("x")));
        });
    }

    /// <summary>Five and five-point-zero read alike through every accessor, and are still not the same
    /// value — which is what keeps <see cref="AttributeValue.Kind"/> meaningful.</summary>
    [Test]
    public void AnIntegerAndARealOfTheSameMagnitudeAreNotEqual()
    {
        Assert.That(AttributeValue.From(5L), Is.Not.EqualTo(AttributeValue.From(5.0)));
    }
}

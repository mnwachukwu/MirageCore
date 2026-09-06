using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>The SpriteFont charset gate (TextValidation): IsValidChar defines exactly which characters the
/// game font can draw — ASCII printable, Latin-1 accents/marks, and the Œ/œ ligature — and must stay in
/// lockstep with the font atlas (a char that validates but isn't in the font renders as a missing glyph).
/// LocalizationParity runs this over real strings; these pin the boundaries + the Filter fallback directly.</summary>
[TestFixture]
public class TextValidationTests
{
    [Test]
    public void IsValidChar_AsciiPrintable_InRange_ControlsOut()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextValidation.IsValidChar(' '), Is.True, "space is the low bound");
            Assert.That(TextValidation.IsValidChar('~'), Is.True, "tilde is the high bound");
            Assert.That(TextValidation.IsValidChar('A'), Is.True);
            Assert.That(TextValidation.IsValidChar((char)0x1F), Is.False, "control char below space");
            Assert.That(TextValidation.IsValidChar((char)0x7F), Is.False, "DEL just above tilde");
        });
    }

    [Test]
    public void IsValidChar_Latin1AccentsAndMarks_InRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextValidation.IsValidChar('¡'), Is.True, "inverted marks are the low bound");
            Assert.That(TextValidation.IsValidChar('ÿ'), Is.True, "ÿ is the high bound");
            foreach (char c in "éñçàõü¿«»ªº")
                Assert.That(TextValidation.IsValidChar(c), Is.True, $"'{c}' is a needed FR/ES/PT mark");
            Assert.That(TextValidation.IsValidChar((char)0xA0), Is.False, "the NBSP gap just below ¡");
        });
    }

    [Test]
    public void IsValidChar_OeLigature_ButNotOtherLatinExtendedA()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextValidation.IsValidChar('Œ'), Is.True);
            Assert.That(TextValidation.IsValidChar('œ'), Is.True);
            Assert.That(TextValidation.IsValidChar((char)0x100), Is.False, "Ā — inside the font's gap between ÿ and Œ");
            Assert.That(TextValidation.IsValidChar((char)0x2605), Is.False, "★ — a symbol the font can't draw");
        });
    }

    [Test]
    public void IsValidText_TrueOnlyWhenEveryCharIsValid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextValidation.IsValidText("Café ¿qué? Œuf"), Is.True);
            Assert.That(TextValidation.IsValidText("bad★char"), Is.False, "one bad char fails the whole string");
        });
    }

    [Test]
    public void Filter_ReplacesOnlyInvalidChars()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextValidation.Filter("x★y"), Is.EqualTo("x?y"), "default replacement is '?'");
            Assert.That(TextValidation.Filter("x★y", '#'), Is.EqualTo("x#y"), "custom replacement");
            Assert.That(TextValidation.Filter("café"), Is.EqualTo("café"), "valid accents survive untouched");
        });
    }

    // The all-valid fast path returns the SAME instance — no allocation.
    [Test]
    public void Filter_AllValid_ReturnsSameInstance()
    {
        const string s = "Hello café ¿qué? Œuf";
        Assert.That(TextValidation.Filter(s), Is.SameAs(s));
    }
}

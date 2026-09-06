using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// The credit line four surfaces read from: the client's credits screen, the editor's About dialog,
/// the server shell's About dialog and the console's <c>/credits</c>.
///
/// <para>The copyright year is the part that rots on its own. It is the only thing here that changes
/// without anybody editing it, and it changes on a date rather than on a commit — so it is written as
/// a function of the year and tested at years that have not happened yet.</para>
/// </summary>
[TestFixture]
public class CreditsTests
{
    [Test]
    public void InTheFirstYear_TheCopyrightIsASingleYear()
    {
        Assert.That(Credits.CopyrightYears(2026), Is.EqualTo("2026"));
    }

    [Test]
    public void AfterTheFirstYear_ItBecomesARange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Credits.CopyrightYears(2027), Is.EqualTo("2026-2027"));
            Assert.That(Credits.CopyrightYears(2031), Is.EqualTo("2026-2031"));
        });
    }

    /// <summary>A clock set wrong, or a machine in a timezone that has not ticked over, must not print
    /// a backwards range like "2026-2025".</summary>
    [Test]
    public void AYearBeforeTheStart_StillReadsAsTheStartYear()
    {
        Assert.That(Credits.CopyrightYears(2020), Is.EqualTo("2026"));
    }

    [Test]
    public void TheCopyrightLine_NamesTheStudio()
    {
        Assert.That(Credits.CopyrightLine(2031), Is.EqualTo("Copyright (c) 2026-2031 Pluperfect Development"));
    }

    /// <summary>Spelled "(c)", never the © sign. This string reaches the game client, whose SpriteFonts
    /// are the one renderer in the project that cannot be handed a glyph at runtime.</summary>
    [Test]
    public void TheCopyrightLine_AvoidsTheGlyphTheClientCannotDraw()
    {
        Assert.That(Credits.CopyrightLine(2031), Does.Not.Contain("©"));
    }

    /// <summary>Every credit string the apps render has to be drawable by the client's SpriteFonts,
    /// which cover ASCII and Latin-1. A name pasted in from somewhere else is exactly how an
    /// undrawable character would arrive.</summary>
    [Test]
    public void EveryCreditString_IsRenderableByTheClient()
    {
        static bool Renderable(char c) =>
            (c >= ' ' && c <= '~') || (c >= '¡' && c <= 'ÿ') || c == 'Œ' || c == 'œ';

        var strings = new[]
        {
            Credits.Author, Credits.AuthorHandles, Credits.Studio,
            Credits.SiteUrl, Credits.CopyrightLine(2031),
        };

        var bad = strings.SelectMany(s => s.Where(c => !Renderable(c)).Select(c => $"'{c}' in \"{s}\""))
                         .ToList();

        Assert.That(bad, Is.Empty, string.Join(", ", bad));
    }

    [Test]
    public void TheSiteUrl_IsTheStudioSite()
    {
        Assert.That(Credits.SiteUrl, Is.EqualTo("https://pluperfect.dev"));
    }
}

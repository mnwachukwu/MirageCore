using Mirage.Client.Shell.Ui;
using Mirage.Editor.Localization;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Localization;

/// <summary>
/// The icon vocabulary, and the two tables that have to answer for every name in it.
///
/// <para>🔴 <b>Three halves of one thing, and a missing one is silent.</b> Core says which glyphs a
/// game may name; the editor holds a vector path per name and the client holds a pixel mask per name.
/// A glyph added to the vocabulary and drawn on neither side is a family that renders as a blank
/// space — on one surface only, which is worse, because the other one looks right.</para>
///
/// <para>This is the same shape <c>GameKeyMapTests</c> is, for the same reason: a list of names and a
/// table keyed by them, where the two are written in different files and nothing else compares
/// them.</para>
/// </summary>
[TestFixture]
public class GameIconTests
{
    /// <summary>🔴 Every offered glyph has a shape on both surfaces, and neither draws one the
    /// vocabulary does not offer.</summary>
    [Test]
    public void EveryOfferedGlyphIsDrawnByBothSurfaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameIconPaths.Drawn, Is.EquivalentTo(GameIcon.Offered),
                "the editor's paths and the vocabulary have drifted");

            Assert.That(GameIconArt.Drawn, Is.EquivalentTo(GameIcon.Offered),
                "the client's masks and the vocabulary have drifted — re-run tools/gen-icon-masks.cs");
        });
    }

    /// <summary>The default is itself offered, since every fallback lands on it.</summary>
    [Test]
    public void TheDefaultIsOneOfTheOfferedGlyphs() =>
        Assert.That(GameIcon.Offered, Does.Contain(GameIcon.Default));

    /// <summary>Blank is offered, because most of what a game declares names no glyph and that cannot
    /// be the answer that fails.</summary>
    [Test]
    public void BlankIsOfferedAndAnythingElseUnknownIsNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameIcon.IsOffered(""), Is.True);
            Assert.That(GameIcon.IsOffered(null), Is.True);
            Assert.That(GameIcon.IsOffered("leaf"), Is.True);
            Assert.That(GameIcon.IsOffered("Leaf"), Is.False, "ordinal, like every other name on the wire");
            Assert.That(GameIcon.IsOffered("dragon"), Is.False);
        });
    }

    /// <summary>⚠ A renderer meeting a name it has never heard of falls back rather than failing. A
    /// client older than the game it joined still draws a usable rail.</summary>
    [Test]
    public void AnUnknownGlyphFallsBackRatherThanFailing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameIconPaths.For("dragon"), Is.EqualTo(GameIconPaths.For(GameIcon.Default)));
            Assert.That(GameIconArt.For("dragon"), Is.EqualTo(GameIconArt.For(GameIcon.Default)));
            Assert.That(GameIconPaths.For(""), Is.EqualTo(GameIconPaths.For(GameIcon.Default)));
        });
    }

    /// <summary>Every mask is the declared size and none of them is empty — a glyph rasterized to
    /// nothing draws nothing, and looks exactly like a glyph nobody declared.</summary>
    [Test]
    public void EveryMaskIsTheRightSizeAndHasPixelsInIt()
    {
        Assert.Multiple(() =>
        {
            foreach (string icon in GameIcon.Offered)
            {
                uint[] rows = GameIconArt.For(icon);

                Assert.That(rows, Has.Length.EqualTo(GameIconArt.Size), icon);
                Assert.That(rows, Has.Some.Not.EqualTo(0u), $"'{icon}' rasterized to nothing");
            }
        });
    }

    /// <summary>Core's own sections each name one, so a game's family is told apart from them rather
    /// than sharing the default with them.</summary>
    [Test]
    public void CoresOwnFamiliesEachNameAGlyph()
    {
        Assert.Multiple(() =>
        {
            foreach (RecordFamily family in CoreRecordFamilies.World)
            {
                Assert.That(family.Icon, Is.Not.Empty, family.Id);
                Assert.That(GameIcon.IsOffered(family.Icon), Is.True, family.Id);
            }

            Assert.That(CoreRecordFamilies.World.Select(f => f.Icon).Distinct().Count(),
                Is.EqualTo(CoreRecordFamilies.World.Count),
                "two of Core's own sections wearing one glyph is the thing this exists to stop");
        });
    }
}

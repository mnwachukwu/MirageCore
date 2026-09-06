using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Rendering;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Rendering;

/// <summary>
/// How a decoded sheet becomes something a premultiplied blend can draw.
///
/// <para>Two authored formats, two paths. A BMP names its transparent color with its top-left pixel; a PNG
/// carries its own alpha and has to be premultiplied, because everything here draws through
/// <c>BlendState.AlphaBlend</c> while the decoder hands back straight alpha.</para>
///
/// <para>Both failure modes are quiet. A PNG left straight shows a halo of its background around every
/// edge; a PNG run through the color key loses whatever color its transparent pixels happened to be
/// stored as, everywhere in the sheet.</para>
/// </summary>
[TestFixture]
public class SheetPixelsTests
{
    // ── Color key ─────────────────────────────────────────────────────────────

    /// <summary>The top-left pixel names the transparent color, and every pixel of that color goes with it.
    /// This is the convention every shipped BMP is authored against.</summary>
    [Test]
    public void TheTopLeftColorBecomesTransparentThroughout()
    {
        var pixels = new[]
        {
            new Color(255, 0, 255), new Color(10, 20, 30),
            new Color(255, 0, 255), new Color(40, 50, 60),
        };

        SheetPixels.ApplyColorKey(pixels);

        Assert.That(pixels[0].A, Is.EqualTo(0));
        Assert.That(pixels[2].A, Is.EqualTo(0));
        Assert.That(pixels[1], Is.EqualTo(new Color(10, 20, 30)), "art must be left alone");
        Assert.That(pixels[3], Is.EqualTo(new Color(40, 50, 60)));
    }

    /// <summary>A keyed pixel becomes (0,0,0,0), which is already premultiplied. If it kept its color, a
    /// premultiplied blend would add that color wherever the sheet is meant to be see-through.</summary>
    [Test]
    public void AKeyedPixelIsFullyZeroed()
    {
        var pixels = new[] { new Color(255, 0, 255), new Color(255, 0, 255) };

        SheetPixels.ApplyColorKey(pixels);

        foreach (var p in pixels)
            Assert.That((p.R, p.G, p.B, p.A), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)0)));
    }

    // ── Premultiply ───────────────────────────────────────────────────────────

    /// <summary>Opaque pixels are untouched, which is almost the whole sheet. Scaling them would darken
    /// every solid tile by a rounding error.</summary>
    [Test]
    public void OpaquePixelsAreUnchanged()
    {
        var pixels = new[] { new Color(10, 20, 30, 255), new Color(255, 255, 255, 255) };

        SheetPixels.Premultiply(pixels);

        Assert.That(pixels[0], Is.EqualTo(new Color(10, 20, 30, 255)));
        Assert.That(pixels[1], Is.EqualTo(new Color(255, 255, 255, 255)));
    }

    /// <summary>A half-transparent pixel has its color scaled to match. Left straight it would composite at
    /// full strength, so anti-aliased edges would read as a bright fringe.</summary>
    [Test]
    public void AHalfTransparentPixelIsScaledByItsAlpha()
    {
        var pixels = new[] { new Color(200, 100, 50, 128) };

        SheetPixels.Premultiply(pixels);

        Assert.That(pixels[0].A, Is.EqualTo(128), "alpha itself is not scaled");
        Assert.That(pixels[0].R, Is.EqualTo(200 * 128 / 255));
        Assert.That(pixels[0].G, Is.EqualTo(100 * 128 / 255));
        Assert.That(pixels[0].B, Is.EqualTo(50 * 128 / 255));
    }

    /// <summary>🔴 The one that matters most. Art exported over a colored background keeps that color in
    /// its fully transparent pixels, and a premultiplied blend would add it around every edge as a halo.
    /// Zeroing it is what makes the transparency actually transparent.</summary>
    [Test]
    public void AFullyTransparentPixelLosesItsColor()
    {
        var pixels = new[] { new Color(255, 0, 0, 0) };

        SheetPixels.Premultiply(pixels);

        Assert.That((pixels[0].R, pixels[0].G, pixels[0].B), Is.EqualTo(((byte)0, (byte)0, (byte)0)),
            "a transparent red pixel would otherwise tint whatever is behind it");
    }

    /// <summary>🔴 The case that made the color key wrong for PNGs. A sheet whose transparent background is
    /// stored as black would, under the key, take every genuinely black pixel in the art with it — an
    /// outline, a shadow, a pupil. Premultiplying touches none of them.</summary>
    [Test]
    public void OpaqueBlackSurvivesBesideTransparentBlack()
    {
        var pixels = new[]
        {
            new Color(0, 0, 0, 0),      // transparent background
            new Color(0, 0, 0, 255),    // a black outline in the art
            new Color(120, 200, 90, 255),
        };

        SheetPixels.Premultiply(pixels);

        Assert.That(pixels[1], Is.EqualTo(new Color(0, 0, 0, 255)), "the outline must still be there");
        Assert.That(pixels[2], Is.EqualTo(new Color(120, 200, 90, 255)));
        Assert.That(pixels[0].A, Is.EqualTo(0));
    }

    /// <summary>The same sheet run through the color key instead loses that outline, which is exactly why
    /// the two formats take different paths. This states the difference rather than leaving it implied.</summary>
    [Test]
    public void TheColorKeyWouldHaveEatenThatBlackOutline()
    {
        var pixels = new[]
        {
            new Color(0, 0, 0, 0),
            new Color(0, 0, 0, 255),
            new Color(120, 200, 90, 255),
        };

        SheetPixels.ApplyColorKey(pixels);

        Assert.That(pixels[1].A, Is.EqualTo(0),
            "the key matches on color alone, so opaque black goes with the transparent black");
    }

    /// <summary>An empty sheet is not a crash. A zero-byte or unreadable file reaches here as no pixels at
    /// all, and the loader should return an empty texture rather than fail the whole asset load.</summary>
    [Test]
    public void NoPixelsIsNotAFailure()
    {
        Assert.That(() => SheetPixels.ApplyColorKey([]), Throws.Nothing);
        Assert.That(() => SheetPixels.Premultiply([]), Throws.Nothing);
    }
}

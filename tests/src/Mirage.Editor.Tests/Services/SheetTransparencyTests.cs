using Mirage.Editor.Services;
using Mirage.Shared;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// Which transparency model a sheet uses, and whether it has any.
///
/// <para>The rule is the extension: a BMP names its transparent color with its top-left pixel, a PNG
/// carries its own alpha. What the manager adds on top is noticing a PNG that carries neither — nothing
/// keys a PNG, so one exported flat renders as a solid rectangle over everything beneath it, with no error
/// anywhere.</para>
///
/// <para>The detection has to be right in both directions. A PNG can be transparent through an alpha
/// channel or through a <c>tRNS</c> chunk, and reading only the color type would report perfectly good
/// palette art as opaque — a warning that fires on correct files teaches people to ignore the list.</para>
/// </summary>
[TestFixture]
public class SheetTransparencyTests
{
    private string _dir = "";

    [SetUp]
    public void MakeFolder()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-alpha-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void DropFolder()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp folder is not worth failing on */ }
    }

    private SheetEntry Scan(string fileName)
    {
        var scan = SheetLibrary.Scan(_dir, Constants.MaxTilesets);
        return scan.Sheets.Single(s => Path.GetFileName(s.Path) == fileName);
    }

    private string Bmp(string fileName)
    {
        string path = Path.Combine(_dir, fileName);
        var header = new byte[54];
        header[0] = 0x42; header[1] = 0x4D;
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(64).CopyTo(header, 18);
        BitConverter.GetBytes(64).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);
        File.WriteAllBytes(path, header);
        return path;
    }

    // A PNG header with the given color type, and optionally a tRNS chunk before IDAT.
    private string Png(string fileName, byte colorType, bool withTrns = false)
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        bytes.AddRange(Chunk("IHDR",
        [
            .. BigEndian(64), .. BigEndian(64),
            8, colorType, 0, 0, 0,
        ]));
        if (withTrns) bytes.AddRange(Chunk("tRNS", [0, 0, 0]));
        bytes.AddRange(Chunk("IDAT", [0]));

        string path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, [.. bytes]);
        return path;
    }

    private static byte[] BigEndian(int value) => [.. BitConverter.GetBytes(value).Reverse()];

    private static byte[] Chunk(string type, byte[] payload) =>
        [.. BigEndian(payload.Length), .. System.Text.Encoding.ASCII.GetBytes(type), .. payload, 0, 0, 0, 0];

    // ── Which model ───────────────────────────────────────────────────────────

    /// <summary>A BMP is color-keyed. Every sheet shipped today is one, so this is the path that must not
    /// change.</summary>
    [Test]
    public void ABmpIsColorKeyed()
    {
        Bmp("0_tiles.bmp");

        Assert.That(Scan("0_tiles.bmp").Transparency, Is.EqualTo(SheetTransparency.ColorKey));
    }

    /// <summary>A PNG with an alpha channel uses it, and is not keyed.</summary>
    [Test]
    public void APngWithAnAlphaChannelUsesIt()
    {
        Png("1_rgba.png", colorType: 6);

        Assert.That(Scan("1_rgba.png").Transparency, Is.EqualTo(SheetTransparency.Alpha));
    }

    /// <summary>Grayscale-plus-alpha counts too. Reading only for RGBA would miss it.</summary>
    [Test]
    public void GrayscaleWithAlphaCountsAsAlpha()
    {
        Png("2_graya.png", colorType: 4);

        Assert.That(Scan("2_graya.png").Transparency, Is.EqualTo(SheetTransparency.Alpha));
    }

    /// <summary>Palette art with a tRNS chunk is transparent, even though its color type says otherwise.
    /// This is the false positive worth avoiding: such a sheet is perfectly good and must not be flagged.</summary>
    [Test]
    public void PaletteArtWithATrnsChunkIsTransparent()
    {
        Png("3_palette.png", colorType: 3, withTrns: true);

        var sheet = Scan("3_palette.png");
        Assert.That(sheet.Transparency, Is.EqualTo(SheetTransparency.Alpha));
        Assert.That(SheetLibrary.Scan(_dir, Constants.MaxTilesets).Problems, Is.Empty);
    }

    /// <summary>A flat PNG has no transparency at all, and nothing will key it — so it draws as a solid
    /// block. That is the state worth naming.</summary>
    [Test]
    public void AFlatPngHasNoTransparency()
    {
        Png("4_flat.png", colorType: 2);

        Assert.That(Scan("4_flat.png").Transparency, Is.EqualTo(SheetTransparency.None));
    }

    // ── Reported ──────────────────────────────────────────────────────────────

    /// <summary>The flat PNG is reported. Without it the author sees a sheet that loads, looks right in the
    /// list, and paints an opaque rectangle over everything.</summary>
    [Test]
    public void AFlatPngIsReported()
    {
        Png("0_flat.png", colorType: 2);

        var problem = SheetLibrary.Scan(_dir, Constants.MaxTilesets).Problems.SingleOrDefault();

        Assert.That(problem?.Kind, Is.EqualTo(SheetProblemKind.PngWithoutTransparency));
        Assert.That(problem!.Index, Is.EqualTo(0));
    }

    /// <summary>A BMP is never reported for this. It has no alpha by definition and does not need any —
    /// its key is what makes it transparent, and flagging every BMP would bury the list.</summary>
    [Test]
    public void ABmpIsNeverReportedForMissingAlpha()
    {
        Bmp("0_tiles.bmp");

        Assert.That(SheetLibrary.Scan(_dir, Constants.MaxTilesets).Problems, Is.Empty);
    }
}

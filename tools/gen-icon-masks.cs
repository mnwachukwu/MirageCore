#:package SkiaSharp@3.119.0
#:package SkiaSharp.NativeAssets.Win32@3.119.0

// The game client's copy of the icon vocabulary, rasterized from the editor's own paths.
//
//     dotnet run --file gen-icon-masks.cs            dry run, and writes a sheet to look at
//     dotnet run --file gen-icon-masks.cs -- --apply
//
// In:   editor/src/Mirage.Editor/Localization/GameIconPaths.cs
// Out:  client/src/Mirage.Client.Shell/Ui/GameIconArt.cs
//       tools/icons.png            every glyph, as the client will draw it
//
// 🔴 The editor draws vector geometry and the client draws pixels, and neither can use the other's
// form: Avalonia fills a path, and MonoGame has no path filler. So there are two tables for one set of
// names, and the hazard is the ordinary one — a glyph in the vocabulary with nothing behind it on one
// side draws nothing at all, on that side only. GameIconTests holds all three sets equal.
//
// Rasterizing here rather than writing the masks by hand is what keeps the two SHAPES the same as
// well as the two sets. A mask typed out by hand is a second drawing of the same glyph.
//
// ⚠ C# rather than TypeScript, against the usual rule for kept scripts, because this rasterizes vector
// paths and there is no Node path to that without a native dependency. Same reason the site's
// gen-og-image.cs is C#.
//
// ⚠ A SNAPSHOT. Nothing fails when a path changes and this is not re-run — the client keeps drawing
// the previous shape under the right name, which is cosmetic drift rather than a missing icon. Re-run
// it when you change a path.

using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

// Walked up from where it was RUN, not from the assembly: a single-file script is built into a
// temp folder, so AppContext.BaseDirectory is nowhere near the repository.
string root = Repository(Directory.GetCurrentDirectory());

string pathsFile = Path.Combine(
    root, "editor", "src", "Mirage.Editor", "Localization", "GameIconPaths.cs");
string outFile = Path.Combine(
    root, "client", "src", "Mirage.Client.Shell", "Ui", "GameIconArt.cs");
string sheetFile = Path.Combine(root, "tools", "icons.png");

bool apply = args.Contains("--apply");

if (!File.Exists(pathsFile))
{
    Console.Error.WriteLine($"no glyph paths at {pathsFile}");
    return 1;
}

// Each entry is ["name"] = "..." , possibly continued with + "..." across several lines.
var entries = Regex.Matches(
    File.ReadAllText(pathsFile),
    @"\[""(?<name>[a-z]+)""\]\s*=\s*(?<body>(?:""(?:[^""\\]|\\.)*""\s*\+?\s*)+),",
    RegexOptions.Singleline);

var glyphs = new List<(string Name, string Path)>();

foreach (Match entry in entries)
{
    string joined = string.Concat(
        Regex.Matches(entry.Groups["body"].Value, @"""((?:[^""\\]|\\.)*)""")
             .Select(part => part.Groups[1].Value));

    glyphs.Add((entry.Groups["name"].Value, joined));
}

if (glyphs.Count == 0)
{
    Console.Error.WriteLine("no glyphs were found — the table's shape must have changed");
    return 1;
}

// 🔴 THIRTY-TWO A SIDE, NOT SIXTEEN, and that is not a detail. The paths are drawn on a 16x16 box
// with one-unit gaps in them — between the four squares of `quads`, between a paw's toes, down a book's
// spine. Rasterized at sixteen those gaps are a single pixel and every one of them closed up: `quads`
// came out a solid square, `paw` a blob. At thirty-two they are two pixels and all of them survive,
// and the client scales the mask down with filtering rather than drawing it a pixel to a pixel.
const int Box = 16, Size = 32, Over = 4;
var masks = new List<(string Name, uint[] Rows)>();

foreach ((string name, string path) in glyphs)
{
    SKPath shape = SKPath.ParseSvgPathData(path)
        ?? throw new InvalidOperationException($"'{name}' is not parseable path data.");

    // Avalonia's StreamGeometry fills even-odd unless the data says otherwise, which is what the
    // glyphs with holes in them (coin, clock, dice, scroll) rely on.
    shape.FillType = SKPathFillType.EvenOdd;

    var info = new SKImageInfo(Size * Over, Size * Over, SKColorType.Rgba8888, SKAlphaType.Premul);
    using SKSurface surface = SKSurface.Create(info);
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Scale((float)Size / Box * Over);

    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        surface.Canvas.DrawPath(shape, paint);
    }

    using SKImage image = surface.Snapshot();
    using SKBitmap big = SKBitmap.FromImage(image);

    var rows = new uint[Size];
    for (int y = 0; y < Size; y++)
    {
        uint row = 0;
        for (int x = 0; x < Size; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < Over; sy++)
            {
                for (int sx = 0; sx < Over; sx++)
                {
                    if (big.GetPixel(x * Over + sx, y * Over + sy).Alpha >= 128) covered++;
                }
            }

            // Half the samples, so a pixel the shape only clips is left out and one it mostly
            // covers is kept.
            if (covered * 2 >= Over * Over) row |= 1u << (Size - 1 - x);
        }

        rows[y] = row;
    }

    masks.Add((name, rows));
}

// ---- The sheet, so the pixels can be looked at ----------------------------------------------

const int Cell = 96, Pad = 10, Cols = 7, Scale = 2;
int sheetRows = (masks.Count + Cols - 1) / Cols;

using (SKSurface sheet = SKSurface.Create(
    new SKImageInfo(Cols * Cell, sheetRows * (Cell + 22))))
{
    SKCanvas canvas = sheet.Canvas;
    canvas.Clear(new SKColor(0x1b, 0x1e, 0x24));

    using var on = new SKPaint { Color = new SKColor(0xd4, 0xd4, 0xd4) };
    using var box = new SKPaint
    {
        Color = new SKColor(0x3a, 0x3f, 0x4a), Style = SKPaintStyle.Stroke,
    };
    using var font = new SKFont(SKTypeface.FromFamilyName("Consolas"), 13);
    using var label = new SKPaint { Color = new SKColor(0x9a, 0xa0, 0xac), IsAntialias = true };

    for (int i = 0; i < masks.Count; i++)
    {
        float x = i % Cols * Cell + Pad, y = i / Cols * (Cell + 22) + Pad;
        canvas.DrawRect(new SKRect(x, y, x + Size * Scale, y + Size * Scale), box);

        for (int row = 0; row < Size; row++)
        {
            for (int col = 0; col < Size; col++)
            {
                if ((masks[i].Rows[row] & (1u << (Size - 1 - col))) == 0) continue;
                canvas.DrawRect(
                    new SKRect(x + col * Scale, y + row * Scale,
                               x + col * Scale + Scale, y + row * Scale + Scale), on);
            }
        }

        canvas.DrawText(masks[i].Name, x, y + Size * Scale + 15, SKTextAlign.Left, font, label);
    }

    using SKImage image = sheet.Snapshot();
    using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
    using FileStream file = File.Create(sheetFile);
    data.SaveTo(file);
}

// ---- The client's table ----------------------------------------------------------------------

var text = new StringBuilder();
text.Append("""
using System.Collections.Generic;
using Mirage.Shared.Extensibility;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// The shape behind every name in <see cref="GameIcon.Offered"/>, as this client draws it.
///
/// <para><b>GENERATED by tools/gen-icon-masks.cs from the editor's paths. Do not edit.</b> The editor
/// fills vector geometry and this draws pixels, so the two cannot share a form — but they can share a
/// source, and rasterizing the editor's own paths is what keeps one name from meaning two different
/// pictures.</para>
///
/// <para>One <c>uint</c> per row, thirty-two rows, the high bit leftmost. ⚠ <b>Thirty-two a side, for
/// a glyph drawn on a 16x16 box:</b> the gaps in these shapes are one unit wide, and rasterized at
/// sixteen every one of them closes up — <c>quads</c> becomes a solid square. Drawn into a texture and
/// scaled down with filtering, this reads as vector art and costs no texture to ship.</para>
/// </summary>
public static class GameIconArt
{
    /// <summary>The mask every glyph is held at, in pixels a side.</summary>
    public const int Size = 32;

    private static readonly Dictionary<string, uint[]> ByName = new(System.StringComparer.Ordinal)
    {

""");

foreach ((string name, uint[] rows) in masks)
{
    text.Append($"        [\"{name}\"] =\n        [\n            ");
    for (int i = 0; i < rows.Length; i++)
    {
        text.Append($"0x{rows[i]:X8},");
        text.Append((i + 1) % 4 == 0 ? "\n            " : " ");
    }

    text.Append("        ],\n\n");
}

text.Append("""
    };

    /// <summary>Every name this client can draw, for the test that holds it against the vocabulary.</summary>
    public static IReadOnlyCollection<string> Drawn => ByName.Keys;

    /// <summary>The rows of a glyph, falling back to <see cref="GameIcon.Default"/> for one this build
    /// has never heard of — a client older than the game it joined still draws something.</summary>
    public static uint[] For(string? icon) =>
        ByName.TryGetValue(GameIcon.Or(icon), out uint[]? rows) ? rows : ByName[GameIcon.Default];
}

""");

string written = text.ToString().ReplaceLineEndings("\n");

if (apply)
{
    File.WriteAllText(outFile, written);
    Console.WriteLine($"wrote {outFile}");
}
else
{
    Console.WriteLine($"would write {outFile}");
}

Console.WriteLine($"  {masks.Count} glyph(s); sheet at {sheetFile}");
return 0;

static string Repository(string from)
{
    var here = new DirectoryInfo(from);
    while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx")))
    {
        here = here.Parent;
    }

    return here?.FullName
        ?? throw new InvalidOperationException("The repository root is not above this script.");
}

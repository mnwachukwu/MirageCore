using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Every authored string in the seed has to be drawable by the client's SpriteFonts.
///
/// <para>Those fonts cover ASCII, Latin-1 from the inverted marks up, and the French OE ligature.
/// A character outside that does not render as a box — <c>SpriteFont.MeasureString</c> THROWS, and
/// the client dies on the frame it first lays the text out. One smart dash pasted into one
/// conversation node is enough, and it only shows when a player opens that particular NPC.</para>
///
/// <para>The fonts also carry a DefaultCharacter now, so an unknown glyph substitutes rather than
/// throwing. This fixture is the other half: the client survives bad text, and the seed does not
/// ship any.</para>
/// </summary>
[TestFixture]
public class SeedRenderableTextTests
{
    private static readonly string[] Collections =
        ["conversations", "npcs", "quests", "items", "spells", "classes", "shops"];

    /// <summary>Mirrors the CharacterRegions in <c>Content/fonts/*.spritefont</c>.</summary>
    private static bool Renderable(char c) =>
        (c >= ' ' && c <= '~') || (c >= '¡' && c <= 'ÿ')
        || c == 'Œ' || c == 'œ' || c == '\n' || c == '\r' || c == '\t';

    private static string SeedDir() =>
        Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Host", "world");

    private static void Collect(JsonElement el, string where, List<string> bad)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                string s = el.GetString() ?? "";
                foreach (char c in s)
                    if (!Renderable(c))
                    {
                        bad.Add($"{where}: U+{(int)c:X4} in \"{Trim(s)}\"");
                        return;
                    }
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Collect(p.Value, $"{where}.{p.Name}", bad);
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in el.EnumerateArray()) Collect(item, $"{where}[{i++}]", bad);
                break;
        }
    }

    private static string Trim(string s) => s.Length <= 60 ? s : s[..57] + "...";

    /// <summary>Baked in at build time rather than walked up from the output directory, so a redirected
    /// build fails here instead of quietly finding nothing and checking nothing.</summary>
    private static string RepoRoot() =>
        typeof(SeedRenderableTextTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

    /// <summary>The two string tables the CLIENT draws: its own UI text, and every line the server
    /// sends it. Both go through the same SpriteFonts as the seed.
    ///
    /// <para>The editor's tables are deliberately not here. Avalonia draws those with system fonts, so
    /// its arrows, its play and stop glyphs and its Δ belong exactly where they are — the constraint is
    /// the SpriteFont, not the language.</para></summary>
    [Test]
    public void EveryClientFacingLangString_IsDrawableByTheClientFont()
    {
        string root = RepoRoot();
        Assert.That(Directory.Exists(root), Is.True, $"repository root not found: {root}");

        string[] dirs =
        [
            Path.Combine(root, "client", "src", "Mirage.Client.Shell", "lang"),
            Path.Combine(root, "server", "src", "Mirage.Server.Core", "lang"),
        ];

        var bad = new List<string>();
        int scanned = 0;
        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                scanned++;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var entry in doc.RootElement.EnumerateObject())
                    Collect(entry.Value, $"{Path.GetFileName(dir)}/{Path.GetFileName(file)}:{entry.Name}", bad);
            }
        }

        Assert.That(scanned, Is.GreaterThan(0), "no language files found to check");
        Assert.That(bad, Is.Empty,
            "these would throw out of SpriteFont.MeasureString the moment they were shown:\n  "
            + string.Join("\n  ", bad.Take(20)));
    }

    /// <summary>A content guard rather than a unit test — it reads the shipped seed, so it carries the
    /// Content category and runs with the other checks instead of in a unit pass.</summary>
    [Test]
    [Category("Content")]
    public void EverySeedString_IsDrawableByTheClientFont()
    {
        string seed = SeedDir();
        Assert.That(Directory.Exists(seed), Is.True, $"the tracked seed is missing: {seed}");

        var bad = new List<string>();
        foreach (string collection in Collections)
        {
            string dir = Path.Combine(seed, collection);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                Collect(doc.RootElement, $"{collection}/{Path.GetFileName(file)}", bad);
            }
        }

        Assert.That(bad, Is.Empty,
            "these would throw out of SpriteFont.MeasureString the moment a player read them:\n  "
            + string.Join("\n  ", bad.Take(20)));
    }
}

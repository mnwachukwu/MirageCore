using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mirage.Shared.Tests.Ui;

/// <summary>
/// <c>Mirage.Ui</c> is three XAML resource dictionaries and no C#, so what can break in it is the XAML
/// itself: a renamed key, a duplicate key, a reference to a resource nobody defines.
///
/// <para>Avalonia resolves those at RUNTIME. A <c>{StaticResource}</c> naming a key that does not exist
/// throws only when the control is realised, and a duplicate key silently picks one — neither is a build
/// error, and both reach a screenshot before they reach a compiler. These read the dictionaries as XML,
/// which needs no Avalonia app, no display and no UI thread.</para>
/// </summary>
[TestFixture]
public class ThemeResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemeDir()
    {
        string root = typeof(ThemeResourceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string dir = Path.Combine(root, "shared", "src", "Mirage.Ui", "Theme");
        Assert.That(Directory.Exists(dir), Is.True, $"Theme folder not found: {dir}");
        return dir;
    }

    private static List<(string File, XDocument Doc)> ThemeFiles() =>
        [.. Directory.GetFiles(ThemeDir(), "*.axaml")
            .OrderBy(f => f)
            .Select(f => (Path.GetFileName(f), XDocument.Load(f)))];

    /// <summary>Every <c>x:Key</c> in the theme, with the file that declares it.</summary>
    private static List<(string Key, string File, string Element)> DeclaredKeys() =>
        [.. ThemeFiles().SelectMany(t => t.Doc.Descendants()
            .Where(e => e.Attribute(X + "Key") is not null)
            .Select(e => (e.Attribute(X + "Key")!.Value, t.File, e.Name.LocalName)))];

    [Test]
    public void EveryThemeFileParses()
    {
        var files = ThemeFiles();
        Assert.That(files, Is.Not.Empty, "the theme folder holds no .axaml");
        Assert.That(files.Select(f => f.File), Does.Contain("Colors.axaml"));
    }

    /// <summary>A duplicate key is not an error to Avalonia — one definition simply wins, and which one
    /// depends on merge order.</summary>
    [Test]
    public void NoResourceKeyIsDeclaredTwice()
    {
        var dupes = DeclaredKeys()
            .GroupBy(k => k.Key)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} in {string.Join(" + ", g.Select(x => x.File))}")
            .ToList();

        Assert.That(dupes, Is.Empty, string.Join(Environment.NewLine, dupes));
    }

    /// <summary>Every <c>SolidColorBrush</c> resolves to a key declared as a <c>Color</c>. Pointing one at
    /// a missing key, or at a resource that is not a colour, throws only when the control is realised.
    ///
    /// <para>Not every Color has a brush, and that is intended: a handful are consumed as colours —
    /// gradient stops, and the FluentAvalonia overrides that are typed <c>Color</c> by the control set.</para></summary>
    [Test]
    public void EveryBrushWrapsADeclaredColor()
    {
        var colors = DeclaredKeys().Where(k => k.Element == "Color").Select(k => k.Key).ToHashSet();

        var brushes = ThemeFiles()
            .SelectMany(t => t.Doc.Descendants()
                .Where(e => e.Name.LocalName == "SolidColorBrush")
                .Select(e => (t.File,
                              Key: e.Attribute(X + "Key")?.Value ?? "(no key)",
                              Color: e.Attribute("Color")?.Value ?? "")))
            .ToList();

        var problems = brushes
            .Select(b => (b, m: Regex.Match(b.Color, @"^\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9]+)\s*\}$")))
            .Where(x => x.m.Success && !colors.Contains(x.m.Groups[1].Value))
            .Select(x => $"{x.b.File}: {x.b.Key} wraps {x.m.Groups[1].Value}, which is not a declared Color")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(brushes, Is.Not.Empty, "no <SolidColorBrush> found — the palette moved or was renamed");
            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        });
    }

    /// <summary>Colours are written <c>#AARRGGBB</c> throughout. A six-digit value is still valid XAML and
    /// still opaque, so the inconsistency is invisible until someone edits the alpha of the odd one out.</summary>
    [Test]
    public void EveryColorIsFullyQualifiedHex()
    {
        var bad = ThemeFiles()
            .SelectMany(t => t.Doc.Descendants()
                .Where(e => e.Name.LocalName == "Color" && e.Attribute(X + "Key") is not null)
                .Select(e => (t.File, Key: e.Attribute(X + "Key")!.Value, Value: e.Value.Trim())))
            .Where(c => !Regex.IsMatch(c.Value, "^#[0-9A-Fa-f]{8}$"))
            .Select(c => $"{c.File}: {c.Key} = {c.Value}")
            .ToList();

        Assert.That(bad, Is.Empty, string.Join(Environment.NewLine, bad));
    }

    /// <summary>Every <c>Mirage*</c> resource the styles reference is defined somewhere in the theme.
    /// Non-Mirage keys are Avalonia's and FluentAvalonia's own and are not this project's to guarantee.</summary>
    [Test]
    public void EveryMirageResourceReferenced_IsDeclared()
    {
        var declared = DeclaredKeys().Select(k => k.Key).ToHashSet();

        var referenced = Directory.GetFiles(ThemeDir(), "*.axaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f),
                    @"\{(?:Static|Dynamic)Resource\s+(Mirage[A-Za-z0-9]*)\s*\}")
                .Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        var missing = referenced.Where(r => !declared.Contains(r.Key))
            .Select(r => $"{r.File} references {r.Key}, which nothing declares")
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(referenced, Is.Not.Empty, "no Mirage* resource references found — the styles moved");
            Assert.That(missing, Is.Empty, string.Join(Environment.NewLine, missing));
        });
    }

    /// <summary>The csproj ships the theme with one glob. A dictionary added outside <c>Theme/</c> compiles
    /// and is simply never packed, so the app falls back and the new styling never appears.</summary>
    [Test]
    public void TheCsprojShipsTheWholeThemeFolder()
    {
        string root = typeof(ThemeResourceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string csproj = File.ReadAllText(
            Path.Combine(root, "shared", "src", "Mirage.Ui", "Mirage.Ui.csproj"));

        Assert.That(csproj, Does.Contain("<AvaloniaResource Include=\"Theme\\**\\*.axaml\" />"),
            "the theme glob changed; a .axaml outside it would never be packed");
    }
}

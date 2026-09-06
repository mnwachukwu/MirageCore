using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Tests.World;

/// <summary>
/// Three sizes are 16x12, and they are three different things: how big a map is, how big the camera's
/// window is, and how far gameplay reaches.
///
/// <para>Because the three numbers match, one can be written in terms of another and everything still
/// works — until a map is 256x256, at which point that map silently hands its occupants a larger camera
/// and a longer spell range than a small one, with no test failing and nothing to see in a diff.</para>
///
/// <para>Only the source can be checked here: at runtime all three are the number 16, so an assertion that
/// they are equal proves nothing and an assertion that they differ would be false. So these read
/// <c>Constants.cs</c> and <c>WorldCoordHelper.cs</c> and hold each group to declaring its own literals.</para>
/// </summary>
[TestFixture]
public class MapSizeIndependenceTests
{
    private static string RepoRoot()
    {
        string root = typeof(MapSizeIndependenceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static string SharedSource(string file)
    {
        string path = Path.Combine(RepoRoot(), "shared", "src", "Mirage.Shared", file);
        Assert.That(File.Exists(path), Is.True, $"Source not found: {path}");
        return File.ReadAllText(path);
    }

    // The right-hand side of `public const int Name = ...;`, wherever it is declared.
    private static string Definition(string source, string name)
    {
        var m = Regex.Match(source, $@"public\s+const\s+int\s+{Regex.Escape(name)}\s*=\s*([^;]+);");
        Assert.That(m.Success, Is.True, $"No `public const int {name}` found. If it was renamed, this test "
                                      + "has to be taught the new name — do not delete it.");
        return m.Groups[1].Value.Trim();
    }

    // ── The camera does not read a map's size ─────────────────────────────────

    [TestCase("ViewportTilesX")]
    [TestCase("ViewportTilesY")]
    public void TheViewport_IsNotDerivedFromTheMapSize(string name)
    {
        string definition = Definition(SharedSource("Constants.cs"), name);

        Assert.That(definition, Does.Match(@"^\d+$"),
            $"Constants.{name} is `{definition}`, not a literal. The camera's window is a property of the "
            + "render target; deriving it from a map's size makes a large map show more of the world.");
    }

    // ── Gameplay reach does not read a map's size ─────────────────────────────

    /// <summary>The spell circle is pinned to the VIEWPORT's short half-extent. Off the map size it would
    /// grow with the map, so casting range would depend on where you are standing.</summary>
    [Test]
    public void TheSpellRadius_IsDerivedFromTheViewportAndNothingElse()
    {
        string definition = Definition(SharedSource("Constants.cs"), "SpellRangeTiles");

        Assert.Multiple(() =>
        {
            Assert.That(definition, Does.Contain("ViewportTilesY"),
                "the spell circle is the largest one fitting the viewport, so it is measured from the viewport");
            foreach (string mapName in (string[])["MaxMapX", "MaxMapY", "DefaultMapWidth", "DefaultMapHeight"])
            {
                Assert.That(definition, Does.Not.Contain(mapName),
                    $"SpellRangeTiles reads {mapName}: a large map would grant a longer cast.");
            }
        });
    }

    // ── The map size does not read the camera ─────────────────────────────────

    [TestCase("DefaultMapWidth")]
    [TestCase("DefaultMapHeight")]
    public void TheDefaultMapSize_IsNotDerivedFromTheViewport(string name)
    {
        string definition = Definition(SharedSource("Constants.cs"), name);

        Assert.That(definition, Does.Match(@"^\d+$"),
            $"Constants.{name} is `{definition}`, not a literal. A new map's size is an authoring default; "
            + "tying it to the camera means resizing the window resizes maps.");
    }

    /// <summary>WorldCoordHelper re-exports both groups under its own names. Each alias must point at its
    /// OWN group's constant; these four lines are where crossing them is easiest and least visible.</summary>
    [TestCase("ViewportTilesX", "Constants.ViewportTilesX")]
    [TestCase("ViewportTilesY", "Constants.ViewportTilesY")]
    [TestCase("MapTilesX", "Constants.DefaultMapWidth")]
    [TestCase("MapTilesY", "Constants.DefaultMapHeight")]
    public void WorldCoordHelper_AliasesEachGroupToItself(string name, string expected)
    {
        Assert.That(Definition(SharedSource("WorldCoordHelper.cs"), name), Is.EqualTo(expected));
    }

    // ── The default size is for STAMPING a new map, and nothing else ──────────

    /// <summary>The names that mean "the size a map is when nothing says otherwise". A live map's size is
    /// <c>MapRecord.Width</c>/<c>Height</c>, or the stride <c>MapGrid</c> was built with.</summary>
    private static readonly string[] DefaultSizeNames =
    [
        "Constants.DefaultMapWidth", "Constants.DefaultMapHeight",
        "Constants.MaxMapX", "Constants.MaxMapY",
        "WorldCoordHelper.MapTilesX", "WorldCoordHelper.MapTilesY",
    ];

    /// <summary>The only places allowed to name one, each because there is no map there to measure.</summary>
    private static readonly (string File, string Why)[] StampingSites =
    [
        ("shared/src/Mirage.Shared/Constants.cs", "declares them"),
        ("shared/src/Mirage.Shared/WorldCoordHelper.cs", "aliases them, and grids a center map that does not exist"),
        ("shared/src/Mirage.Shared/Records/MapRecord.cs", "the tile grid a blank map is stamped with"),
        ("shared/src/Mirage.Shared/Serialization/TileArrayConverter.cs", "the tile grid an empty array deserializes to"),
        ("shared/src/Mirage.Shared/Protocol/Packets/MapPackets.cs", "a new map packet's dimensions"),
        ("server/src/Mirage.Server.Core/Configuration/ServerConfig.cs", "the default spawn tile in a fresh config"),
        ("client/src/Mirage.Client.Core/Logic/Camera.cs", "the stride before the first map arrives; Update overwrites it"),
    ];

    private static IEnumerable<string> ProductionSources(string root)
    {
        foreach (string area in (string[])["client/src", "server/src", "shared/src", "editor/src"])
        {
            string dir = Path.Combine(root, area.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(Directory.Exists(dir), Is.True, $"Source area not found: {dir}");
            foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                yield return rel;
            }
        }
    }

    /// <summary>
    /// 🔴 Nothing outside <see cref="StampingSites"/> may name a default map size.
    ///
    /// <para>The failure this prevents is silent. Half a calculation reads a real map's width off the record
    /// while the other half assumes 16, the two halves are compared, and on a default-sized map they agree
    /// perfectly — so it ships, and the first map authored at another size gets earshot, occupancy and
    /// pathing that quietly answer nonsense. Nothing throws and no diff looks wrong.</para>
    ///
    /// <para>A <c>??</c> is exempt: there is no map on the other side of it to measure.</para>
    /// </summary>
    [Test]
    public void ADefaultMapSize_IsNeverUsedAsALiveMapsSize()
    {
        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (string rel in ProductionSources(root))
        {
            if (StampingSites.Any(s => rel.Equals(s.File, StringComparison.OrdinalIgnoreCase))) continue;
            string[] lines = File.ReadAllLines(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // prose may name them
                foreach (string name in DefaultSizeNames)
                {
                    int at = line.IndexOf(name, StringComparison.Ordinal);
                    if (at < 0) continue;
                    // Exempt only as the right-hand side of a ??: there is no map on the other side to measure.
                    int fallback = line.IndexOf("??", StringComparison.Ordinal);
                    if (fallback >= 0 && fallback < at) continue;
                    offenders.Add($"{rel}:{i + 1}  {line.Trim()}");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "A default map size is being used as a live map's size:\n  " + string.Join("\n  ", offenders)
            + "\n\nRead the real size instead — MapRecord.Width/Height, MapGrid.TilesX/TilesY, or "
            + "ClientState.MapTilesX/Y. WorldCoordHelper.ToWorldRelative(maps, m, m, x, y) and "
            + "MapGrid.CenterToWorld both give a center-cell world coordinate at the map's own size. If this "
            + "really is a new map being stamped, add the file to StampingSites with the reason.");
    }

    /// <summary>The list is only meaningful while every entry is real; a stale path silently exempts nothing
    /// and hides that the rule has drifted.</summary>
    [Test]
    public void EveryStampingSite_StillExists()
    {
        string root = RepoRoot();
        foreach (var (file, why) in StampingSites)
        {
            string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, $"Exempted as \"{why}\", but there is no {file}");
        }
    }

    // ── And the values themselves, so a typo in any literal is caught ─────────

    [Test]
    public void TheThreeGroupsStillAgreeOnTheNumbersTheyHappenToShare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Constants.DefaultMapWidth, Is.EqualTo(16));
            Assert.That(Constants.DefaultMapHeight, Is.EqualTo(12));
            Assert.That(Constants.MaxMapX, Is.EqualTo(15));
            Assert.That(Constants.MaxMapY, Is.EqualTo(11));
            Assert.That(Constants.ViewportTilesX, Is.EqualTo(16));
            Assert.That(Constants.ViewportTilesY, Is.EqualTo(12));
            Assert.That(Constants.SpellRangeTiles, Is.EqualTo(5), "the r=5 circle every range test is written against");
        });
    }
}

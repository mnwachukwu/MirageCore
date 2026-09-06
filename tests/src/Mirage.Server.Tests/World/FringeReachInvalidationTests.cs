using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Which decks can take a spawn is CACHED on <see cref="GameWorld"/>, and the cache is only as good as the
/// invalidation. It answers a whole-world question — a deck on one map reached by a ramp on another — so
/// replacing any map bears on the answer for maps far away from it. A cache that outlives the map it was
/// built from answers for a world nobody is standing in: it refuses spawns on decks that are reachable,
/// and puts them on decks that are not.
///
/// <para>The behaviour half is straightforward. The other half is a SOURCE SCAN, because nothing else would
/// notice: a new path that swaps a map in compiles perfectly, spawns keep working, and the world simply
/// goes on answering with the map it saw at boot. There are only ever a handful of such paths, so the rule
/// is that a file which replaces a map or rewrites its tiles or links must also drop the cache.</para>
/// </summary>
[TestFixture]
public class FringeReachInvalidationTests
{
    const int Deck = 303;

    static void Deck1(GameWorld world, int mapNum, int y, int fromX, int toX)
    {
        var map = world.Maps[mapNum];
        for (int x = fromX; x <= toX; x++)
            map.Tile[x, y] = map.Tile[x, y].WithArt(LayerType.Fringe, [Deck]);
    }

    static void Ramp(GameWorld world, int mapNum, int x, int y)
    {
        var map = world.Maps[mapNum];
        map.Tile[x, y] = map.Tile[x, y] with
        {
            FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down },
        };
    }

    [Test]
    public void ReplacingAMap_ChangesTheAnswerOnceTheCacheIsDropped()
    {
        var world = new GameWorld();
        Deck1(world, 1, y: 4, fromX: 4, toX: 9);
        Ramp(world, 1, 7, 5);
        Assume.That(world.IsFringeSpawnable(1, 7, 4), Is.True, "the deck starts reachable");

        // The map is replaced wholesale, the way a boot load or an authoring save does it.
        world.Maps[1] = new MapRecord(world.Maps[1].Width, world.Maps[1].Height);

        Assert.That(world.IsFringeSpawnable(1, 7, 4), Is.True,
            "until it is told, the world answers with the map it last saw — which is exactly the bug");

        world.InvalidateFringeReach();

        Assert.That(world.IsFringeSpawnable(1, 7, 4), Is.False, "and the new map has no deck at all");
    }

    [Test]
    public void ADeckBecomingReachableIsSeenToo()
    {
        var world = new GameWorld();
        Deck1(world, 1, y: 4, fromX: 4, toX: 9);
        Assume.That(world.IsFringeSpawnable(1, 7, 4), Is.False, "no ramp yet");

        Ramp(world, 1, 7, 5);
        world.InvalidateFringeReach();

        Assert.That(world.IsFringeSpawnable(1, 7, 4), Is.True);
    }

    // ── The source scan ──────────────────────────────────────────────────────────

    /// <summary>The repository root, baked in by the csproj at build time. Deliberately NOT a walk up from
    /// <c>AppContext.BaseDirectory</c>: that finds nothing when the suite is built to a redirected output
    /// path, and the check would skip rather than fail — a guard that can silently not run.</summary>
    static string RepoRoot()
    {
        string root = typeof(FringeReachInvalidationTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    // Replacing a map record, rewriting one of its tiles, or repointing one of its links. Each changes what
    // the flood would find, and none of them is visible to the cache.
    static readonly Regex ReplacesAMap = new(@"\.Maps\[[^\]]+\]\s*=[^=]", RegexOptions.Compiled);
    static readonly Regex RewritesATile = new(@"\.Tile\[[^\]]+\]\s*=[^=]", RegexOptions.Compiled);
    static readonly Regex RepointsALink = new(@"\b(map|Map)\.(Up|Down|Left|Right)\s*=[^=]", RegexOptions.Compiled);

    [Test]
    public void EveryPathThatReplacesAMap_DropsTheCache()
    {
        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (string area in new[] { "server" })
        {
            string dir = Path.Combine(root, area);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                // GameWorld owns the cache; it is where the dropping is implemented, not a caller of it.
                if (rel.EndsWith("/World/GameWorld.cs")) continue;

                string text = File.ReadAllText(file);
                bool mutates = ReplacesAMap.IsMatch(text) || RewritesATile.IsMatch(text) || RepointsALink.IsMatch(text);
                if (mutates && !text.Contains("InvalidateFringeReach")) offenders.Add(rel);
            }
        }

        Assert.That(offenders, Is.Empty,
            "these replace a map, its tiles or its links without dropping the cached fringe reachability, "
            + "so the world goes on answering with the map it saw before: " + string.Join(", ", offenders));
    }

    [Test]
    public void TheScanWouldActuallyCatchSomething()
    {
        // A guard that matches nothing is a guard nobody notices has stopped working.
        string root = RepoRoot();
        int mutating = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "server"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.EndsWith("/World/GameWorld.cs")) continue;
            string text = File.ReadAllText(file);
            if (ReplacesAMap.IsMatch(text) || RewritesATile.IsMatch(text) || RepointsALink.IsMatch(text)) mutating++;
        }

        Assert.That(mutating, Is.GreaterThan(0),
            "the patterns match no file at all — the map-mutation paths were renamed out from under this");
    }
}

using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// The shipped world's authored content actually reaches the code that reads it.
///
/// <para>🔴 <b>An unknown key is silently dropped, which makes authored content that nobody reads look
/// exactly like content that is not there.</b> A placement written under the wrong name parses without
/// complaint into a record of zeroes, and the spawner then declines it for being empty — no warning, no
/// log line, nothing in a test, and an NPC that has simply never existed. The counts in the seed check
/// do not help: the RECORD is present and correct, and it is the pointer to it that goes nowhere.</para>
///
/// <para>This reads the seed the way the server does and asserts that what it finds is alive.</para>
/// </summary>
[TestFixture]
public class SeedWorldIsAliveTests
{
    private static string SeedWorld
    {
        get
        {
            string root = typeof(SeedWorldIsAliveTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "RepoRoot").Value!;
            return Path.Combine(root, "server", "src", "Mirage.Server.Host", "world");
        }
    }

    private static IEnumerable<(string File, MapRecord Map)> Maps()
    {
        string dir = Path.Combine(SeedWorld, "maps");
        Assert.That(Directory.Exists(dir), Is.True, $"no seed maps at {dir} — if the seed moved, teach this test where");

        foreach (string path in Directory.EnumerateFiles(dir, "map*.json"))
        {
            var map = JsonSerializer.Deserialize<MapRecord>(File.ReadAllText(path), RecordJson.Options);
            if (map is not null) yield return (Path.GetFileName(path), map);
        }
    }

    /// <summary>🔴 Every NPC the seed places names a real one. A placement reading zero is one the
    /// spawner drops on the floor.</summary>
    [Test]
    public void EveryPlacedNpc_NamesARealOne()
    {
        var placements = Maps().SelectMany(m => m.Map.Npcs.Select((n, i) => (m.File, Index: i, Entry: n))).ToList();

        Assert.That(placements, Is.Not.Empty,
            "the seed places no NPCs at all — either the demo lost them, or this is reading the wrong field");

        Assert.Multiple(() =>
        {
            foreach (var (file, index, entry) in placements)
            {
                Assert.That(entry.Npc, Is.GreaterThan(0),
                    $"{file} placement {index} resolves to NPC 0, so nothing spawns. A placement written "
                    + "under a key this record does not declare reads back as zero.");

                string record = Path.Combine(SeedWorld, "npcs", $"npc{entry.Npc}.json");
                Assert.That(File.Exists(record), Is.True, $"{file} places NPC {entry.Npc}, which has no record");
            }
        });
    }

    /// <summary>
    /// 🔴 Every key in every seed file is one its record declares.
    ///
    /// <para>This is the general form of the check above, and it catches the same failure everywhere
    /// rather than for NPCs alone: a key under a name the record does not know is dropped in silence, so
    /// authored content reads back as a default and whatever consumes it declines to act. Reading the
    /// seed with <see cref="JsonUnmappedMemberHandling.Disallow"/> turns that silence into a failure with
    /// the file and the key in it.</para>
    ///
    /// <para>Strict for the SEED specifically. A world somebody else authored is read leniently on
    /// purpose — a file from a later build must not stop a server — but this one ships in the repository
    /// as the worked example, and an example nothing reads teaches the wrong shape.</para>
    /// </summary>
    [Test]
    public void EveryKeyInTheSeed_IsOneItsRecordDeclares()
    {
        var strict = new JsonSerializerOptions(RecordJson.Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        (string Folder, Type Record)[] families =
        [
            ("maps", typeof(MapRecord)),
            ("items", typeof(ItemRecord)),
            ("npcs", typeof(NpcRecord)),
            ("shops", typeof(ShopRecord)),
            ("conversations", typeof(ConversationRecord)),
        ];

        var problems = new List<string>();
        int read = 0;
        foreach (var (folder, record) in families)
        {
            string dir = Path.Combine(SeedWorld, folder);
            if (!Directory.Exists(dir)) continue;

            foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
            {
                read++;
                try { JsonSerializer.Deserialize(File.ReadAllText(path), record, strict); }
                catch (JsonException ex)
                {
                    problems.Add($"  {folder}/{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        Assert.That(read, Is.GreaterThan(0), "no seed records were read — has the seed moved?");
        Assert.That(problems, Is.Empty,
            "the seed authors keys nothing reads, so that content silently does not exist:\n"
            + string.Join("\n", problems));
    }

    /// <summary>A placement without a pin spawns somewhere random, which is a fine thing to author and a
    /// poor thing to demonstrate with: the demo's NPCs are where they are so a player can find them.</summary>
    [Test]
    public void EveryPlacedNpc_IsPinnedToATile()
    {
        Assert.Multiple(() =>
        {
            foreach (var (file, map) in Maps())
            {
                for (int i = 0; i < map.Npcs.Count; i++)
                {
                    Assert.That(map.Npcs[i].HasPin, Is.True,
                        $"{file} placement {i} names no tile, so it spawns wherever there is room");
                }
            }
        });
    }

    /// <summary>
    /// 🔴 The seed's second walkable plane is reachable, sealed, and has ground underneath it.
    ///
    /// <para>A bridge is two halves and either one missing is silent. A deck with no ramp is a surface
    /// nothing can climb onto; a ramp whose ground side faces the wrong way is a solid block that reads
    /// as scenery. Neither is malformed — the file loads, the tiles draw, and only walking the movement
    /// rules says the thing cannot be used.</para>
    ///
    /// <para>The third trap is the plane's own shape: the fringe plane is UNIFORM and walkable wherever
    /// it is not told otherwise, so a deck authored without railings is not a bridge but an invisible
    /// floor over the whole map. This walks the plane from the ramps and insists every square it reaches
    /// was authored as part of it.</para>
    /// </summary>
    [Test]
    public void TheSecondPlane_IsReachableSealedAndHasGroundUnderIt()
    {
        var layered = Maps().Where(m => Tiles(m.Map).Any(t => t.Tile.FringeAttr is not null)).ToList();

        Assert.That(layered, Is.Not.Empty,
            "no seed map declares a fringe plane, so the two-plane movement rules ship with nothing "
            + "standing on them — author a deck and its ramps, or teach this test where they went");

        Assert.Multiple(() =>
        {
            foreach (var (file, map) in layered)
            {
                var view = new MapView(map);
                var plane = Tiles(map).Where(t => t.Tile.FringeAttr is not null)
                                      .Select(t => (t.X, t.Y)).ToHashSet();
                var ramps = Tiles(map).Where(t => t.Tile.FringeAttr is { Type: TileType.LayerRamp })
                                      .Select(t => (t.X, t.Y, Side: t.Tile.FringeAttr!.RampGroundSide)).ToList();
                var deck = Tiles(map).Where(t => t.Tile.FringeAttr is { Type: TileType.Walkable })
                                     .Select(t => (t.X, t.Y)).ToHashSet();

                Assert.That(ramps, Is.Not.Empty, $"{file} has a fringe plane and no ramp onto it");
                Assert.That(deck, Is.Not.Empty, $"{file} has ramps that lead to no walkable deck");

                // A ramp is mounted from the ground at its foot, moving up it. Ramps deeper into a block
                // have another ramp at their foot, which reads Blocked from below — those are interior.
                var mounted = new HashSet<(int, int)>();
                foreach (var (x, y, side) in ramps)
                {
                    var (dx, dy) = WorldCoordHelper.DirDelta(side);
                    if (view.At(x + dx, y + dy) is not { } foot) continue;
                    if (LayerLogic.AttrFor(foot, WorldLayer.Ground).Type != TileType.Walkable) continue;

                    bool climbs = LayerLogic.CanEnter(view, x, y, 1, WorldLayer.Ground, Opposite(side), out var landed)
                                  && landed == WorldLayer.Fringe;
                    Assert.That(climbs, Is.True,
                        $"{file} ramp at {x},{y} faces {side}, and stepping onto it from the ground at "
                        + $"{x + dx},{y + dy} does not climb — a ramp nobody can mount is scenery");
                    mounted.Add((x, y));
                }

                Assert.That(mounted, Is.Not.Empty,
                    $"{file} has ramps, and every one of them is walled in on its ground side");

                var reached = Reachable(view, mounted);

                Assert.That(deck.Except(reached), Is.Empty,
                    $"{file} authors deck squares the ramps do not lead to");
                Assert.That(reached.Except(plane), Is.Empty,
                    $"{file} lets a walker leave the deck onto squares nobody authored — the fringe plane "
                    + "is walkable wherever it is not told otherwise, so a deck needs a Blocked fringe "
                    + "attribute around its edge to be a bridge rather than an invisible floor");

                Assert.That(deck.Any(d => LayerLogic.AttrFor(map.Tile[d.X, d.Y], WorldLayer.Ground).Type
                                          == TileType.Walkable), Is.True,
                    $"{file} has a deck with nothing walkable under any of it, which is a floor rather "
                    + "than a bridge");
            }
        });
    }

    /// <summary>Every square of the fringe plane a walker can reach from the given mount points, by the
    /// same rules movement uses.</summary>
    private static HashSet<(int X, int Y)> Reachable(MapView view, IEnumerable<(int, int)> from)
    {
        var seen = from.ToHashSet();
        var queue = new Queue<(int X, int Y)>(seen);
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            foreach (var dir in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
            {
                var (dx, dy) = WorldCoordHelper.DirDelta(dir);
                int nx = cx + dx, ny = cy + dy;
                if (seen.Contains((nx, ny)) || view.At(nx, ny) is not { } next) continue;
                if (!LayerLogic.CanEnter(view, nx, ny, 1, WorldLayer.Fringe, dir, out var layer)) continue;
                if (layer != WorldLayer.Fringe) continue;
                // A ramp's own surface is the fringe plane's, so it reads LayerRamp rather than Walkable.
                if (LayerLogic.AttrFor(next, WorldLayer.Fringe).Type
                    is not (TileType.Walkable or TileType.LayerRamp)) continue;
                seen.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }
        return seen;
    }

    private static Direction Opposite(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };

    private static IEnumerable<(int X, int Y, TileRecord Tile)> Tiles(MapRecord map)
    {
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                yield return (x, y, map.Tile[x, y]);
    }

    /// <summary>One map as the movement rules read the world: a coordinate off the map is nothing, which
    /// is what stops a walk at the edge.</summary>
    private sealed class MapView(MapRecord map) : LayerLogic.IWorldTileView
    {
        public TileRecord? At(int worldX, int worldY) =>
            worldX < 0 || worldY < 0 || worldX >= map.Width || worldY >= map.Height
                ? null
                : map.Tile[worldX, worldY];
    }
}

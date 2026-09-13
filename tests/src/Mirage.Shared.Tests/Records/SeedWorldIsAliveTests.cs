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
}

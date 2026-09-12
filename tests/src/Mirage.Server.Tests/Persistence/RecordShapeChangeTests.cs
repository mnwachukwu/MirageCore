using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.Persistence;

/// <summary>
/// What a record file written by an OLDER build does when this build reads it.
///
/// <para>Two rules govern every field a record loses or gains, and they point opposite ways. REMOVING a
/// property is safe: the reader ignores a member it has no home for, so a file carrying it still loads and
/// every declared field arrives intact. Changing a persisted property's TYPE is not, and neither is RENAMING
/// an enum member — enums are written as names, and a name the type does not define throws.</para>
///
/// <para>🔴 The type-change failure is invisible where it matters most. An account file that does not
/// deserialize is caught and returns null; the existence check still answers yes; the password check loads,
/// gets null, and reports the password as wrong. A player is locked out permanently with one log line as the
/// only evidence. This is the only fixture that reads a record written in a shape the build does not itself
/// produce, so it is the only one that can catch either rule breaking.</para>
/// </summary>
[TestFixture]
public class RecordShapeChangeTests
{
    private static readonly JsonSerializerOptions Options = RecordJson.Options;

    // ── Removing a property ──────────────────────────────────────────────────

    /// <summary>A player file carrying six properties the record does not declare. It must load, and every
    /// property the record DOES declare must arrive intact.</summary>
    [Test]
    public void APlayerFileCarryingRemovedProperties_StillLoads()
    {
        const string json = """
        {
          "name": "Reedwyn",
          "sex": "Female",
          "class": 2,
          "sprite": 14,
          "level": 30,
          "exp": 91234,
          "respawnPenaltySteps": 4,
          "lastDeathUtc": 1750000000,
          "diedInWar": true,
          "diedInTerritory": 7,
          "map": 15,
          "x": 9,
          "y": 4,
          "dir": "Left"
        }
        """;

        var p = JsonSerializer.Deserialize<PlayerRecord>(json, Options);

        Assert.That(p, Is.Not.Null, "a file naming properties this build has dropped must still deserialize");
        Assert.Multiple(() =>
        {
            Assert.That(p!.Name, Is.EqualTo("Reedwyn"));
            Assert.That(p.Map, Is.EqualTo(15));
            Assert.That(p.X, Is.EqualTo(9));
            Assert.That(p.Y, Is.EqualTo(4));
            Assert.That(p.Dir, Is.EqualTo(Direction.Left), "an enum beside a dropped one still reads");
        });
    }

    /// <summary>The same for a guild, whose undeclared properties include a nested one: the roster and the
    /// vault must arrive intact past them.</summary>
    [Test]
    public void AGuildFileCarryingRemovedProperties_StillLoads()
    {
        const string json = """
        {
          "name": "The Oarsmen",
          "color": 16711680,
          "motd": "Row.",
          "level": 5,
          "exp": 400000,
          "perksActive": false,
          "vaultGold": 12500,
          "vaultValor": 90,
          "seasonScore": 41,
          "seasonStanding": 2,
          "foundingWeekday": "Tuesday",
          "members": [ { "login": "matt", "rank": "Leader", "charName": "Reedwyn" } ]
        }
        """;

        var g = JsonSerializer.Deserialize<GuildRecord>(json, Options);

        Assert.That(g, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(g!.Name, Is.EqualTo("The Oarsmen"));
            Assert.That(g.VaultGold, Is.EqualTo(12500));
            Assert.That(g.Members, Has.Count.EqualTo(1));
            Assert.That(g.Members[0].Rank, Is.EqualTo(GuildRank.Leader), "the roster's own enum still reads");
            Assert.That(g.Attributes, Is.Not.Null, "an older file simply has no attribute bag; it must not be null");
        });
    }

    /// <summary>🔴 RENAMING an enum member is as fatal as changing its type, and for the same reason:
    /// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> throws on a name it does not
    /// recognise rather than defaulting. A file naming the old member does not load AT ALL — the record is
    /// lost whole, not one field of it.
    ///
    /// <para>So an enum member is renamed only in a change that also remaps every authored file naming it.
    /// Leave the two apart and the world stops loading.</para></summary>
    [Test]
    public void ARecordNamingAnUndefinedEnumMember_Throws()
    {
        const string json = """{ "name": "Wolf", "behavior": "AttackOnSight", "range": 6 }""";

        Assert.That(() => JsonSerializer.Deserialize<NpcRecord>(json, Options),
            Throws.InstanceOf<JsonException>(),
            "an unrecognised enum name must fail loudly, so a rename can never half-land");
    }

    /// <summary>The tile converter is the exception, and the difference is worth knowing before relying on
    /// either. It is hand-written, and it FALLS BACK to <see cref="TileType.Walkable"/> on a name it does not
    /// recognise — so a renamed tile type does not stop a map loading. It silently retypes every tile that
    /// carried the old name, which no error reports and no reader notices.</summary>
    [Test]
    public void AMapNamingAnUndefinedTileType_FallsBackSilently()
    {
        // Write a real map through the real serializer, so the tile-array shape under test is the one on
        // disk rather than one written by hand here.
        var authored = new MapRecord(2, 1) { Name = "M" };
        authored.Tile[0, 0] = authored.Tile[0, 0] with { Type = TileType.Blocked };
        authored.Tile[1, 0] = authored.Tile[1, 0] with { Type = TileType.Blocked };
        string onDisk = JsonSerializer.Serialize(authored, Options);
        Assume.That(onDisk, Does.Contain("Blocked"), "sanity: the type is written by name");

        // One of the two tiles now names a type the enum does not define.
        int at = onDisk.IndexOf("Blocked", onDisk.IndexOf("Blocked", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        string renamed = onDisk[..at] + "Vanished" + onDisk[(at + "Blocked".Length)..];

        var m = JsonSerializer.Deserialize<MapRecord>(renamed, Options);

        Assert.That(m, Is.Not.Null, "an unrecognised TILE type does not stop the map loading");
        Assert.Multiple(() =>
        {
            Assert.That(m!.Tile[0, 0].Type, Is.EqualTo(TileType.Blocked), "a name it knows reads normally");
            Assert.That(m.Tile[1, 0].Type, Is.EqualTo(TileType.Walkable),
                "and one it does not becomes Walkable, with nothing reported");
        });
    }

    // ── Changing a property's type ───────────────────────────────────────────

    /// <summary>The rule that makes the above safe is the same one that makes a type change fatal: every
    /// persisted enum is a NAME on disk. Widening one to an integer leaves every file on disk with a string in
    /// a numeric slot.</summary>
    [Test]
    public void EveryPersistedEnum_IsWrittenAsAName()
    {
        string player = JsonSerializer.Serialize(new PlayerRecord { Name = "N", Dir = Direction.Right }, Options);
        string guild = JsonSerializer.Serialize(
            new GuildRecord { Name = "G", Members = { new GuildMember { Login = "m", Rank = GuildRank.Officer } } }, Options);
        string npc = JsonSerializer.Serialize(new NpcRecord { Name = "W", Behavior = NpcBehavior.Pursue }, Options);

        Assert.Multiple(() =>
        {
            Assert.That(player, Does.Contain("\"Right\"").And.Not.Contain("\"dir\": 3"));
            Assert.That(guild, Does.Contain("\"Officer\""));
            Assert.That(npc, Does.Contain("\"Pursue\""));
        });
    }

    /// <summary>And the failure that causes, stated outright: a name in a slot the type says is numeric throws
    /// rather than defaulting, so the whole record is lost, not one field of it.</summary>
    [Test]
    public void AnEnumNameInANumericSlot_Throws()
    {
        const string json = """{ "name": "Reedwyn", "map": "Fifteen" }""";

        Assert.That(() => JsonSerializer.Deserialize<PlayerRecord>(json, Options),
            Throws.InstanceOf<JsonException>(),
            "a word where a number belongs is a file this build cannot read, and it says so");
    }
}

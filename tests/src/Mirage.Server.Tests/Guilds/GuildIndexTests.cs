using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.Guilds;

/// <summary>
/// A guild's number lives in its filename. <c>GuildRecord.Index</c> is <c>[JsonIgnore]</c>d and filled in
/// by the loader, because the guild, territory and war code holds a guild detached from any dictionary key
/// and asks it which one it is — <c>DirtyGuilds.Add(guild.Index)</c>, territory challenges, war credit.
///
/// <para>The loader keying the dictionary by filename is not enough on its own: the record itself is what
/// the rest of the server reads, so the number has to be stamped onto it, not just used as a key.</para>
///
/// <para>Fixtures are built in a temp directory. Guilds are runtime state — nothing ships any — so there
/// is no authored content to read even if reading it were the right idea.</para>
/// </summary>
[TestFixture]
public class GuildIndexTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-guild-" + Guid.NewGuid().ToString("N"));
        _svc = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private void WriteRaw(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_dir, "guilds", fileName), json);

    private static string FileFor(int num) => $"{GuildRecord.FileStem}{num}.json";

    /// <summary>The number reaches the RECORD, not just the dictionary key — everything downstream reads
    /// <c>guild.Index</c> off a guild it was handed, with no key in sight.</summary>
    [Test]
    public async Task TheLoaderNumbersEachGuildFromItsFilename()
    {
        WriteRaw(FileFor(3), """{"name":"Ironbound"}""");

        var loaded = await _svc.LoadAllGuildsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 3 }));
            Assert.That(loaded[3].Index, Is.EqualTo(3), "the record itself must carry its number");
            Assert.That(loaded[3].Name, Is.EqualTo("Ironbound"));
        });
    }

    /// <summary>A guild file copied into another slot carries the old number. The filename wins.</summary>
    [Test]
    public async Task AStaleIndexInTheFile_IsIgnored()
    {
        WriteRaw(FileFor(6), """{"index":42,"name":"Copied"}""");

        var loaded = await _svc.LoadAllGuildsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 6 }));
            Assert.That(loaded[6].Index, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task SavingThenLoading_RoundTripsEveryGuild()
    {
        await _svc.SaveGuildAsync(1, new GuildRecord { Index = 1, Name = "Ironbound" });
        await _svc.SaveGuildAsync(12, new GuildRecord { Index = 12, Name = "Ashfell" });

        var loaded = await _svc.LoadAllGuildsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 1, 12 }));
            Assert.That(loaded[1].Name, Is.EqualTo("Ironbound"));
            Assert.That(loaded[12].Name, Is.EqualTo("Ashfell"));
            Assert.That(loaded[12].Index, Is.EqualTo(12));
        });
    }

    [Test]
    public async Task SaveWritesTheNameLoadLooksFor_AndNoIndexInside()
    {
        await _svc.SaveGuildAsync(9, new GuildRecord { Index = 9, Name = "Written" });

        string expected = Path.Combine(_dir, "guilds", FileFor(9));
        Assert.That(File.Exists(expected), Is.True, $"expected the guild at {expected}");

        using var doc = JsonDocument.Parse(File.ReadAllText(expected));
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("index", out _), Is.False,
                "the number is the filename; writing it inside too gives it a second copy to disagree with");
            Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Written"));
        });
    }

    [TestCase("notes.json")]
    [TestCase("guild.json")]
    [TestCase("guildX.json")]
    public async Task FilesThatDoNotNameAGuild_AreIgnored(string fileName)
    {
        WriteRaw(fileName, """{"name":"Should Not Load"}""");

        var loaded = await _svc.LoadAllGuildsAsync();

        Assert.That(loaded, Is.Empty);
    }

    // ── Retirement ────────────────────────────────────────────────────────────

    /// <summary>Retiring keeps everything: the file, the name and the roster. Only the guild's presence
    /// among the live ones goes.</summary>
    [Test]
    public async Task ARetiredGuild_IsKeptButNotLoadedAsLive()
    {
        var guild = new GuildRecord { Index = 3, Name = "Disbanding" };
        guild.Members.Add(new GuildMember { Login = "founder", Rank = GuildRank.Leader });
        await _svc.SaveGuildAsync(3, guild);

        await _svc.RetireGuildAsync(3, guild);

        string path = Path.Combine(_dir, "guilds", FileFor(3));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Multiple(() =>
        {
            Assert.That(_svc.LoadAllGuildsAsync().Result, Is.Empty);
            Assert.That(doc.RootElement.GetProperty("disbanded").GetBoolean(), Is.True);
            Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Disbanding"),
                "the history is preserved, not erased");
            Assert.That(doc.RootElement.GetProperty("members").GetArrayLength(), Is.EqualTo(1));
        });
    }

    /// <summary>The case the high-water mark exists for: the HIGHEST guild disbands. Taking the maximum of
    /// the live guilds would hand its number straight to the next one founded, and an account, a territory
    /// controller or a war still holding that number would silently point at a different guild.</summary>
    [Test]
    public async Task RetiringTheHighestGuild_DoesNotFreeItsNumber()
    {
        await _svc.SaveGuildAsync(1, new GuildRecord { Index = 1, Name = "One" });
        await _svc.SaveGuildAsync(2, new GuildRecord { Index = 2, Name = "Two" });
        await _svc.SaveGuildAsync(3, new GuildRecord { Index = 3, Name = "Three" });

        await _svc.RetireGuildAsync(3, new GuildRecord { Index = 3, Name = "Three" });

        var live = await _svc.LoadAllGuildsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(live.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(_svc.HighestGuildNumberAsync().Result, Is.EqualTo(3),
                "the next guild founded takes 4");
        });
    }

    [Test]
    public async Task RetiringAMiddleGuild_LeavesAHoleThatIsNeverRefilled()
    {
        await _svc.SaveGuildAsync(1, new GuildRecord { Index = 1, Name = "One" });
        await _svc.SaveGuildAsync(2, new GuildRecord { Index = 2, Name = "Two" });
        await _svc.SaveGuildAsync(3, new GuildRecord { Index = 3, Name = "Three" });

        await _svc.RetireGuildAsync(2, new GuildRecord { Index = 2, Name = "Two" });

        var live = await _svc.LoadAllGuildsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(live.Keys, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(_svc.HighestGuildNumberAsync().Result, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AFreshWorld_HasIssuedNoGuildNumbers()
    {
        Assert.That(await _svc.HighestGuildNumberAsync(), Is.EqualTo(0));
    }

    /// <summary>The mark survives a restart with no counter to keep in step: it is read back off the two
    /// folders, so nothing can drift out of agreement with what is on disk.</summary>
    [Test]
    public async Task TheHighWaterMark_SurvivesReadingItBackFromDisk()
    {
        await _svc.SaveGuildAsync(7, new GuildRecord { Index = 7, Name = "Seven" });
        await _svc.RetireGuildAsync(7, new GuildRecord { Index = 7, Name = "Seven" });

        var reopened = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());

        Assert.Multiple(() =>
        {
            Assert.That(reopened.HighestGuildNumberAsync().Result, Is.EqualTo(7));
            Assert.That(reopened.LoadAllGuildsAsync().Result, Is.Empty);
        });
    }

    /// <summary>Retiring writes the record, so a number with no file yet still becomes spoken for — a
    /// guild founded and disbanded before its first ordinary save cannot hand its number on.</summary>
    [Test]
    public async Task RetiringAGuildWithNoFileYet_StillClaimsTheNumber()
    {
        await _svc.SaveGuildAsync(1, new GuildRecord { Index = 1, Name = "One" });

        await _svc.RetireGuildAsync(4, new GuildRecord { Index = 4, Name = "Brief" });

        Assert.Multiple(() =>
        {
            Assert.That(_svc.LoadAllGuildsAsync().Result.Keys, Is.EquivalentTo(new[] { 1 }));
            Assert.That(_svc.HighestGuildNumberAsync().Result, Is.EqualTo(4));
        });
    }

    /// <summary>The flag is what retires the number, so a guild saved normally must never carry it.</summary>
    [Test]
    public async Task AnOrdinarySave_IsNotDisbanded()
    {
        await _svc.SaveGuildAsync(1, new GuildRecord { Index = 1, Name = "Live" });

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "guilds", FileFor(1))));
        Assert.That(doc.RootElement.GetProperty("disbanded").GetBoolean(), Is.False);
    }
}

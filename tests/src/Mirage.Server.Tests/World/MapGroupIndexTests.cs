using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A map group's number lives in exactly one place: its filename. <c>MapGroupRecord.Index</c> is
/// <c>[JsonIgnore]</c>d and filled in by the loader, because the guild, territory and combat code holds a
/// group detached from any dictionary key and asks it which one it is.
///
/// <para>Every other record keys off its filename the same way and stores no id of its own, so there is
/// no second copy to disagree with. A file that carries one anyway — written before the field was ignored,
/// or copied from another group — is not believed.</para>
///
/// <para>These build their own map groups in a temp directory. The guarantee under test belongs to the
/// loader; pointing it at the shipped seed would test whichever files happen to be committed today.</para>
/// </summary>
[TestFixture]
public class MapGroupIndexTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-mapgroup-" + Guid.NewGuid().ToString("N"));
        _svc = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private void WriteRaw(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_dir, "map_groups", fileName), json);

    /// <summary>The stem the loader writes and reads. Named through the record so a rename moves both.</summary>
    private static string FileFor(int num) => $"{MapGroupRecord.FileStem}{num}.json";

    [Test]
    public async Task TheLoaderNumbersEachGroupFromItsFilename()
    {
        WriteRaw(FileFor(4), """{"name":"Fenn's Landing"}""");

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 4 }));
            Assert.That(loaded[4].Index, Is.EqualTo(4), "the territory code reads Index off a detached group");
            Assert.That(loaded[4].Name, Is.EqualTo("Fenn's Landing"));
        });
    }

    /// <summary>A world written before the field was ignored still has <c>"index"</c> in its files, and a
    /// group copied to a new slot carries the old one. Neither is believed over the filename.</summary>
    [Test]
    public async Task AStaleIndexInTheFile_IsIgnored()
    {
        WriteRaw(FileFor(7), """{"index":99,"name":"Copied From Another Group"}""");

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 7 }));
            Assert.That(loaded[7].Index, Is.EqualTo(7));
            Assert.That(loaded[7].Name, Is.EqualTo("Copied From Another Group"));
        });
    }

    [Test]
    public async Task SavingThenLoading_RoundTripsEveryGroup()
    {
        await _svc.SaveMapGroupAsync(2, new MapGroupRecord { Index = 2, Name = "Harbour", Music = 9 });
        await _svc.SaveMapGroupAsync(5, new MapGroupRecord { Index = 5, Name = "Catacombs", Territory = true });

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 2, 5 }));
            Assert.That(loaded[2].Name, Is.EqualTo("Harbour"));
            Assert.That(loaded[2].Music, Is.EqualTo(9));
            Assert.That(loaded[5].Territory, Is.True);
        });
    }

    /// <summary>Groups are stored sparsely, so the loader keys by the number in the name rather than by
    /// enumeration order — which would sort "1, 10, 2" and hand back the wrong record.</summary>
    [Test]
    public async Task GroupsAreKeyedByTheirNumber_NotByDirectoryOrder()
    {
        foreach (int n in new[] { 1, 2, 10, 11, 100 })
            await _svc.SaveMapGroupAsync(n, new MapGroupRecord { Index = n, Name = $"Group {n}" });

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 1, 2, 10, 11, 100 }));
            foreach (int n in new[] { 1, 2, 10, 11, 100 })
                Assert.That(loaded[n].Name, Is.EqualTo($"Group {n}"), $"group {n} resolved to the wrong record");
        });
    }

    /// <summary>Anything that is not <c>map_group{N}.json</c> is skipped rather than guessed at: the stem
    /// has to match exactly and what follows it has to parse as a number.</summary>
    [TestCase("notes.json")]
    [TestCase("map_group.json")]
    [TestCase("map_groupX.json")]
    [TestCase("mapgroup3.json")]
    public async Task FilesThatDoNotNameAGroup_AreIgnored(string fileName)
    {
        WriteRaw(fileName, """{"index":3,"name":"Should Not Load"}""");

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.That(loaded, Is.Empty);
    }

    /// <summary>A file the loader cannot parse is skipped, not fatal: one bad group must not stop a server
    /// from booting with the rest of its world.</summary>
    [Test]
    public async Task AnUnparseableFile_DoesNotStopTheOthersLoading()
    {
        WriteRaw(FileFor(1), "{ this is not json");
        await _svc.SaveMapGroupAsync(2, new MapGroupRecord { Index = 2, Name = "Fine" });

        var loaded = await _svc.LoadAllMapGroupsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 2 }));
            Assert.That(loaded[2].Name, Is.EqualTo("Fine"));
        });
    }

    /// <summary>The file the loader writes is the file it reads — the name comes from the shared stem, so
    /// a rename on one side cannot silently orphan the other — and it carries no id of its own.</summary>
    [Test]
    public async Task SaveWritesTheNameLoadLooksFor_AndNoIndexInside()
    {
        await _svc.SaveMapGroupAsync(8, new MapGroupRecord { Index = 8, Name = "Written" });

        string expected = Path.Combine(_dir, "map_groups", FileFor(8));
        Assert.That(File.Exists(expected), Is.True, $"expected the group at {expected}");

        using var doc = JsonDocument.Parse(File.ReadAllText(expected));
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("index", out _), Is.False,
                "the number is the filename; writing it inside too gives it a second copy to disagree with");
            Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Written"));
        });
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A game's family on disk, in a folder the engine learned about from the module that declared it.
///
/// <para>Folder, filename and padding all come off the <see cref="RecordFamily"/>, so these pin the one
/// thing that makes a game's records portable: the world folder a player hands over carries a module's
/// records the same way it carries Core's, with no line in the engine naming them.</para>
/// </summary>
[TestFixture]
public class ModuleRecordPersistenceTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private static readonly RecordFamily Species = new()
    {
        Id = "Species",
        Directory = "species",
        FilePrefix = "species",
        DefaultLimit = 3,
        LimitIsFixed = true,
    };

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-modrec-" + Guid.NewGuid().ToString("N"));
        _svc = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private string FileFor(int num) => Path.Combine(_dir, "species", $"species{num}.json");

    /// <summary>The folder does not exist until a module declares the family, so the first load is what
    /// creates it and fills it with blanks — the same thing Core's own families get at boot.</summary>
    [Test]
    public async Task TheFirstLoad_CreatesTheFolderAndAFilePerSlot()
    {
        var (records, padded) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(padded, Is.EqualTo(3));
            Assert.That(records, Has.Length.EqualTo(4), "1-based, with index 0 unused");
            Assert.That(File.Exists(FileFor(1)), Is.True);
            Assert.That(File.Exists(FileFor(3)), Is.True);
            Assert.That(File.Exists(FileFor(4)), Is.False, "and stops at the family's limit");
        });
    }

    [Test]
    public async Task ASavedRecord_ComesBackOnTheNextLoad()
    {
        await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        await _svc.SaveModuleRecordAsync(Species, 2, new AttributeBag()
            .Set("name", "Vulpine")
            .Set("baseSpeed", 65)
            .Set("evolves", true));

        var (records, padded) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(padded, Is.Zero, "nothing had to be created the second time");
            Assert.That(records[2]["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(records[2]["baseSpeed"].AsLong(), Is.EqualTo(65));
            Assert.That(records[2]["evolves"].AsBool(), Is.True);
            Assert.That(records[1].IsEmpty, Is.True, "and its neighbours are still blank");
        });
    }

    /// <summary>A key no field describes survives the round trip. An older editor opening a world
    /// authored against a newer build of the game must not quietly strip what it cannot show.</summary>
    [Test]
    public async Task AKeyNothingDescribes_SurvivesTheRoundTrip()
    {
        await _svc.SaveModuleRecordAsync(Species, 1, new AttributeBag().Set("hiddenAbility", "Drizzle"));

        var (records, _) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.That(records[1]["hiddenAbility"].AsText(), Is.EqualTo("Drizzle"));
    }

    [Test]
    public async Task ASlotOutsideTheFamilysRange_IsNotWritten()
    {
        await _svc.SaveModuleRecordAsync(Species, 0, new AttributeBag().Set("name", "Nowhere"));
        await _svc.SaveModuleRecordAsync(Species, 4, new AttributeBag().Set("name", "Nowhere"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(FileFor(0)), Is.False);
            Assert.That(File.Exists(FileFor(4)), Is.False);
        });
    }

    // Both come off the family the module declared, so a game adding a family needs no line in the engine.
    [Test]
    public async Task TheFolderAndFilenameComeFromTheFamily()
    {
        var moves = new RecordFamily { Id = "Moves", DefaultLimit = 1, LimitIsFixed = true };

        await _svc.SaveModuleRecordAsync(moves, 1, new AttributeBag().Set("name", "Ember"));

        Assert.That(File.Exists(Path.Combine(_dir, "moves", "move1.json")), Is.True,
                    "a blank Directory falls back to the id lowercased, and the prefix to that singularized");
    }

    /// <summary>A corrupt file leaves that slot blank and the rest of the family loaded. One unreadable
    /// record is not a reason to refuse to start.</summary>
    [Test]
    public async Task AnUnreadableFile_CostsOnlyItsOwnSlot()
    {
        await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);
        await _svc.SaveModuleRecordAsync(Species, 3, new AttributeBag().Set("name", "Intact"));
        await File.WriteAllTextAsync(FileFor(2), "{ this is not json");

        var (records, _) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(records[2].IsEmpty, Is.True);
            Assert.That(records[3]["name"].AsText(), Is.EqualTo("Intact"));
        });
    }
}

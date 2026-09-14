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
    /// creates it — and it creates NOTHING inside it.
    ///
    /// <para>🔴 A blank slot used to be written out as a file, which meant a world holding twenty
    /// authored records became some five thousand files the first time a server opened it. The array is
    /// already padded in memory before the read, so the write bought nothing and cost a world folder that
    /// could not be handed to anybody as the handful of files it actually is.</para></summary>
    [Test]
    public async Task TheFirstLoad_CreatesTheFolderAndNothingInIt()
    {
        var (records, loaded) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Zero, "nothing was there to read");
            Assert.That(records, Has.Length.EqualTo(4), "1-based, with index 0 unused");
            Assert.That(records[1].IsEmpty, Is.True, "and every slot is a blank in memory");

            Assert.That(Directory.Exists(Path.Combine(_dir, "species")), Is.True,
                "the folder is made, so the editor and a save both have somewhere to go");
            Assert.That(Directory.GetFiles(Path.Combine(_dir, "species")), Is.Empty,
                "and a slot nobody has authored is not a file");
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

        var (records, loaded) = await _svc.LoadAllModuleRecordsAsync(Species, limit: 3);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(1), "the one authored record, and only it");
            Assert.That(Directory.GetFiles(Path.Combine(_dir, "species")), Has.Length.EqualTo(1),
                "a save writes one file; the blanks around it stay unwritten");
            Assert.That(records[2]["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(records[2]["baseSpeed"].AsLong(), Is.EqualTo(65));
            Assert.That(records[2]["evolves"].AsBool(), Is.True);
            Assert.That(records[1].IsEmpty, Is.True, "and its neighbors are still blank");
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

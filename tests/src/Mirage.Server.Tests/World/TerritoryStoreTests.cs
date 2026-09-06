using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Where territory state lives, and what a world folder is left holding.
///
/// <para>A world travels: it gets zipped up, handed over, opened in the editor. Ownership, income and a
/// war-night queue belong to the one server that produced them, so they go in the DATA folder beside the
/// accounts and the guilds. Keyed by map group, because a territory is the maps of its group.</para>
///
/// <para>These use two different roots on purpose. Pointed at one folder the split would pass whatever it
/// was written as, which is exactly the bug worth catching.</para>
/// </summary>
[TestFixture]
public class TerritoryStoreTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _world = "";
    private string _data = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        string root = Path.Combine(Path.GetTempPath(), "mirage-territory-" + Guid.NewGuid().ToString("N"));
        _world = Path.Combine(root, "world");
        _data = Path.Combine(root, "data");
        _svc = new JsonPersistenceService(_world, _data, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            string root = Path.GetDirectoryName(_world)!;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch { /* best-effort temp cleanup */ }
    }

    private static TerritoryRecord Held(int group, int guild) => new()
    {
        MapGroup = group, ControllingGuild = guild, WeeksHeld = 3,
        PendingTerritoryIncome = 700, IncomeThisWeek = 4200, PreviousWeekIncome = 3500,
        LastWeekRollDate = new DateOnly(2026, 8, 23), Challengers = { 5, 6 }, DefenderAbandoned = true,
    };

    [Test]
    public async Task ATerritoryIsWrittenBesideTheInstallation_NotIntoTheWorld()
    {
        await _svc.SaveTerritoryAsync(4, Held(4, 2));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_data, "territories", "territory4.json")), Is.True,
                "territory state belongs to this server");
            Assert.That(Directory.Exists(Path.Combine(_world, "territories")), Is.False,
                "a world handed to somebody else must not carry it");
        });
    }

    [Test]
    public async Task EveryFieldSurvivesARestart()
    {
        await _svc.SaveTerritoryAsync(4, Held(4, 2));

        var loaded = (await _svc.LoadAllTerritoriesAsync())[4];

        Assert.Multiple(() =>
        {
            Assert.That(loaded.ControllingGuild, Is.EqualTo(2));
            Assert.That(loaded.WeeksHeld, Is.EqualTo(3));
            Assert.That(loaded.PendingTerritoryIncome, Is.EqualTo(700), "a restart must not drop accrued income");
            Assert.That(loaded.IncomeThisWeek, Is.EqualTo(4200));
            Assert.That(loaded.PreviousWeekIncome, Is.EqualTo(3500));
            Assert.That(loaded.LastWeekRollDate, Is.EqualTo(new DateOnly(2026, 8, 23)));
            Assert.That(loaded.Challengers, Is.EqualTo(new[] { 5, 6 }), "registrations are made before war night");
            Assert.That(loaded.DefenderAbandoned, Is.True);
        });
    }

    [Test]
    public async Task TheLoaderKeysEachTerritoryFromItsFilename()
    {
        Directory.CreateDirectory(Path.Combine(_data, "territories"));
        // A file claiming a group that is not its own: the filename wins, or a copied file scores for the
        // wrong maps.
        File.WriteAllText(Path.Combine(_data, "territories", "territory9.json"),
            """{"mapGroup":1,"controllingGuild":8}""");

        var loaded = await _svc.LoadAllTerritoriesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 9 }));
            Assert.That(loaded[9].MapGroup, Is.EqualTo(9));
            Assert.That(loaded[9].ControllingGuild, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task AServerThatHasNeverRunAWarNightHasNoTerritoryFiles()
    {
        Assert.That(await _svc.LoadAllTerritoriesAsync(), Is.Empty,
            "a contestable group with no file is simply unclaimed; nothing needs seeding");
    }

    [Test]
    public async Task DeletingATerritoryLeavesTheGroupAlone()
    {
        await _svc.SaveMapGroupAsync(4, new MapGroupRecord { Index = 4, Name = "The Reed Shallows", Territory = true });
        await _svc.SaveTerritoryAsync(4, Held(4, 2));

        await _svc.DeleteTerritoryAsync(4);

        var territories = await _svc.LoadAllTerritoriesAsync();
        var groups = await _svc.LoadAllMapGroupsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(territories, Is.Empty);
            Assert.That(groups[4].Territory, Is.True,
                "the maps are still contestable; only who held them is gone");
        });
    }

    [Test]
    public async Task AnAuthoredGroupCarriesNothingAboutWhoHoldsIt()
    {
        await _svc.SaveMapGroupAsync(4, new MapGroupRecord { Index = 4, Name = "The Reed Shallows", Territory = true });

        string json = await File.ReadAllTextAsync(Path.Combine(_world, "map_groups", "map_group4.json"));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("territory"), "the group is what declares one");
            foreach (string key in new[] { "controllingGuild", "pendingIncome", "weeksHeld", "challengers" })
                Assert.That(json, Does.Not.Contain(key), $"an authored group wrote {key}");
        });
    }
}

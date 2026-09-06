using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Tests.World;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// Marketplace listings and trade journals are numbered by their filename, the same way every other
/// per-file record is. <c>Id</c> exists in memory — a listing is quoted back to a buyer, and the next
/// journal number is taken from the highest loaded — but it is <c>[JsonIgnore]</c>d, so there is no second
/// copy inside the file to disagree with the name it is stored under.
/// </summary>
[TestFixture]
public class DerivedIdTests
{
    private sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

    private string _dir = "";
    private JsonPersistenceService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-ids-" + Guid.NewGuid().ToString("N"));
        _svc = new JsonPersistenceService(_dir, _dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private void WriteRaw(string folder, string fileName, string json)
    {
        string dir = Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), json);
    }

    // ── Market listings ───────────────────────────────────────────────────────

    [Test]
    public async Task AListing_TakesItsIdFromItsFilename()
    {
        WriteRaw("market", $"{MarketListing.FileStem}5.json", """{"seller":"ana","price":40}""");

        var loaded = await _svc.LoadAllMarketListingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 5 }));
            Assert.That(loaded[5].Id, Is.EqualTo(5), "the market quotes this id back to a buyer");
        });
    }

    [Test]
    public async Task AStaleListingId_IsIgnored()
    {
        WriteRaw("market", $"{MarketListing.FileStem}5.json", """{"id":88,"seller":"ana"}""");

        var loaded = await _svc.LoadAllMarketListingsAsync();

        Assert.That(loaded[5].Id, Is.EqualTo(5));
    }

    [Test]
    public async Task SavingAListing_WritesNoIdInside()
    {
        await _svc.SaveMarketListingAsync(3, new MarketListing { Id = 3, Seller = "ana" });

        string path = Path.Combine(_dir, "market", $"{MarketListing.FileStem}3.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("id", out _), Is.False);
            Assert.That(doc.RootElement.GetProperty("seller").GetString(), Is.EqualTo("ana"));
        });
    }

    [Test]
    public async Task ListingsRoundTripThroughDisk()
    {
        await _svc.SaveMarketListingAsync(1, new MarketListing { Id = 1, Seller = "ana", Price = 40 });
        await _svc.SaveMarketListingAsync(20, new MarketListing { Id = 20, Seller = "bo", Price = 900 });

        var loaded = await _svc.LoadAllMarketListingsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Keys, Is.EquivalentTo(new[] { 1, 20 }));
            Assert.That(loaded[20].Seller, Is.EqualTo("bo"));
            Assert.That(loaded[20].Price, Is.EqualTo(900));
        });
    }

    // ── Trade journals ────────────────────────────────────────────────────────

    /// <summary>The next journal number is <c>max(loaded Id) + 1</c>, so a record disagreeing with its own
    /// filename would skew the whole sequence and eventually overwrite a journal.</summary>
    [Test]
    public async Task AJournal_TakesItsIdFromItsFilename()
    {
        WriteRaw("trades", $"{TradeJournal.FileStem}7.json", """{"aLogin":"ana","bLogin":"bo"}""");

        var loaded = await _svc.LoadAllTradeJournalsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].Id, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AStaleJournalId_IsIgnored()
    {
        WriteRaw("trades", $"{TradeJournal.FileStem}7.json", """{"id":999,"aLogin":"ana"}""");

        var loaded = await _svc.LoadAllTradeJournalsAsync();

        Assert.That(loaded[0].Id, Is.EqualTo(7), "999 would push the next journal number past every real one");
    }

    [TestCase("notes.json")]
    [TestCase("journal.json")]
    [TestCase("journalX.json")]
    public async Task FilesThatDoNotNameAJournal_AreIgnored(string fileName)
    {
        WriteRaw("trades", fileName, """{"aLogin":"ana"}""");

        Assert.That(await _svc.LoadAllTradeJournalsAsync(), Is.Empty);
    }

    [Test]
    public async Task SavingAJournal_WritesNoIdInside_AndNamesTheFileByIt()
    {
        _svc.SaveTradeJournal(new TradeJournal { Id = 4, ALogin = "ana", BLogin = "bo" });

        string path = Path.Combine(_dir, "trades", $"{TradeJournal.FileStem}4.json");
        Assert.That(File.Exists(path), Is.True, $"expected the journal at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(doc.RootElement.TryGetProperty("id", out _), Is.False);

        var loaded = await _svc.LoadAllTradeJournalsAsync();
        Assert.That(loaded[0].Id, Is.EqualTo(4), "the id survives the round trip through the filename alone");
    }
}

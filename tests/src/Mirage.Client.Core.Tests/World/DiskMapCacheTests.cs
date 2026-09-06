using Mirage.Client.Core.Cache;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.World;

/// <summary>The client's on-disk map cache: JSON round-trip, the in-memory revision index (drives the
/// revision-keyed staleness check without disk I/O), and re-indexing existing files when reopened.</summary>
[TestFixture]
public class DiskMapCacheTests
{
    string _dir = "";

    [SetUp]
    public void SetUp()
        => _dir = Path.Combine(Path.GetTempPath(), "mirage-diskcache-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task SaveThenLoad_RoundTripsMap()
    {
        var cache = new DiskMapCache(_dir);
        await cache.SaveAsync(3, new MapRecord { Name = "Kordavan", Revision = 7 });

        var loaded = await cache.LoadAsync(3);
        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Name, Is.EqualTo("Kordavan"));
            Assert.That(loaded.Revision, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task GetCachedRevision_KnownAndUnknown()
    {
        var cache = new DiskMapCache(_dir);
        await cache.SaveAsync(5, new MapRecord { Revision = 42 });
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetCachedRevision(5), Is.EqualTo(42));
            Assert.That(cache.GetCachedRevision(99), Is.EqualTo(-1), "an uncached map reports -1");
        });
    }

    // A fresh cache over the same directory re-indexes revisions from the files already on disk.
    [Test]
    public async Task NewCache_ReindexesExistingRevisions()
    {
        await new DiskMapCache(_dir).SaveAsync(8, new MapRecord { Revision = 13 });
        var reopened = new DiskMapCache(_dir);
        Assert.That(reopened.GetCachedRevision(8), Is.EqualTo(13), "revisions are indexed on construction");
    }

    [Test]
    public async Task LoadAsync_MissingMap_ReturnsNull()
        => Assert.That(await new DiskMapCache(_dir).LoadAsync(404), Is.Null);
}

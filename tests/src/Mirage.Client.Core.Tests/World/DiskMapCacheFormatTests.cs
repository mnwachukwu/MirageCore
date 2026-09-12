using Mirage.Client.Core.Cache;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.World;

/// <summary>
/// The cache's format stamp.
///
/// <para>🔴 A map's <c>revision</c> tracks what an AUTHOR changed, so it cannot catch a change to what the
/// FILE MEANS. The record converters fall back rather than throw — an unknown tile type reads as
/// <see cref="TileType.Walkable"/> — so without this stamp a renamed member would turn every door in a
/// player's cache into open floor, on every machine, with nothing to report it.</para>
/// </summary>
[TestFixture]
public class DiskMapCacheFormatTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp dir nobody else reads */ }
    }

    [Test]
    public async Task AFreshCache_KeepsWhatItWasGiven()
    {
        var cache = new DiskMapCache(_dir);
        await cache.SaveAsync(3, new MapRecord { Name = "Hub", Revision = 7 });

        var reopened = new DiskMapCache(_dir);

        Assert.That(reopened.GetCachedRevision(3), Is.EqualTo(7));
        Assert.That((await reopened.LoadAsync(3))!.Name, Is.EqualTo("Hub"));
    }

    [Test]
    public async Task ACacheWrittenUnderAnotherFormat_IsDropped()
    {
        var cache = new DiskMapCache(_dir);
        await cache.SaveAsync(3, new MapRecord { Name = "Hub", Revision = 7 });

        await File.WriteAllTextAsync(Path.Combine(_dir, "format.txt"), (DiskMapCache.FormatVersion - 1).ToString());
        var reopened = new DiskMapCache(_dir);

        var loaded = await reopened.LoadAsync(3);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.GetCachedRevision(3), Is.EqualTo(-1), "a stale entry is a cache MISS, not a stale hit");
            Assert.That(loaded, Is.Null);
        });
    }

    // A cache from before the stamp existed has no marker at all, and is exactly the case this guards.
    [Test]
    public async Task ACacheWithNoStampAtAll_IsDropped()
    {
        var cache = new DiskMapCache(_dir);
        await cache.SaveAsync(3, new MapRecord { Name = "Hub", Revision = 7 });
        File.Delete(Path.Combine(_dir, "format.txt"));

        var reopened = new DiskMapCache(_dir);

        Assert.That(reopened.GetCachedRevision(3), Is.EqualTo(-1));
    }

    [Test]
    public void DroppingACache_StampsItSoItIsDroppedOnlyOnce()
    {
        _ = new DiskMapCache(_dir);

        string marker = Path.Combine(_dir, "format.txt");
        Assert.That(File.Exists(marker), Is.True);
        Assert.That(File.ReadAllText(marker).Trim(), Is.EqualTo(DiskMapCache.FormatVersion.ToString()));
    }

    // The stamp is worth nothing if nobody bumps it, so this states in one place what a bump is FOR: it
    // rises when a record's on-disk meaning changes. Renaming Key/KeyOpen to Door/Plate is what took it to 2.
    [Test]
    public void TheFormatVersion_IsPastTheUnstampedEra()
    {
        Assert.That(DiskMapCache.FormatVersion, Is.GreaterThan(1));
    }
}

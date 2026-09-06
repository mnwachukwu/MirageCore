using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// Seeding runs once, against somebody's world, and there is no undo. The rule is deliberately blunt:
/// the data directory EXISTING is the whole test. An empty one is somebody's blank canvas and is left
/// exactly as found — reading emptiness as "fresh install" would refill a world they cleared on purpose,
/// every single launch.
/// </summary>
[TestFixture]
public class SeedDeployTests
{
    private string _root = "";
    private string _seed = "";
    private string _data = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirage-seed-" + Guid.NewGuid().ToString("N"));
        _seed = Path.Combine(_root, "seed-world");
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(_seed, "maps"));
        File.WriteAllText(Path.Combine(_seed, "motd.json"), "{}");
        File.WriteAllText(Path.Combine(_seed, "maps", "map1.json"), "{\"name\":\"shipped\"}");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Test]
    public void NoDataDirectory_LaysTheSeedDown()
    {
        int copied = SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.EqualTo(2), "both seed files should have been laid down");
            Assert.That(File.Exists(Path.Combine(_data, "motd.json")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_data, "maps", "map1.json")), Does.Contain("shipped"),
                "nested folders travel with the seed");
        });
    }

    /// <summary>The case the whole rule exists for.</summary>
    [Test]
    public void AnEmptyDataDirectory_IsLeftAlone()
    {
        Directory.CreateDirectory(_data);

        int copied = SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.Zero, "an empty data dir is a deliberate blank world, not a fresh install");
            Assert.That(Directory.EnumerateFileSystemEntries(_data), Is.Empty, "and it stays blank");
        });
    }

    [Test]
    public void AnAuthoredWorld_IsNeverOverwritten()
    {
        Directory.CreateDirectory(Path.Combine(_data, "maps"));
        File.WriteAllText(Path.Combine(_data, "maps", "map1.json"), "{\"name\":\"mine\"}");

        int copied = SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.Zero);
            Assert.That(File.ReadAllText(Path.Combine(_data, "maps", "map1.json")), Does.Contain("mine"),
                "the authored map must survive untouched");
            Assert.That(File.Exists(Path.Combine(_data, "motd.json")), Is.False,
                "and nothing from the seed may be added alongside it");
        });
    }

    /// <summary>Seeding is once-only in practice: the second launch finds the directory it made.</summary>
    [Test]
    public void SeedingTwice_OnlyEverRunsOnce()
    {
        int first = SeedDeploy.SeedIfAbsent(_seed, _data);
        File.WriteAllText(Path.Combine(_data, "maps", "map1.json"), "{\"name\":\"edited\"}");

        int second = SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(2));
            Assert.That(second, Is.Zero, "the second launch must find the world it laid down and leave it");
            Assert.That(File.ReadAllText(Path.Combine(_data, "maps", "map1.json")), Does.Contain("edited"),
                "an edit made after the first launch survives the second");
        });
    }

    [Test]
    public void NoSeedShipped_DoesNothingAndCreatesNothing()
    {
        Directory.Delete(_seed, recursive: true);

        int copied = SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.Zero);
            Assert.That(Directory.Exists(_data), Is.False, "an absent seed must not leave an empty data dir behind");
        });
    }

    /// <summary>Staging is what keeps a failed copy from looking like a finished one. Nothing is left where
    /// the data dir goes, so the next launch tries again instead of running on half a world.</summary>
    [Test]
    public void TheStagingFolder_IsNeverLeftBehind()
    {
        SeedDeploy.SeedIfAbsent(_seed, _data);

        Assert.That(Directory.Exists(_data + ".seeding"), Is.False, "staging should have been moved, not copied");
    }

    // ── One file at a time, for the config files ──────────────────────────────

    [Test]
    public void NoConfigFile_LaysTheShippedOneDown()
    {
        string shipped = Path.Combine(_seed, "motd.json");
        string target = Path.Combine(_root, "state", "motd.json");

        Assert.Multiple(() =>
        {
            Assert.That(SeedDeploy.SeedFileIfAbsent(shipped, target), Is.True);
            Assert.That(File.Exists(target), Is.True, "the parent folder is created on the way");
        });
    }

    /// <summary>The whole point: an operator's settings are theirs, and a later version's defaults never
    /// land on top of them.</summary>
    [Test]
    public void AnExistingConfigFile_IsLeftExactlyAsFound()
    {
        string shipped = Path.Combine(_seed, "motd.json");
        string target = Path.Combine(_root, "state", "motd.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "{\"mine\":true}");

        Assert.Multiple(() =>
        {
            Assert.That(SeedDeploy.SeedFileIfAbsent(shipped, target), Is.False);
            Assert.That(File.ReadAllText(target), Does.Contain("mine"));
        });
    }

    /// <summary>An EMPTY file is an existing file. Same rule as the empty world dir above.</summary>
    [Test]
    public void AnEmptyConfigFile_IsStillAnExistingOne()
    {
        string shipped = Path.Combine(_seed, "motd.json");
        string target = Path.Combine(_root, "state", "motd.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "");

        Assert.Multiple(() =>
        {
            Assert.That(SeedDeploy.SeedFileIfAbsent(shipped, target), Is.False);
            Assert.That(new FileInfo(target).Length, Is.Zero);
        });
    }

    [Test]
    public void NoShippedFile_DoesNothing()
    {
        string target = Path.Combine(_root, "state", "absent.json");

        Assert.Multiple(() =>
        {
            Assert.That(SeedDeploy.SeedFileIfAbsent(Path.Combine(_seed, "absent.json"), target), Is.False);
            Assert.That(File.Exists(target), Is.False);
        });
    }
}

using Mirage.Server.Core.Configuration;
using Mirage.Server.Host.Net;
using Mirage.Shared;
using NUnit.Framework;
using System;
using System.IO;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Everything the server has to keep across a version has to live somewhere the updater does not
/// touch.
///
/// <para>🔴 An installed server runs out of a Velopack <c>current/</c> folder, and applying an update
/// replaces that folder wholesale with the contents of the new package. Verified in 2026-09: a probe
/// app that wrote a file beside its own exe, packed at two versions and updated, came back with the
/// file gone and a fresh one in its place. So <c>AppContext.BaseDirectory</c> is where shipped content
/// is read from and NOTHING is kept.</para>
///
/// <para>The identity is the sharpest case — a new fingerprint is every pinned client refusing to
/// connect with "This server's identity has changed" — but the data dir holds the accounts, which is
/// the quieter loss: the world is re-seeded from the package, so it looks intact.</para>
/// </summary>
[TestFixture]
public class StateOutlivesTheBinariesTests
{
    private static bool IsUnderTheExeFolder(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(AppContext.BaseDirectory),
            StringComparison.OrdinalIgnoreCase);

    [Test]
    public void TheIdentityIsNotKeptBesideTheExecutable() =>
        Assert.That(IsUnderTheExeFolder(SelfSignedCertificate.DefaultPath), Is.False,
            $"The identity would be destroyed by the next update: {SelfSignedCertificate.DefaultPath}");

    [Test]
    public void TheDefaultDataDirIsNotBesideTheExecutable() =>
        Assert.That(IsUnderTheExeFolder(ServerPaths.ResolveDataDir(new ServerConfig())), Is.False,
            "The accounts would be destroyed by the next update.");

    [Test]
    public void TheDefaultWorldDirIsNotBesideTheExecutable() =>
        Assert.That(IsUnderTheExeFolder(ServerPaths.ResolveWorldDir(new ServerConfig())), Is.False,
            "The world would be destroyed by the next update.");

    // The whole point of the setting: an operator who names a folder gets that folder, not a path
    // rewritten underneath them.
    [Test]
    public void AConfiguredDataDirIsUsedVerbatim()
    {
        string configured = Path.Combine(Path.GetTempPath(), "mirage-data-dir");
        Assert.That(ServerPaths.ResolveDataDir(new ServerConfig { DataDir = configured }),
            Is.EqualTo(configured));
    }

    [Test]
    public void AConfiguredWorldDirIsUsedVerbatim()
    {
        string configured = Path.Combine(Path.GetTempPath(), "mirage-world-dir");
        Assert.That(ServerPaths.ResolveWorldDir(new ServerConfig { WorldDir = configured }),
            Is.EqualTo(configured));
    }

    // What an operator SET, as opposed to what they accumulated: the port, the language, the management
    // token, and the DataDir/WorldDir overrides that point at everything else.
    [Test]
    public void TheOperatorsConfigIsNotKeptBesideTheExecutable() =>
        Assert.That(IsUnderTheExeFolder(ServerConfigStore.DefaultPath), Is.False,
            $"serverconfig.json would be destroyed by the next update: {ServerConfigStore.DefaultPath}");

    [Test]
    public void TheLogSettingsAreNotKeptBesideTheExecutable() =>
        Assert.That(IsUnderTheExeFolder(AppSettingsStore.DefaultPath), Is.False,
            $"appsettings.json would be destroyed by the next update: {AppSettingsStore.DefaultPath}");

    // The other half of the pair: both files still SHIP beside the exe, because that copy is the
    // defaults a fresh install starts from. A seed source that moved would seed nothing.
    [Test]
    public void BothConfigFilesStillShipBesideTheExecutable() =>
        Assert.Multiple(() =>
        {
            Assert.That(IsUnderTheExeFolder(ServerConfigStore.ShippedPath), Is.True);
            Assert.That(IsUnderTheExeFolder(AppSettingsStore.ShippedPath), Is.True);
        });
}

/// <summary>
/// The same invariant from the other end: not which path a property returns, but whether creating the
/// identity actually puts a byte in the install folder.
///
/// <para>The install folder is READ-ONLY at runtime. Beyond surviving an update, that is what lets the
/// server run from a Linux AppImage or a macOS .app at all — both mount their payload read-only, so a
/// write beside the exe does not land somewhere unlucky, it fails. This runs on all three platforms in
/// CI, which is the only way the non-Windows branches of <see cref="UserPaths"/> are ever executed.</para>
/// </summary>
[TestFixture]
public class TheInstallFolderIsNeverWrittenToTests
{
    private string? _savedOverride;
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        // Redirects the per-user root, so the real installation's identity is never read or replaced.
        _savedOverride = UserPaths.RootOverride;
        _root = Path.Combine(Path.GetTempPath(), "mirage-state-" + Guid.NewGuid().ToString("N"));
        UserPaths.RootOverride = _root;
    }

    [TearDown]
    public void TearDown()
    {
        UserPaths.RootOverride = _savedOverride;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Test]
    public void CreatingTheIdentity_LeavesTheInstallFolderUntouched()
    {
        string[] before = TopLevelFiles();

        // No argument: the path under test is the one the two listeners actually use.
        using var cert = SelfSignedCertificate.LoadOrCreate();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(SelfSignedCertificate.DefaultPath), Is.True, "the identity was written");
            Assert.That(SelfSignedCertificate.DefaultPath, Does.StartWith(_root), "and written under the state root");
            Assert.That(TopLevelFiles(), Is.EquivalentTo(before), "nothing new beside the executable");
        });
    }

    private static string[] TopLevelFiles() =>
        Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.TopDirectoryOnly);
}

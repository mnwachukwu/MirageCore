using NUnit.Framework;
using System.Runtime.InteropServices;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// <see cref="UserPaths"/>, which is the one place the engine knows where a per-user file belongs on
/// each operating system — Windows known folders, macOS <c>~/Library</c>, the Linux XDG spec.
///
/// These assert the *current* platform's branch rather than mocking one, because there is nothing to
/// mock: the code switches on <see cref="RuntimeInformation.IsOSPlatform"/>, which no test can lie
/// about. That is deliberate, and it is why CI runs this suite on all three operating systems — on
/// any single machine two of the three branches are dead code no test can reach.
///
/// The three roots are not simply three names for one idea, and the tests below pin where they
/// coincide as carefully as where they differ: Windows folds Data and Cache together (both
/// <c>%LocalAppData%</c>), macOS folds Config and Data together (both Application Support), and Linux
/// keeps all three apart. Collapsing the wrong pair silently puts a regenerable cache somewhere that
/// roams between machines, or settings somewhere a cleaner is entitled to delete.
/// </summary>
[TestFixture]
public class UserPathsTests
{
    private const string AppName = "Mirage Source Remastered";
    private const string XdgName = "mirage-source-remastered";

    private static readonly string[] XdgVars = ["XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME"];

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly Dictionary<string, string?> _savedXdg = [];

    /// <summary>
    /// Environment variables are process-global, so the XDG override tests would otherwise leak into
    /// whatever ran next. Saved and restored around every test in the fixture rather than only the
    /// ones that write, so a failure mid-test cannot strand a value either.
    /// </summary>
    [SetUp]
    public void SaveXdg()
    {
        foreach (string name in XdgVars) _savedXdg[name] = Environment.GetEnvironmentVariable(name);
    }

    [TearDown]
    public void RestoreXdg()
    {
        foreach ((string name, string? value) in _savedXdg) Environment.SetEnvironmentVariable(name, value);
        _savedXdg.Clear();
    }

    // ── Where each kind lands, on whichever OS is running ────────────────────────────────────────

    [Test]
    public void Config_LandsInThePlatformsSettingsLocation()
    {
        string actual = new UserPaths(AppName).Config();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.That(actual, Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, "Library", "Application Support", AppName)));
        else
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, ".config", XdgName)));
    }

    [Test]
    public void Data_LandsInThePlatformsAppDataLocation()
    {
        string actual = new UserPaths(AppName).Data();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.That(actual, Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, "Library", "Application Support", AppName)));
        else
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, ".local", "share", XdgName)));
    }

    [Test]
    public void Cache_LandsInThePlatformsCacheLocation()
    {
        string actual = new UserPaths(AppName).Cache();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.That(actual, Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, "Library", "Caches", AppName)));
        else
            Assert.That(actual, Is.EqualTo(Path.Combine(Home, ".cache", XdgName)));
    }

    // ── Which roots coincide, and which must not ─────────────────────────────────────────────────

    /// <summary>Settings roam; bulk data and cache stay machine-local. Only Windows draws that line,
    /// because only Windows has a roaming profile to draw it with.</summary>
    [Test]
    public void Config_IsSeparateFromData_OnlyWhereThePlatformDistinguishesThem()
    {
        var paths = new UserPaths(AppName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.That(paths.Config(), Is.EqualTo(paths.Data()),
                "macOS keeps settings and app data together under Application Support.");
        else
            Assert.That(paths.Config(), Is.Not.EqualTo(paths.Data()));
    }

    /// <summary>A cache is something an OS cleaner may delete. On macOS and Linux it has its own
    /// home saying so; on Windows it shares Local with app data, which is the platform's own
    /// convention rather than an oversight.</summary>
    [Test]
    public void Cache_IsSeparateFromData_ExceptOnWindows()
    {
        var paths = new UserPaths(AppName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.That(paths.Cache(), Is.EqualTo(paths.Data()),
                "Windows puts both under %LocalAppData%.");
        else
            Assert.That(paths.Cache(), Is.Not.EqualTo(paths.Data()));
    }

    // ── Folder naming ────────────────────────────────────────────────────────────────────────────

    /// <summary>XDG directories are lowercase and dash-separated by convention, so the display name
    /// is slugged — and only there.</summary>
    [Test]
    [Platform("Linux")]
    public void Linux_UsesASluggedFolderName()
    {
        string config = new UserPaths(AppName).Config();

        Assert.That(config, Does.EndWith(XdgName));
        Assert.That(config, Does.Not.Contain(AppName), "the human-readable name should not survive slugging");
    }

    /// <summary>Windows and macOS per-user folders carry the human-readable name, spaces and
    /// capitals intact — a user browsing to it should recognize the game.</summary>
    [Test]
    [Platform("Win,MacOsX")]
    public void WindowsAndMac_UseTheHumanReadableFolderName()
        => Assert.That(new UserPaths(AppName).Config(), Does.EndWith(AppName));

    /// <summary>Each application constructs its own instance with its own name, which is what keeps
    /// the client's settings out of the editor's.</summary>
    [Test]
    public void DifferentAppNames_GetDifferentRoots()
    {
        Assert.That(new UserPaths("Some Game").Config(), Is.Not.EqualTo(new UserPaths("Some Game Editor").Config()));
    }

    // ── XDG overrides, which only Linux honors ──────────────────────────────────────────────────

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    [TestCase("XDG_CONFIG_HOME")]
    [TestCase("XDG_DATA_HOME")]
    [TestCase("XDG_CACHE_HOME")]
    public void Linux_HonorsTheXdgOverride(string variable)
    {
        string root = Path.Combine(Path.GetTempPath(), "xdg-override-probe");
        Environment.SetEnvironmentVariable(variable, root);

        var paths = new UserPaths(AppName);
        string actual = variable switch
        {
            "XDG_CONFIG_HOME" => paths.Config(),
            "XDG_DATA_HOME" => paths.Data(),
            _ => paths.Cache(),
        };

        Assert.That(actual, Is.EqualTo(Path.Combine(root, XdgName)));
    }

    /// <summary>The spec treats an empty variable as unset, and the implementation checks the length
    /// rather than only for null. Without that, an exported-but-empty <c>XDG_CONFIG_HOME</c> — which
    /// a shell profile can produce by accident — would put every settings file at the filesystem
    /// root.</summary>
    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void Linux_TreatsAnEmptyXdgVariableAsUnset()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "");

        Assert.That(new UserPaths(AppName).Config(), Is.EqualTo(Path.Combine(Home, ".config", XdgName)));
    }

    // ── Path composition ─────────────────────────────────────────────────────────────────────────

    /// <summary>Asking for the root itself hands back the directory, not the directory plus a
    /// trailing separator — so callers can compare two roots for equality, and appending a name
    /// later cannot produce a doubled separator.</summary>
    [Test]
    public void NoParts_ReturnsTheRootWithNoTrailingSeparator()
    {
        string root = new UserPaths(AppName).Config();

        Assert.That(root, Is.Not.Empty);
        Assert.That(root, Does.Not.EndWith(Path.DirectorySeparatorChar.ToString()));
        Assert.That(root, Does.Not.EndWith(Path.AltDirectorySeparatorChar.ToString()));
    }

    [Test]
    public void Parts_NestUnderTheRootInOrder()
    {
        var paths = new UserPaths(AppName);

        Assert.That(paths.Config("appsettings.json"),
            Is.EqualTo(Path.Combine(paths.Config(), "appsettings.json")));
        Assert.That(paths.Cache("maps", "map1.json"),
            Is.EqualTo(Path.Combine(paths.Cache(), "maps", "map1.json")));
    }

    /// <summary>Every root is absolute, so a caller can hand one straight to the filesystem without
    /// knowing what the working directory happens to be.</summary>
    [Test]
    public void EveryRoot_IsAbsolute()
    {
        var paths = new UserPaths(AppName);

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(paths.Config()), Is.True);
            Assert.That(Path.IsPathRooted(paths.Data()), Is.True);
            Assert.That(Path.IsPathRooted(paths.Cache()), Is.True);
        });
    }
}

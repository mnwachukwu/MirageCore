using Mirage.Server.Core.Configuration;
using NUnit.Framework;

namespace Mirage.Server.Tests.Configuration;

/// <summary>
/// Loading and saving <c>serverconfig.json</c>. What matters is the WRONG file: silently reverting to
/// stock rules is worse than refusing to start, because the operator's switches look accepted. Every
/// failure path yields a working config AND a message, and these assert on both.
/// </summary>
[TestFixture]
public class ServerConfigStoreTests
{
    string _dir = "";

    [SetUp]
    public void CreateScratchDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveScratchDir()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    string Path_(string name) => Path.Combine(_dir, name);

    /// <summary>The weekly boundary is DERIVED, never stored — it was a second constant documented as "the
    /// day after war night", and two settings could drift apart. Wrapping Saturday to Sunday is the case
    /// the modulo exists for, and it is also the shipped default.</summary>
    [TestCase(DayOfWeek.Saturday, DayOfWeek.Sunday)]
    [TestCase(DayOfWeek.Sunday, DayOfWeek.Monday)]
    [TestCase(DayOfWeek.Wednesday, DayOfWeek.Thursday)]
    public void WeekResetDay_IsAlwaysTheDayAfterWarNight(DayOfWeek warNight, DayOfWeek expected)
    {
        var schedule = new ScheduleConfig { WarNightDay = warNight };

        Assert.That(schedule.WeekResetDay, Is.EqualTo(expected));
    }

    [Test]
    public void TheStockScheduleIsSaturdayEightPm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerConfig.Default.Schedule.WarNightDay, Is.EqualTo(DayOfWeek.Saturday));
            Assert.That(ServerConfig.Default.Schedule.WarNightHour, Is.EqualTo(20));
            Assert.That(ServerConfig.Default.Schedule.WeekResetDay, Is.EqualTo(DayOfWeek.Sunday));
        });
    }

    /// <summary>The spawn defaults have to equal what the old computed constants produced, or an existing
    /// world silently moves its front door the first time it boots on this build.</summary>
    [Test]
    public void TheStockSpawnIsTheMiddleOfMapOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerConfig.Default.Spawn.Map, Is.EqualTo(1));
            Assert.That(ServerConfig.Default.Spawn.X, Is.EqualTo((Mirage.Shared.Constants.MaxMapX + 1) / 2));
            Assert.That(ServerConfig.Default.Spawn.Y, Is.EqualTo((Mirage.Shared.Constants.MaxMapY + 1) / 2));
        });
    }

    [Test]
    public void NoFileAtAll_IsStockRulesAndNotAComplaint()
    {
        // The common case by far: an operator who has never changed anything. It must not look like an
        // error, or the warning becomes noise that hides the one that matters.
        var (config, error) = ServerConfigStore.Load(Path_("absent.json"));

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(config.DeathPenalty.DurabilityLoss, Is.True);
            Assert.That(config.DeathPenalty.ItemDrop, Is.True);
            Assert.That(config.DeathPenalty.ExpLoss, Is.True);
            Assert.That(config.Port, Is.EqualTo(Mirage.Shared.Constants.GamePort));
            Assert.That(config.Language, Is.EqualTo("en"));
        });
    }

    [Test]
    public void PortAndLanguage_LiveHereNow_NotInAppSettings()
    {
        // Split by what they configure: appsettings.json is the APPLICATION (Serilog), hand-authored and
        // commented; this is the SERVER and is machine-owned, which is what lets the shell rewrite it.
        string path = Path_("moved.json");
        File.WriteAllText(path, """{ "port": 7777, "language": "fr" }""");

        var (config, error) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(config.Port, Is.EqualTo(7777));
            Assert.That(config.Language, Is.EqualTo("fr"));
            Assert.That(config.DeathPenalty.ItemDrop, Is.True, "and the rules still default");
        });
    }

    [Test]
    public void EditingOneSetting_LeavesTheOthersAlone()
    {
        // The shell's two forms own different parts of this file — the rules tab owns three switches,
        // the language picker owns one string — and each amends the loaded config with `with` rather
        // than building a fresh one. Building fresh would silently reset the port every time somebody
        // pressed Save, which is the kind of thing nobody notices until a server comes up on 4000.
        string path = Path_("partial-edit.json");
        ServerConfigStore.Save(path, new ServerConfig { Port = 7777, Language = "pt" });

        var (loaded, _) = ServerConfigStore.Load(path);
        ServerConfigStore.Save(path, loaded with
        {
            DeathPenalty = new DeathPenaltyConfig { ExpLoss = false },
        });
        var (after, _) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(after.DeathPenalty.ExpLoss, Is.False, "the edit landed");
            Assert.That(after.Port, Is.EqualTo(7777), "and the port survived it");
            Assert.That(after.Language, Is.EqualTo("pt"), "as did the language");
        });
    }

    [Test]
    public void MalformedFile_FallsBackToStockRules_ButSaysSo()
    {
        string path = Path_("broken.json");
        File.WriteAllText(path, "{ \"deathPenalty\": { \"itemDrop\": ");

        var (config, error) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(config.DeathPenalty.ItemDrop, Is.True, "a server still boots");
            Assert.That(error, Is.Not.Null, "but never in silence");
            Assert.That(error, Does.Contain("broken.json"), "and the message names the file");
        });
    }

    [Test]
    public void PartialFile_TakesTheDefaultsForWhateverItOmits()
    {
        // The forward-compatibility case: today's file read by a build that has since grown a field.
        // Absent means default, so an old config never silently switches a new rule off.
        string path = Path_("partial.json");
        File.WriteAllText(path, "{ \"deathPenalty\": { \"expLoss\": false } }");

        var (config, error) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(config.DeathPenalty.ExpLoss, Is.False, "what it said");
            Assert.That(config.DeathPenalty.ItemDrop, Is.True, "and defaults for what it did not");
            Assert.That(config.DeathPenalty.DurabilityLoss, Is.True);
        });
    }

    [Test]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        // Nothing the shell writes puts a comment here, but a hand-edited file may carry one, and a
        // config silently ignored is the failure this whole type exists to avoid. Comments do not survive
        // the shell's next save, which is the documented cost of the file being machine-owned.
        string path = Path_("annotated.json");
        File.WriteAllText(path, """
            {
              // no gear damage on this server
              "deathPenalty": {
                "durabilityLoss": false,
              },
            }
            """);

        var (config, error) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(config.DeathPenalty.DurabilityLoss, Is.False);
        });
    }

    [Test]
    public void SaveThenLoad_RoundTripsEverySwitch()
    {
        string path = Path_("round-trip.json");
        var written = new ServerConfig
        {
            DeathPenalty = new DeathPenaltyConfig { DurabilityLoss = false, ItemDrop = true, ExpLoss = false },
        };

        string? saveError = ServerConfigStore.Save(path, written);
        var (read, loadError) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(saveError, Is.Null);
            Assert.That(loadError, Is.Null);
            Assert.That(read, Is.EqualTo(written), "records compare by value, so this covers every field at once");
        });
    }

    [Test]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // The write goes through a .tmp and a move so an interrupted save can't truncate the file the
        // next boot reads. The temp must not survive a successful one.
        string path = Path_("clean.json");

        ServerConfigStore.Save(path, ServerConfig.Default);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        });
    }

    [Test]
    public void Save_OverwritesAnExistingConfig()
    {
        // The shell's normal path: the file already exists, because it ships with the server.
        string path = Path_("existing.json");
        ServerConfigStore.Save(path, ServerConfig.Default);

        string? error = ServerConfigStore.Save(path, new ServerConfig
        {
            DeathPenalty = new DeathPenaltyConfig { ItemDrop = false },
        });
        var (read, _) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(read.DeathPenalty.ItemDrop, Is.False);
        });
    }

    // ── Remote management ─────────────────────────────────────────────────────

    [Test]
    public void RemoteManagement_IsOffUntilItIsConfigured()
    {
        // A server must never be remotely reachable because nobody said otherwise.
        var (config, _) = ServerConfigStore.Load(Path_("absent.json"));

        Assert.Multiple(() =>
        {
            Assert.That(config.Management.Port, Is.Zero);
            Assert.That(config.Management.Token, Is.Empty);
            Assert.That(config.Management.IsEnabled, Is.False);
        });
    }

    [TestCase(0, "", false, TestName = "Neither")]
    [TestCase(4001, "", false, TestName = "PortWithNoToken")]
    [TestCase(0, "secret", false, TestName = "TokenWithNoPort")]
    [TestCase(4001, "secret", true, TestName = "Both")]
    public void RemoteManagement_NeedsBothAPortAndAToken(int port, string token, bool expected)
    {
        // Half-configured is a misconfiguration, not a half-open server: a port with no token would be an
        // unauthenticated console, and the listener refuses rather than opening one.
        var management = new ManagementConfig { Port = port, Token = token };

        Assert.That(management.IsEnabled, Is.EqualTo(expected));
    }

    [Test]
    public void RemoteManagement_RoundTripsThroughTheFile()
    {
        string path = Path_("management.json");
        var written = new ServerConfig
        {
            Management = new ManagementConfig { Port = 4001, Token = "abc123" },
        };

        ServerConfigStore.Save(path, written);
        var (read, error) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(read.Management.Port, Is.EqualTo(4001));
            Assert.That(read.Management.Token, Is.EqualTo("abc123"));
        });
    }

    [Test]
    public void IsEnabled_IsDerived_AndStaysOutOfTheFile()
    {
        // Serialized, it would be a second place the answer is written down, and the two could disagree.
        string path = Path_("derived.json");
        ServerConfigStore.Save(path, new ServerConfig
        {
            Management = new ManagementConfig { Port = 4001, Token = "abc123" },
        });

        Assert.That(File.ReadAllText(path), Does.Not.Contain("isEnabled"));
    }

    [Test]
    public void TurningManagementOff_KeepsTheToken()
    {
        // The shell writes port 0 rather than clearing the token, so switching remote access back on does
        // not mean redistributing a new secret to everyone who had the old one.
        string path = Path_("toggled.json");
        ServerConfigStore.Save(path, new ServerConfig
        {
            Management = new ManagementConfig { Port = 4001, Token = "abc123" },
        });

        var (loaded, _) = ServerConfigStore.Load(path);
        ServerConfigStore.Save(path, loaded with
        {
            Management = loaded.Management with { Port = 0 },
        });
        var (after, _) = ServerConfigStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(after.Management.IsEnabled, Is.False);
            Assert.That(after.Management.Token, Is.EqualTo("abc123"));
        });
    }

    [Test]
    public void DefaultPath_SitsInTheStateDir()
    {
        // Resolved off an absolute root rather than the working directory, because the shell starts the
        // server as a child process and a child inherits a working directory it never chose. The state
        // dir rather than the install, because an update replaces the install — see
        // StateOutlivesTheBinariesTests.
        Assert.That(ServerConfigStore.DefaultPath,
            Is.EqualTo(ServerPaths.Data(ServerConfigStore.FileName)));
    }

    [Test]
    public void ShippedPath_SitsBesideTheExecutable()
    {
        // The package's copy, and only ever a seed source.
        Assert.That(ServerConfigStore.ShippedPath,
            Is.EqualTo(Path.Combine(AppContext.BaseDirectory, ServerConfigStore.FileName)));
    }
}

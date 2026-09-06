using Mirage.Server.Core.Configuration;
using NUnit.Framework;

namespace Mirage.Server.Tests.Configuration;

/// <summary>
/// Editing appsettings.json without regenerating it. The file is hand-authored structure that no typed
/// model describes, so what matters is that a save changes the five known values and NOTHING else, and
/// that a value it cannot find is reported rather than invented.
/// </summary>
[TestFixture]
public class AppSettingsStoreTests
{
    string _dir = "";

    [SetUp]
    public void CreateScratchDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveScratchDir()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // The shipped shape, trimmed to what the store addresses: both packet overrides, and two File sinks
    // at DIFFERENT depths of the same WriteTo array — which is the whole reason sinks are found by their
    // log path rather than by index.
    private const string Shipped = """
        {
          "Serilog": {
            "MinimumLevel": {
              "Default": "Debug",
              "Override": {
                "Microsoft": "Warning",
                "Mirage.Server.Host.Net.TcpPacketDispatcher": "Warning",
                "Mirage.Server.Host.Net.ReceiveLoop": "Warning"
              }
            },
            "WriteTo": [
              { "Name": "Logger", "Args": { "configureLogger": {
                  "Filter": [ { "Name": "ByExcludingWhere", "Args": { "expression": "SourceContext like 'x%'" } } ],
                  "WriteTo": [ { "Name": "File", "Args": {
                      "path": "logs/server-.log", "rollingInterval": "Day", "retainedFileCountLimit": 7,
                      "outputTemplate": "[{Timestamp}] {Message}" } } ] } } },
              { "Name": "Logger", "Args": { "configureLogger": {
                  "WriteTo": [
                    { "Name": "Console", "Args": { "outputTemplate": "[{Timestamp}] {Message}" } },
                    { "Name": "File", "Args": {
                      "path": "logs/network-.log", "rollingInterval": "Day", "retainedFileCountLimit": 3,
                      "outputTemplate": "[{Timestamp}] {Message}" } } ] } } }
            ]
          }
        }
        """;

    string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void ReadsEveryKnobOutOfTheShippedShape()
    {
        var (log, error) = AppSettingsStore.Load(Write("shipped.json", Shipped));

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(log.Available, Is.EqualTo(LogKnobs.All));
            Assert.That(log.MinimumLevel, Is.EqualTo("Debug"));
            Assert.That(log.LogOutgoingPackets, Is.False, "packet logging ships off");
            Assert.That(log.LogIncomingPackets, Is.False);
            Assert.That(log.ServerLogRetentionDays, Is.EqualTo(7));
            Assert.That(log.NetworkLogRetentionDays, Is.EqualTo(3));
        });
    }

    [Test]
    public void FindsEachFileSinkByItsLogPath_NotByPosition()
    {
        // The two File sinks sit at different depths and different indices. Reading 7 and 3 back the right
        // way round is the whole assertion: an index-based reader would swap or miss them.
        var (log, _) = AppSettingsStore.Load(Write("sinks.json", Shipped));

        Assert.Multiple(() =>
        {
            Assert.That(log.ServerLogRetentionDays, Is.EqualTo(7));
            Assert.That(log.NetworkLogRetentionDays, Is.EqualTo(3));
        });
    }

    [Test]
    public void SaveThenLoad_RoundTripsEveryKnob()
    {
        string path = Write("round-trip.json", Shipped);
        var (loaded, _) = AppSettingsStore.Load(path);

        string? error = AppSettingsStore.Save(path, loaded with
        {
            MinimumLevel = "Warning",
            LogOutgoingPackets = true,
            LogIncomingPackets = true,
            ServerLogRetentionDays = 30,
            NetworkLogRetentionDays = 14,
        });
        var (after, _) = AppSettingsStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(after.MinimumLevel, Is.EqualTo("Warning"));
            Assert.That(after.LogOutgoingPackets, Is.True);
            Assert.That(after.LogIncomingPackets, Is.True);
            Assert.That(after.ServerLogRetentionDays, Is.EqualTo(30));
            Assert.That(after.NetworkLogRetentionDays, Is.EqualTo(14));
        });
    }

    [Test]
    public void Save_LeavesTheStructureAlone()
    {
        // The failure this whole approach exists to avoid: regenerating the file and losing the
        // hand-authored Logger split, the filter expressions or the output templates.
        string path = Write("structure.json", Shipped);
        var (loaded, _) = AppSettingsStore.Load(path);

        AppSettingsStore.Save(path, loaded with { MinimumLevel = "Error" });
        string text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("ByExcludingWhere"), "the filter survived");
            Assert.That(text, Does.Contain("SourceContext like 'x%'"), "as did its expression");
            Assert.That(text, Does.Contain("outputTemplate"), "and the templates");
            Assert.That(text, Does.Contain("rollingInterval"), "and the sink arguments it does not own");
            Assert.That(text, Does.Contain("\"Microsoft\": \"Warning\""), "and an override it does not own");
        });
    }

    [Test]
    public void PacketSwitches_MapToDebugAndWarning()
    {
        string path = Write("packets.json", Shipped);
        var (loaded, _) = AppSettingsStore.Load(path);

        AppSettingsStore.Save(path, loaded with { LogOutgoingPackets = true, LogIncomingPackets = false });
        string text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("\"Mirage.Server.Host.Net.TcpPacketDispatcher\": \"Debug\""));
            Assert.That(text, Does.Contain("\"Mirage.Server.Host.Net.ReceiveLoop\": \"Warning\""));
        });
    }

    [Test]
    public void AMissingKnob_IsReportedNotInvented()
    {
        // A restructured file must grey the control out. Defaulting the value would mean offering to
        // overwrite a file the store did not understand, which is the one unrecoverable outcome here.
        string path = Write("no-sinks.json", """
            { "Serilog": { "MinimumLevel": { "Default": "Debug", "Override": { } } } }
            """);

        var (log, error) = AppSettingsStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(log.Has(LogKnobs.MinimumLevel), Is.True);
            Assert.That(log.Has(LogKnobs.ServerRetention), Is.False);
            Assert.That(log.Has(LogKnobs.NetworkRetention), Is.False);
            Assert.That(log.Has(LogKnobs.OutgoingPackets), Is.False);
            Assert.That(error, Is.Not.Null, "and it says so");
        });
    }

    [Test]
    public void Save_WritesNothingForAKnobThatIsNotAvailable()
    {
        // Save must not CREATE the paths it could not find. Inventing an override or a sink argument would
        // change how the server logs in a way nobody asked for.
        string path = Write("partial.json", """
            { "Serilog": { "MinimumLevel": { "Default": "Debug", "Override": { } } } }
            """);
        var (loaded, _) = AppSettingsStore.Load(path);

        AppSettingsStore.Save(path, loaded with { LogOutgoingPackets = true, ServerLogRetentionDays = 99 });
        string text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("TcpPacketDispatcher"), "no override was invented");
            Assert.That(text, Does.Not.Contain("retainedFileCountLimit"), "no sink argument was invented");
            Assert.That(text, Does.Contain("\"Default\": \"Debug\""), "and what was there survived");
        });
    }

    [Test]
    public void MalformedFile_IsRefused_NotOverwritten()
    {
        string path = Write("broken.json", "{ \"Serilog\": { \"MinimumLevel\": ");

        var (log, loadError) = AppSettingsStore.Load(path);
        string? saveError = AppSettingsStore.Save(path, log);

        Assert.Multiple(() =>
        {
            Assert.That(log.Available, Is.EqualTo(LogKnobs.None));
            Assert.That(loadError, Is.Not.Null);
            Assert.That(saveError, Is.Not.Null, "a file that cannot be parsed is never rewritten");
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ \"Serilog\": { \"MinimumLevel\": "));
        });
    }

    [Test]
    public void MissingFile_IsNotACrash()
    {
        var (log, error) = AppSettingsStore.Load(Path.Combine(_dir, "absent.json"));

        Assert.Multiple(() =>
        {
            Assert.That(log.Available, Is.EqualTo(LogKnobs.None));
            Assert.That(error, Is.Not.Null);
        });
    }

    [Test]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        string path = Write("clean.json", Shipped);
        var (loaded, _) = AppSettingsStore.Load(path);

        AppSettingsStore.Save(path, loaded);

        Assert.That(File.Exists(path + ".tmp"), Is.False);
    }

    [Test]
    public void TheShippedFileHasNoComments()
    {
        // Comments document code or the docs, never a config file. This is also load-bearing now that a
        // tool writes here: a JsonNode round-trip drops them, so a comment would silently disappear.
        string shipped = Path.Combine(TestContext.CurrentContext.TestDirectory, "appsettings.json");
        Assume.That(File.Exists(shipped), "appsettings.json is copied beside the test binaries");

        foreach (string line in File.ReadAllLines(shipped))
            Assert.That(line.TrimStart(), Does.Not.StartWith("//"), $"comment in appsettings.json: {line.Trim()}");
    }
}

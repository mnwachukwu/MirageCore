using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Host;
using Mirage.Server.Host.Logging;
using Mirage.Server.Host.Management;
using Mirage.Server.Host.Net;
using Mirage.Server.Host.Services;
using Mirage.Shared;
using Serilog;
using Serilog.Expressions;
using Serilog.Settings.Configuration;
using Velopack;

// Server entry point and composition root: registers every singleton the game needs, then hands
// control to the generic host. MirageServerService (a hosted service, registered at the bottom) is
// what actually starts the world, the game loop, and the TCP acceptor.
//
// The first four steps are order-sensitive:
//   1. Velopack runs BEFORE anything else — an install or update step may exit the process outright.
//   2. The two config files are laid down in the state dir if this machine has none yet. Both are read
//      from there — serverconfig.json below, appsettings.json by the host.
//   3. The working directory is pinned to the STATE dir, so the relative log paths in appsettings.json
//      resolve somewhere that survives a version rather than wherever the process was launched from.
//   4. A bootstrap console logger is installed before the host exists, so failures during startup
//      are still reported; the host replaces it with the appsettings-configured Serilog pipeline.
VelopackApp.Build().Run();

// ── This installation's own folder ────────────────────────────────────────────
// 🔴 NOT the folder the exe runs from. An installed server runs out of a Velopack `current/` that an
// update replaces wholesale, so everything an operator accumulates — their settings, their logs, and
// at the default their accounts and their world — lasted exactly one version there. See ServerPaths.
string stateRoot = ServerPaths.Data();
Directory.CreateDirectory(stateRoot);

// The package ships both config files as the defaults a fresh install starts with; they become this
// installation's own on first run and are never written back to the install folder. Absence is the only
// trigger, so an operator's edits are never overwritten by a later version.
SeedDeploy.SeedFileIfAbsent(ServerConfigStore.ShippedPath, ServerConfigStore.DefaultPath);
SeedDeploy.SeedFileIfAbsent(AppSettingsStore.ShippedPath, AppSettingsStore.DefaultPath);

// Serilog's file sinks carry RELATIVE paths ("logs/server-.log") that an operator edits by hand, so the
// working directory is what decides where the logs land. It is the state dir for the same reason as
// above. Shipped content is read through AppContext.BaseDirectory explicitly and is unaffected.
Directory.SetCurrentDirectory(stateRoot);

// ── Operator settings ─────────────────────────────────────────────────────────
// Read before anything else, because the language it carries decides what every line below is written
// in — including the complaint about the file itself, which is why THAT one is in English.
// A bad config never blocks a boot: the server runs on stock settings and says so.
// --config points at another file, so a second server can run from this install without disturbing the
// one an operator configured. See StartupArgs.
// 🔴 The path is kept, not just used. A server started with --config reads that file, so anything
// writing settings back has to write THAT file — resolving the default a second time at save time
// sends a scratch server’s settings into the real installation’s config.
var configPath = StartupArgs.ConfigPath(args) ?? ServerConfigStore.DefaultPath;
var (serverConfig, configError) = ServerConfigStore.Load(configPath);
string langDir = Path.Combine(AppContext.BaseDirectory, "lang");
ServerStrings.Load(langDir, serverConfig.Language);

// ── Console capture, for remote operators ─────────────────────────────────────
// Installed BEFORE any logger exists, so it catches the whole pipeline as well as the console commands'
// own writes. Serilog's console sink resolves Console.Out; whether it does so once or per line, by this
// point Console.Out is already the tee.
var consoleTee = new ConsoleTee(Console.Out);
Console.SetOut(consoleTee);

// ── Bootstrap logger (used during startup before appsettings.json is loaded) ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Reported now rather than at load time: the bootstrap logger did not exist yet up there. Always said
// out loud, because the alternative is an operator whose settings silently do nothing.
if (configError is not null) Log.Warning("{ConfigError}", configError);

// ── The two folders ───────────────────────────────────────────────────────────
// Split on one question: does it change while the server runs? The world does not and is the editor's;
// this installation's state does and is the server's. serverconfig.json holds both, because that is where
// an operator sets them from the shell; the appsettings.json DataDir key is where the single path lived
// before the split and is still honored.
//
// Resolved HERE rather than inside ConfigureServices, so the seeding below happens before anything reads
// either folder.
//
// Resolved through ServerPaths rather than here, so the shell's scratch server reads the same folders
// this one does. The defaults are per-user dirs, NOT folders beside the executable — see that class.
string dataDir = ServerPaths.ResolveDataDir(serverConfig);
string worldDir = ServerPaths.ResolveWorldDir(serverConfig);

// A first run on a machine with nothing gets what the package shipped — the world, and the handful of
// defaults an installation starts with. Absence of the folder is the only trigger in both cases: an empty
// one is somebody's blank canvas and is left exactly as found.
int seededWorld = SeedDeploy.SeedIfAbsent(Path.Combine(AppContext.BaseDirectory, "seed-world"), worldDir);
if (seededWorld > 0)
    Log.Information("No world at {WorldDir}; laid down the shipped seed ({Count} files).", worldDir, seededWorld);

int seededData = SeedDeploy.SeedIfAbsent(Path.Combine(AppContext.BaseDirectory, "seed-data"), dataDir);
if (seededData > 0)
    Log.Information("Nothing at {DataDir}; laid down the shipped defaults ({Count} files).", dataDir, seededData);

// ── Build and run the host ────────────────────────────────────────────────────

// The assemblies holding everything appsettings.json names by string. Serilog otherwise finds these by
// scanning for Serilog*.dll beside the executable, and the published server is a single file with no
// .dll files beside it — so the scan finds nothing and the host throws on Build().
//
// Naming them also makes the failure symmetric: a sink added to appsettings.json from a package that is
// not listed here fails in Debug too, rather than only in the packaged build.
//
//   Serilog          WriteTo.Logger, the sub-logger each filtered pipeline is built as
//   Sinks.Console    WriteTo.Console
//   Sinks.File       WriteTo.File
//   Expressions      Filter.ByExcludingWhere, Filter.ByIncludingOnly
var serilogAssemblies = new ConfigurationReaderOptions(
    typeof(Log).Assembly,
    typeof(ConsoleLoggerConfigurationExtensions).Assembly,
    typeof(FileLoggerConfigurationExtensions).Assembly,
    typeof(SerilogExpression).Assembly);

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, lc) => lc.ReadFrom.Configuration(context.Configuration, serilogAssemblies))
    .ConfigureServices((ctx, services) =>
    {
        // ── Shared singletons ─────────────────────────────────────────────────

        // Wall clock and the source of chance. Every system takes these as OPTIONAL constructor
        // parameters defaulting to these same implementations, so registering them changes nothing at
        // runtime — it just makes the production wiring explicit rather than implicit in a null-coalesce,
        // and gives one place to swap them (a fixed clock for a replay harness, a seeded generator for a
        // reproducible stress run). Tests pin them per-system instead of through this container.
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IRandomSource>(SharedRandom.Instance);

        // Registered like the two above: systems take it as an optional parameter defaulting to
        // ServerConfig.Default, so this line is what makes the FILE take effect.
        services.AddSingleton(serverConfig);

        // World state (all mutable game arrays)
        services.AddSingleton<GameWorld>();

        // Player / editor session managers (1-based arrays)
        services.AddSingleton<PlayerManager>();
        services.AddSingleton<EditorSessionManager>();
        services.AddSingleton<EditorLockRegistry>();

        // ── Persistence ───────────────────────────────────────────────────────
        // Resolved before the host was built — see above.

        string logsDir = ctx.Configuration["LogsDir"] ?? ServerPaths.Data("logs");

        Serilog.ILogger chatSerilogLogger = new Serilog.LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(logsDir, "chat", ".log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:HH:mm:ss}] [{ChatType}] {Message:lj}{NewLine}")
            .CreateLogger();
        services.AddSingleton<IChatLog>(new SerilogChatLog(chatSerilogLogger));

        services.AddSingleton<IPersistenceService>(sp =>
            new JsonPersistenceService(worldDir, dataDir,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonPersistenceService>>(),
                sp.GetRequiredService<IChatLog>(),
                serverConfig.Records));
        // Off-thread player saves (game thread snapshots, this writes the file).
        services.AddSingleton<PlayerSaver>();
        // Tracks fire-and-forget persistence tasks: logs faults, drains on shutdown.
        services.AddSingleton<IBackgroundPersistence, BackgroundPersistence>();

        // ── Transport layer ───────────────────────────────────────────────────
        // TcpPacketDispatcher is both the IPacketDispatcher and the concrete type needed
        // by TcpConnectionAcceptor to call RegisterPlayer / RegisterEditor.
        services.AddSingleton<TcpPacketDispatcher>();
        services.AddSingleton<IPacketDispatcher>(sp =>
            sp.GetRequiredService<TcpPacketDispatcher>());

        // ── Game logic ────────────────────────────────────────────────────────
        services.AddSingleton<MovementSystem>();
        services.AddSingleton<CombatSystem>();
        services.AddSingleton<ItemSystem>();
        services.AddSingleton<SpellSystem>();
        services.AddSingleton<ShopSystem>();
        services.AddSingleton<BankSystem>();
        services.AddSingleton<PlayerSpawnSystem>();
        services.AddSingleton<PartySystem>();
        services.AddSingleton<GuildSystem>();
        services.AddSingleton<GuildScheduleSystem>();
        services.AddSingleton<GuildTerritorySystem>();
        services.AddSingleton<GuildWarSystem>();
        services.AddSingleton<MailSystem>();
        services.AddSingleton<MarketSystem>();
        services.AddSingleton<TradeSystem>();
        services.AddSingleton<ObjectiveSystem>();
        services.AddSingleton<QuestSystem>();
        services.AddSingleton<ConversationSystem>();
        // Lazy CombatSystem for QuestSystem — defers resolution to break the CombatSystem↔JoinLeaveSystem
        // ↔QuestSystem construction cycle (QuestSystem only needs it for level-up at reward time).
        services.AddSingleton(p => new Lazy<CombatSystem>(() => p.GetRequiredService<CombatSystem>()));
        services.AddSingleton<SocialSystem>();
        services.AddSingleton<SpawnSystem>();
        services.AddSingleton<ModerationSystem>();
        services.AddSingleton<JoinLeaveSystem>();
        services.AddSingleton<NpcAiSystem>();
        services.AddSingleton<RegenerationSystem>();
        services.AddSingleton<PkExpirySystem>();
        services.AddSingleton<TimeOfDaySystem>();
        services.AddSingleton<WeatherSystem>();
        services.AddSingleton<BloodSystem>();
        services.AddSingleton<GameLoop>();

        // ── Packet handlers ───────────────────────────────────────────────────
        // Game traffic and editor traffic are dispatched by separate handlers; they share the world
        // and the dispatcher but almost nothing else.
        services.AddSingleton<PacketHandler>();
        services.AddSingleton<EditorPacketHandler>();

        // ── Connection acceptor ───────────────────────────────────────────────
        services.AddSingleton<TcpConnectionAcceptor>();

        // ── Remote management ─────────────────────────────────────────────────
        // Off unless serverconfig.json carries both a port and a token.
        services.AddSingleton(consoleTee);

        // Status snapshots for an operator dashboard. Silent unless something asked: --status-events is
        // how the shell tells a child it spawned to write them to stdout, which in that mode only the
        // shell reads. A human in a terminal passes no flag and sees nothing.
        services.AddSingleton(sp =>
        {
            var broadcaster = ActivatorUtilities.CreateInstance<StatusBroadcaster>(sp);
            broadcaster.WriteToStdout = StartupArgs.StatusEvents(args, out var cadence);
            broadcaster.Cadence = cadence;
            return broadcaster;
        });
        services.AddHostedService(sp => sp.GetRequiredService<StatusBroadcaster>());

        // ── Hosted services ───────────────────────────────────────────────────
        // MirageServerService starts the world, game loop, and TCP acceptor.
        services.AddHostedService<MirageServerService>();
        // ConsoleCommands reads admin commands from stdin. Registered as itself as well as a hosted
        // service, because the management listener runs commands through the same instance.
        services.AddSingleton<ConsoleCommands>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsoleCommands>());
        // Singleton first, hosted second, so /management reaches the SAME listener the host started —
        // AddHostedService alone would hand a command its own second instance, bound to nothing.
        // Where the config came from, so /management writes back to the file this server READ.
        services.AddSingleton(new ServerConfigPath(configPath));
        services.AddSingleton<ManagementListener>();
        services.AddHostedService(sp => sp.GetRequiredService<ManagementListener>());
        // Breaks the cycle: the listener takes ConsoleCommands, and ConsoleCommands needs the listener.
        services.AddSingleton<Func<ManagementListener>>(sp => sp.GetRequiredService<ManagementListener>);
    })
    .Build();

// Wire the per-player locale resolver so ServerStrings.ForPlayer(index, …) can read each
// session's Language without ServerStrings holding a direct PlayerManager reference.
var playerManager = host.Services.GetRequiredService<PlayerManager>();
ServerStrings.SetPlayerLocaleResolver(index => playerManager[index].Language);

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server crashed");
}
finally
{
    Log.CloseAndFlush();
}

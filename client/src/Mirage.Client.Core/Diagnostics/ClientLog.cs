using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Runtime.InteropServices;

namespace Mirage.Client.Core.Diagnostics;

/// <summary>
/// The client's file log: one daily-rolling file, written through a level switch so the capture level
/// can change without a restart. The editor's <c>EditorLog</c> is the same shape, with the same template
/// and rolling policy, so one report reads like the other.
///
/// <para>🔴 <b>Without this the client leaves nothing behind.</b> It is a <c>WinExe</c>, so standard
/// output goes nowhere on Windows; a crash reaches the server as an ordinary disconnect, indistinguishable
/// from someone closing the window; and a player sees only a window that vanished. A reproducible crash
/// produced no artifact of any kind, and the only way to learn anything was to reproduce it again with a
/// debugger attached.</para>
///
/// <para><b>The trail matters as much as the stack.</b> A stack trace says where the process died, not
/// what the player was doing when it did. Which map, which warp, which packet went missing is what
/// identifies a bug — and those lines are there when nothing crashes at all, which is the shape most
/// bugs take.</para>
///
/// <para>Static, like the editor's, because the seams worth recording are spread through packet handling,
/// map loading and the game loop, and threading a logger to each would be a larger change than the thing
/// it records. <see cref="Initialize"/> is given the directory rather than resolving one, so this project
/// keeps its ignorance of where the client is installed — and a call made before it is a no-op rather
/// than a crash.</para>
///
/// <para>Nothing here throws. Losing a line is bad; taking the process down from inside the thing that
/// exists to explain a crash is worse.</para>
/// </summary>
public static class ClientLog
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static Logger? _logger;

    /// <summary>The in-memory sink the console reads. It receives every event the file does, whether the
    /// console is open or not — a console that only recorded while visible would be useless for the thing
    /// you want it for, which is finding out what happened before you thought to look.</summary>
    public static readonly ConsoleSink Console = new();

    /// <summary>Where the log files are written, or null before <see cref="Initialize"/>.</summary>
    public static string? Directory { get; private set; }

    /// <summary>Opens the sink in <paramref name="directory"/> and records the session header.
    ///
    /// <para>A log that cannot open must not stop the game starting: the client then runs unlogged, which
    /// is what it does on a read-only or missing directory.</para></summary>
    public static void Initialize(string directory, LogEventLevel level = LogEventLevel.Information,
                                  int retainedFiles = 10)
    {
        LevelSwitch.MinimumLevel = level;
        Directory = directory;

        var previous = _logger;
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                // 🔴 ASYNC, and this one is not optional. A game client logs from the packet thread and the
                // game thread, and the events worth logging cluster exactly where latency shows: a map load,
                // a warp, a disconnect. A synchronous file sink puts a disk write — `shared: true` means a
                // named mutex and an unbuffered flush — in the middle of the map fetch a warp is waiting on,
                // which widens the window the world is being held for. The background worker takes it off
                // both threads; the queue drops rather than blocks if it ever fills, which is the right way
                // round for a logger.
                .WriteTo.Async(a => a.File(
                    Path.Combine(directory, "client-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: retainedFiles,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
                // Not async: it is an in-memory enqueue, and the console showing a line the instant it
                // happened is the point of having it.
                .WriteTo.Sink(Console)
                .CreateLogger();
        }
        catch
        {
            _logger = null;
            Directory = null;
        }
        previous?.Dispose();

        WriteSessionHeader();
    }

    /// <summary>Changes the capture level immediately, without reopening the file.</summary>
    public static void SetLevel(LogEventLevel level) => LevelSwitch.MinimumLevel = level;

    /// <summary>Closes the sink, flushing anything buffered.</summary>
    public static void Shutdown(string reason)
    {
        Info("Session ending: {Reason}", reason);
        _logger?.Dispose();
        _logger = null;
    }

    // ── Level-tagged helpers ──────────────────────────────────────────────────

    public static void Verbose(string template, params object?[] args) => Write(LogEventLevel.Verbose, null, template, args);
    public static void Debug(string template, params object?[] args) => Write(LogEventLevel.Debug, null, template, args);
    public static void Info(string template, params object?[] args) => Write(LogEventLevel.Information, null, template, args);
    public static void Warn(string template, params object?[] args) => Write(LogEventLevel.Warning, null, template, args);
    public static void Warn(Exception ex, string template, params object?[] args) => Write(LogEventLevel.Warning, ex, template, args);
    public static void Error(string template, params object?[] args) => Write(LogEventLevel.Error, null, template, args);
    public static void Error(Exception ex, string template, params object?[] args) => Write(LogEventLevel.Error, ex, template, args);
    public static void Fatal(Exception ex, string template, params object?[] args) => Write(LogEventLevel.Fatal, ex, template, args);

    /// <summary>Whether a level would be written. Guards a call site that would otherwise pay to build an
    /// argument the sink is going to drop.</summary>
    public static bool IsEnabled(LogEventLevel level) => _logger is not null && LevelSwitch.MinimumLevel <= level;

    private static void Write(LogEventLevel level, Exception? ex, string template, object?[] args)
    {
        var log = _logger;
        if (log is null) return;
        try { log.Write(level, ex, template, args); }
        catch { /* a logger that throws must never take the client with it */ }
    }

    // ── Crashes ───────────────────────────────────────────────────────────────

    /// <summary>Records what escapes a thread the game loop never unwinds through — a socket read, a
    /// background download. What escapes the loop itself is caught around it, where the stack is still
    /// worth reading, and reported through <see cref="Fatal"/> the same way.</summary>
    public static void InstallCrashHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Fatal(ex, "Unhandled exception on a background thread. Terminating: {Terminating}", e.IsTerminating);
            _logger?.Dispose();   // the process is going; flush what is buffered
            _logger = null;
        };
    }

    // ── Session header ────────────────────────────────────────────────────────

    /// <summary>What every report needs before the first event: which build produced it, and on what.
    /// The answer decides whether the lines below are even from the code being read.</summary>
    private static void WriteSessionHeader()
    {
        Info("─────────────────────────────────────────────────────────────");
        Info("Mirage client starting. Version {Version}, {Runtime} on {OS} ({Arch}).",
             System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
             RuntimeInformation.FrameworkDescription,
             RuntimeInformation.OSDescription,
             RuntimeInformation.OSArchitecture);
        Info("Log dir {Path}", Directory ?? "(none)");
    }
}

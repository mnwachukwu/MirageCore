using Avalonia.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Mirage.Editor.Services;

/// <summary>
/// The editor's file log: one daily-rolling file under <see cref="EditorPaths.Logs"/>, written through a
/// level switch so the capture level changes from Help > Logging without a restart.
///
/// <para>Every call site goes through the static helpers here rather than holding a logger, because the
/// editor is a view-model app with no container to inject one from. <see cref="Write"/> is the only place
/// that touches Serilog, so a call made before <see cref="Initialize"/> is a no-op instead of a crash.</para>
/// </summary>
public static class EditorLog
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static Logger? _logger;

    /// <summary>The in-memory sink the Console window shows. It survives a <see cref="Reconfigure"/>,
    /// which builds a new file sink — changing retention must not throw away what has been logged.</summary>
    public static readonly ConsoleSink Console = new();
    private static DispatcherTimer? _heartbeat;
    private static System.Threading.Timer? _stallObserver;
    private static long _lastHeartbeatMs;
    private static int _blockReported;

    /// <summary>How late a heartbeat tick has to be before it counts as a stall. The timer asks for one
    /// second; anything past this means the UI thread was busy or blocked in between.</summary>
    private const long StallThresholdMs = 1_500;

    /// <summary>How stale the heartbeat has to get before the off-thread observer calls the UI thread
    /// blocked. Looser than <see cref="StallThresholdMs"/>: the observer runs on its own clock and can
    /// sample just before a due heartbeat, so a tighter bound would report ordinary jitter.</summary>
    private const long BlockedThresholdMs = 3_000;

    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    /// <summary>Where the log files are written. Shown in the configuration window.</summary>
    public static string Directory => EditorPaths.Logs;

    /// <summary>Opens the sink and records the session header. Safe to call once, at startup.</summary>
    public static void Initialize()
    {
        Reconfigure(AppSettings.Current.Logging);
        WriteSessionHeader();
    }

    /// <summary>Applies a level and retention. The level rides a switch and takes effect immediately; a
    /// retention change needs a new sink, so the file is closed and reopened.</summary>
    public static void Reconfigure(LoggingSetting setting)
    {
        LevelSwitch.MinimumLevel = ToSerilog(setting.Level);

        var previous = _logger;
        try
        {
            System.IO.Directory.CreateDirectory(EditorPaths.Logs);
            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                .WriteTo.File(
                    Path.Combine(EditorPaths.Logs, "editor-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: setting.RetainedFileCount,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(Console)
                .CreateLogger();
        }
        catch
        {
            // A log that cannot open must not stop the editor opening. The app runs unlogged.
            _logger = null;
        }
        previous?.Dispose();
    }

    /// <summary>Closes the sink, flushing anything buffered. Called on shutdown.</summary>
    public static void Shutdown(string reason)
    {
        Info("Session ending: {Reason}", reason);
        _heartbeat?.Stop();
        _heartbeat = null;
        _stallObserver?.Dispose();
        _stallObserver = null;
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

    /// <summary>Whether a level would be written. Guards call sites that would otherwise pay to build an
    /// argument the sink is going to drop — the per-tile and per-packet ones.</summary>
    public static bool IsEnabled(LogEventLevel level) => _logger is not null && LevelSwitch.MinimumLevel <= level;

    private static void Write(LogEventLevel level, Exception? ex, string template, object?[] args)
    {
        var log = _logger;
        if (log is null) return;
        try { log.Write(level, ex, template, args); }
        catch { /* a logger that throws must never take the editor with it */ }
    }

    // ── UI-thread stall watchdog ──────────────────────────────────────────────

    /// <summary>Starts the watchdog that reports a UI thread which stopped answering.
    ///
    /// <para>It takes two timers, because one cannot see both failures. A dispatcher timer stamps a heartbeat
    /// and measures how late its own tick was: that catches a thread which was BUSY, and reports the size of
    /// the gap once it comes back. A thread-pool timer watches that stamp go stale: that catches a thread which
    /// is BLOCKED, and reports while it is still stuck — which the dispatcher timer cannot do, because its tick
    /// needs the very thread it is measuring, so a freeze that never lifts logs nothing at all.</para></summary>
    public static void StartStallWatchdog()
    {
        if (_heartbeat is not null) return;
        Volatile.Write(ref _lastHeartbeatMs, NowMs());

        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchdogInterval };
        _heartbeat.Tick += (_, _) =>
        {
            long now = NowMs();
            long gap = now - Volatile.Read(ref _lastHeartbeatMs);
            Volatile.Write(ref _lastHeartbeatMs, now);

            if (Interlocked.Exchange(ref _blockReported, 0) == 1)
                Warn("UI thread is answering again, after {Gap} ms blocked.", gap);
            else if (gap >= StallThresholdMs)
                Warn("UI thread stalled for {Gap} ms (heartbeat expected every {Interval} ms).",
                     gap, (long)WatchdogInterval.TotalMilliseconds);
            else
                Verbose("Heartbeat: {Gap} ms.", gap);
        };
        _heartbeat.Start();

        _stallObserver = new System.Threading.Timer(_ =>
        {
            if (NowMs() - Volatile.Read(ref _lastHeartbeatMs) < BlockedThresholdMs) return;
            if (Interlocked.Exchange(ref _blockReported, 1) == 1) return;   // one report per freeze
            Error("UI thread has not answered for {Threshold} ms and is still not answering. Capture a stack " +
                  "with: dotnet-stack report --process-id {Pid}", BlockedThresholdMs, Environment.ProcessId);
        }, null, WatchdogInterval, WatchdogInterval);

        Info("UI-thread stall watchdog started (stall {Stall} ms, blocked {Blocked} ms).",
             StallThresholdMs, BlockedThresholdMs);
    }

    private static long NowMs() => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

    // ── Session header ────────────────────────────────────────────────────────

    private static void WriteSessionHeader()
    {
        var setting = AppSettings.Current.Logging;
        Info("─────────────────────────────────────────────────────────────");
        Info("Mirage editor starting. Version {Version}, {Runtime} on {OS} ({Arch}).",
             typeof(EditorLog).Assembly.GetName().Version?.ToString() ?? "unknown",
             Environment.Version.ToString(),
             System.Runtime.InteropServices.RuntimeInformation.OSDescription,
             System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        Info("Capture level {Level}, retention {Retention}.", setting.Level, setting.Retention);
        Info("Config dir  {Path}", EditorPaths.Config);
        Info("Data dir    {Path}", EditorPaths.Data);
        Info("Assets dir  {Path}", EditorPaths.Assets);
        Info("Log dir     {Path}", EditorPaths.Logs);
        Info("Language {Language}.", AppSettings.Current.Language);
    }

    private static LogEventLevel ToSerilog(LogCaptureLevel level) => level switch
    {
        LogCaptureLevel.Error => LogEventLevel.Error,
        LogCaptureLevel.Warning => LogEventLevel.Warning,
        LogCaptureLevel.Debug => LogEventLevel.Debug,
        LogCaptureLevel.Verbose => LogEventLevel.Verbose,
        _ => LogEventLevel.Information,
    };
}

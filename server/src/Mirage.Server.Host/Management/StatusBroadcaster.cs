using Microsoft.Extensions.Hosting;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using System.Text.Json;

namespace Mirage.Server.Host.Management;

/// <summary>
/// Assembles <see cref="ServerStatus"/> and hands it to whoever asked for it.
///
/// <para>OFF unless something is listening. The stream it writes to is also a human console, so a
/// terminal must never see a sentinel line: the local shell opts in with a command-line flag on the
/// child it spawned, and a remote operator opts in over its own socket. With neither, this emits
/// nothing at all.</para>
/// </summary>
public sealed class StatusBroadcaster : IHostedService, IDisposable
{
    /// <summary>The backstop cadence. Changes push immediately; this only exists so a missed event
    /// heals itself rather than leaving a dashboard quietly wrong.</summary>
    public static readonly TimeSpan Backstop = TimeSpan.FromSeconds(30);

    /// <summary>How often the timer fires. <see cref="Backstop"/> unless something asked for faster on the
    /// command line — the load benchmark does, because a ramp step is shorter than the backstop and it
    /// needs a reading per step. Nothing else changes it, so an ordinary dashboard stays as quiet.</summary>
    public TimeSpan Cadence { get; set; } = Backstop;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        // The default encoder escapes apostrophes, which a MOTD is full of. It round-trips either way,
        // but this line is read by a human debugging the wire often enough to be worth keeping legible.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly GameLoop _gameLoop;
    private readonly int _port;
    private readonly long _startedAtMs = Environment.TickCount64;

    private Timer? _timer;
    private CancellationTokenSource? _cts;

    private readonly EditorSessionManager _editors;
    private readonly EditorLockRegistry _editorLocks;

    public StatusBroadcaster(GameWorld world, PlayerManager pm, GameLoop gameLoop, ServerConfig config,
                             EditorSessionManager editors, EditorLockRegistry editorLocks)
    {
        _world = world;
        _pm = pm;
        _gameLoop = gameLoop;
        _port = config.Port;
        _editors = editors;
        _editorLocks = editorLocks;
    }

    /// <summary>Connected editor sessions, with whatever each is holding. Read on the game thread with the
    /// rest of the snapshot, so it agrees with the player list it ships beside.</summary>
    private List<EditorSummary> BuildEditorSummaries()
    {
        var list = new List<EditorSummary>();
        for (int i = 1; i <= Constants.MaxEditorSessions; i++)
        {
            var s = _editors.GetSession(i);
            if (s is null || !s.IsConnected) continue;
            list.Add(new EditorSummary
            {
                Slot = i,
                Login = s.IsAuthenticated ? s.Login : "",
                Access = s.IsAuthenticated ? s.AdminLevel.ToString() : "",
                Holding = _editorLocks.HeldBy(i).Select(h => $"{h.Section}#{h.Num}").ToList(),
            });
        }
        return list;
    }

    /// <summary>How many operators are attached. Set by <see cref="ManagementListener"/>, which is the
    /// only thing that knows; kept here so the snapshot has one assembly point.</summary>
    public int OperatorCount { get; set; }

    /// <summary>Set by <c>Program</c> when the child was started with the status flag: the local shell
    /// is the only reader of that stdout, so a sentinel there is private to it.</summary>
    public bool WriteToStdout { get; set; }

    /// <summary>Raised with a serialized machine line for consumers that are not stdout — the management
    /// sessions that asked for status. Empty when nobody has.</summary>
    public event Action<string>? MachineLineReady;

    /// <summary>True when anything at all is listening. Nothing is assembled when nothing is.</summary>
    public bool HasConsumers => WriteToStdout || MachineLineReady is not null;

    /// <summary>Serializes <paramref name="payload"/> behind its prefix and hands it to every consumer.
    ///
    /// <para>Public so the report kinds that are gathered ELSEWHERE — the moderation sweep, which reads
    /// files and cannot run on the game thread — reach the same stream through the same options. What is
    /// shared here is the transport, not the assembly.</para></summary>
    public void Emit<T>(string prefix, T payload)
    {
        if (!HasConsumers) return;
        string line = prefix + JsonSerializer.Serialize(payload, Json);
        if (WriteToStdout) System.Console.WriteLine(line);
        MachineLineReady?.Invoke(line);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Push on the change itself, so a roster is right the moment somebody joins. The timer is only
        // the backstop that heals a missed one.
        _pm.RosterChanged += Publish;
        _timer = new Timer(_ => Publish(), null, Cadence, Cadence);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _pm.RosterChanged -= Publish;
        _cts?.Cancel();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <summary>Asks for a snapshot. Safe to call from anywhere: the assembly itself is posted to the
    /// game thread, which is the only place this state is consistent.</summary>
    public void Publish()
    {
        if (!HasConsumers || _cts is null || _cts.IsCancellationRequested) return;
        _gameLoop.Post(() => Emit(ServerStatus.LinePrefix, Snapshot()));
    }

    // Runs ON the game thread. Reads only; nothing here may mutate world state.
    private ServerStatus Snapshot()
    {
        var players = new List<PlayerSummary>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            var c = sp.Char;
            players.Add(new PlayerSummary
            {
                Slot = i,
                Name = c.Name.Trim(),
                Login = sp.Login,
                Level = c.Level,
                Class = ClassName(c.Class),
                Map = c.Map,
                Access = c.Access.ToString(),
            });
        }

        return new ServerStatus
        {
            TimePhase = _world.TimePhase.ToString(),
            Weather = _world.Weather.ToString(),
            Motd = _world.Motd,
            UptimeSeconds = (Environment.TickCount64 - _startedAtMs) / 1000,
            Port = _port,
            Operators = OperatorCount,
            Players = players,
            Editors = BuildEditorSummaries(),
            Load = SampleLoad(),
        };
    }

    /// <summary>Reads the machine cost since the previous snapshot. Both counters are windowed and reset
    /// on read, so a snapshot describes the interval it closes rather than an average over all of history
    /// — which is what a ramping load test needs, since an average hides the step that broke.</summary>
    private LoadSummary SampleLoad()
    {
        var loop = _gameLoop.Metrics.Sample();

        var process = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long now = Environment.TickCount64;
        double windowMs = now - _lastCpuStampMs;
        double cpuMs = (cpu - _lastCpuTotal).TotalMilliseconds;
        _lastCpuTotal = cpu;
        _lastCpuStampMs = now;

        // Divided by core count, so this is a share of the WHOLE machine. Left undivided it would exceed
        // 1 on any parallel work and read as nonsense beside the game thread's 0-1.
        int cores = Math.Max(1, Environment.ProcessorCount);
        double processCpu = windowMs <= 0 ? 0 : Math.Clamp(cpuMs / (windowMs * cores), 0, 1);

        return new LoadSummary
        {
            GameThread = loop.Utilisation,
            ProcessCpu = processCpu,
            WorkingSetBytes = process.WorkingSet64,
            Overruns = loop.Overruns,
            QueuedPerSecond = loop.WindowSeconds <= 0 ? 0 : loop.QueuedActions / loop.WindowSeconds,
            ProcessorCount = cores,
        };
    }

    private TimeSpan _lastCpuTotal;
    private long _lastCpuStampMs = Environment.TickCount64;

    private string ClassName(int index) =>
        index >= 0 && index < _world.Classes.Length ? _world.Classes[index].Name.Trim() : "";

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}

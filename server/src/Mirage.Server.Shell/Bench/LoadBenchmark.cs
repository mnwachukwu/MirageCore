using Mirage.Server.Core.Configuration;
using Mirage.Shared.Protocol;
using System.Diagnostics;

namespace Mirage.Server.Shell.Bench;

/// <summary>
/// Ramps simulated players against a scratch server until the game thread runs out, and reports how many
/// this machine held.
///
/// <para><b>The activity model is every player walking, four steps a second, from one spawn point.</b>
/// That is deliberately the heaviest thing this server does: movement drives the collision checks and the
/// broadcast fan-out the game thread spends its time on, and a crowd standing in one place is where the
/// fan-out is worst because everyone can see everyone. A live world spreads players over many maps and
/// costs less, so the number this reports is a floor rather than an estimate.</para>
///
/// <para><b>The bench shares the machine.</b> Several hundred TLS connections are being driven from this
/// process, on the same cores the server is using. That is unavoidable without a second box, and it can
/// only ever make the result pessimistic — which is the safe direction for a number an operator is going
/// to set a player limit from. <see cref="BenchReport.MissedBeats"/> is how much of it showed.</para>
/// </summary>
public sealed class LoadBenchmark
{
    /// <summary>What the dialog starts at, and the largest a shipped client can index.</summary>
    public const int DefaultTarget = 500;

    /// <summary>Four moves a second, which is roughly a player holding a direction down.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a step is held before it is read. Long enough for the queue to reach steady
    /// state and for several status snapshots to land, short enough that a full ramp is minutes rather
    /// than an afternoon.</summary>
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(6);

    /// <summary>Game-thread utilisation at which the ramp stops. Not 1.0: past this the loop is already
    /// running late every iteration, and the next step would measure how badly rather than how many.</summary>
    private const double Saturated = 0.97;

    /// <summary>The bands the report is built around, as fractions of the game thread left spare.</summary>
    private static readonly double[] HeadroomBands = [0.50, 0.25, 0.10];

    private volatile SimulatedPlayer[] _active = [];
    private int _missedBeats;
    private int _ordinal;

    /// <summary>The headless server to run. Defaults to the one shipped beside this window, which is what
    /// an operator always wants; settable for the same reason <see cref="Services.ServerProcess"/> exposes
    /// its path, so a harness can point at a build tree.</summary>
    public string ServerExecutable { get; set; } = Services.ServerProcess.DefaultExecutablePath;

    /// <summary>Runs the whole thing: copy the world, boot a scratch server, ramp, report, clean up.</summary>
    public async Task<BenchReport> RunAsync(ServerConfig template, int target,
                                            IProgress<BenchProgress> progress, Action<string> log,
                                            CancellationToken ct)
    {
        target = Math.Clamp(target, 1, Mirage.Shared.Constants.MaxPlayers);
        progress.Report(new BenchProgress(BenchPhase.Preparing, 0, target, null));

        var server = await ScratchServer.StartAsync(
            template, ScratchServer.ResolveDataDir(template), target, ServerExecutable, ct).ConfigureAwait(false);
        server.OutputReceived += log;
        try
        {
            progress.Report(new BenchProgress(BenchPhase.Booting, 0, target, null));
            return await RampAsync(server, target, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            progress.Report(new BenchProgress(BenchPhase.Finishing, 0, target, null));
            foreach (var p in _active) p.Dispose();
            _active = [];
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<BenchReport> RampAsync(ScratchServer server, int target,
                                              IProgress<BenchProgress> progress, CancellationToken ct)
    {
        // Read before anyone joins: most of the total is the world itself, so the cost per player is the
        // difference from here, not the figure at a step.
        long baseline = await BaselineAsync(server, ct).ConfigureAwait(false);
        int cores = server.LatestStatus?.Load.ProcessorCount ?? Environment.ProcessorCount;

        int step = Math.Max(5, target / 20);
        var joined = new List<SimulatedPlayer>();
        var steps = new List<BenchStep>();
        var outcome = BenchOutcome.Reached;
        string? failure = null;

        using var driverStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var driver = Task.Run(() => DriveAsync(driverStop.Token), CancellationToken.None);

        try
        {
            for (int want = Math.Min(step, target); ; want = Math.Min(target, want + step))
            {
                progress.Report(new BenchProgress(BenchPhase.Joining, joined.Count, target, null));
                (int added, string? batchFailure) = await JoinBatchAsync(server, joined, want, ct).ConfigureAwait(false);
                // The FIRST refusal, not the last: what turned a player away the first time the server
                // started struggling is the interesting one, and later batches overwrite it with noise.
                failure ??= batchFailure;
                _active = [.. joined];
                if (added == 0 && joined.Count < want)
                {
                    outcome = BenchOutcome.JoinsFailed;
                    break;
                }

                progress.Report(new BenchProgress(BenchPhase.Measuring, joined.Count, target, null));
                var reading = await MeasureAsync(server, ct).ConfigureAwait(false);
                if (reading is null) { outcome = BenchOutcome.JoinsFailed; break; }
                steps.Add(reading);
                progress.Report(new BenchProgress(BenchPhase.Measuring, reading.Players, target, reading));

                if (joined.FirstOrDefault(p => p.Dropped) is { } lost)
                {
                    failure = lost.DropReason ?? failure;
                    outcome = BenchOutcome.PlayersDropped;
                    break;
                }
                if (reading.GameThread >= Saturated) { outcome = BenchOutcome.ThreadSaturated; break; }
                if (want >= target) break;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = BenchOutcome.Cancelled;
        }
        finally
        {
            driverStop.Cancel();
            try { await driver.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        return new BenchReport
        {
            Steps = steps,
            Bands = [.. HeadroomBands.Select(h => Band(steps, h))],
            Outcome = outcome,
            Peak = steps.Count == 0 ? 0 : steps.Max(s => s.Players),
            Target = target,
            ProcessorCount = cores,
            BaselineBytes = baseline,
            MissedBeats = _missedBeats,
            FailureReason = failure,
        };
    }

    /// <summary>Waits for a snapshot of the empty server. The port is already open by the time this runs,
    /// so this only costs the remainder of one status interval.</summary>
    private static async Task<long> BaselineAsync(ScratchServer server, CancellationToken ct)
    {
        for (int i = 0; i < 40 && server.LatestStatus is null; i++)
            await Task.Delay(250, ct).ConfigureAwait(false);
        return server.LatestStatus?.Load.WorkingSetBytes ?? 0;
    }

    /// <summary>Connects players until <paramref name="want"/> are in the world, a batch at a time.
    /// Concurrent within the batch because that is also what a login rush looks like, and a server that
    /// only holds up when they arrive one at a time has not been tested.</summary>
    private async Task<(int Added, string? Failure)> JoinBatchAsync(ScratchServer server,
                                                                    List<SimulatedPlayer> joined, int want,
                                                                    CancellationToken ct)
    {
        int missing = want - joined.Count;
        if (missing <= 0) return (0, null);

        var attempts = new List<SimulatedPlayer>(missing);
        for (int i = 0; i < missing; i++) attempts.Add(new SimulatedPlayer(Interlocked.Increment(ref _ordinal)));

        var results = await Task.WhenAll(
            attempts.Select(p => p.JoinAsync("127.0.0.1", server.Port, ct))).ConfigureAwait(false);

        int added = 0;
        string? failure = null;
        for (int i = 0; i < attempts.Count; i++)
        {
            if (results[i]) { joined.Add(attempts[i]); added++; continue; }
            failure ??= attempts[i].FailureReason;
            attempts[i].Dispose();
        }
        return (added, failure);
    }

    /// <summary>Holds the step and reads the game thread. Takes the MEDIAN of the snapshots that land
    /// after the first, so one unlucky window — a garbage collection, the batch of joins still settling —
    /// cannot decide where the machine's limit is.</summary>
    private static async Task<BenchStep?> MeasureAsync(ScratchServer server, CancellationToken ct)
    {
        var collected = new List<ServerStatus>();
        var seen = server.LatestStatus;
        var deadline = DateTime.UtcNow + Dwell;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(150, ct).ConfigureAwait(false);
            var latest = server.LatestStatus;
            if (ReferenceEquals(latest, seen) || latest is null) continue;
            seen = latest;
            collected.Add(latest);
        }
        // The first lands while the joins are still being absorbed; it describes the arrival, not the load.
        if (collected.Count > 1) collected.RemoveAt(0);
        if (collected.Count == 0) return null;

        collected.Sort((a, b) => a.Load.GameThread.CompareTo(b.Load.GameThread));
        var median = collected[collected.Count / 2];
        return new BenchStep(
            Players: median.Players.Count,
            GameThread: median.Load.GameThread,
            ProcessCpu: median.Load.ProcessCpu,
            WorkingSetBytes: median.Load.WorkingSetBytes,
            Overruns: median.Load.Overruns,
            PacketsPerSecond: median.Load.QueuedPerSecond,
            Maps: median.Players.Select(p => p.Map).Distinct().Count());
    }

    /// <summary>Moves every connected player, on the beat. A missed beat is counted rather than skipped
    /// quietly: it means the bench itself could not keep up, which makes that step's figure a lower
    /// bound on what the server could have taken.</summary>
    private async Task DriveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            foreach (var p in _active) p.Act();

            var spent = Stopwatch.GetElapsedTime(start);
            if (spent >= Beat) { Interlocked.Increment(ref _missedBeats); continue; }
            try { await Task.Delay(Beat - spent, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Where the ramp crossed a headroom band, interpolated between the two steps either side of
    /// it. A ramp that finished without ever crossing reports its peak as a floor —
    /// <see cref="BenchBand.AtLeast"/> — because nothing was measured past it.
    ///
    /// <para>Pure, and public for it: this is the arithmetic that turns a curve into the number an
    /// operator types into their config, and it is the one part of the benchmark that can be checked
    /// without spending five minutes running one.</para></summary>
    public static BenchBand Band(IReadOnlyList<BenchStep> steps, double headroom)
    {
        double ceiling = 1 - headroom;
        BenchStep? below = null;
        foreach (var s in steps)
        {
            if (s.GameThread <= ceiling) { below = s; continue; }
            // The first step over the line decides it, even if a later one dips back under: a limit read
            // off the optimistic side of the noise is the one that costs an operator a full server.
            if (below is null) return new BenchBand(headroom, 0, s.WorkingSetBytes, AtLeast: false);

            double span = s.GameThread - below.GameThread;
            double t = span <= 0 ? 0 : Math.Clamp((ceiling - below.GameThread) / span, 0, 1);
            return new BenchBand(headroom,
                below.Players + (int)Math.Round((s.Players - below.Players) * t),
                below.WorkingSetBytes + (long)((s.WorkingSetBytes - below.WorkingSetBytes) * t),
                AtLeast: false);
        }
        return new BenchBand(headroom, below?.Players ?? 0, below?.WorkingSetBytes ?? 0, AtLeast: below is not null);
    }
}

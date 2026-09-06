using System.Diagnostics;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// How hard the game thread is working.
///
/// <para><b>The game thread is the server's real ceiling.</b> Everything that touches game state runs on
/// one thread, so a box with eight cores still has exactly one core's worth of simulation. Total process
/// CPU is the wrong number to judge headroom by — a fully pinned game thread reads as ~12% of an 8-core
/// machine, which looks like room and is not.</para>
///
/// <para>Written ONLY by the game thread and read by anyone, so each counter is a plain field written in
/// one place. A reader can tear a pair of longs on a 32-bit runtime; the values are advisory samples for
/// a load report, and pinning them behind a lock would put contention on the thread being measured.</para>
/// </summary>
public sealed class GameLoopMetrics
{
    private long _busyTicks;
    private long _wallTicks;
    private long _iterations;
    private long _overruns;
    private long _queuedActions;
    private long _lastSampleStamp;

    /// <summary>Stopwatch ticks the thread spent RUNNING — draining posted work and running due ticks —
    /// as opposed to parked on the queue wait.</summary>
    public long BusyTicks => Interlocked.Read(ref _busyTicks);

    /// <summary>Stopwatch ticks of wall time the loop has been alive. Busy over wall is the utilisation
    /// that matters.</summary>
    public long WallTicks => Interlocked.Read(ref _wallTicks);

    public long Iterations => Interlocked.Read(ref _iterations);

    /// <summary>Iterations whose work took longer than the shortest tick interval, meaning the loop was
    /// already late when it came round again. The first number to go wrong under load.</summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    /// <summary>Posted actions drained. Rate matters more than total: it is inbound packet volume.</summary>
    public long QueuedActions => Interlocked.Read(ref _queuedActions);

    /// <summary>Records one pass of the loop. <paramref name="busy"/> excludes the queue wait.</summary>
    public void Record(long busy, long wall, int drained, bool overran)
    {
        Interlocked.Add(ref _busyTicks, busy);
        Interlocked.Add(ref _wallTicks, wall);
        Interlocked.Add(ref _queuedActions, drained);
        Interlocked.Increment(ref _iterations);
        if (overran) Interlocked.Increment(ref _overruns);
    }

    /// <summary>Takes a reading and resets the window, so consecutive calls describe the interval between
    /// them rather than all of history. The benchmark samples per ramp step and wants each step's own
    /// numbers, not a running average that hides the moment it fell over.</summary>
    public GameLoopSample Sample()
    {
        long busy = Interlocked.Exchange(ref _busyTicks, 0);
        long wall = Interlocked.Exchange(ref _wallTicks, 0);
        long iterations = Interlocked.Exchange(ref _iterations, 0);
        long overruns = Interlocked.Exchange(ref _overruns, 0);
        long queued = Interlocked.Exchange(ref _queuedActions, 0);
        long now = Stopwatch.GetTimestamp();
        long since = Interlocked.Exchange(ref _lastSampleStamp, now);

        // Wall time between SAMPLES, not the loop's own accounting, when we have a previous stamp: it
        // includes anything that stalled the thread outside the loop's own measurement, which is exactly
        // the sort of thing a load test is looking for.
        long window = since == 0 ? wall : now - since;
        return new GameLoopSample(
            Utilisation: window <= 0 ? 0 : Math.Clamp(busy / (double)window, 0, 1),
            Iterations: iterations,
            Overruns: overruns,
            QueuedActions: queued,
            WindowSeconds: window / (double)Stopwatch.Frequency);
    }
}

/// <summary>One reading of the game thread over a window. <paramref name="Utilisation"/> is 0-1 of ONE
/// core, because that is all the simulation ever gets.</summary>
public readonly record struct GameLoopSample(
    double Utilisation,
    long Iterations,
    long Overruns,
    long QueuedActions,
    double WindowSeconds)
{
    /// <summary>Headroom left on the game thread, 0-1.</summary>
    public double Headroom => 1 - Utilisation;
}

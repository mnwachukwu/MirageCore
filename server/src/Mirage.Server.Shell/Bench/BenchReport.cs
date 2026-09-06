namespace Mirage.Server.Shell.Bench;

/// <summary>One ramp step: what the server looked like with this many players walking on it. The player
/// count is the SERVER's own, taken from its roster rather than from how many connections the bench
/// believes it opened.</summary>
/// <param name="GameThread">0-1 of one core. The number that decides everything — see
/// <see cref="Mirage.Server.Core.GameLogic.GameLoopMetrics"/>.</param>
/// <param name="ProcessCpu">0-1 of the whole machine, for context only.</param>
/// <param name="Maps">How many distinct maps the players were spread over. The biggest caveat on the
/// whole number: a move is broadcast to everyone who can see it, so the same player count costs wildly
/// different amounts crowded into one map than scattered over fifty.</param>
public sealed record BenchStep(
    int Players,
    double GameThread,
    double ProcessCpu,
    long WorkingSetBytes,
    long Overruns,
    double PacketsPerSecond,
    int Maps);

/// <summary>How many players fit while leaving <paramref name="Headroom"/> of the game thread spare.
/// <paramref name="AtLeast"/> means the ramp finished without ever using that much, so the figure is the
/// most that was measured rather than the point where it ran out.</summary>
public sealed record BenchBand(double Headroom, int Players, long WorkingSetBytes, bool AtLeast);

/// <summary>Why the ramp stopped.</summary>
public enum BenchOutcome
{
    /// <summary>Reached the number asked for with the game thread still keeping up.</summary>
    Reached,

    /// <summary>The game thread ran out. The honest answer to "how many can this machine take".</summary>
    ThreadSaturated,

    /// <summary>The server stopped letting players in — its own limit, or it was too busy to finish a
    /// handshake.</summary>
    JoinsFailed,

    /// <summary>Connections that were in the world died on their own, which is worse than being refused
    /// politely.</summary>
    PlayersDropped,

    Cancelled,
}

/// <summary>The whole run.</summary>
public sealed record BenchReport
{
    public IReadOnlyList<BenchStep> Steps { get; init; } = [];

    /// <summary>Players supported at each headroom band, largest headroom first.</summary>
    public IReadOnlyList<BenchBand> Bands { get; init; } = [];

    public BenchOutcome Outcome { get; init; }

    /// <summary>The most players that were in the world at once.</summary>
    public int Peak { get; init; }

    public int Target { get; init; }
    public int ProcessorCount { get; init; }

    /// <summary>Memory with the world loaded and nobody on it. Most of the total is the world, so the
    /// cost per player is the difference between this and a step, not the step itself.</summary>
    public long BaselineBytes { get; init; }

    /// <summary>Beats the BENCH failed to deliver on time. Not a server fault: it means this machine was
    /// also busy pretending to be the players, and the load it applied was lighter than it claimed. Any
    /// non-zero value makes the run a lower bound.</summary>
    public int MissedBeats { get; init; }

    /// <summary>What the server said when it refused a player, when there is one.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>Where the run has got to. The dialog shows this while it works, because a benchmark that
/// takes minutes and says nothing is one an operator kills.</summary>
public sealed record BenchProgress(BenchPhase Phase, int Players, int Target, BenchStep? Step);

public enum BenchPhase
{
    /// <summary>Copying the world to a scratch folder.</summary>
    Preparing,

    /// <summary>Waiting for the scratch server to load and open its port.</summary>
    Booting,

    /// <summary>Connecting the next batch of simulated players.</summary>
    Joining,

    /// <summary>Holding the load steady and reading the game thread.</summary>
    Measuring,

    /// <summary>Disconnecting, stopping the server, deleting the scratch world.</summary>
    Finishing,
}

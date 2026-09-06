namespace Mirage.Shared.Protocol;

/// <summary>
/// A snapshot of what the server currently IS, for an operator's dashboard: who is online and the
/// world state they are in.
///
/// <para>A structured message rather than something scraped out of the console. The console prints a
/// human-facing table, localized into four languages and free to be reworded — a UI built on parsing
/// it breaks the first time somebody edits a string.</para>
/// </summary>
public sealed record ServerStatus
{
    /// <summary>The line prefix that marks a status message on an otherwise human-readable stream. Both
    /// shells filter it out of the console view; a terminal never receives one, because the emitter is
    /// off unless a consumer asked for it.</summary>
    public const string LinePrefix = "@@MIRAGE-STATUS@@ ";

    /// <summary>What an attached operator sends to start receiving <see cref="LinePrefix"/> lines.
    ///
    /// <para>Opt-in, because the management socket is otherwise a plain console and a client that did
    /// not ask should not have to filter machine lines out of it. A LOCAL shell asks by starting the
    /// server with <c>--status-events</c>; a REMOTE one has no command line to pass, so it sends this.</para>
    ///
    /// <para>It lives beside the prefix deliberately. The request and the thing it requests are one
    /// protocol, and keeping the server’s copy inside its own listener is how the client came to never
    /// send it at all — which left a remotely attached shell with a blank dashboard AND a blank
    /// moderation tab, since both ride this one stream.</para></summary>
    public const string RequestStatus = "MIRAGE-WANT-STATUS";

    public string TimePhase { get; init; } = "";
    public string Weather { get; init; } = "";
    public string Motd { get; init; } = "";

    /// <summary>Seconds since the world finished loading.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>The game port, so a dashboard can state it without being told separately.</summary>
    public int Port { get; init; }

    /// <summary>How many operators are attached over the management port.</summary>
    public int Operators { get; init; }

    public IReadOnlyList<PlayerSummary> Players { get; init; } = [];

    /// <summary>Connected editor sessions. Reported apart from <see cref="Players"/> because an editor holds
    /// no character: it has no level, no class and no map, and a row that leaves those blank in a player list
    /// says less than a list of its own.</summary>
    public IReadOnlyList<EditorSummary> Editors { get; init; } = [];

    /// <summary>How hard the machine is working for this world. Rides the same snapshot because a load
    /// report and a dashboard want the same numbers at the same moment.</summary>
    public LoadSummary Load { get; init; } = new();
}

/// <summary>
/// The server's cost, in the three numbers that answer different questions.
///
/// <para><b>Read <see cref="GameThread"/> first.</b> Everything that touches game state runs on ONE
/// thread, so that is the ceiling. A pinned game thread on an eight-core box shows up as ~12% in
/// <see cref="ProcessCpu"/>, which looks like room and is not — the other cores can only ever carry
/// encryption and socket I/O, never simulation.</para>
/// </summary>
public sealed record LoadSummary
{
    /// <summary>0-1 of ONE core: the share of wall time the game thread spent working rather than parked
    /// waiting for the next packet or tick.</summary>
    public double GameThread { get; init; }

    /// <summary>0-1 of the WHOLE machine, across every core. Rises with connection count independently of
    /// the game thread, because TLS and socket work land on the thread pool.</summary>
    public double ProcessCpu { get; init; }

    /// <summary>Resident memory, bytes.</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Loop passes whose work outlasted the shortest tick interval, so the next pass began late.
    /// The first thing to go wrong under load, and it moves well before anything is visible in game.</summary>
    public long Overruns { get; init; }

    /// <summary>Posted actions drained per second — inbound packet volume, near enough.</summary>
    public double QueuedPerSecond { get; init; }

    /// <summary>Cores the machine has. Without it a reader cannot tell whether 12% of the process means
    /// one saturated thread or twelve idle ones.</summary>
    public int ProcessorCount { get; init; }
}

/// <summary>
/// One connected editor session.
///
/// <para><see cref="Holding"/> is what the session has open with unsaved changes — the thing an operator
/// actually wants before ending it, because those edits go with the connection.</para>
/// </summary>
public sealed record EditorSummary
{
    public int Slot { get; init; }
    /// <summary>Account name, or empty while the login is still in flight.</summary>
    public string Login { get; init; } = "";
    public string Access { get; init; } = "";
    /// <summary>Records held, as "Maps#60" strings. Empty when the session is just reading.</summary>
    public IReadOnlyList<string> Holding { get; init; } = [];
}

/// <summary>One online player, in the terms a dashboard row needs. <see cref="Login"/> is carried
/// because the account is what /ban and /setaccess act on, while the NAME is what an operator reads.</summary>
public sealed record PlayerSummary
{
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public string Login { get; init; } = "";
    public int Level { get; init; }
    public string Class { get; init; } = "";
    public int Map { get; init; }
    public string Access { get; init; } = "";
}

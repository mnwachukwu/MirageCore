namespace Mirage.Shared.Protocol;

/// <summary>
/// Everything an operator needs to decide whether a punishment should stand: who is banned, and whose
/// kick or mute is still running.
///
/// <para>Assembled ON DEMAND rather than riding <see cref="ServerStatus"/>. That snapshot is pushed on
/// every roster change, and this one has to read the ban file and sweep the account files — a cost worth
/// paying when an operator opens the page, and not one to pay every time somebody logs in.</para>
///
/// <para>A structured message rather than something scraped out of the console, for the same reason
/// <see cref="ServerStatus"/> is: the console prints a human table in four languages and is free to be
/// reworded.</para>
/// </summary>
public sealed record ModerationReport
{
    /// <summary>The line prefix marking this on an otherwise human-readable stream. Both shells filter it
    /// out of the console view; a terminal never receives one, because nothing is emitted unless a
    /// consumer asked for machine lines.</summary>
    public const string LinePrefix = "@@MIRAGE-MODERATION@@ ";

    public IReadOnlyList<BanSummary> Bans { get; init; } = [];

    /// <summary>Kicks and mutes that have not yet run out. An EXPIRED one is not listed — it is not a
    /// punishment any more, and listing it would make lifting look like it did nothing.</summary>
    public IReadOnlyList<PenaltySummary> Penalties { get; init; } = [];

    /// <summary>Machines refused entry, or reported on arrival, depending on the server's mode.</summary>
    public IReadOnlyList<HardwareBanSummary> HardwareBans { get; init; } = [];

    /// <summary>What a machine-ban match does here — "Signal" or "Block". Carried because the list above
    /// means two different things under the two modes, and a page showing rows without saying which would
    /// let an operator believe they are refusing people they are only watching.</summary>
    public string HardwareBanMode { get; init; } = "";

    /// <summary>How many account files were swept to produce <see cref="Penalties"/>. Shown so an
    /// operator can tell an empty list apart from a list that was never gathered.</summary>
    public int AccountsScanned { get; init; }
}

/// <summary>
/// A banned machine, as an operator sees it.
///
/// <para>The key itself is deliberately absent. It is 64 hex characters that identify a person's computer,
/// it cannot be typed back in usefully, and every lift is by account — so sending it would be handing a
/// dashboard an identifier it has no use for. <see cref="Login"/> is who was signed in when the ban
/// landed, which is the handle the lift takes.</para>
/// </summary>
public sealed record HardwareBanSummary
{
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Unix seconds when the machine was banned.</summary>
    public long BannedAtUtc { get; init; }
}

/// <summary>A banned account. There is no expiry — a ban runs until somebody lifts it.</summary>
public sealed record BanSummary
{
    public string Login { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Unix seconds, or 0 when the entry predates the field.</summary>
    public long BannedAtUtc { get; init; }
}

/// <summary>
/// A kick or a mute that is still running.
///
/// <para><see cref="IsOnline"/> matters for a MUTE specifically: the live player carries its own copy of
/// the expiry, so lifting the account alone would leave them muted until they relogged. A kicked player
/// is by definition not online.</para>
/// </summary>
public sealed record PenaltySummary
{
    public string Login { get; init; } = "";
    /// <summary>"Kick" or "Mute".</summary>
    public string Kind { get; init; } = "";
    /// <summary>Unix seconds the penalty runs until.</summary>
    public long ExpiresUtc { get; init; }
    public bool IsOnline { get; init; }
    /// <summary>The character they are logged in as, when they are. Empty otherwise — an account has no
    /// one character.</summary>
    public string CharName { get; init; } = "";
}

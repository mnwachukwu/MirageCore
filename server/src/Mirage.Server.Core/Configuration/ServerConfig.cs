namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Everything an operator configures about their server: port, operator language, remote access, and the
/// switchable game rules. appsettings.json configures the APPLICATION (Serilog) and is hand-authored;
/// this is machine-owned and rewritten by the shell.
///
/// <para>Only values NO CLIENT READS may live here. <c>Constants</c> is in <c>Mirage.Shared</c> and its
/// values are <c>const</c>, so anything the client compiles against is inlined into it — moving one here
/// desyncs every connected client silently. Cooldowns, the blood model and the record ceilings stay
/// const for that reason.</para>
/// </summary>
public sealed record ServerConfig
{
    /// <summary>The stock rules: what the game does with no config file present. Shared, and safe to
    /// share, because the whole graph is immutable.</summary>
    public static readonly ServerConfig Default = new();

    /// <summary>What this game is called.
    ///
    /// <para>Defaults to the ENGINE's name, which is what an operator who has not renamed anything gets.
    /// A client has no game identity of its own: it is branded with the engine name until a server tells
    /// it this, in the pre-login hello, and shows this from then on.</para>
    ///
    /// <para><b>Never use it for a file path.</b> The executable names, the shell's settings folder and a
    /// player's own settings folder all stay on <c>Constants.GameName</c> — a rename must not move
    /// anybody's files, or relocate the server binary the shell launches.</para></summary>
    public string GameName
    {
        get;
        init => field = value.Trim() is { Length: > 0 } named ? named : Mirage.Shared.Constants.GameName;
    } = Mirage.Shared.Constants.GameName;

    /// <summary>The TCP port the game listens on.</summary>
    public int Port { get; init; } = Mirage.Shared.Constants.GamePort;

    /// <summary>The OPERATOR's language — console, logs, and the shell's chrome. Players are unaffected;
    /// their messages resolve through each session's own locale.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Where THIS INSTALLATION's state lives: accounts, guilds, market listings, trade journals,
    /// seasons, dropped items, the name registry, the ban lists and the clock. Empty means the per-user
    /// <c>data/</c> resolved by <see cref="ServerPaths"/> — not a folder beside the executable, which an
    /// update deletes.
    ///
    /// <para>Everything here changes while the server runs and means nothing beside a different world, so
    /// it is deliberately NOT where the world is — see <see cref="WorldDir"/>.</para>
    ///
    /// <para>Here rather than in appsettings.json because it configures the SERVER, which is the line the
    /// two files are split on.</para></summary>
    public string DataDir { get; init; } = "";

    /// <summary>Where the WORLD lives: maps, the record families, <c>world.json</c> and the MOTD. Empty
    /// means the per-user <c>world/</c> resolved by <see cref="ServerPaths"/>.
    ///
    /// <para>Nothing here changes unless somebody edits it, which is what lets a world be zipped up and
    /// handed to another machine — and what stops a copy carrying anybody's password hashes with it. This
    /// is the folder the EDITOR opens.</para></summary>
    public string WorldDir { get; init; } = "";

    /// <summary>What a player loses when they die.</summary>
    public DeathPenaltyConfig DeathPenalty { get; init; } = new();

    /// <summary>How many players this server accepts at once, and the size of every per-player array it
    /// allocates. Deliberately SMALL by default: the right number depends on the machine, and the server
    /// window's load benchmark measures it rather than asking an operator to guess.
    ///
    /// <para>Clamped to <c>Constants.MaxPlayers</c>, the protocol ceiling. Above that the server would
    /// issue slot numbers a shipped client cannot index.</para></summary>
    /// <para>Every server-side scan walks <c>PlayerManager.Slots</c> — this value — rather than the
    /// constant, so a small world does no work and holds no array for slots it will never use.</para>
    public int MaxPlayers
    {
        get;
        init => field = Math.Clamp(value, 1, Mirage.Shared.Constants.MaxPlayers);
    } = 20;

    /// <summary>Slots held back from the general public for Moderators and above, so a full server can
    /// still be moderated. Read <see cref="EffectiveReservedSlots"/>, never this — the two settings are
    /// independent fields on one record.</summary>
    public int ReservedSlots
    {
        get;
        init => field = Math.Max(0, value);
    } = 2;

    /// <summary>What is actually held back. Clamped against <see cref="MaxPlayers"/> HERE rather than in
    /// the setter because JSON promises no property order: a file listing <c>reservedSlots</c> first would
    /// clamp against a limit that had not been read yet. Never reaches <see cref="MaxPlayers"/>, or every
    /// regular player is locked out permanently.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int EffectiveReservedSlots => Math.Clamp(ReservedSlots, 0, MaxPlayers - 1);

    /// <summary>What happens to players who arrive at a full server.</summary>
    public QueueConfig Queue { get; init; } = new();

    /// <summary>How many of each record family this world has room for. Read
    /// <see cref="Records"/> — the setter clamps, so nothing downstream has to.</summary>
    public Mirage.Shared.RecordLimits Records
    {
        get;
        init => field = (value ?? Mirage.Shared.RecordLimits.Default)
            .Clamped(Mirage.Shared.RecordLimits.Ceiling);
    } = Mirage.Shared.RecordLimits.Default;

    /// <summary>Where a character starts, and where one respawns without a purchased spawn point.</summary>
    public SpawnConfig Spawn { get; init; } = new();

    /// <summary>When the weekly territory contest runs.</summary>
    public ScheduleConfig Schedule { get; init; } = new();

    /// <summary>Remote operator access. Off unless configured.
    ///
    /// <para>The one member with a setter. `/management` changes it on a RUNNING server — the headless
    /// deployment cannot edit a file and bounce the process casually, which is the whole reason the
    /// command exists — so the live config has to be able to catch up with what was just applied.
    /// Replacing the record wholesale rather than editing inside it keeps the swap a single reference
    /// assignment, which the console thread and the game thread can both survive.</para></summary>
    public ManagementConfig Management { get; set; } = new();

    /// <summary>What a machine ban does when it matches.</summary>
    public HardwareBanConfig HardwareBans { get; init; } = new();
}

/// <summary>
/// What happens when a login arrives from a machine an operator has banned.
///
/// <para>Nothing is banned until an operator runs <c>/hwban</c>, so an empty ban list makes the mode moot
/// on a stock server. It only starts mattering once somebody has decided a person should be gone, which
/// is why the default is the one that acts on that decision.</para>
/// </summary>
public sealed record HardwareBanConfig
{
    /// <summary>Refuse or report. See <see cref="HardwareBanMode"/>.</summary>
    public HardwareBanMode Mode { get; init; } = HardwareBanMode.Block;
}

/// <summary>How a machine-ban match is enforced.
/// <para><see cref="Block"/> is FIRST so it is also the enum's zero value, and therefore what a config
/// missing this setting lands on however it got there — the default is expressed once, not twice.</para>
/// </summary>
public enum HardwareBanMode
{
    /// <summary>Refuse the login outright, exactly as an account ban does — and refuse a registration
    /// too, which is the hole the feature exists to close.
    ///
    /// <para>The default. <c>/hwban</c> is a last resort an operator reaches for deliberately, and a ban
    /// that lets the person straight back in is not what they asked for. Two costs come with it and are
    /// worth knowing rather than discovering: machines cloned from one disk image share an identifier, so
    /// a whole computer lab can go down with one ban; and a refusal cannot be explained to the person
    /// refused without saying how the scheme works. <see cref="Signal"/> exists for operators who would
    /// rather look first.</para></summary>
    Block,

    /// <summary>Let them in, and tell the operators. The login succeeds, a warning is logged, and every
    /// Monitor and above online is told which account just signed in from a banned machine.
    ///
    /// <para>Turns the feature into something an account ban cannot do at all — it names the alt — at the
    /// price of not actually stopping anybody.</para></summary>
    Signal,
}

/// <summary>
/// The world's front door: where a new character is placed, and where anyone without a purchased spawn
/// point comes back.
///
/// <para>The defaults are the middle of map 1, which is what these were as computed constants. Nothing on
/// the client reads them, so they are a plain server setting.</para>
/// </summary>
public sealed record SpawnConfig
{
    public int Map { get; init; } = 1;
    public int X { get; init; } = (Mirage.Shared.Constants.MaxMapX + 1) / 2;
    public int Y { get; init; } = (Mirage.Shared.Constants.MaxMapY + 1) / 2;

    /// <summary>Where a character comes back to: the point they paid for at an Inn, or this default when
    /// they have not set one. Death and /home both land here, so neither can drift from the other.
    ///
    /// <para>Plain integers, so the answer can name any tile on any map. A narrower type here would silently
    /// wrap a coordinate past its width and put someone somewhere they never chose.</para></summary>
    public (int Map, int X, int Y) HomeFor(Mirage.Shared.Records.PlayerRecord p) =>
        p.SpawnMap > 0 ? (p.SpawnMap, p.SpawnX, p.SpawnY) : (Map, X, Y);
}

/// <summary>
/// The line at a full server.
///
/// <para>A waiting connection is a socket, a TLS session and a place in a list — it holds NO player slot.
/// That is what keeps a queue cheap enough to be worth having: nothing about a waiting player reaches the
/// game thread until the moment they are let in.</para>
/// </summary>
public sealed record QueueConfig
{
    /// <summary>How many may wait. <b>0 turns queueing off</b> and restores the old behaviour, a refusal
    /// at the door. Capped rather than open because queued sockets cost memory and file handles, and an
    /// unbounded line is a denial of service with extra steps.</summary>
    public int MaxDepth
    {
        get;
        init => field = Math.Max(0, value);
    } = 100;

    /// <summary>How long a dropped connection keeps its place. Covers a network blip without punishing it,
    /// and covers a held slot too: if someone's turn arrives while they are away, the slot waits this long
    /// before going to the next in line.</summary>
    public int GraceSeconds
    {
        get;
        init => field = Math.Max(0, value);
    } = 90;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEnabled => MaxDepth > 0;
}

/// <summary>
/// The weekly territory contest. Server-local, so a world keeps the evening its players actually play on
/// rather than one derived from UTC.
///
/// <para>The DAILY guild settlement is deliberately not here: it runs at midnight on the host box and
/// <c>GuildScheduleSystem</c> walks whole calendar days, which is what makes a slot missed during downtime
/// replay correctly on the next boot.</para>
/// </summary>
public sealed record ScheduleConfig
{
    public DayOfWeek WarNightDay { get; init; } = DayOfWeek.Saturday;

    /// <summary>0-23, server-local.</summary>
    public int WarNightHour { get; init; } = 20;

    /// <summary>The weekly boundary — territory income snapshots, season weeks and the weekly quest reset.
    /// DERIVED as the day after war night, never configured separately: the two were a pair of constants
    /// documented as "the day after", and two settings could be moved out of step with each other.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DayOfWeek WeekResetDay => (DayOfWeek)(((int)WarNightDay + 1) % 7);
}

/// <summary>
/// The remote management channel: the same console the local shell drives, reachable over a socket.
///
/// <para>Both fields must be set for the listener to start. Either one alone is a misconfiguration, not a
/// half-enabled server — an open port with no token would be an unauthenticated console.</para>
/// </summary>
public sealed record ManagementConfig
{
    /// <summary>Listening port, or 0 for off. Separate from the game port so remote administration can be
    /// firewalled without touching the port players connect to.</summary>
    public int Port { get; init; }

    /// <summary>The shared secret an attaching shell must present. Empty refuses the listener.</summary>
    public string Token { get; init; } = "";

    /// <summary>True when this server should accept remote operators. Derived, so it is kept out of the
    /// file — a serialized copy would be a second place the answer could be written down.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEnabled => Port > 0 && Token.Length > 0;
}

/// <summary>
/// The three components of a death penalty, each switchable on its own.
///
/// <para>Two things survive whatever these say: the sub-level-10 spare, and the ORDER at each death site
/// — drops run before durability damage so a piece that breaks still gets its equipped drop chance. The
/// switches gate the penalty helpers from the inside so no call site moves.</para>
/// </summary>
public sealed record DeathPenaltyConfig
{
    /// <summary>Worn gear loses durability on death. Casting reagents ride this switch rather than the
    /// item drop: they are the caster's durability, priced against the same repair curve.</summary>
    public bool DurabilityLoss { get; init; } = true;

    /// <summary>Items fall out of the bag on death.</summary>
    public bool ItemDrop { get; init; } = true;

    /// <summary>Death costs EXP, and enough of it costs levels. Off also means a PvP killer earns
    /// nothing — that reward is transferred out of the victim's loss, so there is nothing to hand
    /// over. PvE EXP is unaffected.</summary>
    public bool ExpLoss { get; init; } = true;
}

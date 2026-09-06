namespace Mirage.Server.Core.Configuration;

/// <summary>Which knobs the file actually exposed. A path that does not resolve is reported rather than
/// guessed at, so a restructured appsettings.json greys a control out instead of being overwritten.</summary>
[Flags]
public enum LogKnobs
{
    None = 0,
    MinimumLevel = 1,
    OutgoingPackets = 2,
    IncomingPackets = 4,
    ServerRetention = 8,
    NetworkRetention = 16,
    All = MinimumLevel | OutgoingPackets | IncomingPackets | ServerRetention | NetworkRetention,
}

/// <summary>
/// The five log settings worth an operator's attention, read out of appsettings.json.
///
/// <para>Everything else in that file is STRUCTURE — the three-way Logger split, the filter expressions,
/// the output templates — and a UI over those would only be a worse text editor. It stays hand-authored.</para>
/// </summary>
public sealed record LogSettings
{
    /// <summary>Serilog levels an operator picks between, coarsest last.</summary>
    public static readonly string[] Levels = ["Debug", "Information", "Warning", "Error"];

    /// <summary>What a packet logger is set to when its switch is on. The packet loggers are filtered into
    /// their own sink, so this decides whether that sink sees anything at all.</summary>
    public const string PacketsOn = "Debug";

    /// <summary>And when it is off. Not "Information" — these loggers emit at Debug, so anything above it
    /// silences them equally, and Warning leaves room for a genuine transport complaint to still land.</summary>
    public const string PacketsOff = "Warning";

    public string MinimumLevel { get; init; } = "Information";
    public bool LogOutgoingPackets { get; init; }
    public bool LogIncomingPackets { get; init; }
    public int ServerLogRetentionDays { get; init; } = 7;
    public int NetworkLogRetentionDays { get; init; } = 3;

    /// <summary>The knobs that resolved on load. Save only writes these.</summary>
    public LogKnobs Available { get; init; } = LogKnobs.All;

    public bool Has(LogKnobs knob) => (Available & knob) != 0;
}

using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

public sealed record WarpMeToPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpMeTo;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record WarpToMePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpToMe;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

/// <summary>Toggle the sender's observer mode. Carries nothing: the server owns the state, so a client
/// cannot ask to be put into it, only to flip it.</summary>
public sealed record GodModePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GodMode;
}

public sealed record WarpToPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpTo;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

public sealed record SetSpritePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetSprite;
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
}

public sealed record SetAccessPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetAccess;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("level")] public AdminLevel Level { get; init; }
}

public sealed record KickPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.KickPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("minutes")] public int Minutes { get; init; }
}

public sealed record BanPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BanPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record MutePlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MutePlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("minutes")] public int Minutes { get; init; }
}

public sealed record RefreshBanListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RefreshBanList;
}

/// <summary>
/// C→S: ban the account AND the machine the target is signed in from. Creator only, and only against
/// somebody ONLINE — the key lives on the live session and nowhere else, so there is nothing to record
/// for a player who is not here. An offline account is what the ordinary ban is for.
/// </summary>
public sealed record HwBanPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.HwBanPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

// ── Lifting a punishment ─────────────────────────────────────────────────────
// Target is an ACCOUNT here, unlike the three above: a kicked or banned person cannot be online to be
// named by their character. The server accepts an online character's name as a convenience and resolves
// it, but the account is what is acted on.

public sealed record UnbanPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UnbanPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record UnkickPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UnkickPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record UnmutePlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UnmutePlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

/// <summary>C→S: lift every machine ban recorded against an account. The account's own ban is a
/// separate lift — <see cref="UnbanPlayerPacket"/> — because the two were applied for different reasons
/// and an operator may well want only one of them gone.</summary>
public sealed record HwUnbanPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.HwUnbanPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

/// <summary>C→S: asks for everything currently in force. Answered with <see cref="ModerationListPacket"/>.</summary>
public sealed record RequestModerationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestModeration;
}

/// <summary>
/// S→C: every ban and every running kick or mute, for the Creator's moderation panel. Replaced
/// wholesale, and pushed again after any lift so the panel never shows a row that is already gone.
///
/// <para>Carries the same summaries the server window's report does — a punishment is a punishment
/// whichever surface is looking at it, and two shapes would be two things to keep in step.</para>
///
/// <para> Nothing here is a password or an account record. It is the login, what was done to it, and
/// when it runs out; a panel that wanted more would be a reason to send less, not more.</para>
/// </summary>
public sealed record ModerationListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ModerationList;
    [JsonPropertyName("bans")] public List<BanSummary> Bans { get; init; } = new();
    [JsonPropertyName("penalties")] public List<PenaltySummary> Penalties { get; init; } = new();
    [JsonPropertyName("hwbans")] public List<HardwareBanSummary> HardwareBans { get; init; } = new();
    /// <summary>"Signal" or "Block" — what a machine-ban match does on this server. The panel says so
    /// out loud, because the same list of rows means two different things under the two modes.</summary>
    [JsonPropertyName("hwmode")] public string HardwareBanMode { get; init; } = "";
    /// <summary>How many accounts were swept, so an empty list is distinguishable from one never gathered.</summary>
    [JsonPropertyName("scanned")] public int AccountsScanned { get; init; }
}

public sealed record MapRespawnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapRespawn;
}

public sealed record MapReportPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapReport;
}

public sealed record SetMotdPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetMotd;
    [JsonPropertyName("msg")] public string Motd { get; init; } = "";
}

public sealed record SetTimeOfDayPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetTimeOfDay;
    [JsonPropertyName("phase")] public TimePhase Phase { get; init; }
}

public sealed record SetWeatherPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetWeather;
    [JsonPropertyName("weather")] public WeatherType Weather { get; init; }
}

public sealed record PlayerInfoRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerInfoRequest;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

/// <summary>C→S: /played — the requester's own playtime readout (current character + account total). No
/// target; playtime is not admin-gated (account details are visible by design).</summary>
public sealed record PlayedRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayedRequest;
}

/// <summary>C→S: /home — go to the character's own spawn point. No destination on the wire: where home
/// is belongs to the server, and a client that could name it could name anywhere.</summary>
public sealed record HomeRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.HomeRequest;
}

/// <summary>C→S: /homecd — how long is left on the /home cooldown. Separate from
/// <see cref="HomeRequestPacket"/> so asking can never spend it.</summary>
public sealed record HomeCooldownRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.HomeCooldownRequest;
}

using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// Sent to client when they first enter the game world
public sealed record WelcomePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Welcome;
    [JsonPropertyName("index")] public int Index { get; init; }
}

public sealed record PlayerInGamePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerInGame;
}

/// <summary>C→S: the dead player clicked Respawn. The server honors it only once the respawn timer has
/// elapsed; an early request is ignored.</summary>
public sealed record RespawnRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RespawnRequest;
}

public sealed record SendPlayerDataPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendPlayerData;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("spriteSheet")] public int SpriteSheet { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    // Two-layer world: the player's logical layer (ground vs bridge-top fringe), so a re-syncing observer
    // renders them on the right layer. Omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
    [JsonPropertyName("map")] public int Map { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("class")] public int Class { get; init; }
    [JsonPropertyName("sex")] public Sex Sex { get; init; }
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("pkExpiryUtc")] public long PkExpiryUtc { get; init; }
    [JsonPropertyName("graceUntilUtc")] public long GraceUntilUtc { get; init; }
    // 0 = not an aggressor. Otherwise UTC seconds when the aggressor flag lapses.
    [JsonPropertyName("aggressorUntilUtc")] public long AggressorUntilUtc { get; init; }
    // Observer mode, for the overhead name only. Nullable on the same rule as the guild fields below:
    // combat and movement broadcasts leave it out and the client keeps its cached value, so a name cannot
    // flicker back to its access colour on the next hit somebody else takes nearby.
    [JsonPropertyName("god")] public bool? GodMode { get; init; }
    // Guild display fields — nullable so ordinary broadcasts (combat/PK/movement) omit them and the
    // client keeps its cached value; only guild-aware broadcasts carry them. GuildId 0 = guildless.
    [JsonPropertyName("gid")] public int? GuildId { get; init; }
    [JsonPropertyName("grank")] public GuildRank? GuildRank { get; init; }
    [JsonPropertyName("gname")] public string? GuildName { get; init; }
    [JsonPropertyName("gopen")] public bool? GuildOpen { get; init; }
    // Overhead guild-name color, packed 0xRRGGBB (0 = unset → a neutral default). Nullable like the
    // other guild fields so only guild-aware broadcasts carry it.
    [JsonPropertyName("gcolor")] public int? GuildColor { get; init; }
    // Leader toggle (field name predates the repurpose): when on, show the guild's SEASONAL STANDING as "(N)"
    // in the overhead cluster. The member RANK word now shows unconditionally; this gates only the
    // standing. Nullable like the other guild fields — carried only on guild-aware broadcasts; the client keeps
    // its cached value otherwise.
    [JsonPropertyName("gshowrank")] public bool? GuildShowRank { get; init; }
    // The guild's 1-based seasonal standing (leaderboard position; 0 = unranked). Shown as "(N)" in the overhead
    // cluster when GuildShowRank is on. Nullable/guild-aware-only, like the fields above.
    [JsonPropertyName("gstanding")] public int? GuildStanding { get; init; }
    // Death state: observers render a corpse while Dead; the victim's own copy drives the
    // death-panel countdown from RespawnReadyUtc.
    [JsonPropertyName("dead")] public bool Dead { get; init; }
    [JsonPropertyName("respawnReadyUtc")] public long RespawnReadyUtc { get; init; }
}

// Slim refresh of the aggressor expiry for a single player. Sent on every aggressor-refresh hit
// during a fight so the client's flashing-name window stays perfectly aligned with the server's
// rolling 30 s timer. Edge transitions (set / clear) ride on SendPlayerDataPacket instead, so this
// packet is only emitted when the flag was already on and the timer was extended.
public sealed record AggressorRefreshPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AggressorRefresh;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("aggressorUntilUtc")] public long AggressorUntilUtc { get; init; }
}

public sealed record LeftGamePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LeftGame;
    [JsonPropertyName("index")] public int Index { get; init; }
}

public sealed record WeatherPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Weather;
    [JsonPropertyName("weather")] public WeatherType Weather { get; init; }
}

public sealed record TimeOfDayPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TimeOfDay;
    [JsonPropertyName("phase")] public TimePhase Phase { get; init; }
    [JsonPropertyName("progress")] public float Progress { get; init; }
}

public sealed record WhoIsOnlinePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WhoIsOnline;
}

public sealed record PlayersOnlinePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayersOnline;
    [JsonPropertyName("count")] public int Count { get; init; }
}

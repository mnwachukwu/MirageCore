using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// One row in a social list (friends, ignore) or a guild roster. All three are per-ACCOUNT lists that
/// display the account plus a snapshot of its most-recently-active character, so they share one shape.
/// <see cref="Online"/> is resolved live by the server when the packet is built (never persisted), and
/// <see cref="LastSeenUtc"/> is only meaningful when offline.
/// </summary>
public sealed record SocialEntry
{
    /// <summary>Account login — the identity of the row (these lists are per-account).</summary>
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>True when this account currently has a character in the world.</summary>
    [JsonPropertyName("online")] public bool Online { get; init; }
    /// <summary>UTC-seconds of last logout; only rendered when <see cref="Online"/> is false. 0 = never recorded.</summary>
    [JsonPropertyName("lastSeenUtc")] public long LastSeenUtc { get; init; }
    /// <summary>The account's active character when online, else its last-active character snapshot.</summary>
    [JsonPropertyName("charName")] public string CharName { get; init; } = "";
    [JsonPropertyName("charClass")] public int CharClass { get; init; }
    [JsonPropertyName("charLevel")] public int CharLevel { get; init; }
    /// <summary>Guild rank — set only on guild-roster rows; always <see cref="GuildRank.None"/> on a
    /// friends/ignore row (those lists are guild-agnostic).</summary>
    [JsonPropertyName("rank")] public GuildRank Rank { get; init; }
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the account's full friends + ignore lists (sent on entering the world and after any
/// change). Both are replaced wholesale.</summary>
public sealed record SocialListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SocialList;
    [JsonPropertyName("friends")] public List<SocialEntry> Friends { get; init; } = new();
    [JsonPropertyName("ignore")] public List<SocialEntry> Ignore { get; init; } = new();
}

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: add the account behind an ONLINE character to my friends list. Addressed by character
/// name (that is what the player can see/right-click); the server resolves it to the account.</summary>
public sealed record SocialAddFriendPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SocialAddFriend;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>C→S: add the account behind an ONLINE character to my ignore list.</summary>
public sealed record SocialAddIgnorePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SocialAddIgnore;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>C→S: drop an account from my friends list. Addressed by login (the row's identity), so it
/// works whether or not that account is online.</summary>
public sealed record SocialRemoveFriendPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SocialRemoveFriend;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>C→S: drop an account from my ignore list.</summary>
public sealed record SocialRemoveIgnorePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SocialRemoveIgnore;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

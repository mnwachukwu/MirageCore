using Mirage.Shared.Records;   // GuildDonationEntry (the donor-log entry lives with the guild record)
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: found a new guild with the given name. The server re-validates funds, name
/// uniqueness, and eligibility, deducts the creation cost, and broadcasts the founding announcement.</summary>
public sealed record GuildCreatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildCreate;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>C→S: disband the sender's guild. The server enforces leader-only + no-other-members.</summary>
public sealed record GuildDisbandPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildDisband;
}

/// <summary>C→S: begin a guild-join offer against <see cref="TargetName"/>. IsRequest=false is an
/// invite (an Officer+ inviting a guildless player); IsRequest=true is a join-request (a guildless
/// player asking an Officer+ of an open guild).</summary>
public sealed record GuildOfferInitiatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildOfferInitiate;
    [JsonPropertyName("target")] public string TargetName { get; init; } = "";
    [JsonPropertyName("req")] public bool IsRequest { get; init; }
}

/// <summary>C→S: respond to the pending guild offer — accept (join / approve) or decline.</summary>
public sealed record GuildOfferRespondPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildOfferRespond;
    [JsonPropertyName("accept")] public bool Accept { get; init; }
}

/// <summary>C→S: leader toggles the guild's open-for-membership flag (open = accepts join-requests,
/// closed = invite-only).</summary>
public sealed record GuildSetOpenPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildSetOpen;
    [JsonPropertyName("open")] public bool Open { get; init; }
}

/// <summary>C→S: leader toggles whether the member rank word shows in the overhead name cluster.</summary>
public sealed record GuildSetShowRankPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildSetShowRank;
    [JsonPropertyName("show")] public bool Show { get; init; }
}

/// <summary>C→S: leave my own guild (a leader must transfer or disband instead).</summary>
public sealed record GuildLeavePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildLeave;
}

/// <summary>C→S: kick a member by account login. Officer+, and cannot target an equal/higher rank.</summary>
public sealed record GuildKickPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildKick;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>C→S: promote a member to officer (Leader only).</summary>
public sealed record GuildPromotePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildPromote;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>C→S: demote an officer to member (Leader only).</summary>
public sealed record GuildDemotePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildDemote;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>C→S: leader offers leadership to an officer (by account login); the officer must accept.</summary>
public sealed record GuildTransferPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildTransfer;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>C→S: leader sets the guild MOTD (shown only in the guild panel, never on login).</summary>
public sealed record GuildSetMotdPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildSetMotd;
    [JsonPropertyName("motd")] public string Motd { get; init; } = "";
}

/// <summary>C→S: leader sets the guild's descriptive labels (up to Constants.MaxGuildLabels).</summary>
public sealed record GuildSetLabelsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildSetLabels;
    [JsonPropertyName("labels")] public List<GuildLabel> Labels { get; init; } = new();
}

/// <summary>C→S (leader): set the guild's overhead color. <see cref="Rgb"/> is packed 0xRRGGBB; the
/// server rejects a value the <see cref="GuildColorPolicy"/> deems reserved (a named palette color).</summary>
public sealed record GuildSetColorPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildSetColor;
    [JsonPropertyName("rgb")] public int Rgb { get; init; }
}

/// <summary>C→S: donate gold from the sender into their guild's vault (a transfer, not a sink).</summary>
public sealed record GuildDonatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildDonate;
    [JsonPropertyName("amount")] public int Amount { get; init; }
}

/// <summary>C→S: a guild-chat line. <see cref="Officer"/> routes it to the Guild Officer channel
/// (leader/officers only) instead of the guild-wide Guild channel. Guildless senders are a no-op.</summary>
public sealed record GuildChatPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildChat;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
    [JsonPropertyName("officer")] public bool Officer { get; init; }
}

// ── Discovery (open-guild browser + applications) ─────────────────────────────

/// <summary>C→S: (guildless) request the current list of open-for-membership guilds.</summary>
public sealed record GuildBrowseRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildBrowseRequest;
}

/// <summary>One open guild in the browser.</summary>
public sealed record GuildBrowseEntry
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("members")] public int Members { get; init; }
    [JsonPropertyName("labels")] public List<GuildLabel> Labels { get; init; } = new();
}

/// <summary>S→C: the open-for-membership guilds a guildless player can apply to.</summary>
public sealed record GuildBrowsePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildBrowse;
    [JsonPropertyName("guilds")] public List<GuildBrowseEntry> Guilds { get; init; } = new();
}

/// <summary>C→S: (guildless) apply to an open guild by index. Held as a pending application the guild's
/// leader/officers review; the outcome is mailed back.</summary>
public sealed record GuildApplyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildApply;
    [JsonPropertyName("index")] public int Index { get; init; }
}

/// <summary>C→S: (leader/officer) approve or reject a pending application, addressed by applicant login.</summary>
public sealed record GuildReviewApplicationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildReviewApplication;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("accept")] public bool Accept { get; init; }
}

/// <summary>C→S: re-send my <see cref="GuildInfoPacket"/>. Sent when the Guild tab opens, so the
/// roster's live online column is current even though a member going offline can't push one.</summary>
public sealed record GuildInfoRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildInfoRequest;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the recipient has a pending guild offer to confirm. OtherName + GuildName describe
/// the other party and guild; Kind selects the prompt — an invite to join, a request to approve, or a
/// leadership transfer to accept.</summary>
public sealed record GuildOfferNotifyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildOfferNotify;
    [JsonPropertyName("guild")] public string GuildName { get; init; } = "";
    [JsonPropertyName("other")] public string OtherName { get; init; } = "";
    [JsonPropertyName("kind")] public GuildOfferKind Kind { get; init; }
}

/// <summary>S→C: everything the Social panel's Guild tab renders — the recipient's guild identity,
/// leader-set presentation (MOTD/labels/open flag), and the full member roster. Sent on entering the
/// world and re-sent to every online member after any guild mutation. <see cref="InGuild"/> false means
/// the recipient is guildless (the tab shows the create/browse on-ramp instead) and the rest is unset.</summary>
public sealed record GuildInfoPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildInfo;
    [JsonPropertyName("inGuild")] public bool InGuild { get; init; }
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("motd")] public string Motd { get; init; } = "";
    [JsonPropertyName("labels")] public List<GuildLabel> Labels { get; init; } = new();
    [JsonPropertyName("open")] public bool OpenForMembership { get; init; }
    [JsonPropertyName("showRankOverhead")] public bool ShowRankOverhead { get; init; }
    [JsonPropertyName("color")] public int Color { get; init; }
    /// <summary>Vault balance — the shared purse.</summary>
    [JsonPropertyName("vaultGold")] public long VaultGold { get; init; }
    /// <summary>Vault dashboard totals: gold credited from any other source, and gold donated by members.</summary>
    [JsonPropertyName("income")] public long Income { get; init; }
    [JsonPropertyName("donations")] public long Donations { get; init; }
    /// <summary>The recipient's own rank — drives which management controls the panel enables.</summary>
    [JsonPropertyName("myRank")] public GuildRank MyRank { get; init; }
    /// <summary>One row per member account, ordered by rank then character level (the panel's display order).</summary>
    [JsonPropertyName("roster")] public List<SocialEntry> Roster { get; init; } = new();
    /// <summary>Pending applicant logins (only acted on by a Leader/Officer; the panel shows the review
    /// controls to them). Empty for a non-open guild or one with no applications.</summary>
    [JsonPropertyName("applications")] public List<string> Applications { get; init; } = new();
    /// <summary>Recent vault donations (newest first) for the Vault tab's donor log — the donor ACCOUNT and
    /// the amount. See <see cref="GuildDonationEntry"/>.</summary>
    [JsonPropertyName("recentDonations")] public List<GuildDonationEntry> RecentDonations { get; init; } = new();
    /// <summary>Recent vault SPENDING for the Vault tab's Spending view. See <see cref="GuildSpendingEntry"/>.</summary>
    [JsonPropertyName("recentSpending")] public List<GuildSpendingEntry> RecentSpending { get; init; } = new();
}

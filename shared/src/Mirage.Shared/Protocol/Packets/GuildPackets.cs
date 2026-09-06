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

/// <summary>C→S: donate valor (the war currency) from the sender into their guild's vault. Vault valor
/// auto-offsets the weekly tax at settlement.</summary>
public sealed record GuildDonateValorPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildDonateValor;
    [JsonPropertyName("amount")] public int Amount { get; init; }
}

/// <summary>C→S: (Officer+) pay one week's tax late to restore suspended guild perks at once.</summary>
public sealed record GuildPayTaxPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildPayTax;
}

/// <summary>C→S: (Leader) acquire a new guild quest.</summary>
public sealed record GuildQuestAcquirePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildQuestAcquire;
}

/// <summary>C→S: (Leader) abandon the active guild quest, forfeiting all progress (no gold refund) so a
/// fresh quest can be acquired.</summary>
public sealed record GuildQuestAbandonPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildQuestAbandon;
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
    [JsonPropertyName("level")] public int Level { get; init; }
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
    [JsonPropertyName("level")] public int Level { get; init; }
    /// <summary>Cumulative guild XP — with <see cref="Level"/>, drives the panel's progress-to-next-level.</summary>
    [JsonPropertyName("exp")] public long Exp { get; init; }
    /// <summary>Vault balances (gold + war-currency valor) and whether level perks are currently in force
    /// (false = suspended for unpaid tax).</summary>
    [JsonPropertyName("vaultGold")] public long VaultGold { get; init; }
    [JsonPropertyName("vaultValor")] public int VaultValor { get; init; }
    [JsonPropertyName("perksActive")] public bool PerksActive { get; init; }
    /// <summary>Everything the guild has earned and not yet banked: what arrives in
    /// <see cref="VaultGold"/> at the next daily settlement.
    ///
    /// <para>🔴 It is a SUM of two accumulators that reach the vault by different routes — the L5 perk pot
    /// (<c>GuildRecord.PendingPerkIncome</c>, banked by <c>CreditDailyGold</c>) and the pending income of
    /// every territory the guild controls (banked by <c>SettleTerritoryIncome</c>, which credits the vault
    /// directly and never touches the perk pot). Reporting only the perk half shows nothing at all to a
    /// guild below level 5, which is most of them, while their territories are earning.</para>
    ///
    /// <para>Kept beside the balance rather than folded into it: it climbs while the guild hunts and drops
    /// to zero when it lands, so adding it in would make the vault look like it lost that gold every
    /// settlement.</para></summary>
    [JsonPropertyName("pendingIncomeTotal")] public long PendingIncomeTotal { get; init; }
    /// <summary>Weekly financial-health running totals for the vault dashboard (income received, member
    /// donations, war spend this week — all on the season-week cadence). The expected weekly tax amount is
    /// derived client-side from Level; <see cref="DaysUntilTax"/> carries its SEPARATE founding-weekday
    /// cadence (1-7, days until the next tax settlement) so the client shows tax on its own schedule.</summary>
    [JsonPropertyName("weeklyIncome")] public long WeeklyIncome { get; init; }
    [JsonPropertyName("weeklyDonations")] public long WeeklyDonations { get; init; }
    [JsonPropertyName("weeklyWarCosts")] public long WeeklyWarCosts { get; init; }
    [JsonPropertyName("daysUntilTax")] public int DaysUntilTax { get; init; }
    /// <summary>The recipient's own rank — drives which management controls the panel enables.</summary>
    [JsonPropertyName("myRank")] public GuildRank MyRank { get; init; }
    /// <summary>One row per member account, ordered by rank then character level (the panel's display order).</summary>
    [JsonPropertyName("roster")] public List<SocialEntry> Roster { get; init; } = new();
    /// <summary>Pending applicant logins (only acted on by a Leader/Officer; the panel shows the review
    /// controls to them). Empty for a non-open guild or one with no applications.</summary>
    [JsonPropertyName("applications")] public List<string> Applications { get; init; } = new();
    /// <summary>The guild's active quest for the Quests board, or null if none.</summary>
    [JsonPropertyName("quest")] public GuildQuestView? Quest { get; init; }
    /// <summary>Active wars for the War sub-panel, one row per opposing guild (empty if the guild is at
    /// peace). See <see cref="GuildWarView"/>.</summary>
    [JsonPropertyName("wars")] public List<GuildWarView> Wars { get; init; } = new();
    /// <summary>Pending officer war-requests awaiting Leader review — populated only for Officer+ recipients
    /// (the leadership queue); empty for a Member. See <see cref="GuildWarRequestView"/>.</summary>
    [JsonPropertyName("warRequests")] public List<GuildWarRequestView> WarRequests { get; init; } = new();
    /// <summary>Every territory (all guilds), alphabetical, for the Territories sub-tab — owner, weeks held,
    /// and previous-week income. Global data (same for everyone); carried here since the tab is guild-scoped.
    /// See <see cref="TerritoryView"/>.</summary>
    [JsonPropertyName("territories")] public List<TerritoryView> Territories { get; init; } = new();
    /// <summary>Recent vault donations (newest first) for the Vault tab's donor log — the donor ACCOUNT, gold
    /// vs valor, and amount. See <see cref="GuildDonationEntry"/>.</summary>
    [JsonPropertyName("recentDonations")] public List<GuildDonationEntry> RecentDonations { get; init; } = new();
    /// <summary>Recent vault SPENDING (war-death repairs the vault absorbed) for the Vault tab's Spending view.
    /// See <see cref="GuildSpendingEntry"/>.</summary>
    [JsonPropertyName("recentSpending")] public List<GuildSpendingEntry> RecentSpending { get; init; } = new();
}

/// <summary>S→C: one territory row for the Territories sub-tab. <see cref="Owner"/> is the
/// controlling guild's name, or blank when unclaimed. <see cref="PreviousWeekIncome"/> is 0 for
/// unclaimed/untaxed. Contesting-guild info rides the own-territory flag.</summary>
public sealed record TerritoryView
{
    /// <summary>The territory's MapGroup index — what a challenge packet references.</summary>
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("owner")] public string Owner { get; init; } = "";
    [JsonPropertyName("weeksHeld")] public int WeeksHeld { get; init; }
    [JsonPropertyName("prevIncome")] public long PreviousWeekIncome { get; init; }
    /// <summary>Gold accrued since the last midnight settlement, not yet in the vault. Sent only for the
    /// viewer’s OWN territory (0 elsewhere) — it is a live read on how hard a guild is being farmed right
    /// now, which is theirs to see and nobody else’s. Recipient-specific.</summary>
    [JsonPropertyName("pendingTerritoryIncome")] public long PendingTerritoryIncome { get; init; }
    /// <summary>Settled income so far this week, before the weekly roll into <see cref="PreviousWeekIncome"/>.
    /// Own territory only, for the same reason. Recipient-specific.</summary>
    [JsonPropertyName("incomeWeek")] public long IncomeThisWeek { get; init; }
    /// <summary>True when the viewing player’s guild controls this territory — what tells a genuine zero
    /// income from a figure that was never sent. Recipient-specific.</summary>
    [JsonPropertyName("ours")] public bool OwnedByUs { get; init; }
    /// <summary>The registered challengers' guild names (comma-joined; blank when none) — the "contesting"
    /// flag for the Territories tab (esp. on the guild's own territory).</summary>
    [JsonPropertyName("contesting")] public string Contesting { get; init; } = "";
    /// <summary>True when the viewing player's own guild is registered to challenge this territory (drives the
    /// Challenge/Withdraw button). Recipient-specific.</summary>
    [JsonPropertyName("byUs")] public bool ChallengedByUs { get; init; }
}

/// <summary>S→C: the active guild quest for the Quests board — target mob, kill progress, rewards, and
/// expiry (the client renders the countdown from <see cref="ExpiresUtc"/>).</summary>
public sealed record GuildQuestView
{
    [JsonPropertyName("npc")] public int TargetNpc { get; init; }
    [JsonPropertyName("npcName")] public string TargetNpcName { get; init; } = "";
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("progress")] public int Progress { get; init; }
    [JsonPropertyName("rewardExp")] public long RewardExp { get; init; }
    [JsonPropertyName("rewardGold")] public long RewardGold { get; init; }
    [JsonPropertyName("expiresUtc")] public long ExpiresUtc { get; init; }
}

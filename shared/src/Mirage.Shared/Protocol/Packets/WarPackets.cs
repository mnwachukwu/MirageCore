using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: declare war on another guild (by its index). Doubles as the "return a declaration"
/// action — if the target has already declared on the sender's guild, the server reciprocates it into a
/// mutual war (free) instead of opening a new one. Leader acts directly; an Officer's attempt posts a
/// leadership request instead. The server re-validates rank, level, limits, and vault funds.</summary>
public sealed record GuildWarDeclarePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarDeclare;
    [JsonPropertyName("target")] public int TargetGuildIndex { get; init; }
}

/// <summary>C→S: declare war on another guild BY NAME. This is the client-facing declare — a player has no
/// full guild roster to pick an index from, so the War panel prompts for the target's name and the server
/// resolves it to a guild index, then runs the same <see cref="GuildWarDeclarePacket"/> logic (fresh
/// declaration or, if that guild already declared on us, a free reciprocation).</summary>
public sealed record GuildWarDeclareByNamePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarDeclareByName;
    [JsonPropertyName("name")] public string TargetName { get; init; } = "";
}

/// <summary>C→S: retract a still one-sided declaration against <see cref="OpponentIndex"/> (allowed only
/// after the retraction lock elapses; a mutual war can't be retracted — it ends via peace/attrition). Leader
/// acts directly; an Officer's attempt posts a leadership request instead.</summary>
public sealed record GuildWarRetractPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarRetract;
    [JsonPropertyName("opponent")] public int OpponentIndex { get; init; }
}

/// <summary>C→S (Leader): accept or deny a pending officer war-request, addressed by its (kind, target).
/// Accept executes the queued action (declare/return or retract); deny discards it.</summary>
public sealed record GuildWarReviewRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarReviewRequest;
    [JsonPropertyName("kind")] public GuildWarRequestKind Kind { get; init; }
    [JsonPropertyName("target")] public int TargetIndex { get; init; }
    [JsonPropertyName("accept")] public bool Accept { get; init; }
}

/// <summary>C→S: a peace action on a mutual war with <see cref="OpponentIndex"/>. Offer =
/// sue for peace (concede) — Leader acts, Officer's attempt queues it; Withdraw/Accept/Reject are
/// Leader-direct (accept ends the war with the accepter as winner; reject leaves it running). With no ante
/// locked, an Offer MUST carry an <see cref="Offering"/> (the vault gold staked as the pot); ignored otherwise.</summary>
public sealed record GuildWarPeacePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarPeace;
    [JsonPropertyName("opponent")] public int OpponentIndex { get; init; }
    [JsonPropertyName("action")] public GuildWarPeaceAction Action { get; init; }
    [JsonPropertyName("offering")] public long Offering { get; init; }
}

/// <summary>C→S (Leader): a consensual-wager action on a mutual war with <see cref="OpponentIndex"/>.
/// Propose stakes a matched ante (<see cref="Amount"/>, up to 50% of our vault, within the first hour
/// of the war going mutual); Accept escrows the opponent's proposal on both sides; Withdraw/Reject clear a
/// pending proposal. Winner-take-all; a cold draw returns each side's stake.</summary>
public sealed record GuildWarWagerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarWager;
    [JsonPropertyName("opponent")] public int OpponentIndex { get; init; }
    [JsonPropertyName("action")] public GuildWarWagerAction Action { get; init; }
    [JsonPropertyName("amount")] public long Amount { get; init; }
}

/// <summary>C→S: register a challenge for <see cref="TerritoryIndex"/> (a MapGroup) at the next war
/// night. Leader acts directly; an Officer's attempt posts a leadership request (TerritoryChallenge
/// kind). Server re-validates: it's a territory, we don't already own it, the challenger slots aren't full,
/// we aren't already challenging elsewhere, and we can afford the cost (a non-refundable sink).</summary>
public sealed record GuildTerritoryChallengePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildTerritoryChallenge;
    [JsonPropertyName("territory")] public int TerritoryIndex { get; init; }
}

/// <summary>C→S (Leader/Officer): withdraw our pending challenge for <see cref="TerritoryIndex"/> before war
/// night. The challenge cost is NOT refunded, and withdrawing does NOT restore a territory this guild
/// abandoned by challenging (that abandonment is irrevocable).</summary>
public sealed record GuildTerritoryWithdrawPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildTerritoryWithdraw;
    [JsonPropertyName("territory")] public int TerritoryIndex { get; init; }
}

/// <summary>C→S (Creator debug): run the daily/weekly/season guild settlement NOW, idempotently,
/// across every guild — without disturbing the normal schedule.</summary>
public sealed record AdminGuildResetPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AdminGuildReset;
    [JsonPropertyName("scope")] public SettlementScope Scope { get; init; }
}

/// <summary>C→S (Creator debug): drive the territory war-night lifecycle off-schedule — start it
/// (full ramp-up), advance the live contest one phase, or bring it straight to cooldown.</summary>
public sealed record AdminTerritoryWarPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AdminTerritoryWar;
    [JsonPropertyName("action")] public TerritoryWarDebugAction Action { get; init; }
}

// ── S→C view models ──────────────────────────────────────────────────────────

/// <summary>One row of the guild's War sub-panel — carried inside <see cref="GuildInfoPacket"/>. The
/// <see cref="Status"/> is resolved server-side as of send time; the client renders the warmup /
/// retraction-lock countdowns from the two UTC stamps.</summary>
public sealed record GuildWarView
{
    [JsonPropertyName("opp")] public int OpponentIndex { get; init; }
    [JsonPropertyName("oppName")] public string OpponentName { get; init; } = "";
    [JsonPropertyName("status")] public GuildWarStatus Status { get; init; }
    /// <summary>UTC-seconds hostilities go live (a warmup countdown, in the future while in warmup).</summary>
    [JsonPropertyName("goLiveUtc")] public long GoLiveUtc { get; init; }
    /// <summary>UTC-seconds this guild declared (for the retraction-lock countdown); 0 if we didn't declare.</summary>
    [JsonPropertyName("declaredUtc")] public long DeclaredUtc { get; init; }
    /// <summary>Gold this guild pays per day to maintain THIS war (the war-tab detail). Non-zero only for a
    /// one-sided war we declared (mutual waives maintenance, a pure defender pays nothing).</summary>
    [JsonPropertyName("dailyCost")] public long DailyCost { get; init; }
    /// <summary>Our side's attrition meter and the opponent's, for the mutual-war tug-of-war bar (both 0 for a
    /// one-sided war, which has no attrition). Push the opponent's to 0 to win.</summary>
    [JsonPropertyName("attrition")] public int Attrition { get; init; }
    [JsonPropertyName("oppAttrition")] public int OpponentAttrition { get; init; }
    /// <summary>Pending peace pleas: whether WE have sued the opponent for peace (we can withdraw), and
    /// whether THEY have sued us (we can accept = we win, or reject).</summary>
    [JsonPropertyName("peaceByUs")] public bool PeaceOfferedByUs { get; init; }
    [JsonPropertyName("peaceByThem")] public bool PeaceOfferedByThem { get; init; }
    /// <summary>Gold each side has staked on a pending peace plea (the no-ante case — it becomes the pot the
    /// accepter wins); 0 for an ante-concession plea or when no plea is out.</summary>
    [JsonPropertyName("peaceGoldByUs")] public long PeaceEscrowByUs { get; init; }
    [JsonPropertyName("peaceGoldByThem")] public long PeaceEscrowByThem { get; init; }
    /// <summary>Wager state: our locked matched ante (<see cref="AnteEscrow"/>, symmetric with the
    /// opponent so the pot is twice this), and any pending ante PROPOSAL from either side awaiting accept/reject.
    /// All 0 when no wager is in play.</summary>
    [JsonPropertyName("ante")] public long AnteEscrow { get; init; }
    [JsonPropertyName("wagerByUs")] public long WagerProposedByUs { get; init; }
    [JsonPropertyName("wagerByThem")] public long WagerProposedByThem { get; init; }
    /// <summary>UTC-seconds the wager window closes (mutual-start + 1h); a new ante can only be agreed before
    /// this. 0 = not mutual / no window. The client shows the ante controls only while <c>now < this</c>.</summary>
    [JsonPropertyName("wagerDeadlineUtc")] public long WagerDeadlineUtc { get; init; }
}

/// <summary>One pending officer war-request in the War sub-panel's review list — carried inside
/// <see cref="GuildInfoPacket"/> for Officer+ recipients only. The Leader accepts/denies each via
/// <see cref="GuildWarReviewRequestPacket"/> addressed by (<see cref="Kind"/>, <see cref="TargetIndex"/>).</summary>
public sealed record GuildWarRequestView
{
    [JsonPropertyName("kind")] public GuildWarRequestKind Kind { get; init; }
    [JsonPropertyName("target")] public int TargetIndex { get; init; }
    [JsonPropertyName("targetName")] public string TargetName { get; init; } = "";
    [JsonPropertyName("by")] public string RequesterName { get; init; } = "";
}

// ── S→C live meter ────────────────────────────────────────────────────────────

/// <summary>S→C: a lightweight live update of one mutual war's attrition meters, pushed to both guilds'
/// members on each war death. The full <see cref="GuildInfoPacket"/> only carries attrition on a request or
/// a broadcasting mutation (a war death persists it silently to avoid per-death broadcast spam), so this
/// tiny packet animates the War panel's tug-of-war bar + trend arrow between full syncs. Values are from the
/// RECIPIENT guild's perspective: <see cref="OurAttrition"/> is their side, <see cref="TheirAttrition"/> the
/// opponent's; <see cref="OpponentIndex"/> identifies which war row to update.</summary>
public sealed record GuildWarAttritionPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildWarAttrition;
    [JsonPropertyName("opp")] public int OpponentIndex { get; init; }
    [JsonPropertyName("ours")] public int OurAttrition { get; init; }
    [JsonPropertyName("theirs")] public int TheirAttrition { get; init; }
}

/// <summary>S→C: the live render state of a territory contest, pushed ONLY to participant-guild members
/// on each contest tick + at setup start; <see cref="Active"/> = false tears it down (flags/HUD vanish)
/// at war end. Carries the capture points (world position + controlling guild + signed meter) and the KotH
/// scoreboard; the client colors flags/circles per-viewer (own guild = blue, enemy = red, neutral = gray) and
/// gates the in-world HUD on standing in a point / in the territory.</summary>
public sealed record TerritoryContestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TerritoryContest;
    [JsonPropertyName("active")] public bool Active { get; init; }
    [JsonPropertyName("terr")] public int TerritoryIndex { get; init; }
    [JsonPropertyName("name")] public string TerritoryName { get; init; } = "";
    /// <summary>Contest phase as an int (0 = Setup, 1 = Contest, 2 = Cooldown) — ContestPhase lives server-side.</summary>
    [JsonPropertyName("phase")] public int Phase { get; init; }
    [JsonPropertyName("points")] public List<ContestPointView> Points { get; init; } = new();
    [JsonPropertyName("scores")] public List<ContestScoreView> Scores { get; init; } = new();
    /// <summary>Where every map of the territory sits in one shared tile space, so a client can place a
    /// capture point on a map it has not loaded.
    ///
    /// <para>🔴 Without this the client can only locate a point on one of the nine maps around it — a map
    /// number alone says nothing about direction or distance. The territory's maps are joined by their
    /// edge links, so flooding those links lays them all out on one grid; the layout cannot change while
    /// a contest runs, so it is computed once and sent with every tick rather than recomputed.</para></summary>
    [JsonPropertyName("layout")] public List<ContestMapView> Layout { get; init; } = new();
}

/// <summary>One map of a contested territory, placed in the territory's shared tile space. A tile on that
/// map is at (<see cref="OriginX"/> + x, <see cref="OriginY"/> + y).</summary>
public sealed record ContestMapView
{
    [JsonPropertyName("map")] public int Map { get; init; }
    [JsonPropertyName("ox")] public int OriginX { get; init; }
    [JsonPropertyName("oy")] public int OriginY { get; init; }
}

/// <summary>One capture point in a <see cref="TerritoryContestPacket"/> — its label + world tile, the guild
/// that currently controls it (0 = neutral/contested), and the signed capture meter for the capture-status HUD.</summary>
public sealed record ContestPointView
{
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("map")] public int Map { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("owner")] public int OwnerGuild { get; init; }
    [JsonPropertyName("challenger")] public int ChallengerGuild { get; init; }
    [JsonPropertyName("meter")] public int Meter { get; init; }
    // Two-layer world: the logical layer the point sits on (Ground omitted on the wire), so the client draws its
    // marker on the right plane (a bridge-top point occludes with the deck).
    [JsonPropertyName("layer")] public WorldLayer Layer { get; init; }
}

/// <summary>One row of the KotH scoreboard in a <see cref="TerritoryContestPacket"/> — a participating guild
/// and its accumulated score, for the top-right territory-score list.</summary>
public sealed record ContestScoreView
{
    [JsonPropertyName("guild")] public int GuildId { get; init; }
    [JsonPropertyName("name")] public string GuildName { get; init; } = "";
    [JsonPropertyName("score")] public long Score { get; init; }
}

/// <summary>C→S: request the current seasonal leaderboard (all guilds), sent when the Standings sub-tab opens.</summary>
public sealed record GuildLeaderboardRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildLeaderboardRequest;
}

/// <summary>S→C: the seasonal leaderboard — every guild, pre-ordered best-first (season score
/// desc, then territory-war K/D, then size, then name). <see cref="Season"/> is the current season number.</summary>
public sealed record GuildLeaderboardPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GuildLeaderboard;
    [JsonPropertyName("season")] public int Season { get; init; }
    [JsonPropertyName("rows")] public List<LeaderboardEntry> Rows { get; init; } = new();
}

/// <summary>One leaderboard row: a guild's name, member-account count, current-season score, and its
/// territory-war kills/deaths (the leaderboard's secondary sort).</summary>
public sealed record LeaderboardEntry
{
    /// <summary>1-based seasonal standing (leaderboard position among scoring guilds); 0 = unranked (score 0).</summary>
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("guild")] public string Guild { get; init; } = "";
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("score")] public long Score { get; init; }
    [JsonPropertyName("kills")] public int Kills { get; init; }
    [JsonPropertyName("deaths")] public int Deaths { get; init; }
}

/// <summary>C→S: request an archived past season for the historical-season browser. <see cref="Season"/> = 0
/// means "the latest archived season".</summary>
public sealed record SeasonArchiveRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SeasonArchiveRequest;
    [JsonPropertyName("season")] public int Season { get; init; }
}

/// <summary>S→C: one archived season's final standings for the browser, plus the ascending list of all
/// archived season numbers (the selector). <see cref="Found"/> = false when nothing has been archived yet.</summary>
public sealed record SeasonArchivePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SeasonArchive;
    [JsonPropertyName("found")] public bool Found { get; init; }
    [JsonPropertyName("season")] public int Season { get; init; }
    [JsonPropertyName("endDate")] public string EndDate { get; init; } = "";
    [JsonPropertyName("seasons")] public List<int> AvailableSeasons { get; init; } = new();
    [JsonPropertyName("standings")] public List<SeasonStanding> Standings { get; init; } = new();
}

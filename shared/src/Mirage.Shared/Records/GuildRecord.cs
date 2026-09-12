using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>A guild — the per-guild save unit (one JSON file per guild under
/// <c>guilds/guild{Index}.json</c>, keyed by <see cref="Index"/>). Membership is per-ACCOUNT: each
/// member's <see cref="AccountRecord.Guild"/> holds this guild's <see cref="Index"/> and
/// <see cref="AccountRecord.GuildRank"/> their rank. <see cref="Members"/> is a roster cache for
/// offline display + fast enumeration, kept in sync with the member accounts at each mutation.
///
/// <para>Core knows a guild as a named, ranked roster with a shared purse, and nothing else. Whatever
/// else a game wants a group to accumulate — standing, a level, a season's score — goes in
/// <see cref="Attributes"/>, where the game names it and Core carries it.</para></summary>
public sealed class GuildRecord
{
    /// <summary>Filename stem inside <c>guilds/</c>; the trailing number is the <see cref="Index"/>.</summary>
    public const string FileStem = "guild";

    /// <summary>Set when the guild disbands. The record is KEPT — name, roster and vault history stay
    /// readable — so a disbanded guild's past is still answerable and its name is not immediately
    /// reusable.</summary>
    public bool Disbanded { get; set; }

    /// <summary>Guild id (>= 1). In memory only — the guild code holds a guild detached from the index
    /// map during creation and disband, and the number lives in the filename, so writing it as well
    /// would let a copied file claim an index that is not its own.</summary>
    [JsonIgnore]
    public int Index { get; set; }

    /// <summary>Unique, player-chosen name (bounded by <see cref="Constants.NameLength"/>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Overhead guild-name color — a free 24-bit RGB value packed <c>0xRRGGBB</c>, leader-chosen
    /// and filtered by <see cref="GuildColorPolicy"/>.</summary>
    public int Color { get; set; }

    /// <summary>Leader-set message shown only in the guild panel (deliberately never on login).</summary>
    public string Motd { get; set; } = "";

    /// <summary>Up to <see cref="Constants.MaxGuildLabels"/> descriptive tags; shown in the info panel
    /// and the open-guild browser.</summary>
    public List<GuildLabel> Labels { get; set; } = new();

    /// <summary>When true the guild appears in the open-guild browser and accepts applications.</summary>
    public bool OpenForMembership { get; set; }

    /// <summary>Leader toggle: show each member's rank word beside their overhead guild name.</summary>
    public bool ShowRankOverhead { get; set; }

    /// <summary>One entry per member ACCOUNT. Authoritative membership/rank lives on each member's
    /// account record; this is the roster cache that makes an offline member displayable.</summary>
    public List<GuildMember> Members { get; set; } = new();

    /// <summary>Pending membership applications — account logins of guildless players who applied via the
    /// open-guild browser. Cleared when accepted or denied.</summary>
    public List<string> Applications { get; set; } = new();

    // ── Vault ────────────────────────────────────────────────────────────────
    /// <summary>Guild vault gold — the shared purse. A bank whose owner is a group rather than a
    /// player, so it follows the same rules as every other store in the exchange layer.</summary>
    public long VaultGold { get; set; }

    /// <summary>Gold credited to the vault by anything other than a member's donation — whatever this game
    /// pays a group for. Core never adds to it on its own; <c>GuildSystem.CreditVault</c> is the seam.</summary>
    public long Income { get; set; }

    /// <summary>Gold donated to the vault by its members.</summary>
    public long Donations { get; set; }

    /// <summary>Recent vault DONATIONS (incoming) for the Vault tab's Donations view — newest first, capped
    /// at <see cref="Constants.GuildRecentVaultLogMax"/>.</summary>
    public List<GuildDonationEntry> RecentDonations { get; set; } = new();

    /// <summary>Recent vault SPENDING (outgoing) — newest first, same cap.</summary>
    public List<GuildSpendingEntry> RecentSpending { get; set; } = new();

    /// <summary>Whatever else this game's guilds accumulate. Core neither reads nor interprets these; it
    /// persists them, syncs the keys a game marks visible, and shows them in the editor by their declared
    /// <see cref="AttributeSchema"/>.</summary>
    public AttributeBag Attributes { get; set; } = new();

    /// <summary>Deep copy for an off-thread save snapshot (the game thread keeps mutating the live
    /// record). Clones the mutable lists so the writer never observes a half-applied change.</summary>
    public GuildRecord Clone()
    {
        var c = (GuildRecord)MemberwiseClone();
        c.Labels = new List<GuildLabel>(Labels);
        c.Applications = new List<string>(Applications);
        c.Members = new List<GuildMember>(Members.Count);
        foreach (var m in Members) c.Members.Add(m.Clone());
        c.RecentDonations = new List<GuildDonationEntry>(RecentDonations);   // entries are immutable records
        c.RecentSpending = new List<GuildSpendingEntry>(RecentSpending);
        c.Attributes = Attributes.Clone();
        return c;
    }
}

/// <summary>A roster row cached on the <see cref="GuildRecord"/> for offline display. <see cref="Rank"/>
/// mirrors the member's <see cref="AccountRecord.GuildRank"/>; the character snapshot is that account's
/// most-recently-active character (so an offline member still shows a meaningful row).</summary>
public sealed class GuildMember
{
    /// <summary>Account login — the membership unit (guild membership is per-account).</summary>
    public string Login { get; set; } = "";
    /// <summary>Display mirror of the account's <see cref="AccountRecord.GuildRank"/>.</summary>
    public GuildRank Rank { get; set; }
    /// <summary>UTC-seconds of the account's last logout; 0 = never recorded. Online-ness is NOT stored
    /// here — it is derived live from the online player slots when the roster is built, so a crash can
    /// never leave this file claiming a member is online.</summary>
    public long LastSeenUtc { get; set; }
    /// <summary>Rolling "recently active" seconds: accrued at logout by session length, RESET when the
    /// offline gap before a session exceeds <see cref="Constants.GuildActiveMemberWindowSeconds"/>. Read
    /// with <see cref="LastSeenUtc"/> by anything that needs to tell a live roster from a stale one.
    /// Persisted (offline-safe).</summary>
    public long ActiveSeconds { get; set; }

    // Snapshot of the account's most-recently-active character, for the roster row when offline.
    public string CharName { get; set; } = "";
    public int CharClass { get; set; }
    public int CharLevel { get; set; }

    /// <summary>A shallow copy is a full copy — every field is a value type or an immutable string.</summary>
    public GuildMember Clone() => (GuildMember)MemberwiseClone();
}

/// <summary>One recent vault donation for the Vault tab's donor log. Records the donor's ACCOUNT login (guild
/// membership is per-account, so the log credits the account for posterity — the transient chat announce still
/// names the character), the amount, and the UTC-seconds time. Held newest-first + capped on
/// <see cref="GuildRecord.RecentDonations"/>; persisted + sent on GuildInfoPacket.</summary>
public sealed record GuildDonationEntry
{
    [JsonPropertyName("account")] public string Account { get; init; } = "";
    [JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonPropertyName("time")] public long TimeUtc { get; init; }
}

/// <summary>One recent vault SPENDING entry for the Vault tab's Spending view — an outgoing gold payment.
/// Records the member ACCOUNT the payment was on behalf of plus the specific CHARACTER it was for (shown in
/// parens), the gold amount, and the UTC-seconds time. Held newest-first + capped on
/// <see cref="GuildRecord.RecentSpending"/>; persisted + sent on GuildInfoPacket.</summary>
public sealed record GuildSpendingEntry
{
    [JsonPropertyName("account")] public string Account { get; init; } = "";
    [JsonPropertyName("char")] public string Character { get; init; } = "";
    [JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonPropertyName("time")] public long TimeUtc { get; init; }
}

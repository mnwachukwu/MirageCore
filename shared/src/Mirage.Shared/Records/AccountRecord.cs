namespace Mirage.Shared.Records;

/// <summary>Serializable account record — only the fields that are saved to disk.</summary>
public sealed class AccountRecord
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";

    // UTC-seconds expiries for admin penalty timers. 0 = inactive.
    public long KickedUntilUtc { get; set; }
    public long MutedUntilUtc { get; set; }

    // Admin access level — per-ACCOUNT (all characters share it; default Player). Mirrored onto each
    // character's runtime PlayerRecord.Access at login (that field is [JsonIgnore] — never persisted
    // per-character). Set via /setaccess; also gates guild membership and PvP (Monitor+ can do neither).
    public AdminLevel Access { get; set; }

    // ── Guild & social (per-account) ──────────────────────────────────────────
    // Guild membership is per-account: every character on the account shares one guild and one rank.
    // Guild holds the owning GuildRecord.Index (0 = guildless); GuildRank is the account's rank.
    // The guild's roster/rank cache (GuildRecord.Members) is kept in sync at every mutation.
    public int Guild { get; set; }
    public GuildRank GuildRank { get; set; }
    // Per-account social lists, by account login. Friends surface presence/last-seen; an ignored
    // account cannot reach this account on any channel (all communication suppressed).
    public List<string> Friends { get; set; } = new();
    public List<string> Ignore { get; set; } = new();

    // Per-account mailbox (net-new; see MailMessage). Delivered whether the account is online or
    // offline; the guild layer is the first sender.
    public List<MailMessage> Mail { get; set; } = new();

    // Sent mail this account composed (player-origin only; system mail has no outbox). Kept so the same
    // in-transit → delivered state shows on the SENDER's end too. Recipient is the addressed account.
    public List<MailMessage> Outbox { get; set; } = new();

    // 1-based char slots: indices 1..MaxChars; index 0 unused
    public PlayerRecord[] Chars { get; set; } = Enumerable.Range(0, Constants.MaxChars + 1)
                                                           .Select(_ => new PlayerRecord())
                                                           .ToArray();

    // Account-shared bank: every character on the account draws from this one vault.
    // 1-based, indices 1..MaxBankSlots; index 0 unused (left null to match the on-disk layout).
    public PlayerInvSlot[] Bank { get; set; } = NewBank();

    public static PlayerInvSlot[] NewBank()
    {
        var bank = new PlayerInvSlot[Constants.MaxBankSlots + 1];
        for (int i = 1; i <= Constants.MaxBankSlots; i++) bank[i] = new PlayerInvSlot();
        return bank;
    }
}

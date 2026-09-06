using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>The per-session surfaces the server pushes wholesale — character select, shop, bank,
/// mail, marketplace, direct trade, and the social/guild payloads.</summary>
public sealed partial class ClientState
{
    // ── Character select ──────────────────────────────────────────────────────

    public SendCharsPacket.CharSlot[] CharSlots { get; set; } = [];

    // ── Active shop ───────────────────────────────────────────────────────────

    public int ActiveShopNum { get; set; }
    public ShopContentsPacket.BarterRow[] ActiveBarters { get; set; } = [];
    /// <summary>Item numbers the open shop sells for gold, in authored order. Only the NUMBERS travel —
    /// the price is <c>ItemRecord.Price</c> from the item definitions the client already holds, so the
    /// panel looks it up rather than being quoted it per entry.</summary>
    public int[] ActiveSales { get; set; } = [];
    // The keeper's shop number carried on OpenInnPacket: the InnPanel resolves banking / set-
    // spawn / market against this rather than the map, so an inn keeper works from anywhere.
    public int ActiveInnShopNum { get; set; }

    // ── Bank (client-side cache; populated by SendBankPacket / BankSlotUpdatePacket) ──

    public PlayerInvSlot[] Bank { get; } = InitBank();
    public bool BankOpen { get; set; }

    private static PlayerInvSlot[] InitBank()
    {
        var arr = new PlayerInvSlot[Constants.MaxBankSlots + 1];
        for (int i = 0; i <= Constants.MaxBankSlots; i++) arr[i] = new PlayerInvSlot();
        return arr;
    }

    // ── Mail (per-account inbox; populated wholesale by MailboxPacket) ─────────

    /// <summary>The local account's mailbox, newest-last in server order. Replaced whole each time
    /// the server pushes a <c>MailboxPacket</c> (on entering the world and after any change).</summary>
    public List<MailMessage> Mail { get; private set; } = new();

    /// <summary>The local account's SENT mail (outbox), newest-last. Replaced whole alongside <see cref="Mail"/>
    /// by every <c>MailboxPacket</c>; shows the same in-transit → delivered state on the sender's end.</summary>
    public List<MailMessage> Outbox { get; private set; } = new();

    /// <summary>Bumped whenever <see cref="Mail"/> / <see cref="Outbox"/> are replaced, so the Mail panel can
    /// rebuild its list only on an actual change instead of hashing the mailbox every frame.</summary>
    public int MailVersion { get; private set; }

    /// <summary>Server UTC-seconds captured with the last mailbox push. A message is "in transit" while its
    /// DeliverAt exceeds this; frozen between pushes (the server re-syncs when a message matures).</summary>
    public long MailNowUtc { get; private set; }

    public void SetMail(List<MailMessage> mail, List<MailMessage> outbox, long nowUtc)
    {
        Mail = mail;
        Outbox = outbox;
        MailNowUtc = nowUtc;
        MailVersion++;
    }

    /// <summary>Count of unread messages — drives the sidebar Mail link's attention color.
    ///
    /// <para>Counts exactly what the inbox SHOWS, which is the only thing the light can honestly mean.
    /// Mail from an ignored account is hidden client-side, and mail still in transit is hidden until it
    /// lands — a light for a message you cannot open, and will not find when you go looking, is worse
    /// than no light.</para>
    ///
    /// <para><see cref="MailNowUtc"/> is the server's clock as of the last mailbox push rather than this
    /// machine's, so the count and the list agree on what has arrived. The server re-pushes when a message
    /// matures, which is what moves one from hidden to counted.</para></summary>
    public int UnreadMailCount()
    {
        int n = 0;
        foreach (var m in Mail)
            if (!m.IsRead && m.DeliverAt <= MailNowUtc && !IsSenderIgnored(m.Sender)) n++;
        return n;
    }

    /// <summary>True if a mail sender (account login) is on the local ignore list. Ignored mail is still
    /// delivered + stored server-side (no server block), but the inbox and unread count hide it client-side
    /// until the sender is un-ignored — at which point the message (and any attachments) reappear.</summary>
    public bool IsSenderIgnored(string sender)
    {
        if (sender.Length == 0) return false;
        foreach (var e in Ignore)
            if (string.Equals(e.Login, sender, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ── Marketplace (global listings; opened from an inn, replaced wholesale by MarketListPacket) ──

    /// <summary>The current marketplace listings, replaced whole each <c>MarketListPacket</c>.</summary>
    public List<MarketListPacket.Entry> Market { get; private set; } = new();

    /// <summary>The local account login (stamped on each market push), so the Market panel can pick out the
    /// player's own listings for its "My Listings" tab.</summary>
    public string MyLogin { get; private set; } = "";

    /// <summary>Bumped whenever <see cref="Market"/> is replaced, so the Market panel rebuilds only on change.</summary>
    public int MarketVersion { get; private set; }

    /// <summary>Set by a market push carrying the open signal; GameplayScreen polls it to open the Market
    /// panel (persistent-flag convention, mirrors <see cref="BankOpen"/>).</summary>
    public bool MarketOpen { get; set; }

    /// <summary>The local account's completed marketplace sales (seller side), for the panel's Sales tab.</summary>
    public List<MarketSale> MarketSales { get; private set; } = new();

    /// <summary>Server UTC-seconds from the last market push, so the panel can render each listing's time-left
    /// (a frozen snapshot between pushes, like <see cref="MailNowUtc"/>).</summary>
    public long MarketNowUtc { get; private set; }

    public void SetMarket(List<MarketListPacket.Entry> listings, List<MarketSale> sales, string meLogin, bool open, long nowUtc)
    {
        Market = listings;
        MarketSales = sales;
        MyLogin = meLogin;
        MarketNowUtc = nowUtc;
        if (open) MarketOpen = true;
        MarketVersion++;
    }

    // ── Direct trade (live two-party session; state pushed by TradeWindowPacket) ──

    public bool TradeActive { get; private set; }
    public string TradePartner { get; private set; } = "";
    public List<PlayerInvSlot> TradeMine { get; private set; } = new();
    public List<PlayerInvSlot> TradeTheirs { get; private set; } = new();
    public bool TradeMyConfirmed { get; private set; }
    public bool TradeTheirConfirmed { get; private set; }
    /// <summary>Bumped whenever the trade window state is replaced, so the panel rebuilds only on a change.</summary>
    public int TradeVersion { get; private set; }

    public void SetTradeWindow(string partner, List<PlayerInvSlot> mine, List<PlayerInvSlot> theirs, bool myOk, bool theirOk, bool open)
    {
        TradeActive = open;
        TradePartner = partner;
        TradeMine = mine;
        TradeTheirs = theirs;
        TradeMyConfirmed = myOk;
        TradeTheirConfirmed = theirOk;
        TradeVersion++;
    }

    // ── Social panel data (per-account; pushed by the server, replaced wholesale) ──

    /// <summary>Latest guild identity + roster for the Social panel's Guild tab; null until the server's
    /// first push. <c>InGuild == false</c> means the tab shows its create/browse on-ramp.</summary>
    public GuildInfoPacket? GuildInfo { get; private set; }

    /// <summary>The account's friends / ignore lists (logins + a live character snapshot when online).</summary>
    public List<SocialEntry> Friends { get; private set; } = new();
    public List<SocialEntry> Ignore { get; private set; } = new();

    /// <summary>Bumped whenever the guild info or the social lists are replaced, so the Social panel can
    /// rebuild its rows only on an actual change instead of re-reading them every frame.</summary>
    public int SocialVersion { get; private set; }

    public void SetGuildInfo(GuildInfoPacket info)
    {
        GuildInfo = info;
        _warTrend.Clear();
        SocialVersion++;
    }

    // Per-opponent attrition trend for the War panel's direction arrow: +1 our meter rose (we gained), -1 it
    // fell, 0 steady/unknown. Cleared on a full GuildInfo push (a fresh snapshot has no known direction) and
    // refreshed by each live attrition push. Keyed by opponent guild index.
    private readonly Dictionary<int, int> _warTrend = new();

    /// <summary>The last-known attrition direction for the war with <paramref name="opponentIndex"/>
    /// (+1 rising / -1 falling / 0 steady or unknown).</summary>
    public int WarTrend(int opponentIndex) => _warTrend.GetValueOrDefault(opponentIndex);

    /// <summary>Apply a live war-attrition push: update the matching war row's meters in place and record the
    /// trend (comparing our new meter to the previous value), so the War panel animates between full syncs.
    /// No-op if the war isn't in the current snapshot yet (a full GuildInfo will carry it).</summary>
    public void ApplyWarAttrition(int opponentIndex, int ourAttrition, int theirAttrition)
    {
        var wars = GuildInfo?.Wars;
        if (wars is null) return;
        for (int i = 0; i < wars.Count; i++)
        {
            if (wars[i].OpponentIndex != opponentIndex) continue;
            _warTrend[opponentIndex] = Math.Sign(ourAttrition - wars[i].Attrition);
            wars[i] = wars[i] with { Attrition = ourAttrition, OpponentAttrition = theirAttrition };
            SocialVersion++;
            return;
        }
    }

    public void SetSocialLists(List<SocialEntry> friends, List<SocialEntry> ignore)
    {
        Friends = friends;
        Ignore = ignore;
        SocialVersion++;
    }

    // ── Moderation (Creator only) ─────────────────────────────────────────────
    // What is currently in force, pushed on request and again after every lift. Replaced wholesale, like
    // the social lists — a partial update would leave a lifted row on screen beside a button that no
    // longer does anything.

    public List<BanSummary> Bans { get; private set; } = new();
    public List<PenaltySummary> Penalties { get; private set; } = new();
    public List<HardwareBanSummary> HardwareBans { get; private set; } = new();

    /// <summary>"Signal" or "Block" — what this server does when a banned machine arrives. Shown beside
    /// the list, because the same rows mean "watched" under one and "refused" under the other.</summary>
    public string HardwareBanMode { get; private set; } = "";

    /// <summary>How many accounts the server swept, so the panel can tell "nothing is in force" apart
    /// from "nothing has been gathered yet".</summary>
    public int ModerationScanned { get; private set; }

    /// <summary>Whether a report has EVER arrived this session. The two empty states read differently
    /// and a count of zero cannot distinguish them.</summary>
    public bool HasModeration { get; private set; }

    /// <summary>Bumped on each push so the panel rebuilds its rows on a real change rather than every
    /// frame — the same trigger the social lists use.</summary>
    public int ModerationVersion { get; private set; }

    public void SetModeration(List<BanSummary> bans, List<PenaltySummary> penalties,
                              List<HardwareBanSummary> hardwareBans, string hardwareBanMode, int scanned)
    {
        Bans = bans;
        Penalties = penalties;
        HardwareBans = hardwareBans;
        HardwareBanMode = hardwareBanMode;
        ModerationScanned = scanned;
        HasModeration = true;
        ModerationVersion++;
    }

    /// <summary>Open-for-membership guilds a guildless player can apply to (the discovery browser),
    /// pushed on request. Replaced wholesale; shares the <see cref="SocialVersion"/> rebuild trigger.</summary>
    public List<GuildBrowseEntry> GuildBrowse { get; private set; } = new();

    public void SetGuildBrowse(List<GuildBrowseEntry> guilds)
    {
        GuildBrowse = guilds;
        SocialVersion++;
    }

    /// <summary>Latest seasonal leaderboard (every guild, pre-ordered best-first) + its season number, or null
    /// until requested; pushed when the Standings sub-tab opens. Shares the <see cref="SocialVersion"/> trigger.</summary>
    public GuildLeaderboardPacket? Leaderboard { get; private set; }

    public void SetLeaderboard(GuildLeaderboardPacket leaderboard)
    {
        Leaderboard = leaderboard;
        SocialVersion++;
    }

    /// <summary>The archived past season currently shown in the historical-season browser (null until the first
    /// request). Shares the <see cref="SocialVersion"/> rebuild trigger.</summary>
    public SeasonArchivePacket? SeasonArchive { get; private set; }

    public void SetSeasonArchive(SeasonArchivePacket archive)
    {
        SeasonArchive = archive;
        SocialVersion++;
    }
}

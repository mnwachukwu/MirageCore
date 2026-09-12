using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Owns all guild state changes. Every entry point runs on the game thread (guild packet handlers
/// are dispatched there), so it mutates <see cref="GameWorld.Guilds"/> and per-account membership
/// (<see cref="AccountRecord.Guild"/>/<see cref="AccountRecord.GuildRank"/>, mirrored on
/// <see cref="ServerPlayer"/>) lock-free. Each touched guild is persisted through a per-guild
/// serialized off-thread write (<see cref="SaveGuild"/>); per-account membership changes are
/// persisted through <see cref="PlayerSaver.MutateAccountInBackground"/>.
/// </summary>
public sealed partial class GuildSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly PlayerSaver _saver;
    private readonly ItemSystem _items;
    private readonly MailSystem _mail;
    private readonly ObjectiveSystem _objectives;
    private readonly ILogger<GuildSystem> _logger;

    // Per-guild-index chain of pending file writes so two saves of the same guild file never race.
    // Only touched on the game thread (every mutation runs there), and DrainAsync is called at
    // shutdown after the game loop has stopped — so no lock is needed (unlike PlayerSaver, whose
    // account writes are also enqueued from off-thread admin handlers).
    private readonly Dictionary<int, Task> _guildWriteChains = new();

    // Live objective-kernel handle for each guild's active quest (keyed by guild index), so an abandon or expiry
    // can Stop tracking before completion. A guild has at most one quest at a time → at most one handle; a
    // completed quest auto-untracks (the kernel sweeps it), so an entry here only needs an explicit Stop for an
    // early cancel. Runtime-only — rebuilt at boot from the persisted quests by ReTrackActiveQuests.
    private readonly Dictionary<int, ObjectiveSystem.Handle> _questHandles = new();

    public GuildSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       IPersistenceService persistence, IBackgroundPersistence bg, PlayerSaver saver,
                       ItemSystem items, MailSystem mail, ObjectiveSystem objectives, ILogger<GuildSystem> logger,
                       IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _world = world;
        _pm = pm;
        _persistence = persistence;
        _bg = bg;
        _saver = saver;
        _items = items;
        _mail = mail;
        _objectives = objectives;
        _logger = logger;
    }

    // ── Lookup ──────────────────────────────────────────────────────────────────

    /// <summary>The guild with this id, or null if none (0 = guildless).</summary>
    public GuildRecord? GuildById(int id) =>
        id >= 1 && _world.Guilds.TryGetValue(id, out var g) ? g : null;

    /// <summary>The guild the player's account belongs to, or null if guildless.</summary>
    public GuildRecord? GuildOf(ServerPlayer sp) => GuildById(sp.Guild);

    /// <summary>Guild-name lookup that ignores case AND underscores (see <see cref="NameRules.Key"/>), so
    /// "The_Gathering" resolves to "TheGathering" — the same canonical identity the creation uniqueness
    /// check uses, blocking underscore/case spoofing of an existing guild. Null if no guild matches.</summary>
    public GuildRecord? GuildByName(string name)
    {
        string key = NameRules.Key(name);
        foreach (var g in _world.Guilds.Values)
            if (NameRules.Key(g.Name) == key) return g;
        return null;
    }

    /// <summary>A fresh, unused guild id. Guilds are unbounded, so this is simply one past the
    /// highest live id (ids are never reused, keeping them stable across a disband).</summary>
    /// <summary>The next guild number, taken from the high-water mark rather than from the live guilds:
    /// a disbanded guild's number is retired, and the highest one disbanding must not hand its number to
    /// the next guild founded. Accounts, territory controllers and wars all reference a guild by number.</summary>
    private int AllocateGuildIndex() => ++_world.HighestGuildNumber;

    // ── Persistence ───────────────────────────────────────────────────────────────

    /// <summary>Persist a guild off-thread (serialized per guild id) AND re-push the Guild-tab data to
    /// its online members. Snapshots a clone on the game thread so the background write always sees
    /// stable, fully-applied state.
    ///
    /// Every mutation already funnels through here, so this doubles as the single "the guild changed"
    /// chokepoint — no new mutation can forget to refresh an open Social panel. The one gap it can't
    /// close is a member going offline (their slot still reads as playing while the leave is being
    /// processed, so that broadcast still shows them online); the client re-requests the roster when the
    /// tab opens, which is what keeps the live online column honest.</summary>
    public void SaveGuild(GuildRecord guild)
    {
        var snapshot = guild.Clone();   // stable snapshot for the off-thread write
        ChainGuildWrite(guild.Index, () => _persistence.SaveGuildAsync(guild.Index, snapshot));
        BroadcastGuildInfo(guild.Index);
    }

    /// <summary>Persist a guild off-thread WITHOUT broadcasting (unlike <see cref="SaveGuild"/>). For a
    /// high-frequency mutation that shouldn't refresh every open panel each time — the guild-war attrition
    /// trickle, which reaches clients on the next full sync (a panel re-request, or any broadcasting
    /// mutation such as the war's resolution). Keeps the meter crash-safe without per-death broadcast spam.</summary>
    public void PersistGuild(GuildRecord guild)
    {
        var snapshot = guild.Clone();
        ChainGuildWrite(guild.Index, () => _persistence.SaveGuildAsync(guild.Index, snapshot));
    }

    // ── Vault ─────────────────────────────────────────────────────────────────────

    /// <summary>Donate gold from the member at <paramref name="index"/> into their guild's vault. Server-
    /// authoritative: re-checks membership + funds, takes the gold (a transfer into the vault, not a sink),
    /// persists, confirms to the donor, and announces it on the Guild channel.</summary>
    public void DonateGold(int index, int amount)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (GuildOf(sp) is not { } guild) { Notify(index, ServerStrings.Guild_NotInOne); return; }
        if (amount <= 0) return;   // the client validates; a non-positive amount is ignored
        if (ItemSystem.CountItem(sp.Char, _world.Items, Constants.GoldItemIndex) < amount)
        {
            Notify(index, ServerStrings.Guild_DonateNeedGold, ("Amount", amount));
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, amount);
        guild.VaultGold += amount;
        guild.Donations += amount;   // vault dashboard: what the members have put in
        RecordDonation(guild, sp.Login, amount);
        SaveGuild(guild);
        NotifyOk(index, ServerStrings.Guild_DonateOk, ("Amount", amount));
        _dispatcher.SendLocalizedChatToGuild(guild.Index, ServerStrings.Guild_DonateAnnounce,
            new ChatMetadata(GameColor.Guild, ChatChannel.Guild), ("Name", sp.Char.TrimmedName), ("Amount", amount));
    }

    // Prepend a donation to the guild's recent-donor log (newest first) + trim to the cap. Records the donor's
    // ACCOUNT login (membership is per-account) for the Vault-tab log; the chat announce still names the character.
    private void RecordDonation(GuildRecord guild, string account, long amount)
    {
        guild.RecentDonations.Insert(0, new GuildDonationEntry
        {
            Account = account,
            Amount = amount,
            TimeUtc = NowUtc,
        });
        if (guild.RecentDonations.Count > Constants.GuildRecentVaultLogMax)
        {
            guild.RecentDonations.RemoveRange(Constants.GuildRecentVaultLogMax,
                guild.RecentDonations.Count - Constants.GuildRecentVaultLogMax);
        }
    }

    /// <summary>Pay <paramref name="amount"/> into a guild's vault from something other than a member's
    /// donation, and count it as income. This is the seam a game credits a group through — Core moves gold into
    /// a vault on its own only when a member donates. Caller persists.</summary>
    public static void CreditVault(GuildRecord guild, long amount)
    {
        if (amount <= 0) return;
        guild.VaultGold += amount;
        guild.Income += amount;
    }

    /// <summary>Append an outgoing vault payment to the guild's recent-SPENDING log (newest first, capped) for
    /// the Vault tab's Spending view. <paramref name="account"/> = the member the payment was on behalf of and
    /// <paramref name="character"/> = the specific character it was for. Caller persists.</summary>
    public void RecordSpending(GuildRecord guild, string account, string character, long amount)
    {
        guild.RecentSpending.Insert(0, new GuildSpendingEntry
        {
            Account = account,
            Character = character,
            Amount = amount,
            TimeUtc = NowUtc,
        });
        if (guild.RecentSpending.Count > Constants.GuildRecentVaultLogMax)
        {
            guild.RecentSpending.RemoveRange(Constants.GuildRecentVaultLogMax,
                guild.RecentSpending.Count - Constants.GuildRecentVaultLogMax);
        }
    }

}

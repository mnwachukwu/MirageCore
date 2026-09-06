using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Persistence;

public interface IPersistenceService
{
    // ── Account ───────────────────────────────────────────────────────────────
    /// <summary>Exact-login existence (case-insensitive via the lowercased filename) — for login, delete, and
    /// change-password, which must match the real account name.</summary>
    Task<bool> AccountExistsAsync(string login);
    /// <summary>Canonical name-collision check for account CREATION only: true if any account's name shares
    /// the same <see cref="Mirage.Shared.NameRules.Key"/> (case- and underscore-insensitive), so
    /// "B_o_b" can't be registered alongside "Bob".</summary>
    Task<bool> AccountNameTakenAsync(string name);
    Task<bool> PasswordOkAsync(string login, string password);
    Task<AccountRecord?> LoadAccountAsync(string login);

    /// <summary>True while no account exists at all. Only the first-account bootstrap asks.</summary>
    bool HasNoAccounts();

    /// <summary>One page of accounts for the editor's account browser, plus how many matched in total.
    ///
    /// <para>The name search runs on the FILE NAMES, which are the logins — so it narrows the set without
    /// opening anything, and only the page's own records are read.</para>
    ///
    /// <para> An <paramref name="access"/> filter cannot work that way: the level lives INSIDE the
    /// record, so every candidate has to be opened to know whether it matches. That is a full scan, and
    /// it is why the two filters have very different costs. Acceptable because it happens when an
    /// operator picks a level, not on every keystroke — and a name search still narrows the candidates
    /// first.</para></summary>
    /// <param name="search">Substring match on the login; empty matches everything.</param>
    /// <param name="access">Exact access level to keep, or null for every level.</param>
    Task<(IReadOnlyList<AccountSummary> page, int total)> ListAccountsAsync(
        string search, AdminLevel? access, int skip, int take);
    Task SaveAccountAsync(AccountRecord account);
    /// <summary>Creates an account. <paramref name="access"/> exists for the very first account on a
    /// fresh server, which is made a Creator so an operator is not locked out of their own world; every
    /// other creation takes the default.</summary>
    Task CreateAccountAsync(string login, string password, AdminLevel access = AdminLevel.Player);
    Task ChangePasswordAsync(string login, string newPassword);
    Task DeleteAccountAsync(string login);

    // ── Characters ────────────────────────────────────────────────────────────
    Task<bool> CharExistsAsync(string name);
    Task AddCharNameAsync(string name);
    Task DeleteCharNameAsync(string name);

    // ── Maps ──────────────────────────────────────────────────────────────────
    Task<MapRecord?> LoadMapAsync(int mapNum);
    Task SaveMapAsync(int mapNum, MapRecord map);

    // ── Game data arrays ──────────────────────────────────────────────────────
    Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync();
    Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync();
    Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync();
    Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync();
    Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync();
    Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync();
    Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync();

    Task SaveItemAsync(int num, ItemRecord item);
    Task SaveNpcAsync(int num, NpcRecord npc);
    Task SaveShopAsync(int num, ShopRecord shop);
    Task SaveSpellAsync(int num, SpellRecord spell);
    Task SaveClassAsync(int num, ClassRecord cls);
    Task SaveQuestAsync(int num, QuestRecord quest);
    Task SaveConversationAsync(int num, ConversationRecord conversation);

    // ── Guilds ────────────────────────────────────────────────────────────────
    Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync();
    Task SaveGuildAsync(int num, GuildRecord guild);

    /// <summary>Write a disbanded guild back with <see cref="GuildRecord.Disbanded"/> set, RETIRING its
    /// number. The record is preserved in full and the file stays put, so the number is never handed out
    /// again — an account, a territory's controller and a war all reference a guild by number, and
    /// reissuing one would silently re-point whatever still holds it at a different guild.</summary>
    Task RetireGuildAsync(int num, GuildRecord guild);

    /// <summary>The largest guild number ever issued, retired ones included. Read once at boot; the next
    /// guild takes this plus one.</summary>
    Task<int> HighestGuildNumberAsync();

    // ── Map groups ──────────────────────────────────────────────────────────────
    Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync();
    Task SaveMapGroupAsync(int num, MapGroupRecord group);

    // ── Territories ─────────────────────────────────────────────────────────────
    // Keyed by the map group whose maps they are, and part of this installation rather than of the world.
    Task<Dictionary<int, TerritoryRecord>> LoadAllTerritoriesAsync();
    Task SaveTerritoryAsync(int mapGroup, TerritoryRecord territory);
    Task DeleteTerritoryAsync(int mapGroup);

    /// <summary>Persist a finished season's final leaderboard to seasons/season{N}.json (archived
    /// in perpetuity).</summary>
    Task SaveSeasonArchiveAsync(int season, SeasonArchive archive);

    /// <summary>Load every archived season (seasons/season{N}.json), ascending by season number — the
    /// perpetual record surfaced by the historical-season browser. Empty if none exist.</summary>
    Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync();
    Task DeleteMapGroupAsync(int num);

    // Marketplace listings — unbounded, keyed by global listing id (mirrors the guild file pattern).
    Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync();
    Task SaveMarketListingAsync(int id, MarketListing listing);
    Task DeleteMarketListingAsync(int id);

    // Rolling marketplace sales history (single JSON file; the seller Sales tab + on-disk admin audit).
    Task<List<MarketSale>> LoadMarketSalesAsync();
    Task SaveMarketSalesAsync(List<MarketSale> sales);

    // Direct-trade write-ahead journals — one file per in-flight swap, keyed by trade id. Save is SYNCHRONOUS
    // and durable (the swap's commit point must hit disk before either character is saved with it applied);
    // load runs once at boot to replay any swap a crash interrupted; delete runs once both characters are saved.
    Task<List<TradeJournal>> LoadAllTradeJournalsAsync();
    void SaveTradeJournal(TradeJournal journal);
    Task DeleteTradeJournalAsync(int id);

    // ── Banlist ───────────────────────────────────────────────────────────────
    /// <summary>Whether an ACCOUNT is banned. A ban is stored against the login, never against a
    /// character — an operator types a character name because that is the handle they have, and the
    /// server resolves it to the account behind it.
    ///
    /// <para>This took an <c>ip</c> argument that was never read, above a comment claiming the check
    /// covered both. It did not, and could not: nothing records an IP when a ban is applied. An IP ban
    /// is a separate feature with its own storage, not something this signature can imply.</para></summary>
    Task<bool> IsBannedAsync(string login);
    Task BanAsync(string login, string reason);
    /// <summary>Removes a login's ban. False when there was nothing to remove, so an operator is told
    /// "not banned" rather than a lie about having lifted something.</summary>
    Task<bool> UnbanAsync(string login);
    /// <summary>The whole ban list, for an operator deciding what to lift. A copy — mutating the result
    /// must not reach the cache.</summary>
    Task<IReadOnlyList<BanEntry>> LoadBanListAsync();
    Task RefreshBanListAsync();

    // ── Hardware banlist ──────────────────────────────────────────────────────
    // A ban on the MACHINE rather than the account, for the case an account ban cannot reach: somebody
    // who simply registers again. Last resort, and separate from the account list in every respect —
    // its own file, its own lift, and a switch that decides whether a match refuses a login or merely
    // reports one. See ServerConfig.HardwareBans.

    /// <summary>Turns the key a client sent into the value this server stores, by salting it with a
    /// per-server secret. Empty in, empty out — a client that could not compute a key is not a match for
    /// anything, and must never collide with another that also could not.</summary>
    Task<string> HashMachineKeyAsync(string clientKey);

    /// <summary>The ban on a hashed key, or null. Takes the output of
    /// <see cref="HashMachineKeyAsync"/>, never a raw client value.</summary>
    Task<HardwareBanEntry?> FindHardwareBanAsync(string hashedKey);

    /// <summary>Records a machine ban. False when that machine was already banned, or the key is empty.</summary>
    Task<bool> HardwareBanAsync(string hashedKey, string login, string reason);

    /// <summary>Lifts every machine ban recorded against <paramref name="login"/>, returning the count.</summary>
    Task<int> HardwareUnbanAsync(string login);

    Task<IReadOnlyList<HardwareBanEntry>> LoadHardwareBanListAsync();

    /// <summary>Sweeps every account file for a kick or mute that has not yet run out, returning the
    /// matches and how many files were read.
    ///
    /// <para>O(accounts), and deliberately so: the penalty timers live on the account records that
    /// ENFORCE them, and a second index would be a copy of the truth that can disagree with it. Only ever
    /// called when an operator asks — never on a tick, never on a login.</para></summary>
    Task<(IReadOnlyList<AccountPenalty> penalties, int scanned)> LoadActivePenaltiesAsync(long nowUtc);

    // ── Dropped map items ─────────────────────────────────────────────────────
    Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum);
    Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items);

    // ── The world folder's own manifest ───────────────────────────────────────
    /// <summary>What the data folder says about itself — its name, and the size a blank map is padded to.
    /// Absent or unreadable, the stock answers apply. The record CEILINGS on it are the editor's offline
    /// concern; a server states its own in serverconfig.json and in the hello.</summary>
    Task<WorldManifest> LoadWorldManifestAsync();

    // ── MOTD ──────────────────────────────────────────────────────────────────
    Task<string> LoadMotdAsync();
    Task SaveMotdAsync(string motd);

    // ── Environment (Time of Day + Weather) ────────────────────────────────────
    Task<EnvironmentState?> LoadEnvironmentAsync();
    Task SaveEnvironmentAsync(EnvironmentState state);

    // ── Log ───────────────────────────────────────────────────────────────────
    Task AddLogAsync(string message, string chatType);
}

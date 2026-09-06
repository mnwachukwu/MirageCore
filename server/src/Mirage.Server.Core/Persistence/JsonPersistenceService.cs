using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Logging;
using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mirage.Server.Core.Persistence;

public sealed class JsonPersistenceService : IPersistenceService
{
    private readonly string _worldPath;
    private readonly string _dataPath;
    private readonly ILogger<JsonPersistenceService> _logger;
    private readonly IChatLog _chatLog;
    private readonly RecordLimits _limits;
    private readonly SemaphoreSlim _accountLock = new(1, 1);
    private readonly SemaphoreSlim _banLock = new(1, 1);
    private List<BanEntry>? _bansCache;
    private readonly SemaphoreSlim _hwBanLock = new(1, 1);
    private HardwareBanList? _hwBansCache;

    private static readonly JsonSerializerOptions Options = Mirage.Shared.Serialization.RecordJson.Options;

    /// <summary><paramref name="limits"/> decides how far each family is padded on load — the same object
    /// <see cref="World.GameWorld"/> sizes its arrays from, so the folder and the world always agree on how
    /// many slots exist.</summary>
    /// <summary>Two roots, split on one question: <b>does it change while the server runs?</b>
    ///
    /// <para><paramref name="worldPath"/> holds what an author wrote — maps, the record families, the
    /// manifest, the MOTD. It changes only when somebody edits it, it travels whole when a world is copied
    /// to another machine, and it is the folder the EDITOR opens.</para>
    ///
    /// <para><paramref name="dataPath"/> holds what this installation accumulated — accounts, guilds,
    /// market listings, trade journals, seasons, dropped items, the name registry, the ban lists and the
    /// clock. It belongs to one server on one machine and is meaningless beside a different world.</para>
    ///
    /// <para>Keeping them apart is what makes a world a thing you can zip up and hand over, and what stops
    /// a copied world from carrying somebody's password hashes with it.</para></summary>
    /// <param name="limits">How far each family is padded on load — the same object
    /// <see cref="World.GameWorld"/> sizes its arrays from, so the folder and the world always agree on how
    /// many slots exist.</param>
    public JsonPersistenceService(string worldPath, string dataPath, ILogger<JsonPersistenceService> logger,
                                  IChatLog chatLog, RecordLimits? limits = null)
    {
        _limits = limits ?? RecordLimits.Default;
        _worldPath = worldPath;
        _dataPath = dataPath;
        _logger = logger;
        _chatLog = chatLog;

        foreach (string dir in new[]
                 {
                     MapsPath, ItemsPath, QuestsPath, ConversationsPath, NpcsPath, ShopsPath, SpellsPath,
                     ClassesPath, MapGroupsPath,
                 })
        {
            Directory.CreateDirectory(dir);
        }

        foreach (string dir in new[]
                 {
                     AccountsPath, MapItemsPath, GuildsPath, SeasonsPath, MarketListingsPath, TradeJournalsPath,
                     TerritoriesPath,
                 })
        {
            Directory.CreateDirectory(dir);
        }
    }

    // ── The world: authored, and unchanged by anything the server does ──────
    private string MapsPath => Path.Combine(_worldPath, "maps");
    private string ItemsPath => Path.Combine(_worldPath, "items");
    private string QuestsPath => Path.Combine(_worldPath, "quests");
    private string ConversationsPath => Path.Combine(_worldPath, "conversations");
    private string NpcsPath => Path.Combine(_worldPath, "npcs");
    private string ShopsPath => Path.Combine(_worldPath, "shops");
    private string SpellsPath => Path.Combine(_worldPath, "spells");
    private string ClassesPath => Path.Combine(_worldPath, "classes");
    private string MapGroupsPath => Path.Combine(_worldPath, "map_groups");

    // ── This installation: everything the server itself writes ──────────────
    private string AccountsPath => Path.Combine(_dataPath, "accounts");
    private string MapItemsPath => Path.Combine(_dataPath, "map_items");
    private string GuildsPath => Path.Combine(_dataPath, "guilds");
    private string SeasonsPath => Path.Combine(_dataPath, "seasons");
    private string MarketListingsPath => Path.Combine(_dataPath, "market");
    private string TerritoriesPath => Path.Combine(_dataPath, "territories");

    private string AccountFile(string login) =>
        Path.Combine(AccountsPath, $"{login.ToLowerInvariant()}.json");

    private string MapFile(int mapNum) =>
        Path.Combine(MapsPath, $"map{mapNum}.json");
    private string DroppedItemFile(int mapNum) =>
        Path.Combine(MapItemsPath, $"map{mapNum}.json");

    private string ItemFile(int num) => Path.Combine(ItemsPath, $"item{num}.json");
    private string QuestFile(int num) => Path.Combine(QuestsPath, $"quest{num}.json");
    private string ConversationFile(int num) => Path.Combine(ConversationsPath, $"conversation{num}.json");
    private string NpcFile(int num) => Path.Combine(NpcsPath, $"npc{num}.json");
    private string ShopFile(int num) => Path.Combine(ShopsPath, $"shop{num}.json");
    private string SpellFile(int num) => Path.Combine(SpellsPath, $"spell{num}.json");
    private string ClassFile(int num) => Path.Combine(ClassesPath, $"class{num}.json");
    private string GuildFile(int num) => Path.Combine(GuildsPath, $"{GuildRecord.FileStem}{num}.json");
    private string MapGroupFile(int num) => Path.Combine(MapGroupsPath, $"{MapGroupRecord.FileStem}{num}.json");
    private string TerritoryFile(int num) => Path.Combine(TerritoriesPath, $"{TerritoryRecord.FileStem}{num}.json");
    private string SeasonFile(int num) => Path.Combine(SeasonsPath, $"season{num}.json");
    private string MarketListingFile(int id) => Path.Combine(MarketListingsPath, $"{MarketListing.FileStem}{id}.json");
    private string MarketSalesFile => Path.Combine(MarketListingsPath, "sales.json");
    private string TradeJournalsPath => Path.Combine(_dataPath, "trades");
    private string TradeJournalFile(int id) => Path.Combine(TradeJournalsPath, $"{TradeJournal.FileStem}{id}.json");

    // ── Account ───────────────────────────────────────────────────────────────

    public Task<bool> AccountExistsAsync(string login) =>
        Task.FromResult(File.Exists(AccountFile(login)));

    // Canonical creation-uniqueness: scan the account files (each named for its lowercased login) and
    // compare identity keys, so underscores/case can't spoof an existing account. Creation is rare, so an
    // O(files) scan is fine; login/delete/change-password keep the exact AccountExistsAsync above.
    public Task<bool> AccountNameTakenAsync(string name)
    {
        string key = NameRules.Key(name);
        if (key.Length == 0) return Task.FromResult(false);   // no identity to collide on (rejected upstream)
        foreach (string file in Directory.EnumerateFiles(AccountsPath, "*.json"))
        {
            if (NameRules.Key(Path.GetFileNameWithoutExtension(file)) == key)
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<bool> PasswordOkAsync(string login, string password)
    {
        var account = await LoadAccountAsync(login);
        if (account is null) return false;

        return PasswordHasher.Verify(password, account.Password);
    }

    public async Task<AccountRecord?> LoadAccountAsync(string login)
    {
        string path = AccountFile(login);
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AccountRecord>(json, Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load account {Login}", login);
            return null;
        }
    }

    public async Task<(IReadOnlyList<AccountSummary> page, int total)> ListAccountsAsync(
        string search, AdminLevel? access, int skip, int take)
    {
        // Names first: the file name IS the login, so a name search and its total run without opening a
        // single record. Ordered so paging is stable — an unordered directory walk can hand back the same
        // account on two different pages.
        var logins = Directory.EnumerateFiles(AccountsPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Where(n => search.Length == 0 || n.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        skip = Math.Max(0, skip);
        take = Math.Max(0, take);

        // An access filter forces every candidate open, because the level is inside the record and not
        // in its name. Only then can the total and the page be honest about how many matched.
        if (access is { } wanted)
        {
            var matched = new List<AccountSummary>();
            foreach (string login in logins)
            {
                var a = await LoadAccountAsync(login);
                if (a is null || a.Access != wanted) continue;
                matched.Add(new AccountSummary(a.Login, a.Access, NamedChars(a)));
            }
            return (matched.Skip(skip).Take(take).ToList(), matched.Count);
        }

        var page = new List<AccountSummary>();
        foreach (string login in logins.Skip(skip).Take(take))
        {
            var account = await LoadAccountAsync(login);
            if (account is null) continue;   // unreadable file: logged by LoadAccountAsync, skipped here
            page.Add(new AccountSummary(account.Login, account.Access, NamedChars(account)));
        }
        return (page, logins.Count);
    }

    private static List<string> NamedChars(AccountRecord account)
    {
        var names = new List<string>();
        for (int i = 1; i < account.Chars.Length; i++)
        {
            string name = account.Chars[i].Name.Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    public async Task SaveAccountAsync(AccountRecord account)
    {
        string path = AccountFile(account.Login);
        await _accountLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(account, Options);
            await WriteAllTextAtomicAsync(path, json);
        }
        finally { _accountLock.Release(); }
    }

    /// <summary>Write a file atomically: stream to a sibling temp file (flushed through to disk), then
    /// rename it over the target. A same-directory rename is atomic on every OS, so a crash or power loss
    /// mid-write leaves the target either wholly the old contents or wholly the new — never truncated or
    /// half-written. The account file gets this because its corruption would lose a player's characters,
    /// bank, and any in-flight trade escrow; a torn write on a plain overwrite could orphan an entire
    /// account. It also makes each trade participant's post-swap record write all-or-nothing per file.</summary>
    private static async Task WriteAllTextAtomicAsync(string path, string contents)
    {
        string tmp = path + ".tmp";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await fs.WriteAsync(bytes);
            await fs.FlushAsync();
        }
        File.Move(tmp, path, overwrite: true);   // atomic same-volume rename over the live file
    }

    /// <summary>Synchronous twin of <see cref="WriteAllTextAtomicAsync"/> — same temp-file + flush-to-disk +
    /// atomic rename, but blocking. Used for the trade-journal commit, which must be durable before the caller
    /// (the game thread) proceeds to apply the swap. Kept tiny (one small file, only on a trade completion).</summary>
    private static void WriteAllTextAtomicSync(string path, string contents)
    {
        string tmp = path + ".tmp";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public async Task CreateAccountAsync(string login, string password, AdminLevel access = AdminLevel.Player)
    {
        var account = new AccountRecord
        {
            Login = login,
            Password = PasswordHasher.Hash(password),
            Access = access,
        };
        await SaveAccountAsync(account);
    }

    /// <summary>Whether no account exists yet. Counts FILES, so it costs a directory read rather than a
    /// parse — and it only decides the very first account's access level.</summary>
    public bool HasNoAccounts() => !Directory.EnumerateFiles(AccountsPath, "*.json").Any();

    public async Task ChangePasswordAsync(string login, string newPassword)
    {
        var account = await LoadAccountAsync(login);
        if (account is null) return;
        account.Password = PasswordHasher.Hash(newPassword);
        await SaveAccountAsync(account);
    }

    public async Task DeleteAccountAsync(string login)
    {
        // Free up every character name registered to this account before removing the file.
        var account = await LoadAccountAsync(login);
        if (account is not null)
        {
            for (int i = 1; i <= Constants.MaxChars; i++)
            {
                string charName = account.Chars[i].Name.Trim();
                if (!string.IsNullOrEmpty(charName))
                    await DeleteCharNameAsync(charName);
            }
        }

        string path = AccountFile(login);
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Characters ────────────────────────────────────────────────────────────

    private string CharNamesFile => Path.Combine(_dataPath, "charnames.json");

    public async Task<bool> CharExistsAsync(string name)
    {
        // Under the same lock as the writers. Reading this file while another character creation is
        // rewriting it throws a sharing violation, which surfaces as a failed character create for
        // whoever lost the race — two people pressing Create at the same moment is enough.
        await _accountLock.WaitAsync();
        try
        {
            // Canonical (case- and underscore-insensitive) collision check: compare identity keys, so "B_o_b"
            // can't be created alongside "Bob". The stored set keeps its lowercased form; keying both sides here
            // also matches any pre-existing underscore entries without a data migration.
            var names = await LoadCharNamesAsync();
            string key = NameRules.Key(name);
            foreach (var n in names)
                if (NameRules.Key(n) == key) return true;
            return false;
        }
        finally { _accountLock.Release(); }
    }

    public async Task AddCharNameAsync(string name)
    {
        // Same lock as SaveAccountAsync — concurrent char creates against the same file would
        // otherwise both load, both add, both write, and one entry would silently disappear.
        await _accountLock.WaitAsync();
        try
        {
            var names = await LoadCharNamesAsync();
            names.Add(name.ToLowerInvariant());
            await WriteCharNamesAsync(names);
        }
        finally { _accountLock.Release(); }
    }

    public async Task DeleteCharNameAsync(string name)
    {
        await _accountLock.WaitAsync();
        try
        {
            var names = await LoadCharNamesAsync();
            names.Remove(name.ToLowerInvariant());
            await WriteCharNamesAsync(names);
        }
        finally { _accountLock.Release(); }
    }

    /// <summary>Atomic, like the account file: this is the registry that stops two players taking the same
    /// character name, and a torn write would either lose names or leave the file unreadable.</summary>
    private Task WriteCharNamesAsync(HashSet<string> names) =>
        WriteAllTextAtomicAsync(CharNamesFile, JsonSerializer.Serialize(names, Options));

    private async Task<HashSet<string>> LoadCharNamesAsync()
    {
        if (!File.Exists(CharNamesFile)) return new HashSet<string>();
        string json = await File.ReadAllTextAsync(CharNamesFile);
        var list = JsonSerializer.Deserialize<List<string>>(json, Options);
        return new HashSet<string>(list ?? []);
    }

    // ── Maps ──────────────────────────────────────────────────────────────────

    public async Task<MapRecord?> LoadMapAsync(int mapNum)
    {
        string path = MapFile(mapNum);
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MapRecord>(json, Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load map {MapNum}", mapNum);
            return null;
        }
    }

    public async Task SaveMapAsync(int mapNum, MapRecord map)
    {
        string json = JsonSerializer.Serialize(map, Options);
        await File.WriteAllTextAsync(MapFile(mapNum), json);
    }

    // ── Dropped map items ─────────────────────────────────────────────────────

    public async Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum)
    {
        string path = DroppedItemFile(mapNum);
        if (!File.Exists(path)) return [];
        string json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<DroppedItemSaveData[]>(json, Options) ?? [];
    }

    public async Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items)
    {
        await File.WriteAllTextAsync(DroppedItemFile(mapNum), JsonSerializer.Serialize(items, Options));
    }

    // ── Game data arrays ──────────────────────────────────────────────────────

    public async Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync()
    {
        var result = new ItemRecord[_limits.Items + 1];
        for (int i = 0; i <= _limits.Items; i++) result[i] = new ItemRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Items, ItemFile);
        return (result, padded);
    }

    public async Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync()
    {
        var result = new NpcRecord[_limits.Npcs + 1];
        for (int i = 0; i <= _limits.Npcs; i++) result[i] = new NpcRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Npcs, NpcFile);
        // Size 0 ("not defined" in a legacy or blank record) normalizes to the 1x1 default so the whole
        // server and the editor see a valid footprint class. Sentinel handling, not a data migration.
        for (int i = 1; i <= _limits.Npcs; i++)
        {
            if (result[i].Size < 1) result[i].Size = 1;
            // Canonicalize the drop table: a line naming no item, or one whose chance can never land, is
            // dropped, and an empty table collapses to none at all. Runs here as well as at editor-save
            // because a hand-authored file reaches the server without the editor ever seeing it.
            result[i].Normalize();
        }
        return (result, padded);
    }

    public async Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync()
    {
        var result = new ShopRecord[_limits.Shops + 1];
        for (int i = 0; i <= _limits.Shops; i++) result[i] = new ShopRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Shops, ShopFile);
        // Compact each shop's trades: drop the legacy null-at-index-0 and any empty slots so the in-memory
        // list is dense (matching how the editor authors + saves them). Legacy shop JSON stored a fixed
        // 1-based array ([null, slot1..slot8]); this normalizes it on load — no file rewrite required.
        foreach (var shop in result)
        {
            shop.BarterItem = shop.BarterItem
                .Where(t => t is not null && (t.GiveItem > 0 || t.GetItem > 0))
                .ToList();
            // Sales list: drop dead item numbers and duplicates. A shop authored before the sales table
            // simply has none, which needs no migration — an absent list deserializes to an empty one.
            shop.Normalize(_limits.Items);
        }

        return (result, padded);
    }

    public async Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync()
    {
        var result = new SpellRecord[_limits.Spells + 1];
        for (int i = 0; i <= _limits.Spells; i++) result[i] = new SpellRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Spells, SpellFile);
        return (result, padded);
    }

    public async Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync()
    {
        var result = new ClassRecord[Constants.MaxClasses + 1];
        for (int i = 0; i <= Constants.MaxClasses; i++) result[i] = new ClassRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxClasses, ClassFile);
        // Canonicalize the starting loadout on load (inert lines out, duplicate spells out, caps applied)
        // so character creation reads one shape and never has to defend against a malformed list.
        for (int i = 1; i <= Constants.MaxClasses; i++) result[i].Normalize();
        return (result, padded);
    }

    public async Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync()
    {
        var result = new QuestRecord[_limits.Quests + 1];
        for (int i = 0; i <= _limits.Quests; i++) result[i] = new QuestRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Quests, QuestFile);
        return (result, padded);
    }

    public async Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync()
    {
        var result = new ConversationRecord[_limits.Conversations + 1];
        for (int i = 0; i <= _limits.Conversations; i++) result[i] = new ConversationRecord();
        int padded = await CheckAndLoadRecordsAsync(result, _limits.Conversations, ConversationFile);
        return (result, padded);
    }

    // Mirrors the map loop in MirageServerService: loads each slot file if it exists,
    // creates a blank file if it doesn't. Returns the count of blank files created.
    private async Task<int> CheckAndLoadRecordsAsync<T>(T[] result, int max, Func<int, string> filePath)
    {
        int created = 0;
        for (int i = 1; i <= max; i++)
        {
            string path = filePath(i);
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    result[i] = JsonSerializer.Deserialize<T>(json, Options) ?? result[i];
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
            }
            else
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result[i], Options));
                created++;
            }
        }
        return created;
    }

    public async Task SaveItemAsync(int num, ItemRecord item)
    {
        if (!SlotValidation.IsValidItemNum(num, _limits.Items)) return;
        await File.WriteAllTextAsync(ItemFile(num), JsonSerializer.Serialize(item, Options));
    }

    public async Task SaveNpcAsync(int num, NpcRecord npc)
    {
        if (!SlotValidation.IsValidNpcNum(num, _limits.Npcs)) return;
        await File.WriteAllTextAsync(NpcFile(num), JsonSerializer.Serialize(npc, Options));
    }

    public async Task SaveShopAsync(int num, ShopRecord shop)
    {
        if (!SlotValidation.IsValidShopNum(num, _limits.Shops)) return;
        await File.WriteAllTextAsync(ShopFile(num), JsonSerializer.Serialize(shop, Options));
    }

    public async Task SaveSpellAsync(int num, SpellRecord spell)
    {
        if (!SlotValidation.IsValidSpellNum(num, _limits.Spells)) return;
        await File.WriteAllTextAsync(SpellFile(num), JsonSerializer.Serialize(spell, Options));
    }

    public async Task SaveClassAsync(int num, ClassRecord cls)
    {
        if (!SlotValidation.IsValidClassNum(num)) return;
        await File.WriteAllTextAsync(ClassFile(num), JsonSerializer.Serialize(cls, Options));
    }

    public async Task SaveQuestAsync(int num, QuestRecord quest)
    {
        if (!SlotValidation.IsValidQuestNum(num, _limits.Quests)) return;
        await File.WriteAllTextAsync(QuestFile(num), JsonSerializer.Serialize(quest, Options));
    }

    public async Task SaveConversationAsync(int num, ConversationRecord conversation)
    {
        if (!SlotValidation.IsValidConversationNum(num, _limits.Conversations)) return;
        await File.WriteAllTextAsync(ConversationFile(num), JsonSerializer.Serialize(conversation, Options));
    }

    // ── Guilds ────────────────────────────────────────────────────────────────
    // Runtime-created and UNBOUNDED: load every guild{N}.json present (keyed by its N), rather than
    // scanning a fixed 1..Max range or blank-padding. Unused numbers simply have no entry.
    public async Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync()
    {
        var result = new Dictionary<int, GuildRecord>();
        if (!Directory.Exists(GuildsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(GuildsPath, $"{GuildRecord.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "guild{N}"
            if (stem.Length <= GuildRecord.FileStem.Length ||
                !int.TryParse(stem.AsSpan(GuildRecord.FileStem.Length), out int index) || index < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var guild = JsonSerializer.Deserialize<GuildRecord>(json, Options);
                if (guild is null) continue;
                // A retired number: the guild disbanded, and its file stays so the number is never
                // reissued. The record is kept for the history, but it is not a live guild.
                if (guild.Disbanded) continue;
                // The FILENAME is the authority on which guild this is; Index is the in-memory copy the
                // guild and territory code reads off a record it holds detached from any key.
                guild.Index = index;
                result[index] = guild;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveGuildAsync(int num, GuildRecord guild)
    {
        if (num < 1) return;
        await File.WriteAllTextAsync(GuildFile(num), JsonSerializer.Serialize(guild, Options));
    }

    public async Task RetireGuildAsync(int num, GuildRecord guild)
    {
        if (num < 1) return;
        guild.Disbanded = true;
        await File.WriteAllTextAsync(GuildFile(num), JsonSerializer.Serialize(guild, Options));
    }

    /// <summary>Read off the folder, so the mark survives a restart with no counter to keep in step with
    /// it. Retired numbers count: their file is still there. A stem that does not parse is ignored,
    /// exactly as the loader ignores it.</summary>
    public Task<int> HighestGuildNumberAsync()
    {
        int highest = 0;
        if (!Directory.Exists(GuildsPath)) return Task.FromResult(0);
        foreach (string path in Directory.EnumerateFiles(GuildsPath, $"{GuildRecord.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Length > GuildRecord.FileStem.Length
                && int.TryParse(stem.AsSpan(GuildRecord.FileStem.Length), out int num)
                && num > highest)
            {
                highest = num;
            }
        }
        return Task.FromResult(highest);
    }

    // Marketplace listings mirror guilds: UNBOUNDED, load every listing{N}.json present (keyed by its id).
    public async Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync()
    {
        var result = new Dictionary<int, MarketListing>();
        if (!Directory.Exists(MarketListingsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(MarketListingsPath, $"{MarketListing.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "listing{N}"
            if (stem.Length <= MarketListing.FileStem.Length ||
                !int.TryParse(stem.AsSpan(MarketListing.FileStem.Length), out int id) || id < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var listing = JsonSerializer.Deserialize<MarketListing>(json, Options);
                if (listing is null) continue;
                // The FILENAME is the authority on which listing this is.
                listing.Id = id;
                result[id] = listing;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveMarketListingAsync(int id, MarketListing listing)
    {
        if (id < 1) return;
        await File.WriteAllTextAsync(MarketListingFile(id), JsonSerializer.Serialize(listing, Options));
    }

    public Task DeleteMarketListingAsync(int id)
    {
        if (id < 1) return Task.CompletedTask;
        string path = MarketListingFile(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<List<MarketSale>> LoadMarketSalesAsync()
    {
        if (!File.Exists(MarketSalesFile)) return new();
        try
        {
            string json = await File.ReadAllTextAsync(MarketSalesFile);
            return JsonSerializer.Deserialize<List<MarketSale>>(json, Options) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load {File}", MarketSalesFile);
            return new();
        }
    }

    public Task SaveMarketSalesAsync(List<MarketSale> sales)
        => File.WriteAllTextAsync(MarketSalesFile, JsonSerializer.Serialize(sales, Options));

    // Trade journals mirror market listings: UNBOUNDED, load every journal{N}.json present at boot.
    public async Task<List<TradeJournal>> LoadAllTradeJournalsAsync()
    {
        var result = new List<TradeJournal>();
        if (!Directory.Exists(TradeJournalsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(TradeJournalsPath, $"{TradeJournal.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "journal{N}"
            if (stem.Length <= TradeJournal.FileStem.Length ||
                !int.TryParse(stem.AsSpan(TradeJournal.FileStem.Length), out int id) || id < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var journal = JsonSerializer.Deserialize<TradeJournal>(json, Options);
                if (journal is null) continue;
                // The FILENAME is the authority on which journal this is; the next id is taken from the
                // highest loaded, so a record disagreeing with its name would skew the sequence.
                journal.Id = id;
                result.Add(journal);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    // SYNCHRONOUS + durable: the swap's commit point must be on disk (fsync'd) BEFORE either character is saved
    // with it applied, so this blocks the caller through a temp-file → flush-to-disk → atomic rename.
    public void SaveTradeJournal(TradeJournal journal)
    {
        if (journal.Id < 1) return;
        WriteAllTextAtomicSync(TradeJournalFile(journal.Id), JsonSerializer.Serialize(journal, Options));
    }

    public Task DeleteTradeJournalAsync(int id)
    {
        if (id < 1) return Task.CompletedTask;
        string path = TradeJournalFile(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Map groups mirror guilds exactly: UNBOUNDED, load every map_group{N}.json present (keyed by its N).
    public async Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync()
    {
        var result = new Dictionary<int, MapGroupRecord>();
        if (!Directory.Exists(MapGroupsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(MapGroupsPath, $"{MapGroupRecord.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "map_group{N}"
            if (stem.Length <= MapGroupRecord.FileStem.Length ||
                !int.TryParse(stem.AsSpan(MapGroupRecord.FileStem.Length), out int index) || index < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var group = JsonSerializer.Deserialize<MapGroupRecord>(json, Options);
                if (group is null) continue;
                // The FILENAME is the authority on which group this is. Index is a denormalized copy the
                // record carries so the guild and territory code can identify a group it holds a reference
                // to, detached from any key — stamping it here keeps the two from ever disagreeing.
                group.Index = index;
                result[index] = group;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveMapGroupAsync(int num, MapGroupRecord group)
    {
        if (num < 1) return;
        await File.WriteAllTextAsync(MapGroupFile(num), JsonSerializer.Serialize(group, Options));
    }

    public async Task SaveSeasonArchiveAsync(int season, SeasonArchive archive)
    {
        if (season < 1) return;
        await File.WriteAllTextAsync(SeasonFile(season), JsonSerializer.Serialize(archive, Options));
    }

    public async Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync()
    {
        var result = new List<SeasonArchive>();
        if (!Directory.Exists(SeasonsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(SeasonsPath, "season*.json"))
        {
            try
            {
                string json = await File.ReadAllTextAsync(path);
                if (JsonSerializer.Deserialize<SeasonArchive>(json, Options) is { } a) result.Add(a);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        result.Sort((a, b) => a.Season.CompareTo(b.Season));
        return result;
    }

    public Task DeleteMapGroupAsync(int num)
    {
        if (num < 1) return Task.CompletedTask;
        string path = MapGroupFile(num);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // ── Territories ───────────────────────────────────────────────────────────
    // Keyed by the MAP GROUP whose maps they are, and stored beside the installation rather than in the
    // world: a world folder says which groups are contestable, and nothing about who won them.

    public async Task<Dictionary<int, TerritoryRecord>> LoadAllTerritoriesAsync()
    {
        var result = new Dictionary<int, TerritoryRecord>();
        if (!Directory.Exists(TerritoriesPath)) return result;
        foreach (string path in Directory.EnumerateFiles(TerritoriesPath, $"{TerritoryRecord.FileStem}*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "territory{N}"
            if (stem.Length <= TerritoryRecord.FileStem.Length ||
                !int.TryParse(stem.AsSpan(TerritoryRecord.FileStem.Length), out int index) || index < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var terr = JsonSerializer.Deserialize<TerritoryRecord>(json, Options);
                if (terr is null) continue;
                // The FILENAME is the authority on which group this belongs to, the same way a map group's
                // own index comes from its filename.
                terr.MapGroup = index;
                result[index] = terr;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveTerritoryAsync(int mapGroup, TerritoryRecord territory)
    {
        if (mapGroup < 1) return;
        await File.WriteAllTextAsync(TerritoryFile(mapGroup), JsonSerializer.Serialize(territory, Options));
    }

    public Task DeleteTerritoryAsync(int mapGroup)
    {
        if (mapGroup < 1) return Task.CompletedTask;
        string path = TerritoryFile(mapGroup);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // ── Banlist ───────────────────────────────────────────────────────────────

    private string BanFile => Path.Combine(_dataPath, "banlist.json");

    private async Task<List<BanEntry>> LoadBansAsync()
    {
        if (_bansCache is not null) return _bansCache;
        await _banLock.WaitAsync();
        try
        {
            if (_bansCache is not null) return _bansCache;
            if (!File.Exists(BanFile))
            {
                _bansCache = [];
                return _bansCache;
            }
            string json = await File.ReadAllTextAsync(BanFile);
            _bansCache = JsonSerializer.Deserialize<List<BanEntry>>(json, Options) ?? [];
            return _bansCache;
        }
        finally { _banLock.Release(); }
    }

    public async Task<bool> IsBannedAsync(string login)
    {
        var bans = await LoadBansAsync();
        return bans.Any(b => string.Equals(b.Login, login, StringComparison.OrdinalIgnoreCase));
    }

    public async Task BanAsync(string login, string reason)
    {
        await _banLock.WaitAsync();
        try
        {
            var bans = await ReadBansUnlockedAsync();
            if (!bans.Any(b => string.Equals(b.Login, login, StringComparison.OrdinalIgnoreCase)))
            {
                bans.Add(new BanEntry
                {
                    Login = login,
                    Reason = reason,
                    BannedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
            }
            await File.WriteAllTextAsync(BanFile, JsonSerializer.Serialize(bans, Options));
            _bansCache = bans;
        }
        finally { _banLock.Release(); }
    }

    public async Task<bool> UnbanAsync(string login)
    {
        await _banLock.WaitAsync();
        try
        {
            var bans = await ReadBansUnlockedAsync();
            int removed = bans.RemoveAll(b => string.Equals(b.Login, login, StringComparison.OrdinalIgnoreCase));
            // The cache is refreshed either way: reaching here means the file was read, and leaving a
            // stale cache behind a no-op lift is how a lifted ban comes back.
            _bansCache = bans;
            if (removed == 0) return false;
            await File.WriteAllTextAsync(BanFile, JsonSerializer.Serialize(bans, Options));
            return true;
        }
        finally { _banLock.Release(); }
    }

    public async Task<IReadOnlyList<BanEntry>> LoadBanListAsync()
    {
        var bans = await LoadBansAsync();
        // A copy: the cache is the list every login check reads, and handing it out would let a caller
        // edit the ban list by accident.
        return bans.ToList();
    }

    // Callers already hold _banLock. Prefers the cache and falls back to the file, which is the shape
    // BanAsync had inline before UnbanAsync needed the same thing.
    private async Task<List<BanEntry>> ReadBansUnlockedAsync()
    {
        if (_bansCache is not null) return _bansCache;
        return File.Exists(BanFile)
            ? JsonSerializer.Deserialize<List<BanEntry>>(await File.ReadAllTextAsync(BanFile), Options) ?? []
            : [];
    }

    public async Task<(IReadOnlyList<AccountPenalty> penalties, int scanned)> LoadActivePenaltiesAsync(long nowUtc)
    {
        var found = new List<AccountPenalty>();
        int scanned = 0;
        foreach (string file in Directory.EnumerateFiles(AccountsPath, "*.json"))
        {
            scanned++;
            AccountRecord? account;
            try
            {
                account = JsonSerializer.Deserialize<AccountRecord>(await File.ReadAllTextAsync(file), Options);
            }
            catch (Exception ex)
            {
                // One unreadable account must not hide every other punishment.
                _logger.LogError(ex, "Failed to read {File} while sweeping for penalties", file);
                continue;
            }
            if (account is null) continue;

            if (account.KickedUntilUtc > nowUtc)
                found.Add(new AccountPenalty(account.Login, PenaltyKind.Kick, account.KickedUntilUtc));
            if (account.MutedUntilUtc > nowUtc)
                found.Add(new AccountPenalty(account.Login, PenaltyKind.Mute, account.MutedUntilUtc));
        }
        return (found, scanned);
    }

    public async Task RefreshBanListAsync()
    {
        await _banLock.WaitAsync();
        try
        {
            _bansCache = File.Exists(BanFile)
                ? JsonSerializer.Deserialize<List<BanEntry>>(await File.ReadAllTextAsync(BanFile), Options) ?? []
                : [];
        }
        finally { _banLock.Release(); }
    }

    // ── Hardware banlist ──────────────────────────────────────────────────────
    // Its own file and its own lock, deliberately separate from the account banlist above: the two are
    // lifted independently (/unban vs /hwunban), and this one carries a salt the other has no use for.

    private string HardwareBanFile => Path.Combine(_dataPath, "hwbanlist.json");

    private async Task<HardwareBanList> LoadHardwareBansAsync()
    {
        if (_hwBansCache is not null) return _hwBansCache;
        await _hwBanLock.WaitAsync();
        try { return _hwBansCache = await ReadHardwareBansUnlockedAsync(); }
        finally { _hwBanLock.Release(); }
    }

    // Caller holds _hwBanLock. A missing or unreadable file yields an EMPTY list rather than throwing:
    // this is read on the login path, and a corrupt ban file must not lock every player out of the game.
    private async Task<HardwareBanList> ReadHardwareBansUnlockedAsync()
    {
        if (_hwBansCache is not null) return _hwBansCache;
        if (!File.Exists(HardwareBanFile)) return new HardwareBanList();
        try
        {
            string json = await File.ReadAllTextAsync(HardwareBanFile);
            return JsonSerializer.Deserialize<HardwareBanList>(json, Options) ?? new HardwareBanList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Failed to read {File}; treating it as empty", HardwareBanFile);
            return new HardwareBanList();
        }
    }

    /// <summary>Salts a client key into the value this server stores. The salt is minted on first use and
    /// never rotated — rotating it would invalidate every ban already recorded, since the stored hashes
    /// could no longer be reproduced from any incoming key.</summary>
    public async Task<string> HashMachineKeyAsync(string clientKey)
    {
        if (clientKey.Length == 0) return "";
        string salt = await SaltAsync();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + "\n" + clientKey)));
    }

    // Reads the per-server salt, minting and persisting one the first time it is needed. Re-reads under
    // the lock before writing: two logins can arrive at an empty salt at once, and the second must adopt
    // the first's rather than replacing it — a replaced salt orphans every ban recorded under the old one.
    private async Task<string> SaltAsync()
    {
        var list = await LoadHardwareBansAsync();
        if (list.Salt.Length > 0) return list.Salt;

        await _hwBanLock.WaitAsync();
        try
        {
            list = await ReadHardwareBansUnlockedAsync();
            if (list.Salt.Length > 0) return list.Salt;
            list = list with { Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            await WriteHardwareBansUnlockedAsync(list);
            return list.Salt;
        }
        finally { _hwBanLock.Release(); }
    }

    public async Task<HardwareBanEntry?> FindHardwareBanAsync(string hashedKey)
    {
        if (hashedKey.Length == 0) return null;
        var list = await LoadHardwareBansAsync();
        return list.Entries.FirstOrDefault(e => string.Equals(e.Key, hashedKey, StringComparison.Ordinal));
    }

    public async Task<bool> HardwareBanAsync(string hashedKey, string login, string reason)
    {
        if (hashedKey.Length == 0) return false;
        await _hwBanLock.WaitAsync();
        try
        {
            var list = await ReadHardwareBansUnlockedAsync();
            if (list.Entries.Any(e => string.Equals(e.Key, hashedKey, StringComparison.Ordinal)))
            {
                _hwBansCache = list;
                return false;
            }
            list.Entries.Add(new HardwareBanEntry
            {
                Key = hashedKey,
                Login = login,
                Reason = reason,
                BannedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            await WriteHardwareBansUnlockedAsync(list);
            return true;
        }
        finally { _hwBanLock.Release(); }
    }

    /// <summary>Lifts every machine banned under <paramref name="login"/>, returning how many went. By
    /// login rather than by key because the key is 64 hex characters an operator has no way to read off a
    /// screen and retype, and one person banned twice from two machines should be freed by one command.</summary>
    public async Task<int> HardwareUnbanAsync(string login)
    {
        await _hwBanLock.WaitAsync();
        try
        {
            var list = await ReadHardwareBansUnlockedAsync();
            int removed = list.Entries.RemoveAll(
                e => string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
            // Cached either way: the file was read to get here, and a stale cache behind a no-op lift is
            // how a lifted ban comes back.
            _hwBansCache = list;
            if (removed > 0) await WriteHardwareBansUnlockedAsync(list);
            return removed;
        }
        finally { _hwBanLock.Release(); }
    }

    public async Task<IReadOnlyList<HardwareBanEntry>> LoadHardwareBanListAsync()
    {
        var list = await LoadHardwareBansAsync();
        return list.Entries.ToList();
    }

    // Caller holds _hwBanLock. Writes through a temp file: this is the only record of who is banned, and
    // an interrupted write would leave the list truncated rather than merely stale.
    private async Task WriteHardwareBansUnlockedAsync(HardwareBanList list)
    {
        string temp = HardwareBanFile + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(list, Options));
        File.Move(temp, HardwareBanFile, overwrite: true);
        _hwBansCache = list;
    }

    // ── MOTD ──────────────────────────────────────────────────────────────────

    // The greeting THIS server gives, not something a world carries: it is written from the server
    // window, it is about whoever is hosting, and a world handed to somebody else should arrive without it.
    private string MotdFile => Path.Combine(_dataPath, "motd.json");

    public async Task<WorldManifest> LoadWorldManifestAsync()
    {
        string path = Path.Combine(_worldPath, WorldManifest.FileName);
        if (!File.Exists(path)) return new WorldManifest();
        try
        {
            return JsonSerializer.Deserialize<WorldManifest>(await File.ReadAllTextAsync(path), Options)
                   ?? new WorldManifest();
        }
        catch (Exception ex)
        {
            // A folder that cannot say what it is still runs, on the stock answers.
            _logger.LogWarning(ex, "Could not read {File}; using the default world settings.", WorldManifest.FileName);
            return new WorldManifest();
        }
    }

    public async Task<string> LoadMotdAsync()
    {
        if (!File.Exists(MotdFile)) return "";
        string json = await File.ReadAllTextAsync(MotdFile);
        return JsonSerializer.Deserialize<string>(json, Options) ?? "";
    }

    public async Task SaveMotdAsync(string motd)
    {
        await File.WriteAllTextAsync(MotdFile, JsonSerializer.Serialize(motd, Options));
    }

    // ── Environment (Time of Day + Weather) ────────────────────────────────────

    private string EnvironmentFile => Path.Combine(_dataPath, "environment.json");

    public async Task<EnvironmentState?> LoadEnvironmentAsync()
    {
        if (!File.Exists(EnvironmentFile)) return null;
        string json = await File.ReadAllTextAsync(EnvironmentFile);
        return JsonSerializer.Deserialize<EnvironmentState>(json, Options);
    }

    public async Task SaveEnvironmentAsync(EnvironmentState state)
    {
        await File.WriteAllTextAsync(EnvironmentFile, JsonSerializer.Serialize(state, Options));
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    public Task AddLogAsync(string message, string chatType)
    {
        _chatLog.Write(message, chatType);
        return Task.CompletedTask;
    }

}

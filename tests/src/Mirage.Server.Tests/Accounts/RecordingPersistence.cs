using Mirage.Server.Core.Persistence;
using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Security;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mirage.Server.Tests.Accounts;

// Records which entities were persisted; every read returns an empty/default result (boot loads aren't
// exercised here).
internal sealed class RecordingPersistence : IPersistenceService
{
    public readonly List<int> SavedMaps = new();
    public readonly List<int> SavedMapGroups = new();
    public readonly List<int> SavedItems = new();
    public readonly List<int> SavedNpcs = new();
    public readonly List<int> SavedShops = new();
    public readonly List<int> SavedSpells = new();
    public readonly List<int> SavedClasses = new();

    public Task SaveMapAsync(int mapNum, MapRecord map)
    {
        SavedMaps.Add(mapNum);
        return Task.CompletedTask;
    }
    public Task SaveMapGroupAsync(int num, MapGroupRecord group)
    {
        SavedMapGroups.Add(num);
        return Task.CompletedTask;
    }
    public Task SaveItemAsync(int num, ItemRecord item)
    {
        SavedItems.Add(num);
        return Task.CompletedTask;
    }
    public Task SaveNpcAsync(int num, NpcRecord npc)
    {
        SavedNpcs.Add(num);
        return Task.CompletedTask;
    }
    public Task SaveShopAsync(int num, ShopRecord shop)
    {
        SavedShops.Add(num);
        return Task.CompletedTask;
    }
    public Task SaveSpellAsync(int num, SpellRecord spell)
    {
        SavedSpells.Add(num);
        return Task.CompletedTask;
    }
    public Task SaveClassAsync(int num, ClassRecord cls)
    {
        SavedClasses.Add(num);
        return Task.CompletedTask;
    }

    // ── Unused reads/writes: benign defaults ──────────────────────────────────
    public Task<bool> AccountExistsAsync(string login) => Task.FromResult(false);
    public Task<bool> AccountNameTakenAsync(string name) => Task.FromResult(false);
    public Task<bool> PasswordOkAsync(string login, string password) => Task.FromResult(false);
    public Task<AccountRecord?> LoadAccountAsync(string login) => Task.FromResult<AccountRecord?>(null);
    public Task<(IReadOnlyList<AccountSummary> page, int total)> ListAccountsAsync(
        string search, AdminLevel? access, int skip, int take) =>
        Task.FromResult<(IReadOnlyList<AccountSummary>, int)>(([], 0));
    public Task SaveAccountAsync(AccountRecord account) => Task.CompletedTask;
    public Task CreateAccountAsync(string login, string password, AdminLevel access = AdminLevel.Player) => Task.CompletedTask;
    public bool HasNoAccounts() => false;
    public Task ChangePasswordAsync(string login, string newPassword) => Task.CompletedTask;
    public Task DeleteAccountAsync(string login) => Task.CompletedTask;
    public Task<bool> CharExistsAsync(string name) => Task.FromResult(false);
    public Task AddCharNameAsync(string name) => Task.CompletedTask;
    public Task DeleteCharNameAsync(string name) => Task.CompletedTask;
    public Task<MapRecord?> LoadMapAsync(int mapNum) => Task.FromResult<MapRecord?>(null);
    public Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync() => Task.FromResult((Array.Empty<ItemRecord>(), 0));
    public Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync() => Task.FromResult((Array.Empty<NpcRecord>(), 0));
    public Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync() => Task.FromResult((Array.Empty<ShopRecord>(), 0));
    public Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync() => Task.FromResult((Array.Empty<SpellRecord>(), 0));
    public Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync() => Task.FromResult((Array.Empty<ClassRecord>(), 0));
    public Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync() => Task.FromResult((Array.Empty<QuestRecord>(), 0));
    public Task SaveQuestAsync(int num, QuestRecord quest) => Task.CompletedTask;
    public Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync() => Task.FromResult((Array.Empty<ConversationRecord>(), 0));
    public Task SaveConversationAsync(int num, ConversationRecord conversation) => Task.CompletedTask;
    public Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync() => Task.FromResult(new Dictionary<int, GuildRecord>());
    public Task SaveGuildAsync(int num, GuildRecord guild) => Task.CompletedTask;
    public Task RetireGuildAsync(int num, GuildRecord guild) => Task.CompletedTask;
    public Task<int> HighestGuildNumberAsync() => Task.FromResult(0);
    public Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync() => Task.FromResult(new Dictionary<int, MapGroupRecord>());
    public Task SaveSeasonArchiveAsync(int season, SeasonArchive archive) => Task.CompletedTask;
    public Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync() => Task.FromResult(new List<SeasonArchive>());
    public Task DeleteMapGroupAsync(int num) => Task.CompletedTask;
    public Task<Dictionary<int, TerritoryRecord>> LoadAllTerritoriesAsync() => Task.FromResult(new Dictionary<int, TerritoryRecord>());
    public Task SaveTerritoryAsync(int mapGroup, TerritoryRecord territory) => Task.CompletedTask;
    public Task DeleteTerritoryAsync(int mapGroup) => Task.CompletedTask;
    public Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync() => Task.FromResult(new Dictionary<int, MarketListing>());
    public Task SaveMarketListingAsync(int id, MarketListing listing) => Task.CompletedTask;
    public Task DeleteMarketListingAsync(int id) => Task.CompletedTask;
    public Task<List<MarketSale>> LoadMarketSalesAsync() => Task.FromResult(new List<MarketSale>());
    public Task SaveMarketSalesAsync(List<MarketSale> sales) => Task.CompletedTask;
    public Task<List<TradeJournal>> LoadAllTradeJournalsAsync() => Task.FromResult(new List<TradeJournal>());
    public void SaveTradeJournal(TradeJournal journal) { }
    public Task DeleteTradeJournalAsync(int id) => Task.CompletedTask;
    public Task<bool> IsBannedAsync(string login) => Task.FromResult(false);
    public Task BanAsync(string login, string reason) => Task.CompletedTask;
    public Task<bool> UnbanAsync(string login) => Task.FromResult(false);
    public Task<IReadOnlyList<BanEntry>> LoadBanListAsync() => Task.FromResult<IReadOnlyList<BanEntry>>([]);
    public Task<(IReadOnlyList<AccountPenalty> penalties, int scanned)> LoadActivePenaltiesAsync(long nowUtc) =>
        Task.FromResult<(IReadOnlyList<AccountPenalty>, int)>(([], 0));
    public Task RefreshBanListAsync() => Task.CompletedTask;
    public Task<string> HashMachineKeyAsync(string clientKey) => Task.FromResult(clientKey);
    public Task<HardwareBanEntry?> FindHardwareBanAsync(string hashedKey) => Task.FromResult<HardwareBanEntry?>(null);
    public Task<bool> HardwareBanAsync(string hashedKey, string login, string reason) => Task.FromResult(false);
    public Task<int> HardwareUnbanAsync(string login) => Task.FromResult(0);
    public Task<IReadOnlyList<HardwareBanEntry>> LoadHardwareBanListAsync() =>
        Task.FromResult<IReadOnlyList<HardwareBanEntry>>([]);
    public Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum) => Task.FromResult(Array.Empty<DroppedItemSaveData>());
    public Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items) => Task.CompletedTask;
    public Task<Mirage.Shared.Records.WorldManifest> LoadWorldManifestAsync() => Task.FromResult(new Mirage.Shared.Records.WorldManifest());
    public Task<string> LoadMotdAsync() => Task.FromResult("");
    public Task SaveMotdAsync(string motd) => Task.CompletedTask;
    public Task<EnvironmentState?> LoadEnvironmentAsync() => Task.FromResult<EnvironmentState?>(null);
    public Task SaveEnvironmentAsync(EnvironmentState state) => Task.CompletedTask;
    public Task AddLogAsync(string message, string chatType) => Task.CompletedTask;
}

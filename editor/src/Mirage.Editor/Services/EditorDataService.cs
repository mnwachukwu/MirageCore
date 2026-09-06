using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;
namespace Mirage.Editor.Services;

public sealed class EditorDataService
{
    // One option set for reading and writing, shared with the server and every generator — see
    // Mirage.Shared/Serialization/RecordJson.cs. A file the editor saves matches what the server writes.
    private static readonly JsonSerializerOptions JsonOpts = Mirage.Shared.Serialization.RecordJson.Options;
    private static readonly JsonSerializerOptions ReadOpts = Mirage.Shared.Serialization.RecordJson.Options;

    // ── Offline data loaded from disk ─────────────────────────────────────────
    public ItemRecord[] OfflineItems { get; private set; } = [];
    public NpcRecord[] OfflineNpcs { get; private set; } = [];
    public ShopRecord[] OfflineShops { get; private set; } = [];
    public SpellRecord[] OfflineSpells { get; private set; } = [];
    public ClassRecord[] OfflineClasses { get; private set; } = [];
    public MapGroupRecord[] OfflineMapGroups { get; private set; } = [];
    public QuestRecord[] OfflineQuests { get; private set; } = [];
    public ConversationRecord[] OfflineConversations { get; private set; } = [];

    // offline maps: index = map number (1-based)
    public MapRecord[] OfflineMaps { get; private set; } = [];

    // ── Named-entry lists (for autocomplete pickers) ──────────────────────────
    private NamedEntry[]? _itemEntries;
    private NamedEntry[]? _npcEntries;
    private NamedEntry[]? _mapEntries;
    private NamedEntry[]? _shopEntries;
    private NamedEntry[]? _spellEntries;
    private NamedEntry[]? _classEntries;
    private NamedEntry[]? _mapGroupEntries;
    private NamedEntry[]? _questEntries;

    public NamedEntry[] ItemEntries => _itemEntries ??= BuildEntries(OfflineItems, r => r.Name);
    public NamedEntry[] NpcEntries => _npcEntries ??= BuildEntries(OfflineNpcs, r => r.Name);
    public NamedEntry[] MapEntries => _mapEntries ??= BuildEntries(OfflineMaps, r => r.Name);
    public NamedEntry[] ShopEntries => _shopEntries ??= BuildEntries(OfflineShops, r => r.Name);
    public NamedEntry[] SpellEntries => _spellEntries ??= BuildEntries(OfflineSpells, r => r.Name);
    public NamedEntry[] ClassEntries => _classEntries ??= BuildEntries(OfflineClasses, r => r.Name);
    public NamedEntry[] MapGroupEntries => _mapGroupEntries ??= BuildEntries(OfflineMapGroups, r => r.Name);
    public NamedEntry[] QuestEntries => _questEntries ??= BuildEntries(OfflineQuests, r => r.Name);

    // When online the server's name list takes precedence; offline JSON may have blank names
    // for entities that were never edited in this editor.
    public NamedEntry[] LiveItemEntries => OnlineItems is not null ? BuildEntriesFromLive(OnlineItems) : ItemEntries;
    public NamedEntry[] LiveNpcEntries => OnlineNpcs is not null ? BuildEntriesFromLive(OnlineNpcs) : NpcEntries;
    public NamedEntry[] LiveMapEntries => OnlineMaps is not null ? BuildEntriesFromLive(OnlineMaps) : MapEntries;
    public NamedEntry[] LiveShopEntries => OnlineShops is not null ? BuildEntriesFromLive(OnlineShops) : ShopEntries;
    public NamedEntry[] LiveSpellEntries => OnlineSpells is not null ? BuildEntriesFromLive(OnlineSpells) : SpellEntries;
    public NamedEntry[] LiveClassEntries => OnlineClasses is not null ? BuildEntriesFromLive(OnlineClasses) : ClassEntries;
    public NamedEntry[] LiveMapGroupEntries => OnlineMapGroups is not null ? BuildEntriesFromLive(OnlineMapGroups) : MapGroupEntries;
    public NamedEntry[] LiveQuestEntries => OnlineQuests is not null ? BuildEntriesFromLive(OnlineQuests) : QuestEntries;

    /// <summary>Builds a picker list over the SERVER's slot range, sized from the live list. The offline
    /// folder is a different world with its own ceiling and cannot bound this one.</summary>
    private static NamedEntry[] BuildEntriesFromLive(EditorDataPacket.NameEntry[] live)
    {
        int size = live.Length == 0 ? 0 : live.Max(e => e.Num) + 1;
        var result = new NamedEntry[size];
        if (size > 0) result[0] = new NamedEntry(0, "(none)");
        for (int i = 1; i < size; i++) result[i] = new NamedEntry(i, "");
        foreach (var e in live)
        {
            if (e.Num >= 1 && e.Num < size)
                result[e.Num] = new NamedEntry(e.Num, e.Name);
        }

        return result;
    }

    private static NamedEntry[] BuildEntries<T>(T[] arr, Func<T, string> getName)
    {
        var result = new NamedEntry[arr.Length];
        if (arr.Length > 0) result[0] = new NamedEntry(0, "(none)");
        for (int i = 1; i < arr.Length; i++)
            result[i] = new NamedEntry(i, getName(arr[i]));
        return result;
    }

    public event Action? EntriesInvalidated;
    private void RaiseEntriesInvalidated() => EntriesInvalidated?.Invoke();

    private void ClearEntryCache() =>
        _itemEntries = _npcEntries = _mapEntries = _shopEntries = _spellEntries = _classEntries = _mapGroupEntries = _questEntries = null;

    // ── Online data pushed by server ──────────────────────────────────────────
    // null when not in online mode; only names are stored — full records are fetched on demand
    public EditorDataPacket.NameEntry[]? OnlineItems { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineNpcs { get; private set; }
    // NPC footprint sizes (EffectiveSize, 1-based) sent with the online data; null offline. Drives size-aware
    // spawn-placement rendering + validation in the map editor.
    public int[]? OnlineNpcSizes { get; private set; }

    /// <summary>Footprint size (EffectiveSize, >= 1) of NPC <paramref name="npcNum"/> — server-sent sizes when
    /// online, else the offline record, else 1.</summary>
    public int NpcSize(int npcNum)
    {
        if (OnlineNpcSizes is { } sizes && npcNum >= 1 && npcNum < sizes.Length) return Math.Max(1, sizes[npcNum]);
        if (npcNum >= 1 && npcNum < OfflineNpcs.Length) return Math.Max(1, OfflineNpcs[npcNum].EffectiveSize);
        return 1;
    }

    /// <summary>Apply a live NPC-size change (an editor UpdateNpc broadcast) into the online size cache so the
    /// map editor's footprint overlay + placement validation stop reading the stale connect-time snapshot.
    /// No-op offline — NpcSize then reads the live offline record anyway.</summary>
    public void UpdateOnlineNpcSize(int npcNum, int size)
    {
        if (OnlineNpcSizes is { } sizes && npcNum >= 1 && npcNum < sizes.Length)
            sizes[npcNum] = Math.Max(1, size);
    }
    public EditorDataPacket.NameEntry[]? OnlineShops { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineSpells { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineMaps { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineClasses { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineMapGroups { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineQuests { get; private set; }
    public EditorDataPacket.NameEntry[]? OnlineConversations { get; private set; }
    // Server-sent set of currency-type item indices (for drop-quantity validation); null when offline.
    private HashSet<int>? _onlineCurrencyItems;
    // Server-sent gate facts for the class editor's starting loadout; null when offline.
    private Dictionary<int, EditorDataPacket.ItemGate>? _onlineItemGates;
    private Dictionary<int, EditorDataPacket.SpellGate>? _onlineSpellGates;

    /// <summary>Everything the starting-loadout gates need about an item, from the LIVE world when
    /// connected and the offline records otherwise. Never mixes the two: an offline folder can be a
    /// completely different world from the server, so falling back per-field would produce a gate answer
    /// that is true of neither.</summary>
    public (ItemType Type, int Power, short LevelReq, List<short>? AllowedClasses)? ItemGate(int num)
    {
        if (num <= 0) return null;
        if (IsOnline)
            return _onlineItemGates is not null && _onlineItemGates.TryGetValue(num, out var g)
                ? (g.Type, g.Power, g.LevelReq, g.AllowedClasses) : null;
        if (num >= OfflineItems.Length || string.IsNullOrEmpty(OfflineItems[num].Name)) return null;
        var r = OfflineItems[num];
        return (r.Type, r.Power, r.LevelReq, r.AllowedClasses);
    }

    /// <summary>As <see cref="ItemGate"/>, for spells.</summary>
    public (SpellType Type, short VitalAmount, short LevelReq, List<short>? AllowedClasses)? SpellGate(int num)
    {
        if (num <= 0) return null;
        if (IsOnline)
            return _onlineSpellGates is not null && _onlineSpellGates.TryGetValue(num, out var g)
                ? (g.Type, g.VitalAmount, g.LevelReq, g.AllowedClasses) : null;
        if (num >= OfflineSpells.Length || string.IsNullOrEmpty(OfflineSpells[num].Name)) return null;
        var r = OfflineSpells[num];
        return (r.Type, r.VitalAmount, r.LevelReq, r.AllowedClasses);
    }

    /// <summary>What item <paramref name="num"/> sells for in a shop's sales table, from the LIVE world when
    /// connected and the offline records otherwise. Null when the item does not exist; 0 is a real answer
    /// (an unpriced item, which the sales table flags rather than hides — listing one gives it away free).</summary>
    public int? ItemPrice(int num)
    {
        if (num <= 0) return null;
        if (IsOnline)
            return _onlineItemGates is not null && _onlineItemGates.TryGetValue(num, out var g) ? g.Price : null;
        if (num >= OfflineItems.Length || string.IsNullOrEmpty(OfflineItems[num].Name)) return null;
        return OfflineItems[num].Price;
    }

    public bool IsOnline => OnlineItems != null;

    /// <summary>How many of each record family exist. The connected server's, from its greeting; the
    /// protocol defaults when offline or against a server too old to greet an editor. A ceiling this
    /// editor was compiled with is a bug rather than a default — see <see cref="RecordLimits"/>.</summary>
    /// <summary>What the open world says about itself — its record ceilings, its name, and the size new
    /// maps are created at. Offline this is the folder's <c>world.json</c>; connected, the ceilings come from
    /// the server's hello and the rest stays local.</summary>
    public WorldManifest Manifest { get; private set; } = new();

    /// <summary>What the connected server calls its world, blank when it has no name or nothing is
    /// connected. The offline equivalent is <see cref="Manifest"/>'s name — a session has one or the
    /// other, never both, because connecting is how an online world is opened.</summary>
    public string OnlineWorldName { get; private set; } = "";

    /// <summary>Shorthand for <c>Manifest.Records</c> — every list in the editor is sized from it.</summary>
    public RecordLimits Limits
    {
        get => Manifest.Records;
        private set => Manifest = Manifest with { Records = value };
    }

    /// <summary>True if item <paramref name="id"/> is a currency-type item. Uses the server-sent currency
    /// set when online; falls back to the offline records otherwise.</summary>
    public bool IsCurrencyItem(int id)
    {
        if (id <= 0) return false;
        return IsOnline
            ? _onlineCurrencyItems?.Contains(id) ?? false
            : id < OfflineItems.Length && OfflineItems[id].Type == ItemType.Currency;
    }

    // ── Offline load ──────────────────────────────────────────────────────────

    /// <summary>Drops every offline record. What closing a world means: nothing is open, so nothing is
    /// listed. Left in place, the next world's lists would open showing the last one's records.</summary>
    public void ClearOffline()
    {
        OfflineItems = [];
        OfflineNpcs = [];
        OfflineShops = [];
        OfflineSpells = [];
        OfflineClasses = [];
        OfflineQuests = [];
        OfflineConversations = [];
        OfflineMaps = [];
        OfflineMapGroups = [];
        Limits = RecordLimits.Default;
        ClearEntryCache();
        RaiseEntriesInvalidated();
    }

    public async Task LoadOfflineAsync()
    {
        // No world open means no records, and no path to read them from: EditorPaths.Data is empty, so
        // every Path.Combine below would resolve against the working directory.
        if (!EditorPaths.HasWorld)
        {
            ClearOffline();
            return;
        }

        var dataPath = EditorPaths.Data;
        EditorLog.Info("Loading the offline data set from {Path}.", dataPath);
        EnsureRecordFolders(dataPath);
        Manifest = await LoadManifestAsync(dataPath);
        OfflineItems = await LoadAllFromDirAsync<ItemRecord>(Path.Combine(dataPath, "items"), "item", Limits.Items);
        OfflineNpcs = await LoadAllFromDirAsync<NpcRecord>(Path.Combine(dataPath, "npcs"), "npc", Limits.Npcs);
        OfflineShops = await LoadAllFromDirAsync<ShopRecord>(Path.Combine(dataPath, "shops"), "shop", Limits.Shops);
        OfflineSpells = await LoadAllFromDirAsync<SpellRecord>(Path.Combine(dataPath, "spells"), "spell", Limits.Spells);
        OfflineClasses = await LoadAllFromDirAsync<ClassRecord>(Path.Combine(dataPath, "classes"), "class", Constants.MaxClasses);
        OfflineQuests = await LoadAllFromDirAsync<QuestRecord>(Path.Combine(dataPath, "quests"), "quest", Limits.Quests);
        OfflineConversations = await LoadAllFromDirAsync<ConversationRecord>(Path.Combine(dataPath, "conversations"), "conversation", Limits.Conversations);
        OfflineMaps = await LoadAllMapsAsync(dataPath, Limits.Maps, Manifest.DefaultMapSize);
        OfflineMapGroups = await LoadAllMapGroupsAsync(dataPath, Limits.MapGroups);
        ClearEntryCache();
        EditorLog.Info("Offline data set loaded: {Items} items, {Npcs} npcs, {Maps} maps, {Groups} map groups.",
            OfflineItems.Count(r => r is not null), OfflineNpcs.Count(r => r is not null),
            OfflineMaps.Count(r => r is not null), OfflineMapGroups.Length);
    }

    /// <summary>What the folder says about itself. A world with no manifest runs on the stock answers.</summary>
    public static async Task<WorldManifest> LoadManifestAsync(string worldPath) =>
        await LoadJsonAsync<WorldManifest>(Path.Combine(worldPath, WorldManifest.FileName)) ?? new WorldManifest();

    /// <summary>Writes the folder's manifest whole, so a setting the caller did not touch is carried
    /// through rather than reset to its default.</summary>
    public static Task SaveManifestAsync(string worldPath, WorldManifest manifest) =>
        WriteJsonAsync(Path.Combine(worldPath, WorldManifest.FileName), manifest);

    /// <summary>The record folders a world folder is made of — the ones an AUTHOR fills. The server reads
    /// the same list, because a folder one of them wrote and the other never read would be a world with a
    /// hole in it.</summary>
    public static string[] RecordFolders => WorldLayout.WorldFolders;

    /// <summary>Puts back any record folder a world is missing, and is what makes the set of them a
    /// property of a world rather than a thing that happens to be there.
    ///
    /// <para>Run on every open, so emptying a world is deleting everything but the manifest: the folders
    /// come back on the next open, and what is gone is the records. Creating a directory that already
    /// exists does nothing, so this costs a world that is intact nothing at all.</para></summary>
    public static void EnsureRecordFolders(string worldPath)
    {
        foreach (string folder in RecordFolders)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(worldPath, folder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A read-only world still opens; it just cannot be repaired. Saving will say so.
                EditorLog.Warn("Could not create {Folder} in {Path}: {Reason}", folder, worldPath, e.Message);
            }
        }
    }

    /// <summary>Makes a world: the manifest that says the folder is one, and an empty directory per record
    /// family.
    ///
    /// <para>The directories are created EMPTY and stay that way — a slot with no file is a blank record,
    /// not a missing one, so nothing is written until something is authored. They exist up front to claim
    /// the names: an operator looking at a fresh world sees the shape it will take, and cannot make a
    /// folder of their own that the editor would later collide with.</para></summary>
    public static async Task CreateWorldAsync(string worldPath, WorldManifest manifest)
    {
        Directory.CreateDirectory(worldPath);
        EnsureRecordFolders(worldPath);
        await SaveManifestAsync(worldPath, manifest);
    }

    // A slot with no file is a blank record, not a missing one, so nothing is written to fill the gap: a
    // world is however many files an author made, and opening one leaves the folder as it was found.
    private static async Task<T[]> LoadAllFromDirAsync<T>(string dir, string prefix, int max) where T : new()
    {
        var result = new T[max + 1];
        for (int i = 0; i <= max; i++) result[i] = new T();
        if (!Directory.Exists(dir)) return result;
        for (int i = 1; i <= max; i++)
        {
            string path = Path.Combine(dir, $"{prefix}{i}.json");
            if (!File.Exists(path)) continue;
            var record = await LoadJsonAsync<T>(path);
            if (record is not null) result[i] = record;
        }
        return result;
    }

    private static async Task<MapRecord[]> LoadAllMapsAsync(string dataPath, int max, MapSize blankSize)
    {
        var mapsDir = Path.Combine(dataPath, "maps");
        var result = new MapRecord[max + 1];
        // An unauthored slot is a blank map at the world's default size — the size this world makes maps.
        for (int i = 1; i <= max; i++) result[i] = new MapRecord(blankSize.Width, blankSize.Height);

        if (!Directory.Exists(mapsDir)) return result;
        foreach (var file in Directory.EnumerateFiles(mapsDir, "map*.json"))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(nameNoExt[3..], out int num) && num >= 1 && num <= max)
            {
                var map = await LoadJsonAsync<MapRecord>(file);
                if (map is not null) result[num] = map;
            }
        }
        return result;
    }

    // MapGroups are directory-scanned (only the map_group{N}.json files that exist), NOT padded with blank
    // files like the record editors — the server stores them sparsely too. Sized to the editor slot cap.
    private static async Task<MapGroupRecord[]> LoadAllMapGroupsAsync(string dataPath, int max)
    {
        var dir = Path.Combine(dataPath, "map_groups");
        var result = new MapGroupRecord[max + 1];
        for (int i = 1; i <= max; i++) result[i] = new MapGroupRecord { Index = i };

        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.EnumerateFiles(dir, $"{MapGroupRecord.FileStem}*.json"))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(file);   // "map_group12"
            if (int.TryParse(nameNoExt[MapGroupRecord.FileStem.Length..], out int num) && num >= 1 && num <= max)
            {
                var g = await LoadJsonAsync<MapGroupRecord>(file);
                if (g is null) continue;
                // The filename decides which group this is; the record's own Index is a denormalized copy
                // for code holding a group detached from its key. Stamped here so the two cannot disagree.
                g.Index = num;
                result[num] = g;
            }
        }
        return result;
    }

    public async Task<MapRecord> LoadSingleMapOfflineAsync(int mapNum)
    {
        var file = Path.Combine(EditorPaths.Data, "maps", $"map{mapNum}.json");
        var blank = Manifest.DefaultMapSize;
        var map = await LoadJsonAsync<MapRecord>(file) ?? new MapRecord(blank.Width, blank.Height);
        OfflineMaps[mapNum] = map;
        return map;
    }

    // ── Online load ───────────────────────────────────────────────────────────

    public void LoadOnline(EditorDataPacket pkt, RecordLimits? limits = null)
    {
        Limits = limits ?? RecordLimits.Default;
        OnlineWorldName = pkt.WorldName;
        OnlineItems = pkt.Items;   // NameEntry[] — names only
        OnlineNpcs = pkt.Npcs;
        OnlineNpcSizes = pkt.NpcSizes;
        OnlineShops = pkt.Shops;
        OnlineSpells = pkt.Spells;
        OnlineMaps = pkt.Maps;
        OnlineClasses = pkt.Classes;
        OnlineMapGroups = pkt.MapGroups;
        OnlineQuests = pkt.Quests;
        OnlineConversations = pkt.Conversations;
        _onlineCurrencyItems = new HashSet<int>(pkt.CurrencyItems);
        _onlineItemGates = pkt.ItemGates.ToDictionary(g => g.Num);
        _onlineSpellGates = pkt.SpellGates.ToDictionary(g => g.Num);
    }

    public void ClearOnline()
    {
        Limits = RecordLimits.Default;
        OnlineWorldName = "";
        OnlineItems = null;
        OnlineNpcs = null;
        OnlineNpcSizes = null;
        OnlineShops = null;
        OnlineSpells = null;
        OnlineMaps = null;
        OnlineClasses = null;
        OnlineMapGroups = null;
        OnlineQuests = null;
        OnlineConversations = null;
        _onlineCurrencyItems = null;
        _onlineItemGates = null;
        _onlineSpellGates = null;
    }

    // ── Online name patching (after online save, keeps type-ahead lists fresh) ─
    public void PatchOnlineMapName(int index, string name) => PatchAndNotify(OnlineMaps, index, name);
    public void PatchOnlineNpcName(int index, string name) => PatchAndNotify(OnlineNpcs, index, name);
    // Items also carry currency-ness in the online snapshot, so reconcile the currency set (an item's Type
    // can flip to/from Currency) alongside the name. Without this the set stays a connect-time snapshot until
    // reconnect, and the shop editor's trade-qty limits (which key off it) go stale.
    public void PatchOnlineItem(int index, string name, ItemType type)
    {
        if (type == ItemType.Currency) _onlineCurrencyItems?.Add(index);
        else _onlineCurrencyItems?.Remove(index);
        PatchAndNotify(OnlineItems, index, name);
    }
    public void PatchOnlineShopName(int index, string name) => PatchAndNotify(OnlineShops, index, name);
    public void PatchOnlineQuestName(int index, string name) => PatchAndNotify(OnlineQuests, index, name);
    public void PatchOnlineConversationName(int index, string name) => PatchAndNotify(OnlineConversations, index, name);
    public void PatchOnlineSpellName(int index, string name) => PatchAndNotify(OnlineSpells, index, name);
    public void PatchOnlineClassName(int index, string name) => PatchAndNotify(OnlineClasses, index, name);
    public void PatchOnlineMapGroupName(int index, string name) => PatchAndNotify(OnlineMapGroups, index, name);

    private void PatchAndNotify(EditorDataPacket.NameEntry[]? store, int index, string name)
    {
        if (store is null) return;
        for (int i = 0; i < store.Length; i++)
        {
            if (store[i].Num == index)
            {
                store[i] = new EditorDataPacket.NameEntry(index, name);
                break;
            }
        }

        RaiseEntriesInvalidated();
    }

    // ── Offline save ──────────────────────────────────────────────────────────

    public async Task SaveOfflineItemAsync(int index, ItemRecord record)
    {
        OfflineItems[index] = record;
        _itemEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "items", $"item{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineNpcAsync(int index, NpcRecord record)
    {
        OfflineNpcs[index] = record;
        _npcEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "npcs", $"npc{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineShopAsync(int index, ShopRecord record)
    {
        OfflineShops[index] = record;
        _shopEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "shops", $"shop{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineQuestAsync(int index, QuestRecord record)
    {
        OfflineQuests[index] = record;
        _questEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "quests", $"quest{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineConversationAsync(int index, ConversationRecord record)
    {
        OfflineConversations[index] = record;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "conversations", $"conversation{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineSpellAsync(int index, SpellRecord record)
    {
        OfflineSpells[index] = record;
        _spellEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "spells", $"spell{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineClassAsync(int index, ClassRecord record)
    {
        OfflineClasses[index] = record;
        _classEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "classes", $"class{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineMapGroupAsync(int index, MapGroupRecord record)
    {
        OfflineMapGroups[index] = record;
        _mapGroupEntries = null;
        await WriteJsonAsync(Path.Combine(EditorPaths.Data, "map_groups", $"{MapGroupRecord.FileStem}{index}.json"), record);
        RaiseEntriesInvalidated();
    }

    public async Task SaveOfflineMapAsync(int mapNum, MapRecord record)
    {
        OfflineMaps[mapNum] = record;
        _mapEntries = null;
        var mapsDir = Path.Combine(EditorPaths.Data, "maps");
        Directory.CreateDirectory(mapsDir);
        await WriteJsonAsync(Path.Combine(mapsDir, $"map{mapNum}.json"), record);
        RaiseEntriesInvalidated();
    }

    // ── Online save (sends packet via EditorConnection) ──────────────────────

    public static EditorSaveMapPacket BuildSaveMapPacket(int mapNum, MapRecord map)
    {
        var tiles = new List<SendMapPacket.TileData>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var t = map.Tile[x, y];
                if (!SendMapPacket.TileData.IsDefault(t))
                    tiles.Add(SendMapPacket.TileData.From(x, y, t));
            }
        }

        return new EditorSaveMapPacket
        {
            MapNum = mapNum,
            Map = new SendMapPacket
            {
                MapNum = mapNum,
                Width = map.Width,
                Height = map.Height,
                // Send the current revision as-is. The server's HandleEditorSaveMap ignores this field
                // and bumps its own `map.Revision++` authoritatively, so any value here is dead data on
                // the wire. The caller is expected to have already called BumpRevision() before us, so
                // this carries the post-bump value purely for logs/debug — it doesn't drive the server.
                Revision = map.Revision,
                Name = map.Name,
                DisplayName = map.DisplayName,
                Moral = map.Moral,
                Up = map.Up,
                Down = map.Down,
                Left = map.Left,
                Right = map.Right,
                Music = map.Music,
                BootMap = map.BootMap,
                BootX = map.BootX,
                BootY = map.BootY,
                Indoors = map.Indoors,
                AlwaysLit = map.AlwaysLit,
                AlwaysDark = map.AlwaysDark,
                GreetingSpeaker = map.GreetingSpeaker,
                JoinSay = map.JoinSay,
                LeaveSay = map.LeaveSay,
                MapGroup = map.MapGroup,
                Tiles = [.. tiles],
                Npcs = map.Npcs.ToArray(),
                Lights = map.Lights.ToArray(),
            },
        };
    }

    public static MapRecord MapRecordFromPacket(SendMapPacket pkt)
    {
        var map = new MapRecord(pkt.Width, pkt.Height)
        {
            Name = pkt.Name,
            DisplayName = pkt.DisplayName,
            Revision = pkt.Revision,
            Moral = pkt.Moral,
            Up = pkt.Up,
            Down = pkt.Down,
            Left = pkt.Left,
            Right = pkt.Right,
            Music = pkt.Music,
            BootMap = pkt.BootMap,
            BootX = pkt.BootX,
            BootY = pkt.BootY,
            Indoors = pkt.Indoors,
            AlwaysLit = pkt.AlwaysLit,
            AlwaysDark = pkt.AlwaysDark,
            GreetingSpeaker = pkt.GreetingSpeaker,
            JoinSay = pkt.JoinSay,
            LeaveSay = pkt.LeaveSay,
            MapGroup = pkt.MapGroup,
        };
        foreach (var t in pkt.Tiles)
            if (map.Contains(t.X, t.Y)) map.Tile[t.X, t.Y] = t.ToTile();
        for (int i = 0; i < pkt.Npcs.Length && i < Constants.MaxMapNpcs; i++)
            map.Npcs.Add(pkt.Npcs[i]);
        map.Lights.AddRange(pkt.Lights);
        return map;
    }

    private static async Task<T?> LoadJsonAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, ReadOpts);
        }
        catch { return default; }
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        try
        {
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, value, JsonOpts);
            File.Move(tmp, path, overwrite: true);
            EditorLog.Info("Wrote {Record} to {Path}.", typeof(T).Name, path);
        }
        catch (Exception ex)
        {
            EditorLog.Error(ex, "Failed writing {Record} to {Path}.", typeof(T).Name, path);
            throw;
        }
    }
}

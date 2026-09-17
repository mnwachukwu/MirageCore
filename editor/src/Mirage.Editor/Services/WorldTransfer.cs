using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Editor.Services;

/// <summary>How far along a transfer is, for the status line.</summary>
public sealed record WorldTransferProgress(string Stage, int Done, int Total);

/// <summary>
/// Moving a whole world between a folder and a server.
///
/// <para>Downloading writes the server's records into a folder, which is then an ordinary world the editor
/// can open. Uploading reads a folder, works out what it would do to the server, and sends only that.</para>
///
/// <para>Two records count as the same when the packets an upload would send for them are the same. That
/// is stricter than it sounds and looser than comparing files: a map's revision and a group's guild
/// territory state differ constantly and no upload can carry them, so a difference there is not a change.
/// Comparing the projection that actually travels means the diff can never promise something the upload
/// would not do, or hide something it would.</para>
/// </summary>
public static class WorldTransfer
{
    /// <summary>How many maps to ask for in one request. Small enough that a frame stays a frame, large
    /// enough that a thousand-map world is twenty round-trips rather than a thousand.</summary>
    public const int MapChunk = 50;

    // ── Record → packet ──────────────────────────────────────────────────────
    // The per-type editors' own projections, reached through the same row view-models a manual save goes
    // through, so an upload writes exactly what an author saving that record by hand would write.

    /// <summary>The picker data the row view-models are built against. Both sides of a comparison use one
    /// of these, so a difference in the diff is always a difference in the records.</summary>
    public sealed class PacketContext(WorldSnapshot reference)
    {
        private HashSet<int>? _currency;

        private static NamedEntry[] Entries<T>(T[] arr, Func<T, string> name)
        {
            var result = new NamedEntry[arr.Length];
            if (arr.Length > 0) result[0] = new NamedEntry(0, "(none)");
            for (int i = 1; i < arr.Length; i++) result[i] = new NamedEntry(i, name(arr[i]));
            return result;
        }

        private NamedEntry[]? _items, _npcs, _maps;

        public NamedEntry[] Items => _items ??= Entries(reference.Items, r => r.Name);
        public NamedEntry[] Npcs => _npcs ??= Entries(reference.Npcs, r => r.Name);
        public NamedEntry[] Maps => _maps ??= Entries(reference.Maps, r => r.Name);

        public bool IsCurrency(int num)
        {
            _currency ??= [.. Enumerable.Range(1, Math.Max(0, reference.Items.Length - 1))
                .Where(i => reference.Items[i].Type == ItemType.Currency)];
            return _currency.Contains(num);
        }
    }

    /// <summary>What an upload would send for one record.
    ///
    /// <para>Core's families each go through their own row view-model, so an upload writes exactly what an
    /// author saving that record by hand would write. A game's family has no row to go through and needs
    /// none: its record IS the bag the save packet carries.</para></summary>
    public static IPacket SavePacketFor(RecordFamily family, int num, object record, PacketContext ctx) => family.Id switch
    {
        "Maps" => ZeroRevision(EditorDataService.BuildSaveMapPacket(num, (MapRecord)record)),
        "MapGroups" => new MapGroupRowViewModel(num, (MapGroupRecord)record, () => ctx.Maps, isLoaded: true)
            .BuildSavePacket(),
        "Items" => new ItemRowViewModel(num, (ItemRecord)record).BuildSavePacket(),
        "NPCs" => new NpcRowViewModel(num, (NpcRecord)record).BuildSavePacket(),
        "Shops" => new ShopRowViewModel(num, (ShopRecord)record, () => ctx.Items, () => ctx.Npcs, ctx.IsCurrency)
            .BuildSavePacket(),
        "Conversations" => new ConversationRowViewModel(num, (ConversationRecord)record, () => ctx.Npcs)
            .BuildSavePacket(),
        _ => new EditorSaveRecordPacket { Family = family.Id, Num = num, Fields = (AttributeBag)record },
    };

    // The server stamps its own revision on save, so the one on the wire is dead data. Left in, every map
    // whose revision moved for any reason would read as a change.
    private static EditorSaveMapPacket ZeroRevision(EditorSaveMapPacket p) =>
        p with { Map = p.Map with { Revision = 0 } };

    /// <summary>An empty record of the family's type. What a slot holds when nothing was authored in
    /// it.</summary>
    public static object Blank(RecordFamily family, int num) => family.Id switch
    {
        "Maps" => new MapRecord(),
        "MapGroups" => new MapGroupRecord { Index = num },
        "Items" => new ItemRecord(),
        "NPCs" => new NpcRecord(),
        "Shops" => new ShopRecord(),
        "Conversations" => new ConversationRecord(),
        _ => new AttributeBag(),
    };

    private static string Canon(RecordFamily family, int num, object record, PacketContext ctx) =>
        PacketSerializer.Serialize(SavePacketFor(family, num, record, ctx));

    // ── Compare ──────────────────────────────────────────────────────────────

    /// <summary>What uploading <paramref name="folder"/> would do to <paramref name="server"/>.</summary>
    public static WorldDiff Compare(WorldSnapshot folder, WorldSnapshot server)
    {
        var ctx = new PacketContext(server);
        var changes = new List<WorldChange>();
        int overCeiling = 0;

        // Both sides' families, not this build's: a folder may carry a family the server does not
        // have, and it is worth reporting rather than passing over.
        foreach (var family in FamilyUnion(folder, server))
        {
            string section = family.Id;
            int serverMax = server.CountOf(section);
            int folderMax = folder.CountOf(section);

            for (int num = 1; num <= Math.Max(serverMax, folderMax); num++)
            {
                object? inFolder = folder.At(section, num);
                object? onServer = server.At(section, num);

                if (num > serverMax)
                {
                    // Nowhere on the server to put it — above its ceiling, or a family it does not have at
                    // all. Only worth reporting when something is actually there.
                    if (inFolder is not null && !IsBlank(family, num, inFolder, ctx)) overCeiling++;
                    continue;
                }

                var blank = Blank(family, num);
                string folderCanon = Canon(family, num, inFolder ?? blank, ctx);
                string serverCanon = Canon(family, num, onServer ?? blank, ctx);
                if (folderCanon == serverCanon) continue;

                string blankCanon = Canon(family, num, blank, ctx);
                var kind = serverCanon == blankCanon ? WorldChangeKind.Added
                    : folderCanon == blankCanon ? WorldChangeKind.Removed
                    : WorldChangeKind.Changed;
                // A removal is named after what is being lost, which is the server's copy.
                string name = WorldSnapshot.NameOf(family, kind == WorldChangeKind.Removed ? onServer : inFolder);
                changes.Add(new WorldChange(section, num, name, kind));
            }
        }

        return new WorldDiff(changes, overCeiling);
    }

    /// <summary>Every family either side holds, the folder's order first. A family only the server has is
    /// still walked: its authored records read as removals, as uploading a folder without them
    /// would do.</summary>
    private static IEnumerable<RecordFamily> FamilyUnion(WorldSnapshot folder, WorldSnapshot server) =>
        [.. folder.Families,
         .. server.Families.Where(f => folder.FamilyOf(f.Id) is null)];

    private static bool IsBlank(RecordFamily family, int num, object record, PacketContext ctx) =>
        Canon(family, num, record, ctx) == Canon(family, num, Blank(family, num), ctx);

    // ── Read a folder ────────────────────────────────────────────────────────

    /// <summary>Reads a world folder without touching the editor's open one.</summary>
    public static async Task<WorldSnapshot> ReadFolderAsync(string root)
    {
        var manifest = await EditorDataService.LoadManifestAsync(root);
        var limits = manifest.Records;
        var families = manifest.Schema.Families;

        // A game's families, read out of the folders their own declarations name. A folder written by a
        // server running a game carries them; one written by a stock server has none.
        var moduleRecords = new Dictionary<string, AttributeBag[]>(StringComparer.Ordinal);
        foreach (var family in families.Where(f => CoreRecordFamilies.Find(f.Id) is null))
        {
            moduleRecords[family.Id] = await ReadDirAsync<AttributeBag>(
                root, family.EffectiveDirectory, family.EffectiveFilePrefix, limits.For(family));
        }

        return new WorldSnapshot
        {
            Limits = limits,
            Families = families,
            ModuleRecords = moduleRecords,
            Items = await ReadDirAsync<ItemRecord>(root, "items", "item", limits.Items),
            Npcs = await ReadDirAsync<NpcRecord>(root, "npcs", "npc", limits.Npcs),
            Shops = await ReadDirAsync<ShopRecord>(root, "shops", "shop", limits.Shops),
            Conversations = await ReadDirAsync<ConversationRecord>(root, "conversations", "conversation", limits.Conversations),
            Maps = await ReadDirAsync<MapRecord>(root, "maps", "map", limits.Maps),
            MapGroups = await ReadGroupsAsync(root, limits.MapGroups),
        };
    }

    private static async Task<T[]> ReadDirAsync<T>(string root, string dir, string prefix, int max) where T : new()
    {
        var result = new T[max + 1];
        for (int i = 0; i <= max; i++) result[i] = new T();
        string path = Path.Combine(root, dir);
        if (!Directory.Exists(path)) return result;
        for (int i = 1; i <= max; i++)
        {
            string file = Path.Combine(path, $"{prefix}{i}.json");
            if (!File.Exists(file)) continue;
            if (await ReadJsonAsync<T>(file) is { } record) result[i] = record;
        }
        return result;
    }

    private static async Task<MapGroupRecord[]> ReadGroupsAsync(string root, int max)
    {
        var result = new MapGroupRecord[max + 1];
        for (int i = 0; i <= max; i++) result[i] = new MapGroupRecord { Index = i };
        string path = Path.Combine(root, "map_groups");
        if (!Directory.Exists(path)) return result;
        for (int i = 1; i <= max; i++)
        {
            string file = Path.Combine(path, $"{MapGroupRecord.FileStem}{i}.json");
            if (!File.Exists(file)) continue;
            if (await ReadJsonAsync<MapGroupRecord>(file) is { } g)
            {
                g.Index = i;
                result[i] = g;
            }
        }
        return result;
    }

    private static async Task<T?> ReadJsonAsync<T>(string path)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, Mirage.Shared.Serialization.RecordJson.Options);
        }
        catch (Exception ex)
        {
            EditorLog.Warn(ex, "Skipping unreadable record {Path}.", path);
            return default;
        }
    }

    // ── Write a folder ───────────────────────────────────────────────────────

    /// <summary>Writes a world into a folder, one file per authored record. Blank slots get no file: a
    /// missing file reads back as a blank record, so writing thousands of them would only be noise.
    /// Returns how many were written.</summary>
    public static async Task<int> WriteFolderAsync(string root, WorldSnapshot world,
        IProgress<WorldTransferProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        // The folder's own name and default map size are kept: a download states the SERVER's ceilings,
        // and says nothing about what the folder receiving them is called.
        var manifest = await EditorDataService.LoadManifestAsync(root);
        // The families as well as the ceilings: a folder that did not record them would read back as a
        // world of Core's families with some unexplained directories beside it.
        await EditorDataService.SaveManifestAsync(root, manifest with
        {
            Records = world.Limits,
            Families = [.. world.Families.Where(f => CoreRecordFamilies.Find(f.Id) is null)],
        });

        var ctx = new PacketContext(world);
        int written = 0;
        int total = world.Sections.Sum(world.CountOf);
        int seen = 0;

        foreach (var family in world.Families)
        {
            string dir = Path.Combine(root, family.EffectiveDirectory);
            Directory.CreateDirectory(dir);
            int max = world.CountOf(family.Id);
            for (int num = 1; num <= max; num++)
            {
                ct.ThrowIfCancellationRequested();
                seen++;
                if (seen % 100 == 0) progress?.Report(new WorldTransferProgress(family.Id, seen, total));
                object? record = world.At(family.Id, num);
                if (record is null || IsBlank(family, num, record, ctx)) continue;
                await WriteJsonAsync(Path.Combine(dir, family.FileNameFor(num)), record);
                written++;
            }
        }

        progress?.Report(new WorldTransferProgress("", total, total));
        return written;
    }

    private static async Task WriteJsonAsync(string path, object value)
    {
        string tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, value, value.GetType(), Mirage.Shared.Serialization.RecordJson.Options);
        File.Move(tmp, path, overwrite: true);
    }

    // ── Read a server ────────────────────────────────────────────────────────

    /// <summary>Reads the connected server's whole world. Every family but maps answers in one round-trip;
    /// maps come a slice at a time, and the progress counts those slices.</summary>
    public static async Task<WorldSnapshot> FetchAsync(EditorConnection conn, RecordLimits limits,
        IProgress<WorldTransferProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new WorldTransferProgress("", 0, 0));

        // What families this server has, from the schema it sent on login. One bulk fetch each, the same
        // as Core's own — there is no line here per family, and a stock server adds no round-trips.
        var families = WorldFamilies.All;
        var moduleRecords = new Dictionary<string, AttributeBag[]>(StringComparer.Ordinal);
        foreach (var family in families.Where(f => CoreRecordFamilies.Find(f.Id) is null))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new WorldTransferProgress(family.Id, 0, 0));
            var bulk = await conn.RequestAllRecordsAsync(family.Id, ct) ?? throw Refused(family.Id);
            moduleRecords[family.Id] = Fill(bulk.Records, limits.For(family), r => r.Num,
                _ => new AttributeBag(), (_, r) => r.Fields);
        }

        var items = await conn.RequestAllItemsAsync(ct) ?? throw Refused("items");
        var npcs = await conn.RequestAllNpcsAsync(ct) ?? throw Refused("npcs");
        var shops = await conn.RequestAllShopsAsync(ct) ?? throw Refused("shops");
        var convs = await conn.RequestAllConversationsAsync(ct) ?? throw Refused("conversations");
        var groups = await conn.RequestAllMapGroupsAsync(ct) ?? throw Refused("map groups");

        var maps = new MapRecord[limits.Maps + 1];
        for (int i = 0; i <= limits.Maps; i++) maps[i] = new MapRecord();
        for (int start = 1; start <= limits.Maps; start += MapChunk)
        {
            ct.ThrowIfCancellationRequested();
            var slice = await conn.RequestAllMapsAsync(start, MapChunk, ct) ?? throw Refused("maps");
            foreach (var m in slice.Maps)
                if (m.MapNum >= 1 && m.MapNum < maps.Length)
                    maps[m.MapNum] = EditorDataService.MapRecordFromPacket(m);
            progress?.Report(new WorldTransferProgress("Maps",
                Math.Min(start + MapChunk - 1, limits.Maps), limits.Maps));
        }

        return new WorldSnapshot
        {
            Limits = limits,
            Families = families,
            ModuleRecords = moduleRecords,
            Items = Fill(items.Items, limits.Items, p => p.ItemNum, _ => new ItemRecord(), (n, p) =>
            {
                var row = new ItemRowViewModel(n, new ItemRecord(), false);
                row.ApplyPacket(p);
                return row.ToRecord();
            }),
            Npcs = Fill(npcs.Npcs, limits.Npcs, p => p.NpcNum, _ => new NpcRecord(), (n, p) =>
            {
                var row = new NpcRowViewModel(n, new NpcRecord(), false);
                row.ApplyPacket(p);
                return row.ToRecord();
            }),
            Shops = Fill(shops.Shops, limits.Shops, p => p.ShopNum, _ => new ShopRecord(), (n, p) =>
            {
                var row = new ShopRowViewModel(n, new ShopRecord(), Empty, Empty, _ => false, null, false);
                row.ApplyPacket(p);
                return row.ToRecord();
            }),
            Conversations = Fill(convs.Conversations, limits.Conversations, p => p.ConvNum,
                _ => new ConversationRecord(), (n, p) =>
            {
                var row = new ConversationRowViewModel(n, new ConversationRecord(), Empty, false);
                row.ApplyPacket(p);
                return row.ToRecord();
            }),
            MapGroups = Fill(groups.MapGroups, limits.MapGroups, p => p.GroupNum,
                n => new MapGroupRecord { Index = n }, (n, p) =>
            {
                var row = new MapGroupRowViewModel(n, new MapGroupRecord { Index = n }, Empty, false);
                row.ApplyPacket(p);
                return row.ToRecord();
            }),
            Maps = maps,
        };
    }

    private static NamedEntry[] Empty() => [];

    private static InvalidOperationException Refused(string what) =>
        new($"The server did not answer for {what}.");

    // The bulk replies are dense arrays whose entries carry their own slot number. Each is turned into a
    // record the way an editor does it: a blank row, the packet applied, then the row's own ToRecord.
    private static TRecord[] Fill<TPacket, TRecord>(TPacket[] source, int max, Func<TPacket, int> numOf,
        Func<int, TRecord> blank, Func<int, TPacket, TRecord> read)
    {
        var result = new TRecord[max + 1];
        for (int i = 0; i <= max; i++) result[i] = blank(i);
        foreach (var p in source)
        {
            int num = numOf(p);
            if (num >= 1 && num <= max) result[num] = read(num, p);
        }
        return result;
    }

    // ── Apply an upload ──────────────────────────────────────────────────────

    /// <summary>Sends one save per change, in the diff's own order. Maps last would be wrong: a map can
    /// name a group that does not exist yet either way, and the server takes the reference as a number
    /// regardless.</summary>
    public static async Task ApplyAsync(EditorConnection conn, WorldSnapshot folder,
        IReadOnlyList<WorldChange> changes, PacketContext ctx,
        IProgress<WorldTransferProgress>? progress = null, CancellationToken ct = default)
    {
        int done = 0;
        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();
            // A change can only name a family the folder holds, because that is where the diff walked it.
            if (folder.FamilyOf(change.Section) is not { } family) continue;
            // A removal blanks the server's slot, which is the folder's own content for that slot.
            object record = folder.At(change.Section, change.Num) ?? Blank(family, change.Num);
            await conn.SendSaveAsync(SavePacketFor(family, change.Num, record, ctx));
            done++;
            if (done % 10 == 0 || done == changes.Count)
                progress?.Report(new WorldTransferProgress(change.Section, done, changes.Count));
        }
    }
}

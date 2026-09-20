using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Host.Net;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using System.Diagnostics;
using System.Reflection;

namespace Mirage.Server.Host.Services;

/// <summary>
/// <see cref="IHostedService"/> that owns the server lifetime:
///   StartAsync — load all game data, spawn NPC map items, start game loop, start TCP listener
///   StopAsync  — stop listener, stop game loop, save all online players
/// </summary>
public sealed class MirageServerService : IHostedService
{
    private readonly GameWorld _world;
    private readonly CoreRegistry _registry;
    private readonly IWorld _actions;
    private readonly PlayerManager _pm;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly PlayerSaver _saver;
    private readonly GameLoop _gameLoop;
    private readonly SpawnSystem _spawn;
    private readonly ItemSystem _items;
    private readonly TimeOfDaySystem _tod;
    private readonly WeatherSystem _weather;
    private readonly GuildSystem _guilds;
    private readonly TradeSystem _trade;
    private readonly TcpConnectionAcceptor _acceptor;
    private readonly ILogger<MirageServerService> _logger;
    private readonly ServerConfig _config;

    private CancellationTokenSource? _cts;

    public MirageServerService(
        CoreRegistry registry,
        IWorld actions,
        GameWorld world,
        PlayerManager pm,
        IPersistenceService persistence,
        IBackgroundPersistence bg,
        PlayerSaver saver,
        GameLoop gameLoop,
        SpawnSystem spawn,
        ItemSystem items,
        TimeOfDaySystem tod,
        WeatherSystem weather,
        GuildSystem guilds,
        TradeSystem trade,
        TcpConnectionAcceptor acceptor,
        ServerConfig config,
        ILogger<MirageServerService> logger)
    {
        _config = config;
        _world = world;
        _registry = registry;
        _actions = actions;
        _pm = pm;
        _persistence = persistence;
        _bg = bg;
        _saver = saver;
        _gameLoop = gameLoop;
        _spawn = spawn;
        _items = items;
        _tod = tod;
        _weather = weather;
        _guilds = guilds;
        _trade = trade;
        _acceptor = acceptor;
        _logger = logger;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var rawVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
        var version = rawVersion.Split('+')[0];
        LocalizedLog.Info(_logger, ServerStrings.Server_Starting,
            ("GameName", _config.GameName), ("Version", version));
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_RunningInLanguage));

        // What game this is. An engine with no modules is a valid server, and "Modules: Core" is the
        // honest way to say so — quieter than a warning, and it makes a module that failed to be added
        // visible on the line an operator already reads.
        _logger.LogInformation("Modules: {Modules}", string.Join(", ", _registry.ModuleNames));

        // The compiled modules are the authority on what can be worn; a world manifest only records what
        // it was authored against, for an editor opening the folder without them.
        _world.EquipSlots = _registry.EquipSlots;

        // Likewise the attribute keys: a client is sent this numbering on join, so a schema left empty here
        // means every value a module declared is unreadable on the far side.
        _world.Attributes = _registry.Attributes;

        // And which of those keys are drawn over a head. Read-only on the client's side: the values
        // arrive as ordinary syncs, so a bar cannot show a number the attribute does not hold.
        _world.OverheadBars = _registry.OverheadBars;
        _world.NameTints = _registry.NameTints;

        // And what the surfaces show. Read-only on the client's side for the same reason: the values
        // arrive as ordinary syncs, so a row cannot show a number the attribute does not hold.
        _world.DisplayFields = _registry.DisplayFields;

        // And the verbs it offers. The client is sent these on join and sends an id back; what the verb
        // DOES stays here, so nothing has to be deployed beside the client.
        _world.Actions = _registry.Actions;
        _world.Panels = _registry.Panels;
        _world.ChatChannels = _registry.ChatChannels;
        _world.Families = _registry.Schema;
        _world.HotkeyBarSlots = _registry.HotkeyBarSlots;
        _world.GuildCost = _registry.GuildCost;

        await LoadWorldDataAsync(ct);

        // Wire the level-up → quest-eligibility refresh now that every system exists (can't be done at
        // construction — the Combat↔Quest DI cycle is broken by a Lazy, so neither can reference the other in
        // its ctor). A gained level may newly satisfy a quest's accept requirements, relighting its giver "?".

        // Spawn map items for every map that has item-spawn tiles
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_SpawningMapItems));
        for (short i = 1; i <= _world.Limits.Maps; i++)
            _items.SpawnMapItems(i);

        // Restore dropped items that survived the last shutdown
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingDroppedItems));
        for (int i = 1; i <= _world.Limits.Maps; i++)
        {
            var drops = await _persistence.LoadDroppedItemsAsync(i);
            if (drops.Length > 0) _items.LoadDroppedItems(i, drops);
        }

        // Runtime-data load summary: guilds (loaded in LoadWorldDataAsync) and the map items now present
        // across all maps (spawned + restored above).
        int mapItemCount = 0;
        for (int i = 1; i <= _world.Limits.Maps; i++) mapItemCount += _world.MapItems[i]?.Count ?? 0;
        LocalizedLog.Info(_logger, ServerStrings.Server_RuntimeDataSummary,
            ("Guilds", _world.Guilds.Count), ("MapItems", mapItemCount));

        StartModules();

        // Spawn NPC slots
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_SpawningNpcs));
        _spawn.SpawnAllMapNpcs();

        _gameLoop.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptor.Start(_cts.Token);

        LocalizedLog.Info(_logger, ServerStrings.Server_Ready,
            ("GameName", _config.GameName), ("ElapsedMs", sw.ElapsedMilliseconds));
    }

    /// <summary>Hands every loaded module the world, once.
    ///
    /// <para>Here and not earlier: the engine is built and the world is loaded, so a module setting
    /// itself up reads what a player would. Here and not later: the loop has not started and the
    /// acceptor is not listening, so nothing it does can race anything.</para>
    ///
    /// <para>⚠ And BEFORE the world's creatures are spawned, because the first thing a game is told
    /// about is a body arriving. A module handed the world afterwards misses every creature the server
    /// started with — which is most of them, and none of them would carry the numbers the game gives
    /// one.</para>
    ///
    /// <para>A module that throws on the way in stops the server. Unlike a fault on the tick, this one
    /// happens before a single player is served, and a game whose setup failed is not a game.</para></summary>
    private void StartModules()
    {
        foreach (var module in _registry.Modules)
        {
            try
            {
                module.Start(_actions);
            }
            catch (Exception ex)
            {
                throw new CoreModuleException($"Module '{module.Name}' failed while starting: {ex.Message}",
                                              module.Name, ex);
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        LocalizedLog.Info(_logger, ServerStrings.Server_ShuttingDown, ("GameName", _config.GameName));

        _acceptor.Stop();
        _gameLoop.Stop();   // game thread fully joined here — state is frozen, safe to read directly

        // Final save of everyone still in-world.  The periodic tick may be up to a minute stale, and
        // disconnect saves posted after the game thread stopped won't run, so flush them here while
        // nothing can mutate the state.  Routed through the per-login chain (see SaveAllOnline); the
        // _saver.DrainAsync below waits for these plus any still-in-flight periodic writes.
        SaveAllOnline();

        // Flush every map's dropped items synchronously — fire-and-forget saves queued during normal
        // play may still be in flight, and an in-flight save could be replaced or lost.  Walk every
        // map with at least one item and write the canonical state from in-memory.
        await SaveAllDroppedItemsAsync();

        // Wait for every per-login account write (periodic char saves + the final SaveAllOnline above)
        // to finish, so the write chain can't lose a save at shutdown.
        try { await _saver.DrainAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error draining account write chain"); }

        // Wait for every queued IBackgroundPersistence task (account logs, world-data saves, the
        // dropped-item writes above) to actually finish before the process exits.  Without this,
        // ContinueWith continuations can race the host's shutdown and silently lose writes.
        try { await _bg.DrainAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error draining background persistence queue"); }

        // Guild-file writes use their own serialized chain (not the IBackgroundPersistence queue), so drain
        // them explicitly — otherwise a just-queued guild save can be lost at exit.
        try { await _guilds.DrainAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error draining guild write chain"); }

        _cts?.Cancel();
        _cts?.Dispose();

        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_Stopped));
    }

    private async Task SaveAllDroppedItemsAsync()
    {
        int saved = 0;
        for (int mapNum = 1; mapNum <= _world.Limits.Maps; mapNum++)
        {
            if (_world.MapItems[mapNum].Count == 0) continue;
            try
            {
                await _items.SaveDroppedItemsForMapAsync(mapNum);
                saved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Shutdown save failed for dropped items on map {MapNum}", mapNum);
            }
        }
        if (saved > 0) LocalizedLog.Info(_logger, ServerStrings.Server_SavedDropsOnShutdown, ("Count", saved));
    }

    private void SaveAllOnline()
    {
        int saved = 0;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;   // covers connected players AND combat ghosts still in-world
            // Route the final save through the per-login chain (like the periodic save): serialized
            // after any still-in-flight write so it can't be lost, and it persists the shared bank too.
            // The game thread is stopped here, so the clones read stable state.
            _saver.SaveCharInBackground(sp.Login, sp.CharNum, sp.Char.Clone(), sp.CloneBank());
            saved++;
        }
        if (saved > 0) LocalizedLog.Info(_logger, ServerStrings.Server_SavedPlayersOnShutdown, ("Count", saved));
    }

    // ── World loading ─────────────────────────────────────────────────────────

    private async Task LoadWorldDataAsync(CancellationToken ct)
    {
        // WHICH set of records everything below comes out of, said before any of it is read: every line
        // that follows is loading this world's items, this world's maps. Operator-facing only — a player
        // sees the GAME's name, never this. Kept on the world as well as logged, so a connected editor can
        // put it in its title bar.
        var manifest = await _persistence.LoadWorldManifestAsync();
        _world.WorldName = manifest.Name;
        _world.Appearances = manifest.Appearances;
        _world.StartingItems = manifest.StartingItems;
        _world.DecalColor = manifest.DecalColor;
        if (manifest.IsNamed)
            LocalizedLog.Info(_logger, ServerStrings.Server_WorldName, ("WorldName", manifest.Name));

        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingGameData));

        // Arrays (1-based; index 0 = unused dummy)
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingItems));
        var (items, itemsLoaded) = await _persistence.LoadAllItemsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingNpcs));
        var (npcs, npcsLoaded) = await _persistence.LoadAllNpcsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingShops));
        var (shops, shopsLoaded) = await _persistence.LoadAllShopsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingConversations));
        var (conversations, conversationsLoaded) = await _persistence.LoadAllConversationsAsync();

        CopyArray(items, _world.Items, _world.Limits.Items);
        CopyArray(npcs, _world.Npcs, _world.Limits.Npcs);
        CopyArray(shops, _world.Shops, _world.Limits.Shops);
        CopyArray(conversations, _world.Conversations, _world.Limits.Conversations);

        // Whatever families the modules added. Core knows nothing about their contents, so each one is
        // loaded as bags out of the folder its own declaration names — no line here per family, and a
        // server with no modules does nothing at all.
        foreach (var family in _registry.Schema.Families)
        {
            if (CoreRecordFamilies.Find(family.Id) is not null) continue;

            int limit = _world.Limits.For(family);
            var (records, loaded) = await _persistence.LoadAllModuleRecordsAsync(family, limit);
            _world.ModuleRecords.Adopt(family, records);
            _logger.LogInformation("Loaded {Count} {Family} of {Limit} slots.", loaded, family.Id, limit);
        }

        // Guilds — runtime-created and unbounded; load every guild file present into the sparse map.
        var guilds = await _persistence.LoadAllGuildsAsync();
        foreach (var (index, guild) in guilds) _world.Guilds[index] = guild;
        // Retired numbers are not in that map, so the high-water mark comes off the folders instead.
        _world.HighestGuildNumber = await _persistence.HighestGuildNumberAsync();
        // Marketplace listings — unbounded like guilds; load every listing file present into the sparse map.
        var marketListings = await _persistence.LoadAllMarketListingsAsync();
        foreach (var (id, listing) in marketListings) _world.MarketListings[id] = listing;

        // Marketplace sales history — a rolling log (also the on-disk admin audit).
        _world.MarketSales.AddRange(await _persistence.LoadMarketSalesAsync());

        // Direct-trade write-ahead recovery — replay any swap a crash interrupted, before players can log in.
        await _trade.RecoverJournalsAsync();

        // Map groups — unbounded like guilds; load every mapgroup file present into the sparse map.
        var mapGroups = await _persistence.LoadAllMapGroupsAsync();
        foreach (var (index, group) in mapGroups) _world.MapGroups[index] = group;

        // Maps — load every one that exists; a slot with no file stands in memory at the size this world
        // says a map is (world.json, read at the top), and is NOT written out. An authored map is whatever
        // size it was authored at.
        //
        // Writing the blanks used to happen here and cost a file per empty slot, which is most of them: a
        // four-map world became a thousand files the first time a server opened it. The editor reads the
        // same folder and has always skipped what is not there.
        var blank = manifest.DefaultMapSize;
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingMaps));
        int mapsLoaded = 0;
        for (short i = 1; i <= _world.Limits.Maps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var map = await _persistence.LoadMapAsync(i);
            if (map is not null)
            {
                _world.Maps[i] = map;
                mapsLoaded++;
            }
            else
            {
                _world.Maps[i] = new MapRecord(blank.Width, blank.Height);
            }
        }

        // Whatever the maps used to say about the upper plane, they now say this. Nothing has asked yet at
        // boot, so this is belt and braces — but it is the rule for every path that replaces a map, and a
        // rule with an exception is one somebody copies.
        _world.InvalidateFringeReach();

        // MOTD. A server with no motd.json greets players anyway, from a default held in memory and never
        // written — so an operator who wants their own is setting one rather than replacing one, and a
        // server that has never been configured still says something.
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingMotd));
        string motd = await _persistence.LoadMotdAsync();
        if (string.IsNullOrWhiteSpace(motd))
        {
            // The resolved name, not the engine constant: this greeting is addressed to PLAYERS, and they
            // are playing whatever the operator and the world between them decided this game is called.
            _world.Motd = ServerStrings.Format(ServerStrings.Server_DefaultMotd, ("GameName", _config.GameName));
            _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_NoMotdHint));
        }
        else
        {
            _world.Motd = motd;
        }

        // Environment — restore Time of Day + Weather from environment.json so both pause while offline.
        var env = await _persistence.LoadEnvironmentAsync() ?? new EnvironmentState(0, WeatherType.Clear, 0);
        _tod.Init(env.TodPositionMs);
        _weather.Init(env.Weather, env.WeatherRemainingMs);

        // And whatever the game kept about the world. A server with no game loaded wrote none, and a
        // world starting fresh has none either.
        if (env.Values is { } values) _world.Values = values;
        if (env.Kept is { } stores) _world.Kept = stores;

        // What the world actually holds, rather than what its ceilings allow.
        LocalizedLog.Info(_logger, ServerStrings.Server_LoadedSummary,
            ("Items", itemsLoaded), ("Npcs", npcsLoaded), ("Shops", shopsLoaded),
            ("Conversations", conversationsLoaded),
            ("Maps", mapsLoaded));
    }

    /// <summary>
    /// Copies elements 1..max from <paramref name="src"/> into <paramref name="dest"/>.
    /// Both arrays are 1-based (index 0 = dummy). Skips out-of-bounds indices silently.
    /// </summary>
    private static void CopyArray<T>(T[] src, T[] dest, int max)
    {
        for (int i = 1; i <= max && i < src.Length && i < dest.Length; i++)
            dest[i] = src[i];
    }
}

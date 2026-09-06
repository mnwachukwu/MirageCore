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
    private readonly GuildScheduleSystem _guildSchedule;
    private readonly GuildTerritorySystem _territory;
    private readonly TradeSystem _trade;
    private readonly CombatSystem _combat;
    private readonly QuestSystem _quests;
    private readonly TcpConnectionAcceptor _acceptor;
    private readonly ILogger<MirageServerService> _logger;
    private readonly ServerConfig _config;

    private CancellationTokenSource? _cts;

    public MirageServerService(
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
        GuildScheduleSystem guildSchedule,
        GuildTerritorySystem territory,
        TradeSystem trade,
        CombatSystem combat,
        QuestSystem quests,
        TcpConnectionAcceptor acceptor,
        ServerConfig config,
        ILogger<MirageServerService> logger)
    {
        _config = config;
        _world = world;
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
        _guildSchedule = guildSchedule;
        _territory = territory;
        _trade = trade;
        _combat = combat;
        _quests = quests;
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

        await LoadWorldDataAsync(ct);

        // Wire the level-up → quest-eligibility refresh now that every system exists (can't be done at
        // construction — the Combat↔Quest DI cycle is broken by a Lazy, so neither can reference the other in
        // its ctor). A gained level may newly satisfy a quest's accept requirements, relighting its giver "?".
        _combat.PlayerLeveledUp = _quests.RefreshEligibility;

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

        // Runtime-data load summary: guilds + archived seasons (loaded in LoadWorldDataAsync) and the map items
        // now present across all maps (spawned + restored above).
        int mapItemCount = 0;
        for (int i = 1; i <= _world.Limits.Maps; i++) mapItemCount += _world.MapItems[i]?.Count ?? 0;
        LocalizedLog.Info(_logger, ServerStrings.Server_RuntimeDataSummary,
            ("Guilds", _world.Guilds.Count), ("Seasons", _world.SeasonArchives.Count), ("MapItems", mapItemCount));

        // Spawn NPC slots
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_SpawningNpcs));
        _spawn.SpawnAllMapNpcs();

        _gameLoop.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptor.Start(_cts.Token);

        LocalizedLog.Info(_logger, ServerStrings.Server_Ready,
            ("GameName", _config.GameName), ("ElapsedMs", sw.ElapsedMilliseconds));
    }

    public async Task StopAsync(CancellationToken ct)
    {
        LocalizedLog.Info(_logger, ServerStrings.Server_ShuttingDown, ("GameName", _config.GameName));

        _acceptor.Stop();
        _gameLoop.Stop();   // game thread fully joined here — state is frozen, safe to read directly

        // Flush per-kill income accrual (guild PendingPerkIncome + territory PendingTerritoryIncome) that the periodic
        // save may not have caught, so a restart never loses it (queued writes are awaited by the drains below).
        _gameLoop.FlushWorldDataNow();

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
        // them explicitly — otherwise a just-queued guild save (vault, income, war state) can be lost at exit.
        try { await _gameLoop.DrainGuildWritesAsync(); }
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
        if (manifest.IsNamed)
            LocalizedLog.Info(_logger, ServerStrings.Server_WorldName, ("WorldName", manifest.Name));

        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingGameData));

        // Arrays (1-based; index 0 = unused dummy)
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingItems));
        var (items, itemsPadded) = await _persistence.LoadAllItemsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingNpcs));
        var (npcs, npcsPadded) = await _persistence.LoadAllNpcsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingShops));
        var (shops, shopsPadded) = await _persistence.LoadAllShopsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingSpells));
        var (spells, spellsPadded) = await _persistence.LoadAllSpellsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingClasses));
        var (classes, classesPadded) = await _persistence.LoadAllClassesAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingQuests));
        var (quests, questsPadded) = await _persistence.LoadAllQuestsAsync();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingConversations));
        var (conversations, conversationsPadded) = await _persistence.LoadAllConversationsAsync();

        CopyArray(items, _world.Items, _world.Limits.Items);
        CopyArray(npcs, _world.Npcs, _world.Limits.Npcs);
        CopyArray(shops, _world.Shops, _world.Limits.Shops);
        CopyArray(spells, _world.Spells, _world.Limits.Spells);
        CopyArray(classes, _world.Classes, Constants.MaxClasses);
        CopyArray(quests, _world.Quests, _world.Limits.Quests);
        CopyArray(conversations, _world.Conversations, _world.Limits.Conversations);

        // Guilds — runtime-created and unbounded; load every guild file present into the sparse map.
        var guilds = await _persistence.LoadAllGuildsAsync();
        foreach (var (index, guild) in guilds) _world.Guilds[index] = guild;
        // Retired numbers are not in that map, so the high-water mark comes off the folders instead.
        _world.HighestGuildNumber = await _persistence.HighestGuildNumberAsync();
        // Re-register every still-active guild quest with the objective kernel — in-memory registrations don't
        // survive a restart (the quests themselves persisted on the guild records).
        _guilds.ReTrackActiveQuests();

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

        // Territory state, keyed by the group whose maps it is. A contestable group with no file is simply
        // unclaimed, so only what a server has actually written is here.
        var territories = await _persistence.LoadAllTerritoriesAsync();
        foreach (var (index, terr) in territories) _world.Territories[index] = terr;

        // Perpetual season archive — load past seasons for the historical-season browser.
        _world.SeasonArchives.AddRange(await _persistence.LoadAllSeasonArchivesAsync());

        // Maps — load all; create an empty file for any that don't exist on disk, at the size this world
        // says a map is (world.json, read at the top). An authored map is whatever size it was authored at.
        var blank = manifest.DefaultMapSize;
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_LoadingMaps));
        int mapsLoaded = 0, mapsCreated = 0;
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
                await _persistence.SaveMapAsync(i, _world.Maps[i]);
                mapsCreated++;
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
            _world.Motd = ServerStrings.Format(ServerStrings.Server_DefaultMotd, ("GameName", Constants.GameName));
            _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_NoMotdHint));
        }
        else
        {
            _world.Motd = motd;
        }

        // Environment — restore Time of Day + Weather from environment.json so both pause while offline,
        // and seed the guild daily-settlement cursor (wall-clock, so downtime is caught up on the first tick).
        var env = await _persistence.LoadEnvironmentAsync() ?? new EnvironmentState(0, WeatherType.Clear, 0);
        _tod.Init(env.TodPositionMs);
        _weather.Init(env.Weather, env.WeatherRemainingMs);
        _guildSchedule.Seed(env.LastSettledDate);
        _guildSchedule.SeedSeason(env.SeasonNumber, env.SeasonStartDate);
        // Seed each guild's cached seasonal standing from the loaded season scores, so the overhead standing is
        // correct on the first login after boot (before the first weekly settlement recomputes it).
        _guildSchedule.RecomputeStandings();
        _territory.Seed(env.NextWarNightUtc);

        LocalizedLog.Info(_logger, ServerStrings.Server_LoadedSummary,
            ("Items", items.Length - 1), ("Npcs", npcs.Length - 1), ("Shops", shops.Length - 1),
            ("Spells", spells.Length - 1), ("Classes", classes.Length - 1),
            ("Quests", quests.Length - 1), ("Conversations", conversations.Length - 1),
            ("Maps", mapsLoaded));
        LocalizedLog.Info(_logger, ServerStrings.Server_PaddedSummary,
            ("Items", itemsPadded), ("Npcs", npcsPadded), ("Shops", shopsPadded),
            ("Spells", spellsPadded), ("Classes", classesPadded),
            ("Quests", questsPadded), ("Conversations", conversationsPadded),
            ("Maps", mapsCreated));
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

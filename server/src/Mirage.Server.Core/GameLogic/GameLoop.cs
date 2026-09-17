using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using System.Collections.Concurrent;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// The single game thread.  EVERYTHING that touches game state runs here, serialized: packet handlers
/// (posted by the network receive loops via <see cref="Post"/>), connect/disconnect, and the periodic
/// AI / spawn / save ticks.  Because there is exactly one thread in the game core, no locks are needed
/// on <see cref="GameWorld"/> or the player slots.
///
/// The thread blocks on an inbound work queue with a timeout sized to the next due tick, so it wakes
/// either when work arrives or when a tick is due — no busy-spin, no separate timer threads.
/// </summary>
public sealed class GameLoop : IDisposable
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly NpcAiSystem _npcAi;
    private readonly PartySystem _party;
    private readonly ItemSystem _items;
    private readonly PlayerSaver _saver;
    private readonly TimeOfDaySystem _tod;
    private readonly WeatherSystem _weather;
    private readonly MailSystem _mail;
    private readonly MarketSystem _market;
    private readonly TradeSystem _trade;
    private readonly DecalSystem _decals;
    private readonly TickSchedule _modules;
    private readonly JoinLeaveSystem _joinLeave;

    /// <summary>Ticks since the loop started — the clock <see cref="DeadlineClock.Tick"/>
    /// deadlines are counted against. Read from the game thread.</summary>
    public long CurrentTick { get; private set; }
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly ILogger<GameLoop> _logger;

    // Wall clock for the periodic save's playtime banking. The loop's own cadence is driven by
    // Environment.TickCount64, which stays as-is — see IClock.
    private readonly IClock _clock;

    private const int AiIntervalMs = Constants.AiTickIntervalMs;
    // Fast NPC MOVEMENT pass — finer than the 500ms brain so a chasing NPC steps at its run cadence (flat
    // ~200ms/tile now that NPC run is SPD-independent) instead of once per brain tick.  Only executes committed
    // chase-steps (no acquisition / BFS-cache rebuild), so it's cheap.  100ms divides the flat 200ms run
    // cleanly, so the client slide matches with no snap.  See NpcAiSystem.RunMovement.
    private const int NpcMoveIntervalMs = 100;
    // Stain dry/broadcast pass — its own cadence so stains fade smoothly and a deposit
    // events reach observers promptly, independent of the 500ms brain.
    private const int DecalIntervalMs = Constants.DecalTickIntervalMs;
    private const int SpawnIntervalMs = 1_000;
    private const int SaveIntervalMs = 60_000;
    // Mailbox maturity sweep — flips in-transit P2P mail to delivered on both ends. Coarse (10-15 min delays).
    private const int MailSweepIntervalMs = 5_000;

    // The base beat. Module work counts in TICKS rather than milliseconds, so this defines one
    // tick, and it is the shortest interval above so module work can run as often as anything
    // Core does.
    private const int ModuleIntervalMs = NpcMoveIntervalMs;
    private const int MaxWaitMs = 250;   // cap the queue wait so shutdown stays responsive

    private readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>How hard this thread is working. Always recorded — the counters are two interlocked adds
    /// per pass, which is nothing against a pass that ran packet handlers — so a load report never needs
    /// the server started in a special mode to be measurable.</summary>
    public GameLoopMetrics Metrics { get; } = new();

    public GameLoop(GameWorld world, PlayerManager pm, NpcAiSystem npcAi, PartySystem party,
                    ItemSystem items, PlayerSaver saver, JoinLeaveSystem joinLeave,
                    TimeOfDaySystem tod, WeatherSystem weather,
                    MailSystem mail, MarketSystem market, TradeSystem trade, DecalSystem decals,
                    IPersistenceService persistence, IBackgroundPersistence bg, ILogger<GameLoop> logger,
                    IClock? clock = null, CoreRegistry? registry = null)
    {
        _world = world;
        _pm = pm;
        _npcAi = npcAi;
        _party = party;
        _items = items;
        _saver = saver;
        _joinLeave = joinLeave;
        _tod = tod;
        _weather = weather;
        _mail = mail;
        _market = market;
        _trade = trade;
        _decals = decals;
        _persistence = persistence;
        _bg = bg;
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
        _modules = (registry ?? CoreRegistry.CoreOnly).Tick;
    }

    /// <summary>
    /// Queues an action to run on the game thread.  This is the ONLY safe way for a network/accept
    /// thread to touch game state.  No-ops once the loop is shutting down (the connection is going away
    /// anyway).
    /// </summary>
    public void Post(Action action)
    {
        if (_queue.IsAddingCompleted) return;
        try { _queue.Add(action); }
        catch (InvalidOperationException) { /* CompleteAdding raced us during shutdown — drop */ }
    }

    public void Start()
    {
        _running = true;
        _thread = new Thread(RunLoop) { Name = "MirageGameThread", IsBackground = true };
        _thread.Start();
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_GameThreadStarted));
    }

    public void Stop()
    {
        _running = false;
        _queue.CompleteAdding();   // unblocks the loop's TryTake immediately
        try { _thread?.Join(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Game thread did not stop cleanly"); }
        _logger.LogInformation(ServerStrings.Get(ServerStrings.Server_GameThreadStopped));
    }

    public void Dispose()
    {
        try { _queue.Dispose(); } catch { /* already disposed */ }
    }

    // ── The game thread ─────────────────────────────────────────────────────────

    private void RunLoop()
    {
        long now = Environment.TickCount64;
        long nextAi = now + AiIntervalMs;
        long nextNpcMove = now + NpcMoveIntervalMs;
        long nextDecal = now + DecalIntervalMs;
        long nextSpawn = now + SpawnIntervalMs;
        long nextSave = now + SaveIntervalMs;
        long nextMailSweep = now + MailSweepIntervalMs;
        long nextModuleTick = now + ModuleIntervalMs;

        long iterationStart = System.Diagnostics.Stopwatch.GetTimestamp();

        while (_running)
        {
            now = Environment.TickCount64;
            long nextDeadline = Math.Min(
                Math.Min(Math.Min(Math.Min(nextAi, nextNpcMove), Math.Min(nextSpawn, nextSave)), nextDecal),
                Math.Min(nextMailSweep, nextModuleTick));
            int wait = (int)Math.Clamp(nextDeadline - now, 0, MaxWaitMs);

            // Wake on the next queued action OR the next tick deadline, then drain everything pending so
            // a burst of packets is processed before the next tick rather than one-per-wakeup.
            int drained = 0;
            // Busy time starts AFTER the queue wait: parked on an empty queue is idle, and counting it as
            // work would report a quiet server as fully loaded.
            long busyStart;
            if (TryTake(wait, out var action))
            {
                busyStart = System.Diagnostics.Stopwatch.GetTimestamp();
                RunQueued(action);
                drained++;
                while (TryTake(0, out var more)) { RunQueued(more); drained++; }
            }
            else
            {
                busyStart = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            now = Environment.TickCount64;
            if (now >= nextAi)
            {
                RunTick(AiTick, "AI");
                nextAi = Schedule(nextAi, now, AiIntervalMs);
            }
            if (now >= nextNpcMove)
            {
                RunTick(NpcMoveTick, "npc-move");
                nextNpcMove = Schedule(nextNpcMove, now, NpcMoveIntervalMs);
            }
            if (now >= nextDecal)
            {
                RunTick(DecalTick, "decals");
                nextDecal = Schedule(nextDecal, now, DecalIntervalMs);
            }
            if (now >= nextSpawn)
            {
                RunTick(SpawnTick, "spawn");
                nextSpawn = Schedule(nextSpawn, now, SpawnIntervalMs);
            }
            if (now >= nextSave)
            {
                RunTick(SaveTick, "save");
                nextSave = Schedule(nextSave, now, SaveIntervalMs);
            }
            if (now >= nextMailSweep)
            {
                RunTick(MailTick, "mail");
                nextMailSweep = Schedule(nextMailSweep, now, MailSweepIntervalMs);
            }
            if (now >= nextModuleTick)
            {
                RunTick(ModuleTick, "modules");
                nextModuleTick = Schedule(nextModuleTick, now, ModuleIntervalMs);
            }

            // End of iteration: persist any player flagged dirty by this tick's packet handlers or AI
            // events (drop/pickup/break/death/level-up/sort) so a hard-disconnect can't roll it back
            // before the 60 s autosave. Cheap when nothing is dirty.
            RunTick(FlushDirtyPlayers, "flush");

            long iterationEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            // Overrun = the work alone outlasted the SHORTEST tick interval, so the next pass is already
            // late before it starts. That is the first symptom of a loop losing the race, well before
            // anything visible goes wrong in game.
            bool overran = (iterationEnd - busyStart) > NpcMoveIntervalMs * (System.Diagnostics.Stopwatch.Frequency / 1000);
            Metrics.Record(iterationEnd - busyStart, iterationEnd - iterationStart, drained, overran);
            iterationStart = iterationEnd;
        }
    }

    // TryTake on a completed BlockingCollection throws once drained; treat that as "no item".
    private bool TryTake(int wait, out Action action)
    {
        try { return _queue.TryTake(out action!, wait); }
        catch (Exception)
        {
            action = null!;
            return false;
        }
    }

    private void RunQueued(Action action)
    {
        try { action(); }
        catch (Exception ex) { _logger.LogError(ex, "Error processing a posted game action"); }
    }

    private void RunTick(Action tick, string name)
    {
        try { tick(); }
        catch (Exception ex) { _logger.LogError(ex, "Error in {Tick} tick", name); }
    }

    // ── Ticks (all run on the game thread) ──────────────────────────────────────

    /// <summary>When a tick that just ran is next due.
    ///
    /// <para>Advances the PREVIOUS deadline by one interval rather than restarting from the clock, so a tick
    /// that ran late does not push every tick after it late as well. Waking a millisecond late 120 times a
    /// minute is how a 500 ms cadence quietly becomes 520, and the drift never comes back.</para>
    ///
    /// <para>A tick more than one interval behind — a long save, a stalled thread — restarts from the clock
    /// instead of trying to catch up, since replaying the beats it missed helps nobody.</para></summary>
    internal static long Schedule(long due, long now, int intervalMs) =>
        due + intervalMs > now ? due + intervalMs : now + intervalMs;

    private void AiTick()
    {
        long now = Environment.TickCount64;
        _npcAi.RunForAllMaps(now);
        // A body a game asked to keep after a disconnect stops being kept here, so the game does not have
        // to come back for it. Nothing to do in an engine where no policy ever asked for one.
        _joinLeave.SweepGhosts();
        _party.Tick(now);
        _trade.Tick();          // cancel trades whose parties drifted out of range + expire stale invites
        _tod.Tick();
        _weather.Tick();
    }

    // Fast NPC movement pass — advances committed chase-steps on each NPC's SPD step-clock (see
    // NpcAiSystem.RunMovement).  Separate from AiTick so the expensive brain work stays at 500ms.
    private void NpcMoveTick()
    {
        _npcAi.RunMovement(Environment.TickCount64);
    }

    // Stain pass — dries every map's stains and broadcasts the maps whose list changed. Cheap: only maps
    // something has actually spilled on carry anything at all.
    private void DecalTick() => _decals.Tick();

    /// <summary>Runs whatever the loaded modules put on this tick, in their registered order.
    ///
    /// <para>Runs after Core's own ticks, so a game reacting to the world sees the world Core has already
    /// finished moving.</para>
    ///
    /// <para><b>Caught per item, not per tick.</b> One module throwing must not silently starve every
    /// module registered after it — that presents as a feature that stopped working rather than as an
    /// error, and a log line naming the tick would not say which module to look at.</para></summary>
    internal void ModuleTick()
    {
        CurrentTick++;
        foreach (var work in _modules.DueOn(CurrentTick))
        {
            try { work.Tick(CurrentTick); }
            catch (Exception ex) { _logger.LogError(ex, "Error in module tick work {Work}", work.Name); }
        }
    }

    private void SpawnTick()
    {
        long now = Environment.TickCount64;
        for (int mapNum = 1; mapNum <= _world.Limits.Maps; mapNum++)
            _items.CheckItemRespawn(mapNum, now);
    }

    // Flip any in-transit P2P mail that matured this interval to delivered, re-syncing the online owners.
    private void MailTick() => _mail.TickMaturity();

    private void SaveTick()
    {
        // Snapshot each online player HERE (consistent, single-threaded), then write off-thread so the
        // file I/O never stalls the game loop.  Clone because the player keeps being mutated while the
        // background write runs.
        long nowUtc = _clock.UtcNowUnix;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            sp.BankPlaytime(nowUtc);   // fold this interval's playtime into the snapshot being persisted
            _saver.SaveCharInBackground(sp.Login, sp.CharNum, sp.Char.Clone(), sp.CloneBank());
            sp.SaveDirty = false;   // periodic save covers this player; skip a redundant dirty-flush
        }
        PersistEnvironmentNow();
        _market.TickExpiry();                       // return listings past their 30-day lifetime to their sellers
        _mail.TickExpiry();                         // delete mail past its 30-day retention (from every online mailbox)
    }

    /// <summary>End-of-iteration flush: persists every player flagged by <see cref="PlayerManager.MarkDirty"/>
    /// this tick, then clears the flag, so an exploitable change (item drop/pickup, durability break,
    /// death, level-up, sort) is durable within one tick — a hard disconnect can't roll it back to the
    /// pre-change state. Off-thread write via <see cref="PlayerSaver"/>, identical to the periodic save;
    /// a no-op scan when nothing is dirty.</summary>
    private void FlushDirtyPlayers()
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.SaveDirty) continue;
            sp.SaveDirty = false;
            if (sp.IsPlaying)
                _saver.SaveCharInBackground(sp.Login, sp.CharNum, sp.Char.Clone(), sp.CloneBank());
        }
    }

    /// <summary>Compose and persist the combined environment state (Time of Day + Weather) off-thread.
    /// The single writer for environment.json — both systems are read here, never self-persisted.  Also
    /// called immediately after an admin /tod or /weather change so the jump survives a crash.</summary>
    public void PersistEnvironmentNow()
    {
        var env = new EnvironmentState(_tod.CurrentPosMs, _weather.CurrentWeather, _weather.CurrentRemainingMs,
                                       _world.Values, _world.Kept);
        _bg.Run(_persistence.SaveEnvironmentAsync(env), nameof(IPersistenceService.SaveEnvironmentAsync));
    }
}

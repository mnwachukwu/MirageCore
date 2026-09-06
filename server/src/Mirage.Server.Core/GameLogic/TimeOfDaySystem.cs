using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Drives the 4-hour day/night cycle.  Must only be called from the game thread.
/// Cycle layout (game-time hours, paused while server is offline):
///   Day 2 h 30 min | Dusk 15 min | Night 1 h | Dawn 15 min
/// </summary>
public sealed class TimeOfDaySystem : GameSystem
{
    private readonly GameWorld _world;

    // TickCount64 value that corresponds to cycle position 0 (start of Day).
    // posMs = (Environment.TickCount64 - _cycleStartMs) % TodCycleDurationMs
    private long _cycleStartMs;
    private TimePhase _lastPhase = TimePhase.Day;

    public TimeOfDaySystem(GameWorld world, IPacketDispatcher dispatcher)
        : base(dispatcher)
    {
        _world = world;
    }

    /// <summary>Seed the cycle from the persisted position (loaded once from environment.json by the host).
    /// Must be called before the game loop starts.</summary>
    public void Init(long savedPosMs)
    {
        long clamped = Math.Clamp(savedPosMs, 0L, Constants.TodCycleDurationMs - 1);
        _cycleStartMs = Environment.TickCount64 - clamped;
        var (phase, progress) = PhaseAt(clamped);
        _world.TimePhase = phase;
        _world.TimeProgress = progress;
        _lastPhase = phase;
    }

    /// <summary>Called every AI tick (500 ms).  Advances the cycle and broadcasts phase changes.</summary>
    public void Tick()
    {
        long posMs = (Environment.TickCount64 - _cycleStartMs) % Constants.TodCycleDurationMs;
        var (phase, progress) = PhaseAt(posMs);
        _world.TimeProgress = progress;

        if (phase == _lastPhase) return;

        var oldPhase = _lastPhase;
        _world.TimePhase = phase;
        _lastPhase = phase;
        _dispatcher.SendToAll(PacketBuilder.TimeOfDay(phase, progress));
        ApplyNightHpTransition(oldPhase, phase);

        // Natural cycle only announces the two major transitions: nightfall (Dusk → Night) and the
        // "night is over" break (Night → Dawn). Dusk and Day arrive quietly.
        switch (phase)
        {
            case TimePhase.Night:
                AnnouncePhase(TimePhase.Night);
                break;
            case TimePhase.Dawn:
                AnnouncePhase(TimePhase.Dawn);
                break;
        }
    }

    /// <summary>
    /// Broadcasts the proclamation for <paramref name="phase"/> (yellow), plus the red NPC-strength
    /// warning when night begins. Shared by the natural cycle and the /tod admin jump.
    /// </summary>
    private void AnnouncePhase(TimePhase phase)
    {
        string key = phase switch
        {
            TimePhase.Dusk => ServerStrings.TimeOfDay_DuskFalls,
            TimePhase.Night => ServerStrings.TimeOfDay_NightFalls,
            TimePhase.Dawn => ServerStrings.TimeOfDay_DawnBreaks,
            _ => ServerStrings.TimeOfDay_DayReturns,
        };
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.Yellow, ChatChannel.Notice));
        if (phase == TimePhase.Night)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.TimeOfDay_NightWarning,
                new ChatMetadata(GameColor.Warning, ChatChannel.Notice));
        }
    }

    /// <summary>
    /// Re-scales every live NPC's current HP and its damage-contribution ledgers whenever the cycle crosses
    /// INTO or OUT OF Night — so HP% and contribution/aggro fractions stay constant across the flip (and
    /// current HP never overshoots the day max). Then re-sends each observed map's NPC snapshot so client HP
    /// bars re-scale to the boosted/unboosted denominator. Fires only when Night-ness actually changes.
    /// </summary>
    private void ApplyNightHpTransition(TimePhase oldPhase, TimePhase newPhase)
    {
        bool wasNight = oldPhase == TimePhase.Night;
        bool isNight = newPhase == TimePhase.Night;
        if (wasNight == isNight) return;
        double ratio = isNight ? Constants.NpcNightHpMultiplier : 1.0 / Constants.NpcNightHpMultiplier;

        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            bool anyNative = false;
            for (int s = 1; s <= Constants.MaxMapNpcs; s++)
            {
                var mn = _world.MapNpcs[m, s];
                if (mn.Num > 0)
                {
                    ScaleNpcVitals(mn, ratio);
                    anyNative = true;
                }
            }
            var guests = _world.MapTraversalNpcs[m];
            for (int i = 0; i < guests.Count; i++)
                if (guests[i].Num > 0) ScaleNpcVitals(guests[i], ratio);

            // Re-sync native NPC bars for observers (traversal guests self-correct on their next resend).
            // BuildMapNpcs is night-aware via EffectiveNpcMaxHp, so it emits the correct denominator.
            if (anyNative && _world.MapObservers[m].Count > 0)
                SendToMap(_world, m, JoinLeaveSystem.BuildMapNpcs(_world, m));
        }
    }

    /// <summary>Scale a live NPC's current HP and its damage-contribution ledgers by <paramref name="ratio"/>
    /// (HP kept ≥ 1). Uniform scaling preserves aggro ordering and the EXP damage-share fractions.</summary>
    private static void ScaleNpcVitals(MapNpcRecord mn, double ratio)
    {
        mn.Hp = Math.Max(1, (int)Math.Round(mn.Hp * ratio, MidpointRounding.AwayFromZero));
        var dmg = mn.DamageByPlayer;
        for (int i = 0; i < dmg.Length; i++)
            if (dmg[i] != 0) dmg[i] = (int)Math.Round(dmg[i] * ratio, MidpointRounding.AwayFromZero);
        if (mn.DamageByNpc is { } list)
        {
            for (int i = 0; i < list.Count; i++)
                list[i] = list[i] with { Damage = (int)Math.Round(list[i].Damage * ratio, MidpointRounding.AwayFromZero) };
        }
    }

    /// <summary>
    /// Immediately jumps to the start of <paramref name="phase"/>, broadcasts the change,
    /// and persists.  Used by the /tod admin command; <paramref name="adminName"/> is named in the
    /// broadcast so all players see who forced the shift.
    /// </summary>
    public void JumpToPhase(TimePhase phase, string adminName)
    {
        long phaseStartMs = phase switch
        {
            TimePhase.Day => 0L,
            TimePhase.Dusk => Constants.TodDayDurationMs,
            TimePhase.Night => Constants.TodNightStartMs,
            TimePhase.Dawn => Constants.TodDawnStartMs,
            _ => 0L,
        };
        var oldPhase = _world.TimePhase;
        _cycleStartMs = Environment.TickCount64 - phaseStartMs;
        _world.TimePhase = phase;
        _world.TimeProgress = 0f;
        _lastPhase = phase;
        _dispatcher.SendToAll(PacketBuilder.TimeOfDay(phase, 0f));
        ApplyNightHpTransition(oldPhase, phase);
        // Always announce an admin jump as a split pair: a public "something shifted" line to everyone,
        // then a staff-only attribution naming the admin. Then the proclamation for the new phase.
        _dispatcher.SendLocalizedChatToAll(ServerStrings.TimeOfDay_UnnaturalShift,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Notice));
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.TimeOfDay_UnnaturalShiftBy,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Notice), ("Admin", adminName));
        AnnouncePhase(phase);
    }

    /// <summary>Returns the phase label key for the current phase (for welcome messages).</summary>
    public string WelcomeKey() => _world.TimePhase switch
    {
        TimePhase.Day => ServerStrings.TimeOfDay_WelcomeDay,
        TimePhase.Dusk => ServerStrings.TimeOfDay_WelcomeDusk,
        TimePhase.Night => ServerStrings.TimeOfDay_WelcomeNight,
        TimePhase.Dawn => ServerStrings.TimeOfDay_WelcomeDawn,
        _ => ServerStrings.TimeOfDay_WelcomeDay,
    };

    /// <summary>Current cycle position in ms.  Exposed for the HUD packet on join.</summary>
    public long CurrentPosMs =>
        (Environment.TickCount64 - _cycleStartMs) % Constants.TodCycleDurationMs;

    private static (TimePhase phase, float progress) PhaseAt(long posMs)
    {
        if (posMs < Constants.TodDayDurationMs)
            return (TimePhase.Day, posMs / (float)Constants.TodDayDurationMs);
        if (posMs < Constants.TodNightStartMs)
            return (TimePhase.Dusk, (posMs - Constants.TodDayDurationMs) / (float)Constants.TodDuskDurationMs);
        if (posMs < Constants.TodDawnStartMs)
            return (TimePhase.Night, (posMs - Constants.TodNightStartMs) / (float)Constants.TodNightDurationMs);
        return (TimePhase.Dawn, (posMs - Constants.TodDawnStartMs) / (float)Constants.TodDawnDurationMs);
    }
}

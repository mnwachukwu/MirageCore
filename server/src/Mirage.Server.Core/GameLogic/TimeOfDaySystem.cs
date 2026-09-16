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

        _world.TimePhase = phase;
        _lastPhase = phase;
        _dispatcher.SendToAll(PacketBuilder.TimeOfDay(phase, progress));

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
    /// Broadcasts the proclamation for <paramref name="phase"/>. Shared by the natural cycle and the
    /// /tod admin jump.
    ///
    /// <para>What the phase MEANS is a game's. The engine says the sun went down and reports the phase
    /// to whatever is listening; whether that makes anything harder is a rule, and rules are declared.</para>
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
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.Yellow, ChatChannel.System));
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
        _cycleStartMs = Environment.TickCount64 - phaseStartMs;
        _world.TimePhase = phase;
        _world.TimeProgress = 0f;
        _lastPhase = phase;
        _dispatcher.SendToAll(PacketBuilder.TimeOfDay(phase, 0f));
        // Always announce an admin jump as a split pair: a public "something shifted" line to everyone,
        // then a staff-only attribution naming the admin. Then the proclamation for the new phase.
        _dispatcher.SendLocalizedChatToAll(ServerStrings.TimeOfDay_UnnaturalShift,
            new ChatMetadata(GameColor.Yellow, ChatChannel.System));
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.TimeOfDay_UnnaturalShiftBy,
            new ChatMetadata(GameColor.Yellow, ChatChannel.System), ("Admin", adminName));
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

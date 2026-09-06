using Mirage.Shared;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// Combined environment state persisted to environment.json: the Time-of-Day cycle position and the
/// current weather with the remaining time on whichever weather timer is live (idle Y while Clear,
/// active Z otherwise). Both pause while the server is offline — positions/durations are stored, never
/// wall-clock, so elapsed downtime never advances either clock.
///
/// <see cref="LastSettledDate"/> is the exception: the guild daily-settlement cursor IS wall-clock, so
/// downtime is caught up on boot rather than paused (an added init property, so positional construction
/// of the paused fields is unchanged).
/// </summary>
public sealed record EnvironmentState(long TodPositionMs, WeatherType Weather, long WeatherRemainingMs)
{
    /// <summary>Server-local calendar date the daily 00:00 guild settlement last ran through.
    /// <see cref="DateOnly.MinValue"/> (the default) = a never-run server (adopts today on first tick, no
    /// retroactive settlement).</summary>
    public DateOnly LastSettledDate { get; init; }

    /// <summary>UTC-seconds of the next scheduled territory war night. Wall-clock (like the
    /// settlement cursor): 0 = unscheduled (computed on first boot); a slot missed during downtime fires once
    /// on boot, then reschedules to the next weekly slot.</summary>
    public long NextWarNightUtc { get; init; }

    /// <summary>The current seasonal-leaderboard season number (1-based). 0 = uninitialized (adopts
    /// 1 on the first weekly boundary). Advances when a 13-week season ends. Wall-clock, like the settlement.</summary>
    public int SeasonNumber { get; init; }

    /// <summary>Server-local date the current season began (a <c>ScheduleConfig.WeekResetDay</c>).
    /// <see cref="DateOnly.MinValue"/> (the default) = uninitialized: adopted on the first weekly boundary,
    /// with no scoring or payout that week (established control carries in).</summary>
    public DateOnly SeasonStartDate { get; init; }
}

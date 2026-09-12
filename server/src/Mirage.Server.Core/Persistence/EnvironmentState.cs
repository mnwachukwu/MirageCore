using Mirage.Shared;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// Combined environment state persisted to environment.json: the Time-of-Day cycle position and the
/// current weather with the remaining time on whichever weather timer is live (idle Y while Clear,
/// active Z otherwise). Both pause while the server is offline — positions/durations are stored, never
/// wall-clock, so elapsed downtime never advances either clock.
/// </summary>
public sealed record EnvironmentState(long TodPositionMs, WeatherType Weather, long WeatherRemainingMs);

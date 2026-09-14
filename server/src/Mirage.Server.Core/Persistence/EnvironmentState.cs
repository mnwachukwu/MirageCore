using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.Persistence;

/// <summary>
/// Combined environment state persisted to environment.json: the Time-of-Day cycle position, the
/// current weather with the remaining time on whichever weather timer is live (idle Y while Clear,
/// active Z otherwise), and whatever a game keeps about the world itself. Both clocks pause while the
/// server is offline — positions/durations are stored, never wall-clock, so elapsed downtime never
/// advances either one.
/// </summary>
/// <param name="Values">A game's own world-scoped values. Null in a file written before games could
/// keep any, and in one written by a server with no game loaded.</param>
public sealed record EnvironmentState(long TodPositionMs, WeatherType Weather, long WeatherRemainingMs,
                                      AttributeBag? Values = null);

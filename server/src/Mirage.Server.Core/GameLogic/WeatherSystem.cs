using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Drives the global weather cycle. Must only be called from the game thread. Mirrors
/// <see cref="TimeOfDaySystem"/>: state on <see cref="GameWorld.Weather"/>, broadcast on change, sent to
/// each player on join, persisted (paused while offline) via the combined environment.json.
/// <para>Two timers, tracked by a single deadline <see cref="_timerFiresAtMs"/>:</para>
/// <list type="bullet">
///   <item>Timer Y (idle, while Clear): 1-2 h. On expiry a 40% roll picks a non-Clear weather by weight.</item>
///   <item>Timer Z (active, while not Clear): a per-type duration. On expiry the weather returns to Clear.</item>
/// </list>
/// </summary>
public sealed class WeatherSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;

    // Absolute TickCount64 deadline for whichever timer is live (Y while Clear, Z otherwise). We persist
    // the REMAINING duration, never a wall-clock time, so offline downtime never advances the countdown.
    private long _timerFiresAtMs;

    public WeatherSystem(GameWorld world, IPacketDispatcher dispatcher, PlayerManager pm,
                         IRandomSource? rng = null)
        : base(dispatcher, rng: rng)
    {
        _world = world;
        _pm = pm;
    }

    public WeatherType CurrentWeather => _world.Weather;
    public long CurrentRemainingMs => Math.Max(0, _timerFiresAtMs - Environment.TickCount64);

    /// <summary>Seed the weather from the persisted slice (loaded once from environment.json by the host).
    /// Must be called before the game loop starts.</summary>
    public void Init(WeatherType weather, long remainingMs)
    {
        long now = Environment.TickCount64;
        _world.Weather = weather;
        _timerFiresAtMs = (weather == WeatherType.Clear && remainingMs <= 0)
            ? now + RollIdleGapMs()                 // fresh install: start a fresh idle gap
            : now + Math.Max(0, remainingMs);       // resume the paused countdown
    }

    /// <summary>Called every AI tick (500 ms).  Fires the live timer when its deadline passes.</summary>
    public void Tick()
    {
        long now = Environment.TickCount64;
        if (now < _timerFiresAtMs) return;

        if (_world.Weather == WeatherType.Clear)
        {
            // Timer Y fired.
            if (RollTriggerHits())
            {
                var picked = RollWeightedWeather();
                ActivateWeather(picked, RollActiveDurationMs(picked), adminName: null);
            }
            else
            {
                _timerFiresAtMs = now + RollIdleGapMs();   // missed the roll: another idle gap
            }
        }
        else
        {
            // Timer Z fired: weather ends.
            DeactivateWeather(adminName: null);
            _timerFiresAtMs = now + RollIdleGapMs();
        }
    }

    /// <summary>Force a weather via the /weather admin command, rolling the appropriate timer.</summary>
    public void SetWeatherAdmin(WeatherType type, string adminName)
    {
        if (type == WeatherType.Clear)
        {
            DeactivateWeather(adminName);
            _timerFiresAtMs = Environment.TickCount64 + RollIdleGapMs();
        }
        else
        {
            ActivateWeather(type, RollActiveDurationMs(type), adminName);
        }
    }

    private void ActivateWeather(WeatherType type, long durationMs, string? adminName)
    {
        var old = _world.Weather;
        _world.Weather = type;
        _timerFiresAtMs = Environment.TickCount64 + durationMs;
        _dispatcher.SendToAll(PacketBuilder.Weather(type));
        // Admin-forced: announce the unnatural shift BEFORE the weather's arrival line.
        if (adminName != null) AnnounceUnnaturalShift(adminName);
        AnnounceArrival(type);
    }

    private void DeactivateWeather(string? adminName)
    {
        var old = _world.Weather;
        _world.Weather = WeatherType.Clear;
        _dispatcher.SendToAll(PacketBuilder.Weather(WeatherType.Clear));
        // Admin-forced: announce the unnatural shift BEFORE the "skies clear" line.
        if (adminName != null) AnnounceUnnaturalShift(adminName);
        _dispatcher.SendLocalizedChatToAll(ServerStrings.Weather_Clears,
            new ChatMetadata(GameColor.Yellow, ChatChannel.System));
    }

    // ── Announcements ──────────────────────────────────────────────────────────

    private void AnnounceArrival(WeatherType type)
    {
        string key = type switch
        {
            WeatherType.Rain => ServerStrings.Weather_RainBegins,
            WeatherType.Snow => ServerStrings.Weather_SnowBegins,
            WeatherType.HeatWave => ServerStrings.Weather_HeatWaveBegins,
            WeatherType.HeavyWind => ServerStrings.Weather_HeavyWindBegins,
            _ => ServerStrings.Weather_Clears,
        };
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.Yellow, ChatChannel.System));

        // A follow-up line only where the engine itself does something. A gale doubles every action's
        // cooldown; rain, snow and heat are weather a game may price however it likes, and Core
        // announcing a cost it does not charge is the engine lying to a player.
        if (type == WeatherType.HeavyWind)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.Weather_HeavyWindEffect,
                new ChatMetadata(GameColor.Warning, ChatChannel.System));
        }
    }

    /// <summary>Returns the welcome-line key for the current weather (for the login batch, mirroring
    /// <see cref="TimeOfDaySystem.WelcomeKey"/>). Self-contained lines that state the weather + its effect.</summary>
    public string WelcomeKey() => _world.Weather switch
    {
        WeatherType.Rain => ServerStrings.Weather_WelcomeRain,
        WeatherType.Snow => ServerStrings.Weather_WelcomeSnow,
        WeatherType.HeatWave => ServerStrings.Weather_WelcomeHeatWave,
        WeatherType.HeavyWind => ServerStrings.Weather_WelcomeHeavyWind,
        _ => ServerStrings.Weather_WelcomeClear,
    };

    /// <summary>Split admin-shift notice: a public "something changed" line to everyone, and a staff-only
    /// attribution naming the admin.</summary>
    private void AnnounceUnnaturalShift(string adminName)
    {
        _dispatcher.SendLocalizedChatToAll(ServerStrings.Weather_UnnaturalShift,
            new ChatMetadata(GameColor.Yellow, ChatChannel.System));
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.Weather_UnnaturalShiftBy,
            new ChatMetadata(GameColor.Yellow, ChatChannel.System), ("Admin", adminName));
    }

    // ── Rolls ────────────────────────────────────────────────────────────────────

    private long RollIdleGapMs() => RandRange(Constants.WeatherIdleMinMs, Constants.WeatherIdleMaxMs);

    private bool RollTriggerHits() => Rng.Next(100) < Constants.WeatherTriggerChancePercent;

    /// <summary>Weighted pick among the non-Clear weathers. The denominator is the weight SUM (not a hard
    /// 100), so retuning any single weight later can't silently skew the distribution.</summary>
    private WeatherType RollWeightedWeather()
    {
        int total = Constants.WeatherWeightRain + Constants.WeatherWeightHeatWave
                    + Constants.WeatherWeightSnow + Constants.WeatherWeightHeavyWind;
        int r = Rng.Next(total);
        if ((r -= Constants.WeatherWeightRain) < 0) return WeatherType.Rain;
        if ((r -= Constants.WeatherWeightHeatWave) < 0) return WeatherType.HeatWave;
        if ((r -= Constants.WeatherWeightSnow) < 0) return WeatherType.Snow;
        return WeatherType.HeavyWind;
    }

    private long RollActiveDurationMs(WeatherType type) => type switch
    {
        WeatherType.Rain => RandRange(Constants.WeatherRainMinMs, Constants.WeatherRainMaxMs),
        WeatherType.HeatWave => RandRange(Constants.WeatherHeatWaveMinMs, Constants.WeatherHeatWaveMaxMs),
        WeatherType.Snow => RandRange(Constants.WeatherSnowMinMs, Constants.WeatherSnowMaxMs),
        WeatherType.HeavyWind => RandRange(Constants.WeatherHeavyWindMinMs, Constants.WeatherHeavyWindMaxMs),
        _ => Constants.WeatherRainMinMs,
    };

    private long RandRange(long minInclusive, long maxInclusive) =>
        Rng.NextInt64(minInclusive, maxInclusive + 1);
}

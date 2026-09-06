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
        // If we booted into Snow, reduce any already-live entities. (None are spawned this early, but this
        // keeps the invariant correct and is safe against future reordering.)
        if (weather == WeatherType.Snow)
            ApplySnowVitalTransition(WeatherType.Clear, WeatherType.Snow);
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
        ApplySnowVitalTransition(old, type);
        _dispatcher.SendToAll(PacketBuilder.Weather(type));
        // Admin-forced: announce the unnatural shift BEFORE the weather's arrival line.
        if (adminName != null) AnnounceUnnaturalShift(adminName);
        AnnounceArrival(type);
    }

    private void DeactivateWeather(string? adminName)
    {
        var old = _world.Weather;
        _world.Weather = WeatherType.Clear;
        ApplySnowVitalTransition(old, WeatherType.Clear);
        _dispatcher.SendToAll(PacketBuilder.Weather(WeatherType.Clear));
        // Admin-forced: announce the unnatural shift BEFORE the "skies clear" line.
        if (adminName != null) AnnounceUnnaturalShift(adminName);
        _dispatcher.SendLocalizedChatToAll(ServerStrings.Weather_Clears,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Notice));
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
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.Yellow, ChatChannel.Notice));
        // Follow-up effect line — mirrors the Night "NPCs grow stronger" warning so players know what changed.
        string? effectKey = type switch
        {
            WeatherType.Rain => ServerStrings.Weather_RainEffect,
            WeatherType.Snow => ServerStrings.Weather_SnowEffect,
            WeatherType.HeatWave => ServerStrings.Weather_HeatWaveEffect,
            WeatherType.HeavyWind => ServerStrings.Weather_HeavyWindEffect,
            _ => null,
        };
        if (effectKey is not null)
            _dispatcher.SendLocalizedChatToAll(effectKey, new ChatMetadata(GameColor.Warning, ChatChannel.Notice));
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
            new ChatMetadata(GameColor.Yellow, ChatChannel.Notice));
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.Weather_UnnaturalShiftBy,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Notice), ("Admin", adminName));
    }

    // ── Snow max-vital transition ───────────────────────────────────────────────

    /// <summary>When Snow-ness flips, rescale every live NPC's and player's current vitals by the Snow
    /// factor's ratio so HP%/MP%/SP% stay constant (no overfill, no gap), then re-sync clients. Night and
    /// Snow compose independently: the Night HP factor is common to both endpoints of a Snow flip and
    /// cancels, so the ratio here is purely the Snow factor. Mirrors
    /// <see cref="TimeOfDaySystem"/>'s ApplyNightHpTransition, generalized to MP/SP and players.</summary>
    private void ApplySnowVitalTransition(WeatherType oldW, WeatherType newW)
    {
        bool wasSnow = oldW == WeatherType.Snow;
        bool isSnow = newW == WeatherType.Snow;
        if (wasSnow == isSnow) return;

        double hpRatio = SnowFactor(isSnow, Constants.WeatherSnowMaxHpMultiplier) / SnowFactor(wasSnow, Constants.WeatherSnowMaxHpMultiplier);
        double mpRatio = SnowFactor(isSnow, Constants.WeatherSnowMaxMpMultiplier) / SnowFactor(wasSnow, Constants.WeatherSnowMaxMpMultiplier);
        double spRatio = SnowFactor(isSnow, Constants.WeatherSnowMaxSpMultiplier) / SnowFactor(wasSnow, Constants.WeatherSnowMaxSpMultiplier);

        // NPCs: native slots + traversal guests on every map; re-sync each observed map's snapshot.
        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            bool anyNative = false;
            for (int s = 1; s <= Constants.MaxMapNpcs; s++)
            {
                var mn = _world.MapNpcs[m, s];
                if (mn.Num > 0)
                {
                    ScaleNpcVitalsForSnow(mn, hpRatio, mpRatio, spRatio);
                    anyNative = true;
                }
            }
            var guests = _world.MapTraversalNpcs[m];
            for (int i = 0; i < guests.Count; i++)
                if (guests[i].Num > 0) ScaleNpcVitalsForSnow(guests[i], hpRatio, mpRatio, spRatio);
            if (anyNative && _world.MapObservers[m].Count > 0)
                SendToMap(_world, m, JoinLeaveSystem.BuildMapNpcs(_world, m));
        }

        // Players: recompute the weather-adjusted max, scale current to the new max, re-sync bars.
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying) continue;
            var p = _pm[i].Char;
            if (p.Class < 1 || p.Class >= _world.Classes.Length) continue;
            int oldMaxHp = p.MaxHp, oldMaxMp = p.MaxMp, oldMaxSp = p.MaxSp;
            StatFormulas.RefreshPlayerMaxVitals(p, _world.Classes[p.Class], newW);
            // HP keeps a >=1 floor (never scale a live player to 0); MP/SP may legitimately sit at 0.
            if (oldMaxHp > 0) p.Hp = Math.Max(1, (int)Math.Round(p.Hp * (double)p.MaxHp / oldMaxHp, MidpointRounding.AwayFromZero));
            p.Hp = Math.Min(p.Hp, p.MaxHp);
            if (oldMaxMp > 0) p.Mp = (int)Math.Round(p.Mp * (double)p.MaxMp / oldMaxMp, MidpointRounding.AwayFromZero);
            p.Mp = Math.Min(p.Mp, p.MaxMp);
            if (oldMaxSp > 0) p.Sp = (int)Math.Round(p.Sp * (double)p.MaxSp / oldMaxSp, MidpointRounding.AwayFromZero);
            p.Sp = Math.Min(p.Sp, p.MaxSp);
            SendToMap(_world, p.Map, PacketBuilder.SendHp(i, p.Hp, p.MaxHp));
            SendToMap(_world, p.Map, PacketBuilder.SendMp(i, p.Mp, p.MaxMp));
            SendToMap(_world, p.Map, PacketBuilder.SendSp(i, p.Sp, p.MaxSp));
        }
    }

    private static double SnowFactor(bool snowing, double snowMultiplier) => snowing ? snowMultiplier : 1.0;

    /// <summary>Scale a live NPC's current HP/MP/SP and its damage-contribution ledgers by the Snow ratios.
    /// Ledger scaling (by hpRatio) preserves aggro ordering and EXP damage-share fractions.
    ///
    /// <para>Current vitals are held to both ends: capped at the NPC's effective max for the weather in
    /// force (<c>_world.Weather</c> already holds it by the time this runs), with a >= 1 floor on HP only —
    /// MP and SP may sit at 0. Rounding away from zero overfills without the cap.</para></summary>
    private void ScaleNpcVitalsForSnow(MapNpcRecord mn, double hpRatio, double mpRatio, double spRatio)
    {
        var npc = _world.Npcs[mn.Num];
        mn.Hp = Math.Min(Math.Max(1, (int)Math.Round(mn.Hp * hpRatio, MidpointRounding.AwayFromZero)), _world.EffectiveNpcMaxHp(npc));
        mn.Mp = Math.Min((int)Math.Round(mn.Mp * mpRatio, MidpointRounding.AwayFromZero), _world.EffectiveNpcMaxMp(npc));
        mn.Sp = Math.Min((int)Math.Round(mn.Sp * spRatio, MidpointRounding.AwayFromZero), _world.EffectiveNpcMaxSp(npc));
        var dmg = mn.DamageByPlayer;
        for (int i = 0; i < dmg.Length; i++)
            if (dmg[i] != 0) dmg[i] = (int)Math.Round(dmg[i] * hpRatio, MidpointRounding.AwayFromZero);
        if (mn.DamageByNpc is { } list)
        {
            for (int i = 0; i < list.Count; i++)
                list[i] = list[i] with { Damage = (int)Math.Round(list[i].Damage * hpRatio, MidpointRounding.AwayFromZero) };
        }
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

using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Regenerates HP/MP/SP for players.  Combat status supplants safe-zone status — if you're
/// in combat, the safe-zone cadence does not apply (combat is harsh wherever you are).
/// <list type="bullet">
///   <item>HP — every 2.5 s out of combat (1.25 s in safe zones), paused in combat.</item>
///   <item>MP/SP out of combat — every 2.5 s (1 s in safe zones).</item>
///   <item>MP/SP in combat — every 5 s regardless of map type (safe zone ignored).</item>
/// </list>
/// HP and the in-combat MP/SP tickers are independent so HP can regen faster without speeding up
/// combat MP/SP regen too.</summary>
public sealed class RegenerationSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly CombatSystem _combat;

    // HP regen — only fires when out of combat.  Fractional seconds are fine here (the ticker is
    // millisecond-comparison), so 1.25 s in safe zones is just 1_250 ms.  Normal-map HP at 2.5 s
    // matches the OOC MP/SP cadence; safe-zone HP keeps a 2× advantage over normal-map HP.
    private const long HpRegenIntervalMs = 2_500;
    private const long HpSafeRegenIntervalMs = 1_250;
    // MP/SP in-combat cadence — single rate regardless of safe zone (combat overrides the safe-zone
    // modifier; a player fighting inside a safe zone gets normal combat regen, not a buffed version).
    // 5 s matches the NPC tick rate, so casters can sustain spells at half the OOC throughput.
    private const long InCombatMpSpRegenIntervalMs = 5_000;
    // MP/SP out-of-combat fast cadence.
    private const long FastRegenIntervalMs = 2_500;
    private const long FastSafeRegenIntervalMs = 1_000;
    /// <summary>How long after a run step stamina stays held. Slightly over one tick of the fast cadence
    /// so a sustained sprint never slips a refund between steps, and short enough that stopping starts
    /// paying back within a step or two.</summary>
    private const long SprintRegenHoldMs = 3_000;
    private long _lastHpRegenTick;
    private long _lastHpSafeRegenTick;
    private long _lastInCombatMpSpRegenTick;
    private long _lastFastRegenTick;
    private long _lastFastSafeRegenTick;

    public RegenerationSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                              JoinLeaveSystem joinLeave, CombatSystem combat,
                              IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _world = world;
        _pm = pm;
        _joinLeave = joinLeave;
        _combat = combat;
        long now = Environment.TickCount64;
        _lastHpRegenTick = now;
        _lastHpSafeRegenTick = now;
        _lastInCombatMpSpRegenTick = now;
        _lastFastRegenTick = now;
        _lastFastSafeRegenTick = now;
    }

    public void Tick(long now)
    {
        bool hpRegenTick = now - _lastHpRegenTick >= HpRegenIntervalMs;
        if (hpRegenTick) _lastHpRegenTick = now;
        bool hpSafeRegenTick = now - _lastHpSafeRegenTick >= HpSafeRegenIntervalMs;
        if (hpSafeRegenTick) _lastHpSafeRegenTick = now;
        bool inCombatMpSpRegenTick = now - _lastInCombatMpSpRegenTick >= InCombatMpSpRegenIntervalMs;
        if (inCombatMpSpRegenTick) _lastInCombatMpSpRegenTick = now;
        bool fastRegenTick = now - _lastFastRegenTick >= FastRegenIntervalMs;
        if (fastRegenTick) _lastFastRegenTick = now;
        bool fastSafeRegenTick = now - _lastFastSafeRegenTick >= FastSafeRegenIntervalMs;
        if (fastSafeRegenTick) _lastFastSafeRegenTick = now;

        long nowUtc = NowUtc;

        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying || _pm[i].Char.Dead) continue;   // a corpse neither regenerates nor expires flags
            var p = _pm[i].Char;

            if (_pm[i].PkGraceUntilUtc > 0 && _pm[i].PkGraceUntilUtc <= nowUtc)
                _combat.BreakGrace(i);

            // Aggressor natural-expiry: catches the non-zero/now-passed transition that
            // IsAggressor returns false for. Clears + broadcasts so observers stop flashing.
            _combat.ExpireAggressorIfLapsed(i, now);

            bool inCombat = _pm[i].IsInCombat(now);

            if (!inCombat && _pm[i].WasInCombat)
            {
                _pm[i].WasInCombat = false;
                _pm[i].ClearDamageCredit();
                if (_pm[i].IsGhost)
                {
                    // Ghost's combat timer has expired — remove from world.
                    _joinLeave.ClearGhost(i);
                    continue;
                }
                SendMsg(i, ServerStrings.RegenerationSystem_CombatEnded, GameColor.BrightGreen);
            }

            bool inSafeZone = _world.MoralOf(p.Map) == MapMoral.Safe;
            bool hpTick = inSafeZone ? hpSafeRegenTick : hpRegenTick;
            // MP: combat status supplants safe-zone status — in combat, the single in-combat cadence
            // applies regardless of map type.  Out of combat, the safe-zone modifier kicks in for the
            // fast cadence.
            bool manaTick = inCombat
                ? inCombatMpSpRegenTick
                : (inSafeZone ? fastSafeRegenTick : fastRegenTick);

            // SP does not tick AT ALL while the player is spending it — not in combat, and not while a
            // sprint is still running. Stamina is the one vital whose costs are paid continuously (a run
            // is five to seven points a SECOND), so any rate large enough to feel like recovery would
            // refund a sprint as fast as it was spent. Gating the tick instead of shrinking it is what
            // lets the resting rate stay generous while the pool remains a real budget.
            bool sprinting = now - _pm[i].LastRunAt < SprintRegenHoldMs;
            bool staminaTick = !inCombat && !sprinting
                            && (inSafeZone ? fastSafeRegenTick : fastRegenTick);

            if (!hpTick && !manaTick && !staminaTick) continue;

            // Heat Wave / Snow halve regen magnitude (folded before the formula's round + floor).
            double regenMult = WeatherEffects.RegenMultiplier(_world.WeatherOn(p.Map));
            bool changed = false;
            if (!inCombat && hpTick && p.Hp < p.MaxHp)
            {
                p.Hp = Math.Min(p.Hp + StatFormulas.GetPlayerHpRegen(p, regenMult), p.MaxHp);
                changed = true;
            }
            if (manaTick && p.Mp < p.MaxMp)
            {
                p.Mp = Math.Min(p.Mp + StatFormulas.GetPlayerMpRegen(p, regenMult), p.MaxMp);
                changed = true;
            }
            if (staminaTick && p.Sp < p.MaxSp)
            {
                p.Sp = Math.Min(p.Sp + StatFormulas.GetPlayerSpRegen(p, regenMult), p.MaxSp);
                changed = true;
            }
            if (changed)
            {
                SendToMap(_world, p.Map, PacketBuilder.SendHp(i, p.Hp, p.MaxHp));
                SendToMap(_world, p.Map, PacketBuilder.SendMp(i, p.Mp, p.MaxMp));
                SendToMap(_world, p.Map, PacketBuilder.SendSp(i, p.Sp, p.MaxSp));
            }
        }
    }
}

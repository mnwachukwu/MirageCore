using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Stat and vitals formulas for players and NPCs: max pools, per-tick regen, and an NPC's
/// virtual level.  Each function has a primitive overload (raw stat ints) plus a record overload
/// that delegates.  Client previews and other call sites without a full record use the primitive
/// form.  All methods are pure functions — no side effects.
/// </summary>
public static class StatFormulas
{
    // ── Player max vitals ─────────────────────────────────────────────────────
    //
    // HP and MP use a shifted-quadratic shape `(X + 15)² / PoolDivisor` where X bundles level,
    // the player's stat, and the class stat.  The shift is what keeps the low end sane — a pure
    // `X²/K` would crater it (a fresh L=1 character would have ~5 HP) — and a single shifted
    // quadratic gives one continuous curve from L=1 to endgame with no slope change partway up.
    //
    // SP stays LINEAR: SP costs are percentage-of-pool (block/dodge/crit drain a fixed % of
    // max), so growing the pool just inflates absolute SP without changing events-per-fight.

    // Pool divisor — the smaller the divisor, the bigger the pool.  NPC HP uses the same divisor
    // to keep player/NPC HP ratios in step.
    private const double PoolDivisor = 15.0;

    // Shared low-end floor for shifted-quadratic pool/regen formulas.  NPCs use the SAME shift as
    // players — their missing "level" is supplied by NpcLevel, not a deeper shift.
    private const int VitalCurveShift = 15;

    // Weight of the player's own stat in the X-bundle for the SP pool.  Expressed as a double so
    // each odd-stat point shows up in the pool instead of vanishing until the next even value.
    private const double PlayerStatBundleWeight = 0.5;

    // DEF's weight in the HP pool, and Int's in the MP pool.  Lower than PlayerStatBundleWeight
    // because DEF is double-duty (HP + all mitigation), so a high-DEF bruiser's HP lead over a
    // glass build stays bounded.  Raise to widen that gap.
    private const double PlayerHpDefWeight = 0.22;

    // SP pool is linear in Spd; this doubles the X-bundle.
    private const int LinearSpPoolMultiplier = 2;

    // Percent → multiplier denominator.  Kept named so /100 isn't a literal in formula bodies.
    private const double PercentDenominator = 100.0;

    // TTK-feel multiplier on the HP pool (both players and NPCs).  A flat multiplier scales HP
    // symmetrically, lengthening TTK at every level while preserving win-rates and the level-growth
    // curve.  Applied to the combat HP pool only, NOT to the NPC base HP that kill-EXP reads, so
    // EXP rewards are unaffected.  Higher = longer fights.
    public const double HpPoolMultiplier = 1.5;

    public static int GetPlayerMaxHp(int level, int playerDef, int classDef) =>
        (int)Math.Round(PlayerMaxHpD(level, playerDef, classDef), MidpointRounding.AwayFromZero);

    // HP is a clean single-stat quadratic off DEF — no Str contribution, so the warrior gets no
    // survivability edge the caster can't match.
    private static double PlayerMaxHpD(int level, int playerDef, int classDef)
    {
        double x = level + playerDef * PlayerHpDefWeight + classDef;
        double shifted = x + VitalCurveShift;
        return shifted * shifted / PoolDivisor * HpPoolMultiplier;
    }

    // MP mirrors the HP pool at the same multiplier, so MP and HP sit on one scale rather than MP
    // dwarfing HP with a burst barely denting it.  A burst costs ~15-30% of MP (~4-8 casts): enough
    // to cover about one fight before running dry, and bursting drains it faster.
    public const double MpPoolMultiplier = HpPoolMultiplier;

    public static int GetPlayerMaxMp(int level, int playerInt, int classInt) =>
        (int)Math.Round(PlayerMaxMpD(level, playerInt, classInt), MidpointRounding.AwayFromZero);

    private static double PlayerMaxMpD(int level, int playerInt, int classInt)
    {
        // MP uses the same weight as HP (not SP's) so it mirrors the HP pool off Int — keeping MP on
        // HP's scale is what makes mana a resource the player actually feels.
        double x = level + playerInt * PlayerHpDefWeight + classInt;
        double shifted = x + VitalCurveShift;
        return shifted * shifted / PoolDivisor * MpPoolMultiplier;
    }

    public static int GetPlayerMaxSp(int level, int playerSpd, int classSpd) =>
        (int)Math.Round((level + playerSpd * PlayerStatBundleWeight + classSpd) * LinearSpPoolMultiplier, MidpointRounding.AwayFromZero);

    public static int GetPlayerMaxHp(PlayerRecord p, ClassRecord cls) =>
        GetPlayerMaxHp(p.Level, p.Def, cls.Def);

    public static int GetPlayerMaxMp(PlayerRecord p, ClassRecord cls) =>
        GetPlayerMaxMp(p.Level, p.Int, cls.Int);

    public static int GetPlayerMaxSp(PlayerRecord p, ClassRecord cls) =>
        GetPlayerMaxSp(p.Level, p.Spd, cls.Spd);

    /// <summary>Refresh the cached MaxHp/MaxMp/MaxSp fields on <paramref name="p"/> from the
    /// current formulas.  MaxHp/MaxMp/MaxSp are JsonIgnore-marked caches of the formula output —
    /// they are NEVER the source of truth.  Call this anywhere player level / stats / class
    /// change so the cache stays aligned with what the formula would produce.  Single seam: every
    /// callsite that mutates stats routes through here, so no drift is possible.</summary>
    public static void RefreshPlayerMaxVitals(PlayerRecord p, ClassRecord cls) =>
        RefreshPlayerMaxVitals(p, cls, WeatherType.Clear);

    /// <summary>Weather-aware refresh: Snow temporarily shrinks the max pools (HP -10%, MP/SP -20%).
    /// The multiplier scales the rounded base max, mirroring the Night NPC HP boost in
    /// <c>GameWorld.EffectiveNpcMaxHp</c>.  Any caller that mutates stats mid-Snow
    /// must pass the live weather so the cache reflects the reduced pool.</summary>
    public static void RefreshPlayerMaxVitals(PlayerRecord p, ClassRecord cls, WeatherType weather)
    {
        double hpM = weather == WeatherType.Snow ? Constants.WeatherSnowMaxHpMultiplier : 1.0;
        double mpM = weather == WeatherType.Snow ? Constants.WeatherSnowMaxMpMultiplier : 1.0;
        double spM = weather == WeatherType.Snow ? Constants.WeatherSnowMaxSpMultiplier : 1.0;
        p.MaxHp = (int)Math.Round(GetPlayerMaxHp(p, cls) * hpM, MidpointRounding.AwayFromZero);
        p.MaxMp = (int)Math.Round(GetPlayerMaxMp(p, cls) * mpM, MidpointRounding.AwayFromZero);
        p.MaxSp = (int)Math.Round(GetPlayerMaxSp(p, cls) * spM, MidpointRounding.AwayFromZero);
    }

    // ── Sub-potion vital exchange ─────────────────────────────────────────────
    // A Sub* potion drains one vital and pays into the other two. Paying a flat share of the DRAINED
    // AMOUNT into each is only coherent while every pool is the same size, and they are not: HP and MP
    // mirror each other, but SP is LINEAR and far smaller. At max level that makes the exchange run one
    // way and be worthless the other — a SubHp overflows a 901-point stamina bar while a SubSp pays under
    // 1% of a 12,675-point HP bar.
    //
    // Converting through POOL FRACTIONS instead makes it symmetric and pool-size agnostic: spending a
    // quarter of one bar buys an eighth of each of the others, whatever their absolute sizes, at every
    // level and for every build.

    /// <summary>Vital points a Sub* potion actually takes: its own <paramref name="vitalAmount"/>, or
    /// everything the player can spare if that is less.  0 means there is nothing to spend and the potion
    /// must be refused outright.
    ///
    /// <para>A short pour is fine and is paid for accordingly — <see cref="SubPotionGain"/> sizes the
    /// payout on what was DRAINED, not on the item, so spending 899 of a 3,000-point potion buys 899
    /// points' worth of exchange rather than the full one.</para>
    ///
    /// <para><b>HP reserves a point; MP and SP do not.</b>  A potion must never be lethal, and the reason
    /// is structural rather than cosmetic: player death is raised ONLY by the combat damage path, which
    /// sets <c>Dead</c> and zero HP together. Nothing sweeps for a zero-HP player anywhere else, so a
    /// drain that emptied the bar would leave a LIVE character standing at 0 — no corpse, no respawn,
    /// and regen quietly ticking them back up. Running dry on mana or stamina kills nobody and is
    /// allowed.</para></summary>
    public static int SubPotionDrain(int vitalAmount, int current, bool isHp) =>
        Math.Max(0, Math.Min(vitalAmount, current - (isHp ? 1 : 0)));

    /// <summary>Whether drinking would actually change anything — a full bar for an Add* potion, nothing
    /// left to spend for a Sub*. A sip that would do nothing costs neither the item nor the drinking
    /// clock, so the server reads this before spending either and the client reads it before starting
    /// its own sweep. One copy, because a client that guessed differently would either gray a usable
    /// slot or leave a dead one lit.</summary>
    public static bool PotionWouldDoSomething(ItemType type, int vitalAmount,
                                              int hp, int maxHp, int mp, int maxMp, int sp, int maxSp) => type switch
    {
        ItemType.PotionAddHp => hp < maxHp,
        ItemType.PotionAddMp => mp < maxMp,
        ItemType.PotionAddSp => sp < maxSp,
        ItemType.PotionSubHp => SubPotionDrain(vitalAmount, hp, isHp: true) > 0,
        ItemType.PotionSubMp => SubPotionDrain(vitalAmount, mp, isHp: false) > 0,
        ItemType.PotionSubSp => SubPotionDrain(vitalAmount, sp, isHp: false) > 0,
        _ => true,   // not a potion: nothing here has an opinion about it
    };

    /// <summary>What a Sub* potion pays into ONE of the other two vitals: the fraction of its own pool
    /// that was actually drained, times <see cref="Constants.SubPotionExchangePercent"/>%, applied to the
    /// RECEIVING pool.  Shared by the server's item-use path and the client's tooltip so the number a
    /// player is shown is the number they get.</summary>
    public static int SubPotionGain(int drained, int drainedMax, int targetMax)
    {
        if (drained <= 0 || drainedMax <= 0 || targetMax <= 0) return 0;
        double fraction = Math.Min(1.0, (double)drained / drainedMax);
        return (int)Math.Round(fraction * Constants.SubPotionExchangePercent / PercentDenominator * targetMax,
            MidpointRounding.AwayFromZero);
    }

    // ── Player regen ──────────────────────────────────────────────────────────
    // HP and MP regen use the SAME shifted-quadratic shape as the corresponding pool, scaled by
    // RegenDivisor.  That keeps `regen / pool` roughly constant at every level (~7-10% per tick)
    // rather than getting proportionally weaker as the quadratic pool grows.  Smaller divisor =
    // faster regen.
    //
    // SP regen stays LINEAR because the SP pool itself is linear (Spd × 2).  Matched shape: regen
    // and pool grow at the same rate, so % per tick stays constant by construction.
    //
    // Floors of 2 (player) and 1 (NPC) ensure 0-stat characters still get a tick at full strength.
    // A weather-REDUCED tick (mult < 1) instead floors at ReducedRegenFloor so the penalty can push
    // a low-stat regen below the normal floor without ever stalling to 0.

    private const double RegenDivisor = 100.0;
    // Spd / 4 — shared by player AND NPC SP regen. This is a RESTING rate and nothing else: stamina does
    // not tick while its owner is spending it (RegenerationSystem holds the tick through combat and
    // through a sprint), so the rate never has to be small enough to lose a race against a 5-7 point a
    // second sprint. It is sized for how long a bar should take to come back once you stop, and the pool
    // is what decides how long you could go in the first place.
    private const double SpRegenWeight = 0.25;
    private const int PlayerVitalRegenFloor = 2;
    // SP's own floor, separate from HP and MP's because it is the only one a player ever sits on: HP and
    // MP regen are quadratic in their stat and shifted, so even a stat of zero clears 2, while SP is
    // linear and starts at nothing. It is a comfort dial rather than a balance lever — what stops a
    // sprint being free is the held tick, not a small rate — so it is set by how long a build that
    // bought no SPD should wait, and raising it costs nothing anywhere else.
    private const int PlayerSpRegenFloor = 4;
    private const int NpcVitalRegenFloor = 1;
    private const int NpcMpRegenFloor = 2;
    private const int ReducedRegenFloor = 1;           // floor for a weather-reduced tick (all vital types)

    // The optional `mult` folds a weather regen modifier (Heat Wave / Snow = 0.5) into the magnitude
    // BEFORE the round + floor; see RoundRegen.
    // Regen scales with its pool's multiplier so the bigger pools refill at the SAME %-per-tick —
    // without that, larger pools would take proportionally longer to top off and resting would feel
    // worse.  SP is unscaled (linear pool, no multiplier).
    public static int GetPlayerHpRegen(int def, double mult = 1.0) =>
        RoundRegen(SquaredShifted(def, VitalCurveShift) / RegenDivisor * mult * HpPoolMultiplier, mult, PlayerVitalRegenFloor);
    public static int GetPlayerMpRegen(int @int, double mult = 1.0) =>
        RoundRegen(SquaredShifted(@int, VitalCurveShift) / RegenDivisor * mult * MpPoolMultiplier, mult, PlayerVitalRegenFloor);
    // SP carries its own pool's multiplier the way HP and MP carry theirs, so all three regens are the
    // stat times the vital's scale. The shape still differs because the POOLS differ: HP and MP are
    // shifted-quadratic and take a squared stat, SP is linear and takes a linear one.
    public static int GetPlayerSpRegen(int spd, double mult = 1.0) =>
        RoundRegen(spd * SpRegenWeight * mult * LinearSpPoolMultiplier, mult, PlayerSpRegenFloor);

    public static int GetPlayerHpRegen(PlayerRecord p, double mult = 1.0) => GetPlayerHpRegen(p.Def, mult);
    public static int GetPlayerMpRegen(PlayerRecord p, double mult = 1.0) => GetPlayerMpRegen(p.Int, mult);
    public static int GetPlayerSpRegen(PlayerRecord p, double mult = 1.0) => GetPlayerSpRegen(p.Spd, mult);

    private static double SquaredShifted(int x, int shift)
    {
        double s = x + shift;
        return s * s;
    }

    // Round + floor a per-tick regen magnitude.  At full strength (mult == 1) round to nearest (ties
    // away from zero) and apply the vital's normal floor.  When a weather modifier REDUCES the tick
    // (mult < 1, e.g. Heat Wave / Snow's 0.5) round DOWN so the penalty always bites, and drop to
    // ReducedRegenFloor so a reduced tick can dip below the normal floor — the Math.Max still keeps
    // every vital type at >= 1.
    private static int RoundRegen(double value, double mult, int normalFloor) =>
        mult < 1.0
            ? Math.Max((int)Math.Floor(value), ReducedRegenFloor)
            : Math.Max((int)Math.Round(value, MidpointRounding.AwayFromZero), normalFloor);

    // ── NPC max vitals ────────────────────────────────────────────────────────
    // Every pool clamps to a minimum of 2 so a 0-stat NPC still has a slot (no instant-death
    // 0-HP NPC, no 0-MP starve-on-spawn for an Int=0 mage spec, etc.).

    // Favor scales with the mob's own Str+Def, so lowering it shortens the fights that HAVE a big basis —
    // the upper bands — and barely moves a low-band mob, whose basis earns it almost none either way.
    private const int NpcHpFavorMaxPct = 10;   // max NPC HP favor over an equivalent player
    private const int NpcHpFavorScalingStat = 500;  // basis stat at which favor reaches the cap

    // An NPC has no earned level, so one is inferred from its point spread via NpcLevel (below) — a
    // SINGLE level shared by HP and MP so a lopsided stat spread is dampened exactly as a player's
    // level dampens it (an NPC's HP off Def and MP off Int can't run away from each other).

    private const int NpcMaxHpFloor = 2;
    private const int NpcMaxMpFloor = 2;
    private const int NpcMaxSpFloor = 2;

    /// <summary>NPC HP — a clean single-stat quadratic off DEF (no Str contribution, matching the player
    /// formula), times an NPC-only favor bump.  Str/Int are pure offense here just as they are for players.
    ///
    /// Shape (the PLAYER HP formula, with <see cref="NpcLevel"/> supplying the missing level):
    ///   baseHp = (npcLevel + 0.22·Def + 15)² / PoolDivisor
    ///   final  = baseHp × (1 + favor)                ← PvE-only tankiness bump, up to +<see cref="NpcHpFavorMaxPct"/>%
    ///
    /// Favor is the "slightly tougher than an equivalent player" bump.  Its ramp keys off total physical
    /// investment (Str + Def), which is orthogonal to the STR/INT offense mirror (players have no favor, and
    /// NPC-vs-NPC favor is symmetric), so it hands a Str-heavy NPC no advantage in the PvP-symmetry sense.
    ///
    /// <b>Tuning knobs:</b>
    /// - <see cref="NpcHpFavorMaxPct"/>: raise to make endgame NPCs tankier.
    /// - <see cref="NpcHpFavorScalingStat"/>: lower to make favor ramp faster to the cap; raise to keep
    ///   the low/mid tier closer to player parity.</summary>
    public static int GetNpcMaxHp(int str, int def, int @int, int spd, int extraHp = 0)
    {
        double baseHp = NpcBaseVitalD(NpcLevel(str, def, @int, spd), def);

        // Favor scales with total physical investment (Str + Def) so Str-heavy NPCs still see the
        // endgame tankiness bump — favor isn't a Def-only reward.
        double favorBasis = Math.Max(str, 0) + Math.Max(def, 0);
        double favorMaxFraction = NpcHpFavorMaxPct / PercentDenominator;
        double favor = Math.Min(favorMaxFraction, favorBasis * favorMaxFraction / NpcHpFavorScalingStat);
        // Same HpPoolMultiplier as the player, applied to the COMBAT pool only — GetNpcBaseHp stays
        // un-multiplied so NPC kill-EXP is unaffected.  Keeps player/NPC HP in step so PvE lengthens too.
        double favoredHp = baseHp * (1.0 + favor) * HpPoolMultiplier;

        // ExtraHp is a flat 1:1 designer add ON TOP of the stat pool (favor/multiplier don't touch it) — the
        // boss/wall lever (see NpcRecord.ExtraHp).  Added after the floor so a buffed statless NPC still gets it.
        return (int)Math.Max(Math.Round(favoredHp, MidpointRounding.AwayFromZero), NpcMaxHpFloor) + Math.Max(extraHp, 0);
    }

    /// <summary>The most stat value a character of <paramref name="level"/> may hold: the class's opening
    /// allotment plus every point levelling has granted since.  The inverse of <see cref="NpcLevel"/>.</summary>
    public static int PointBudgetForLevel(int level) =>
        Constants.PlayerBaseStatTotal + Constants.PointsPerLevel * (Math.Max(level, 1) - 1);

    /// <summary>Everything a character sheet holds against that budget — the four stats plus the points
    /// not yet spent.  A death drain can leave it under budget; nothing legitimate puts it over.</summary>
    public static int PointsHeld(int str, int def, int spd, int @int, int points) =>
        Math.Max(str, 0) + Math.Max(def, 0) + Math.Max(spd, 0) + Math.Max(@int, 0) + Math.Max(points, 0);

    /// <summary>Whether a character sheet is one the game itself could have produced.</summary>
    public static bool IsWithinPointBudget(int level, int str, int def, int spd, int @int, int points) =>
        PointsHeld(str, def, spd, @int, points) <= PointBudgetForLevel(level);

    /// <summary>An NPC's player-faithful virtual level, inferred from its point spread exactly as a player's
    /// level relates to theirs: an authored class starts at <see cref="Constants.PlayerBaseStatTotal"/> and each
    /// level grants <see cref="Constants.PointsPerLevel"/> more, so level = (statSum - 20)/3 + 1.  ALL FOUR stats
    /// count (SPD included): a fast NPC represents a higher-invested, higher-level character, tankier by its level
    /// floor exactly like a SPD-heavy player — SPD buys DURABILITY (the level floor feeds HP + mitigation), never
    /// damage.  This ONE level drives NPC vitals, mitigation, EXP, and the on-target readout — no separate
    /// "combat" level.  FLOORED to a whole number like a real player's level (a level-L player has exactly
    /// <see cref="Constants.PlayerBaseStatTotal"/> + 3·(L-1) points, so a stat spread maps to
    /// floor((statSum - 20)/3) + 1 — "level L until L+1 is fully earned"; no NPC ever sits at a fractional level).
    /// Clamped to >= 1 (a statless filler NPC reads level 1).</summary>
    public static int NpcLevel(int str, int def, int @int, int spd)
    {
        int statSum = Math.Max(str, 0) + Math.Max(def, 0) + Math.Max(@int, 0) + Math.Max(spd, 0);
        // Integer division floors for the normal (statSum >= 20) case; Max(1,..) covers sub-baseline statless NPCs.
        return Math.Max(1, (statSum - Constants.PlayerBaseStatTotal) / Constants.PointsPerLevel + 1);
    }

    public static int NpcLevel(NpcRecord npc) => NpcLevel(npc.Str, npc.Def, npc.Int, npc.Spd);

    /// <summary>Shared NPC vital-pool base — the PLAYER pool shape <c>(level + 0.22·stat + 15)² / PoolDivisor</c>
    /// with <paramref name="npcLevel"/> from <see cref="NpcLevel"/>.  Feeds HP (stat = Def) AND MP
    /// (stat = Int) so the two mirror each other exactly like the player's HP/MP.</summary>
    private static double NpcBaseVitalD(double npcLevel, int stat)
    {
        double shifted = npcLevel + Math.Max(stat, 0) * PlayerHpDefWeight + VitalCurveShift;
        return shifted * shifted / PoolDivisor;
    }

    // MP mirrors NPC HP off Int — same NpcBaseVitalD shape, shared NpcLevel, and MpPoolMultiplier, with no
    // favor.  The shared level bounds the MP/HP ratio for any stat spread, so no cap is needed: a high-Int
    // NPC's Int also raises its level, which lifts its HP, so MP can't dwarf HP.
    public static int GetNpcMaxMp(int str, int def, int @int, int spd) =>
        Math.Max((int)Math.Round(NpcBaseVitalD(NpcLevel(str, def, @int, spd), @int) * MpPoolMultiplier, MidpointRounding.AwayFromZero), NpcMaxMpFloor);

    // SP: linear at Spd × NpcSpPoolMultiplier.  Combat-neutral — block/crit/dodge each cost a PERCENTAGE of
    // max SP, so proc counts don't depend on pool size.  Its only real effect is RUN stamina: a chasing NPC
    // (or a kiting caster) runs until SP drains, then walks while rebuilding a reservoir before it may sprint
    // again.  NPC run speed is flat (SPD does not scale it — players outrun NPCs purely by speccing SPD), so
    // this pool is the only thing SPD buys an NPC: how LONG it can chase.  Higher = longer sustained chases.
    private const int NpcSpPoolMultiplier = 2;
    public static int GetNpcMaxSp(int spd) =>
        Math.Max(spd * NpcSpPoolMultiplier, NpcMaxSpFloor);

    public static int GetNpcMaxHp(NpcRecord npc) => GetNpcMaxHp(npc.Str, npc.Def, npc.Int, npc.Spd, npc.ExtraHp);
    public static int GetNpcMaxMp(NpcRecord npc) => GetNpcMaxMp(npc.Str, npc.Def, npc.Int, npc.Spd);
    public static int GetNpcMaxSp(NpcRecord npc) => GetNpcMaxSp(npc.Spd);

    // ── NPC regen ─────────────────────────────────────────────────────────────
    // Three regens, one per vital pool — same shape rules as the player:
    //   HP regen = max((Def+15)² / RegenDivisor, 1)  — quadratic, matches the NPC HP pool curve
    //   MP regen = max((Int+15)² / RegenDivisor, 2)  — quadratic, full parity with the player
    //   SP regen = max(Spd/2, 2)                     — linear, full parity with the player
    // Shared RegenDivisor with the player formulas above, so a single dial controls both sides'
    // %-per-tick.  NPCs tick on a slower cadence than players, so even at equal magnitudes per tick
    // they regen at roughly half the player's per-second rate.
    // Floors: 1 (HP) and 2 (MP/SP) — mage NPCs cycle MP continuously (cast → regen → cast) and need
    // the tighter floor; SP shares the player's floor of 2.

    public static int GetNpcHpRegen(int def, double mult = 1.0) =>
        RoundRegen(SquaredShifted(def, VitalCurveShift) / RegenDivisor * mult * HpPoolMultiplier, mult, NpcVitalRegenFloor);
    public static int GetNpcMpRegen(int @int, double mult = 1.0) =>
        RoundRegen(SquaredShifted(@int, VitalCurveShift) / RegenDivisor * mult * MpPoolMultiplier, mult, NpcMpRegenFloor);
    // SP regen is mirrored to the player: same weight, same pool multiplier AND same floor, so
    // GetNpcSpRegen == GetPlayerSpRegen at every Spd.
    public static int GetNpcSpRegen(int spd, double mult = 1.0) =>
        RoundRegen(spd * SpRegenWeight * mult * LinearSpPoolMultiplier, mult, PlayerSpRegenFloor);

    public static int GetNpcHpRegen(NpcRecord npc, double mult = 1.0) => GetNpcHpRegen(npc.Def, mult);
    public static int GetNpcMpRegen(NpcRecord npc, double mult = 1.0) => GetNpcMpRegen(npc.Int, mult);
    public static int GetNpcSpRegen(NpcRecord npc, double mult = 1.0) => GetNpcSpRegen(npc.Spd, mult);
}

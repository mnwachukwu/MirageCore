using Mirage.Shared.Records;

namespace Mirage.Shared;

public static class ExpFormulas
{
    private const double TnlConstant = 500.0;
    private const double TnlExponent = 2.0;

    // TNL grows as `level^TnlExponent × TnlConstant`.  With a linear-stat-sum EXP curve and
    // stat budgets growing ~linearly with player level, kills/level scales as L^(TnlExponent − 1):
    //   1.5  → √L growth  (kills cap in the low hundreds at L=255 — pre-rebalance)
    //   2.0  → linear in L ("thousands at endgame")
    //   2.5  → L^1.5 growth (tens of thousands at endgame; brutal grind)
    // L=1 always costs exactly TnlConstant because 1^anything = 1 — so low-level pacing is
    // unaffected by bumping the EXPONENT; only mid-late game gets the "modern MMO" grind shape.
    //
    // THE TWO KNOBS DO DIFFERENT JOBS and it is easy to reach for the wrong one.  The exponent
    // BENDS the curve — it changes how the road is distributed across levels.  The constant SCALES
    // it: TNL is linear in TnlConstant, so kills/level and total wall-clock scale with it exactly
    // and nothing else moves.  Time-to-kill, the share of the road below level 127, and "reaching
    // 100 is ~6% of the way to 255" are all identical at any constant.  So the constant is the
    // right knob for wall-clock — and mob stats are NOT, because they also feed ExpForKill and
    // expected TTK and would move three things while you were aiming at one.
    //
    // The constant is sized for wall-clock: 500 puts the road to 255 at about 3,741h of pure killing
    // against the level-matched bestiary.  Re-measure with .Tools/Simulations/ExpCurve/exp-curve.cs,
    // which takes --matched and --tnl=N.
    public static long TnlForLevel(int level) =>
        (long)Math.Round(Math.Pow(level, TnlExponent) * TnlConstant, MidpointRounding.AwayFromZero);

    // _expFloor[L] = total EXP required to enter level L (sum of TNL(1..L-1))
    private static readonly long[] _expFloor = BuildTable();
    public static readonly long MaxTotalExp;

    static ExpFormulas()
    {
        MaxTotalExp = _expFloor[Constants.MaxLevel + 1];
    }

    private static long[] BuildTable()
    {
        var t = new long[Constants.MaxLevel + 2]; // indices 0..256; index 0 unused
        t[1] = 0;
        for (int L = 2; L <= Constants.MaxLevel + 1; L++)
            t[L] = t[L - 1] + TnlForLevel(L - 1);
        return t;
    }

    public static long ExpFloorForLevel(int level)
    {
        if (level <= 1) return 0L;
        if (level > Constants.MaxLevel) return _expFloor[Constants.MaxLevel + 1];
        return _expFloor[level];
    }

    // ── NPC kill EXP (PLAYER-RELATIVE) ─────────────────────────────────────────
    // EXP for a kill = ToughnessExpScale × expectedTtk × danger — BOTH measured against the killing player, so the
    // reward tracks how hard THAT fight was for THEM, not the mob's absolute stats:
    //   - expectedTtk = mob HP ÷ your normal per-hit.  A DETERMINISTIC number from stats (NOT the observed swing
    //     count), so crits/blocks change how the fight plays out but never the payout.  A mob you out-level and
    //     one-shot yields ~1 → tiny EXP: no farming trivial mobs, and no separate "gap tier" is needed.
    //   - danger = 1 + DangerWeight × (its best hit on YOU ÷ your max HP), clamped — the risk premium.  Offense is
    //     rewarded, but only as threat-to-YOU (a mob far below you is no threat → no bonus), so you can't farm safe
    //     mobs OR trivial ones.  A dangerous glass cannon pays the premium; it can also kill you, so it's earned.
    // EXP-per-time therefore lands at ~flat baseline (ToughnessExpScale) × danger — the only farmable edge is
    // fighting genuinely dangerous content, which is the healthy incentive.  ExtraHp rides in via the mob's HP, so
    // a boss's inflated pool lengthens the fight and is paid for automatically.  A shared kill scales each player's
    // solo value by their damage share (each earns their own rate for the slice of the mob they personally killed).
    //
    // Tuning: ToughnessExpScale = overall magnitude (pace vs the TNL curve); DangerWeight/DangerMax = risk premium.
    public const int ToughnessExpScale = 70;
    public const double DangerWeight = 5.0;
    public const double DangerMax = 3.0;

    /// <summary>Player-relative EXP for ONE solo kill.  All six inputs are plain numbers so this stays a pure,
    /// RNG-free function: the same (player, mob) always yields the same EXP.  <paramref name="mobHit"/> is the
    /// mob's best raw attack (<see cref="NpcBestHit"/>), <paramref name="playerHit"/> the killer's normal swing.
    /// A shared kill multiplies the result by the killer's damage-share fraction.</summary>
    public static int ExpForKill(int mobHp, int mobMit, int mobHit, int playerHit, int playerMit, int playerMaxHp)
    {
        // Toughness = the EXPECTED fight length vs this attacker (computed from stats, NOT the observed swing
        // count — so crits/blocks never touch the reward).  A mob you one-shot yields ~1 → near-zero EXP.
        // perHit uses the PvE floor (player-hits-NPC) so a floored hybrid's expectedTtk matches the real, shorter
        // fight — else EXP over-pays it for a slog the floor prevents.  A pure never floors, so it's unaffected.
        double perHit = Math.Max(playerHit - mobMit, playerHit * CombatFormulas.PveMinDamageFloorPercent);
        double expectedTtk = Math.Max(mobHp, 1) / Math.Max(perHit, 1.0);
        // Danger = how big a bite the mob takes out of YOU per hit (its threat = the risk you took on).  Fold in
        // the NPC-vs-player disfavor: a mob lands only NpcVsPlayerDamageMultiplier of its post-mit hit
        // (CombatSystem.ApplyNpcDamageToPlayer), so the danger it's priced on must be softened to match — else EXP
        // over-pays for a threat the mob no longer poses.
        double toYou = Math.Max(mobHit - playerMit, mobHit * CombatFormulas.MinDamageFloorPercent) * Constants.NpcVsPlayerDamageMultiplier;
        double danger = Math.Clamp(1.0 + DangerWeight * (toYou / Math.Max(playerMaxHp, 1)), 1.0, DangerMax);
        return (int)Math.Max(Math.Round(ToughnessExpScale * expectedTtk * danger, MidpointRounding.AwayFromZero), 1);
    }

    /// <summary>The mob's best raw attack — melee OR spell, whichever threatens more — for the danger term.</summary>
    public static int NpcBestHit(NpcRecord npc) =>
        Math.Max(CombatFormulas.NpcMeleeBaseDamage(npc.Str), CombatFormulas.NpcSpellBaseMagnitude(npc.Int));

    // Editor-preview reference: a TYPICAL player of a level spends its whole point budget evenly on STR and DEF
    // (a balanced melee bruiser), wearing matched gear.  Preview-only — live EXP always uses the real killer.
    private static int ReferenceStat(int level) =>
        Math.Max(Constants.PlayerBaseStatTotal + Constants.PointsPerLevel * (level - 1), Constants.PlayerBaseStatTotal) / 2;

    /// <summary>Editor estimate: the EXP this NPC would give a typical evenly-invested STR/DEF player of
    /// <paramref name="playerLevel"/> (matched weapon + armor).  Runs the exact <see cref="ExpForKill"/> the live
    /// award uses, just fed a synthetic reference player instead of a real one — so the editor can preview the
    /// reward across the level band the designer intends to place the mob in.</summary>
    public static int EstimatedExpVsLevel(int str, int def, int @int, int spd, int extraHp, int playerLevel)
    {
        int s = ReferenceStat(playerLevel);   // STR = DEF = half the level's budget; INT/SPD = 0
        int playerHit = CombatFormulas.NpcMeleeBaseDamage(s);                          // UnarmedDamage(Str) + matched weapon
        int playerMit = CombatFormulas.PlayerProtection(playerLevel, s)
                        + 2 * CombatFormulas.GearMitigation(s, s) + CombatFormulas.ShieldMitigation(s, s);  // matched armor+helmet+shield
        int playerMaxHp = StatFormulas.GetPlayerMaxHp(playerLevel, s, 0);
        int mobHit = Math.Max(CombatFormulas.NpcMeleeBaseDamage(str), CombatFormulas.NpcSpellBaseMagnitude(@int));
        return ExpForKill(StatFormulas.GetNpcMaxHp(str, def, @int, spd, extraHp), CombatFormulas.NpcProtection(str, def, @int, spd),
                          mobHit, playerHit, playerMit, playerMaxHp);
    }

    // The trivial/strong gap tier was RETIRED: the player-relative ExpForKill above self-nullifies a mob you
    // out-level (you one-shot it → expectedTtk ~1 → near-zero EXP), so a separate below/above-level multiplier is
    // pure redundancy now.  (The on-target strength readout in PacketHandler is separate flavor text and stays.)
    private const double PercentDenominator = 100.0;   // shared by the party-kill bonus below

    /// <summary>Partner kill bonus — EXP awarded when your party partner contributes damage
    /// to a kill.  Within <see cref="PartyLevelGap"/> levels: pays <see cref="PartnerKillBonusPercent"/>%
    /// of the partner's <b>base</b> contribution EXP (pre-<see cref="PartyExpBonus"/>, so the
    /// contributor multiplier doesn't compound into the partner kill bonus).  Paid to EVERY
    /// in-band party member whose partner contributed — active co-fighters AND a passive
    /// partner who dealt no damage — so there's no perverse incentive to underperform for a
    /// bonus cut.  Outside the gap, or when the partner's base is 0 (no contribution, or integer
    /// math rounded their share to 0), returns 0 — caller skips the gain message.  Max-level
    /// contributors do generate a theoretical base, so a low-level partner still gets the bonus
    /// when a max-level friend lands the kill.</summary>
    public static long PartnerKillBonus(int selfLevel, int partnerLevel, long partnerBaseExp)
    {
        int gap = Math.Abs(selfLevel - partnerLevel);
        if (gap > PartyLevelGap) return 0;
        return (long)Math.Round(partnerBaseExp * PartnerKillBonusPercent / PercentDenominator, MidpointRounding.AwayFromZero);
    }

    /// <summary>Max self↔partner level gap for any party reward.  Within the gap: each
    /// contributor gets <see cref="PartyExpBonus"/>× on their own contribution EXP, and each
    /// party member gets <see cref="PartnerKillBonusPercent"/>% of the partner's base
    /// contribution (via <see cref="PartnerKillBonus"/>).  Outside the gap: both rewards are
    /// zero, closing the warm-body/dual-box exploit (a parked far-off-level alt nets the
    /// high-level main no extra EXP).</summary>
    public const int PartyLevelGap = 5;

    /// <summary>Share of partner's base contribution paid as a partner kill bonus to each
    /// in-band party member.  See <see cref="PartnerKillBonus"/>.</summary>
    private const int PartnerKillBonusPercent = 25;

    /// <summary>Multiplier on a contributor's own contribution EXP when their party partner
    /// is on the same map AND within <see cref="PartyLevelGap"/> levels.  Gap-gated to keep
    /// the bonus strictly a same-level partying incentive.</summary>
    public const double PartyExpBonus = 1.2;

    /// <summary>Per-weather EXP reward multiplier. Non-Clear weathers grant a bonus that compounds
    /// multiplicatively with the Night boost and the party bonus. Clear = 1.0 (no change).</summary>
    public static double WeatherExpMultiplier(WeatherType weather) => weather switch
    {
        WeatherType.Rain => Constants.WeatherRainExpMultiplier,
        WeatherType.HeatWave => Constants.WeatherHeatWaveExpMultiplier,
        WeatherType.Snow => Constants.WeatherSnowExpMultiplier,
        WeatherType.HeavyWind => Constants.WeatherHeavyWindExpMultiplier,
        _ => 1.0,
    };

    // ── Death-penalty EXP loss ─────────────────────────────────────────────────
    // A normal death costs Exp/10.  PK victims pay double.  Caller takes
    // Math.Min vs the player's current Exp, since loss can exceed it.

    private const double NormalDeathLossDivisor = 10.0;
    private const int PkDeathLossMultiplier = 2;

    /// <summary>Normal death penalty: TNL/10.</summary>
    public static long DeathExpLossNormal(int level) =>
        (long)Math.Round(TnlForLevel(level - 1) / NormalDeathLossDivisor, MidpointRounding.AwayFromZero);

    /// <summary>PK-flagged death penalty: 2× normal.</summary>
    public static long DeathExpLossPk(int level) =>
        (long)Math.Round(TnlForLevel(level - 1) * PkDeathLossMultiplier / NormalDeathLossDivisor, MidpointRounding.AwayFromZero);
}

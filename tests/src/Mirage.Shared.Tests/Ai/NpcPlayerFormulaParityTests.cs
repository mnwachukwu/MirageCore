using NUnit.Framework;

namespace Mirage.Shared.Tests.Ai;

// NPC ↔ PLAYER FORMULA PARITY.
//
// The combat redesign is a "symmetric mirror": an NPC is meant to be another actor running the SAME
// vital / offense / defense / regen math as a player at a matched virtual level (StatFormulas.NpcLevel),
// with only a short, DELIBERATE list of PvE-tuning asymmetries. This fixture is the single source of
// truth for "mirrored vs intentionally different": a failing test means a future edit silently diverged
// the two sides, and we must either fix the divergence or move the case into the documented-asymmetry
// half on purpose.
//
// Everything here is a pure public static in Mirage.Shared, so no GameWorld / reflection is needed —
// the formulas are called directly and compared across a stat/level sweep.
[TestFixture]
public class NpcPlayerFormulaParityTests
{
    // Representative sweep: 0 (floors), low, mid, and endgame (cap 255).
    static readonly int[] Sweep = { 0, 1, 5, 10, 30, 60, 100, 150, 200, 255 };

    // ─────────────────────────────────────────────────────────────────────────
    //  A. PARITY THAT MUST HOLD  (player == NPC — the mirror)
    // ─────────────────────────────────────────────────────────────────────────

    // HP regen shares one body: RoundRegen(SquaredShifted(def,15)/100 * mult * HpPoolMultiplier, ...).
    // The only nominal difference is the floor (player 2, NPC 1), but the raw is >= 3.375 at def=0 and
    // only climbs, so the floor never binds and the two are equal for every def.
    [Test]
    public void HpRegen_PlayerEqualsNpc()
    {
        foreach (int def in Sweep)
        {
            Assert.That(StatFormulas.GetNpcHpRegen(def), Is.EqualTo(StatFormulas.GetPlayerHpRegen(def)),
                $"HP regen must mirror at def={def}");
        }
    }

    // MP regen shares the body AND the floor (both 2) — full parity by construction. This is the exact
    // formula the Part-2 investigation confirmed is mirrored (the caster "why so regenerative" answer was
    // the POOL, via classInt — see the asymmetry test — never the regen amount).
    [Test]
    public void MpRegen_PlayerEqualsNpc()
    {
        foreach (int i in Sweep)
        {
            Assert.That(StatFormulas.GetNpcMpRegen(i), Is.EqualTo(StatFormulas.GetPlayerMpRegen(i)),
                $"MP regen must mirror at int={i}");
        }
    }

    // SP regen is MIRRORED too: player and NPC both use Spd/2 with floor 2,
    // so they are equal at every Spd — including the low 0-2 band where the floor binds. (Was a documented
    // asymmetry; moved into the parity half when SP regen was unified.)
    [Test]
    public void SpRegen_PlayerEqualsNpc()
    {
        foreach (int spd in Sweep)
        {
            Assert.That(StatFormulas.GetNpcSpRegen(spd), Is.EqualTo(StatFormulas.GetPlayerSpRegen(spd)),
                $"SP regen must mirror at spd={spd}");
        }
    }

    // STR and INT are the SAME offense stat: unarmed base off Str is bit-for-bit spell power off Int.
    [Test]
    public void OffenseBase_UnarmedEqualsSpellPower()
    {
        foreach (int s in Sweep)
        {
            Assert.That(CombatFormulas.SpellPower(s), Is.EqualTo(CombatFormulas.UnarmedDamage(s)),
                $"spell power (Int) must equal unarmed (Str) at stat={s}");
        }
    }

    // The gear contribution mirrors too: a weapon's Power pulls the same weight off Str as a spell's
    // VitalAmount does off Int (both 2x GearMitigation), so weapon and spell scale alike.
    [Test]
    public void OffenseGear_WeaponEqualsSpellContribution()
    {
        foreach (int data in Sweep)
        {
            foreach (int stat in Sweep)
            {
                Assert.That(CombatFormulas.SpellContribution(data, stat), Is.EqualTo(CombatFormulas.WeaponContribution(data, stat)),
                    $"spell VitalAmount contribution must equal weapon Power contribution at data={data}, stat={stat}");
            }
        }
    }

    // The full NPC offense composition is the player's: NPC melee base = UnarmedDamage(Str)+WeaponContribution(Str,Str)
    // and NPC spell base = SpellPower(Int)+SpellContribution(Int,Int) — identical curves off their respective stat.
    [Test]
    public void OffenseComposition_NpcMeleeMirrorsNpcSpell_AtEqualStat()
    {
        foreach (int stat in Sweep)
        {
            int meleeBase = CombatFormulas.UnarmedDamage(stat) + CombatFormulas.WeaponContribution(stat, stat);
            int spellBase = CombatFormulas.SpellPower(stat) + CombatFormulas.SpellContribution(stat, stat);
            Assert.That(spellBase, Is.EqualTo(meleeBase), $"NPC spell base must mirror NPC melee base at stat={stat}");
        }
    }

    // Vital-pool base SHAPE: player and NPC both use (level + 0.22*stat + 15)^2 / 15 * 1.5, with the NPC's
    // missing level supplied by NpcLevel. MP carries NO favor, so it is the clean witness of the shared base:
    // an NPC's MP equals a classInt-free player's MP at level == NpcLevel, exactly.
    [Test]
    public void VitalPoolBase_NpcMpEqualsPlayerMp_AtNpcLevel()
    {
        foreach (int s in Sweep)
        {
            foreach (int i in Sweep)
            {
                int nl = StatFormulas.NpcLevel(s, s, i, s);   // balanced-ish spread → a definite NpcLevel
                Assert.That(StatFormulas.GetNpcMaxMp(s, s, i, s),
                    Is.EqualTo(StatFormulas.GetPlayerMaxMp(nl, i, 0)),
                    $"NPC MP pool must equal a classInt-free player's at level==NpcLevel (str/def/spd={s}, int={i}, NpcLevel={nl})");
            }
        }
    }

    // Mitigation BASE curve: strip the NPC's baked-in gear (def=0 → the armor/helmet/shield chips are 0) and
    // the NPC's protection is exactly the player's level-primary curve at level == NpcLevel.
    [Test]
    public void MitigationBase_NpcEqualsPlayer_AtNpcLevel_GearStripped()
    {
        foreach (int s in Sweep)
        {
            foreach (int i in Sweep)
            {
                int nl = StatFormulas.NpcLevel(s, 0, i, s);   // def=0 zeroes every gear chip in NpcProtection
                Assert.That(CombatFormulas.NpcProtection(s, 0, i, s),
                    Is.EqualTo(CombatFormulas.PlayerProtection(nl, 0)),
                    $"gear-stripped NPC mit must equal the player base curve at level==NpcLevel (str/spd={s}, int={i}, NpcLevel={nl})");
            }
        }
    }

    // The SP reaction-cost FORMULA is shared (one function each): block/crit = 2% of MaxSp, dodge = 4%.
    // Only the POOL the % is taken of differs (see the SP-pool asymmetry) — the cost rule itself is mirrored.
    [Test]
    public void SpReactionCost_SharedFormula()
    {
        foreach (int maxSp in new[] { 10, 50, 100, 300, 500 })
        {
            Assert.That(CombatFormulas.SpCostForBlockOrCrit(maxSp), Is.EqualTo(System.Math.Max((int)System.Math.Round(maxSp * 0.02, System.MidpointRounding.AwayFromZero), 1)));
            Assert.That(CombatFormulas.SpCostForDodge(maxSp), Is.EqualTo(System.Math.Max((int)System.Math.Round(maxSp * 0.04, System.MidpointRounding.AwayFromZero), 1)));
        }
    }

    /// <summary>What the price actually buys, which is the figure worth tuning: a full pool is fifty
    /// blocks or twenty-five dodges, at every level. The percentage on its own says nothing, because the
    /// pool it is taken from grows with level and so does the cost.</summary>
    [Test]
    public void AFullPool_BuysFiftyBlocksOrTwentyFiveDodges()
    {
        foreach (int maxSp in new[] { 100, 300, 500, 900 })
        {
            Assert.That(maxSp / CombatFormulas.SpCostForBlockOrCrit(maxSp), Is.EqualTo(50), $"blocks at {maxSp}");
            Assert.That(maxSp / CombatFormulas.SpCostForDodge(maxSp), Is.EqualTo(25), $"dodges at {maxSp}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  B. DELIBERATE ASYMMETRIES  (player != NPC — each with the reason it differs)
    //  A change here failing means the intended difference moved; re-confirm it's on purpose.
    // ─────────────────────────────────────────────────────────────────────────

    // SP POOL is STRUCTURALLY different: player = (level + Spd/2 + classSpd)*2 = 2*level + Spd (has a LEVEL
    // term, Spd weighted x1); NPC = Spd*2 (no level term, Spd weighted x2). Neither strictly dominates (they even
    // tie at level==Spd/2) — the point is the SHAPE differs: an NPC's SPD buys only run DURATION, un-inflated by a
    // virtual level, while a player's SP scales with level.
    [Test]
    public void Asymmetry_SpPool_PlayerHasLevelTerm_NpcDoesNot()
    {
        foreach (int spd in new[] { 10, 30, 100 })
        {
            Assert.That(StatFormulas.GetNpcMaxSp(spd), Is.EqualTo(System.Math.Max(spd * 2, 2)),
                $"NPC SP pool is a flat Spd*2 at spd={spd}");
            // The player pool responds to LEVEL at fixed Spd; the NPC pool (no level parameter) cannot.
            Assert.That(StatFormulas.GetPlayerMaxSp(200, spd, 0), Is.GreaterThan(StatFormulas.GetPlayerMaxSp(1, spd, 0)),
                $"player SP pool rises with level at spd={spd} (the level term the NPC lacks)");
        }
    }

    // NPC HP FAVOR: an NPC is deliberately a bit tankier than the equivalent player — up to +30% HP, ramped by
    // physical investment (Str+Def). So NPC HP >= the player base at NpcLevel, strictly greater once Str+Def>0.
    [Test]
    public void Asymmetry_NpcHpFavor_MakesNpcTankier()
    {
        // Statless (Str=Def=0) → favor 0 → exactly the player base (the boundary case).
        int nl0 = StatFormulas.NpcLevel(0, 0, 0, 0);
        Assert.That(StatFormulas.GetNpcMaxHp(0, 0, 0, 0), Is.EqualTo(StatFormulas.GetPlayerMaxHp(nl0, 0, 0)),
            "with no physical investment the favor is 0, so NPC HP equals the player base");
        // With physical investment the favor bump lifts NPC HP strictly above the player base at NpcLevel.
        foreach (int d in new[] { 30, 60, 100, 200 })
        {
            int nl = StatFormulas.NpcLevel(d, d, 0, 0);
            Assert.That(StatFormulas.GetNpcMaxHp(d, d, 0, 0), Is.GreaterThan(StatFormulas.GetPlayerMaxHp(nl, d, 0)),
                $"NPC HP favor must make it tankier than the player base at str=def={d}");
        }
    }

    // NPC MITIGATION carries a fully-kitted defender's gear baked in (armor+helmet full + shield 1/4) since NPCs
    // wear none — so at def>0 an NPC out-mitigates the bare player base curve at the same NpcLevel.
    [Test]
    public void Asymmetry_NpcMitigation_HasBakedGear()
    {
        foreach (int d in new[] { 10, 30, 100, 200 })
        {
            int nl = StatFormulas.NpcLevel(0, d, 0, 0);
            Assert.That(CombatFormulas.NpcProtection(0, d, 0, 0), Is.GreaterThan(CombatFormulas.PlayerProtection(nl, d)),
                $"NPC baked-in gear must add mit over the bare player base at def={d}");
        }
    }

    // NEGATION: player chance includes a LEVEL term with larger divisors and higher caps; NPC chance is
    // stat-only with smaller divisors and lower caps (NPC DEF also drives HP/EXP, so its negation is reined).
    [Test]
    public void Asymmetry_Negation_PlayerLevelScaled_HigherCaps()
    {
        // Caps: player block/crit/spellcrit 35, dodge 15; NPC block/crit 25, dodge 10.
        Assert.That(CombatFormulas.PlayerBlockChancePerMille(9999, 9999), Is.EqualTo(35));
        Assert.That(CombatFormulas.PlayerDodgeChancePerMille(9999, 9999), Is.EqualTo(15));
        Assert.That(CombatFormulas.PlayerCriticalChancePerMille(9999, 9999), Is.EqualTo(35));
        Assert.That(CombatFormulas.SpellCriticalChancePerMille(9999, 9999), Is.EqualTo(35));
        Assert.That(CombatFormulas.NpcBlockChancePerMille(9999), Is.EqualTo(25));
        Assert.That(CombatFormulas.NpcDodgeChancePerMille(9999), Is.EqualTo(10));
        Assert.That(CombatFormulas.NpcCriticalChancePerMille(9999), Is.EqualTo(25));
        Assert.That(CombatFormulas.NpcSpellCriticalChancePerMille(9999), Is.EqualTo(25));
        // Player chance rises with LEVEL at fixed stat; NPC chance ignores level entirely.
        Assert.That(CombatFormulas.PlayerBlockChancePerMille(50, 200), Is.GreaterThan(CombatFormulas.PlayerBlockChancePerMille(50, 0)),
            "player negation scales with level");
        Assert.That(CombatFormulas.NpcSpellCriticalChancePerMille(50), Is.EqualTo(CombatFormulas.NpcCriticalChancePerMille(50)),
            "NPC spell-crit mirrors NPC melee-crit (both off their offense stat, same NPC dials)");
    }

    // CLASS BONUS: a player's vital pool gets a class-stat head-start (classInt/classDef/classSpd in the pool
    // input); an NPC has NO class, so a same-Int NPC's pool is smaller BY DESIGN. This is the exact thing that
    // made NPC casters look "too regenerative" (the shared regen is a bigger % of the smaller NPC pool).
    [Test]
    public void Asymmetry_PlayerClassPoolBonus_HasNoNpcAnalog()
    {
        // A Mage-like classInt head-start enlarges the player MP pool; the NPC formula has no class parameter.
        Assert.That(StatFormulas.GetPlayerMaxMp(14, 40, 13), Is.GreaterThan(StatFormulas.GetPlayerMaxMp(14, 40, 0)),
            "classInt enlarges the player MP pool");
        Assert.That(StatFormulas.GetPlayerMaxHp(14, 40, 13), Is.GreaterThan(StatFormulas.GetPlayerMaxHp(14, 40, 0)),
            "classDef enlarges the player HP pool");
    }
}

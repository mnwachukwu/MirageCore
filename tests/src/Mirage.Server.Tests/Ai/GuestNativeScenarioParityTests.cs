using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

// GUEST ↔ NATIVE SCENARIO PARITY (runtime layer).
//
// The structural suite (GuestNativeNpcParityTests) proves a guest IS a native record at the data level.
// This one goes further: it stands up a REAL NpcAiSystem (real CombatSystem + MovementSystem + BloodSystem,
// only the packet dispatcher stubbed to a no-op — the heavy sub-systems a chase STEP never touches are left
// null), places a native and an equivalent guest in the SAME situation in separate fresh worlds, runs the
// actual fast movement pass, and asserts the resulting movement is bit-identical. This is the end-to-end
// guard for the ### 7 class of bug (a code path that stepped natives but not guests, or at a different pace).
[TestFixture]
public class GuestNativeScenarioParityTests
{
    const int Map = 1, NpcNum = 1, TargetIdx = 5;

    // Resulting movement state we compare between a native and a guest run.
    readonly record struct StepOutcome(int X, int Y, Direction Dir, MovementType MoveType, double Sp, bool Sprinting);

    // ── The scenarios: a chasing NPC whose player target sits some distance away, one movement tick ──

    // (MoveType is a one-shot the stepper sets during the broadcast then FinishChaseStep resets to Walking, so a
    //  post-tick read always shows Walking; run-vs-walk is detected by SP drain + the ChaseSprinting latch instead.)

    // Opening approach (PRE-contact), target spotted AT the stroll ceiling (NpcApproachWalkMaxGap tiles): an AoS mob
    // that hasn't rolled a charge STROLLS in at a walk — the close-range menace a player can slip past — guest and
    // native identically.  A target one tile farther flips it to a rush (next test).
    [Test]
    public void OpeningApproach_StrollsWhenTargetWithinCeiling_GuestMatchesNative()
    {
        int ceilingY = 6 + Constants.NpcApproachWalkMaxGap;   // gap == the stroll ceiling → still a walk
        var native = RunChaseStep(guest: false, npcX: 8, npcY: 6, targetX: 8, targetY: ceilingY, hasContact: false, sprinting: false);
        var guest = RunChaseStep(guest: true, npcX: 8, npcY: 6, targetX: 8, targetY: ceilingY, hasContact: false, sprinting: false);
        Assert.That(guest, Is.EqualTo(native), "a guest must stroll the opening approach exactly like a native");
        Assert.That(native.Y, Is.EqualTo(7), "the mob strolls one tile toward the target");
        Assert.That(native.Sprinting, Is.False, "a target within the stroll ceiling -> walk, no charge latch set");
        Assert.That(native.Sp, Is.EqualTo(20), "a stroll drains no SP");
    }

    // Opening approach (PRE-contact), target spotted just PAST the stroll ceiling (ceiling + 1): the AoS mob RUSHES
    // the opening gap instead of strolling, latching the charge (ChaseSprinting) so it commits to melee.  Covers
    // both "spotted far → rush" and "a stalked target opens the gap past the ceiling → the walk becomes a charge".
    [Test]
    public void OpeningApproach_RushesWhenTargetPastCeiling_GuestMatchesNative()
    {
        int pastCeilingY = 6 + Constants.NpcApproachWalkMaxGap + 1;   // one tile past the ceiling → rush/charge
        var native = RunChaseStep(guest: false, npcX: 8, npcY: 6, targetX: 8, targetY: pastCeilingY, hasContact: false, sprinting: false);
        var guest = RunChaseStep(guest: true, npcX: 8, npcY: 6, targetX: 8, targetY: pastCeilingY, hasContact: false, sprinting: false);
        Assert.That(guest, Is.EqualTo(native), "a guest must rush the opening approach exactly like a native");
        Assert.That(native.Y, Is.EqualTo(7), "the mob steps one tile toward the target");
        Assert.That(native.Sprinting, Is.True, "a target past the stroll ceiling -> charge, latched to melee");
        Assert.That(native.Sp, Is.EqualTo(19), "a rush-step drains 1 SP");
    }

    // Post-contact WALK band: with NpcChaseSprintGapTiles=3 a mob that's already reached its target follows at a
    // WALK while the target sits just INSIDE the sprint gap (threshold-1 tiles off) and is NOT latched — guest and
    // native identically.  This is the hysteresis "walk when close" band that lets a player slip PAST (the band is
    // empty at a threshold of 2, where any non-adjacent gap sprints — which is why 3 is the sticky-but-passable dial).
    [Test]
    public void ChaseStep_Walk_GuestMatchesNative()
    {
        int walkBandY = 6 + Constants.NpcChaseSprintGapTiles - 1;   // target just inside the sprint gap → walk (not adjacent, below threshold)
        var native = RunChaseStep(guest: false, npcX: 8, npcY: 6, targetX: 8, targetY: walkBandY, hasContact: true, sprinting: false);
        var guest = RunChaseStep(guest: true, npcX: 8, npcY: 6, targetX: 8, targetY: walkBandY, hasContact: true, sprinting: false);
        Assert.That(guest, Is.EqualTo(native), "a guest must walk-step exactly like a native (post-contact, target inside the sprint gap)");
        Assert.That(native.Y, Is.EqualTo(7), "the mob should step one tile toward the target");
        Assert.That(native.Sprinting, Is.False, "a target inside the sprint gap, un-latched -> walk, no sprint latch set");
        Assert.That(native.Sp, Is.EqualTo(20), "a walk-step drains no SP");
    }

    // Post-contact, target 8 tiles away (>= the sprint gap) → both SPRINT one tile, drain the same SP, latch identically.
    [Test]
    public void ChaseStep_Run_GuestMatchesNative()
    {
        var native = RunChaseStep(guest: false, npcX: 2, npcY: 6, targetX: 10, targetY: 6, hasContact: true, sprinting: false);
        var guest = RunChaseStep(guest: true, npcX: 2, npcY: 6, targetX: 10, targetY: 6, hasContact: true, sprinting: false);
        Assert.That(guest, Is.EqualTo(native), "a guest must run-step exactly like a native (target opened the sprint gap)");
        Assert.That(native.X, Is.EqualTo(3), "the mob should step one tile toward the target");
        Assert.That(native.Sprinting, Is.True, "a gap past the sprint threshold post-contact sets the sprint latch");
        Assert.That(native.Sp, Is.EqualTo(19), "a run-step drains 1 SP (a walk drains none)");
    }

    // Post-contact, LATCHED, target just INSIDE the sprint gap (threshold-1 tiles): the sprint latch HOLDS it
    // running toward the not-yet-adjacent target instead of dropping to a walk — the hysteresis's "keep sprinting
    // until adjacent" half.  Same geometry as ChaseStep_Walk but latched → sprints, isolating the latch as the
    // deciding factor below the threshold.  Guest and native identically, SP draining.
    [Test]
    public void ChaseStep_LatchedSprintNearTarget_GuestMatchesNative()
    {
        int walkBandY = 6 + Constants.NpcChaseSprintGapTiles - 1;   // inside the sprint gap: only the latch keeps it running (an un-latched mob would walk here)
        var native = RunChaseStep(guest: false, npcX: 8, npcY: 6, targetX: 8, targetY: walkBandY, hasContact: true, sprinting: true);
        var guest = RunChaseStep(guest: true, npcX: 8, npcY: 6, targetX: 8, targetY: walkBandY, hasContact: true, sprinting: true);
        Assert.That(guest, Is.EqualTo(native), "a latched sprint must be held identically by guest and native down to adjacency");
        Assert.That(native.Sprinting, Is.True, "a latched, non-adjacent chaser keeps sprinting even inside the sprint gap");
        Assert.That(native.Sp, Is.EqualTo(19), "still running => SP drained (didn't drop to a walk)");
    }

    // Adjacent target (gap 1): neither steps (the brain swings); both face the target and CLEAR the sprint latch.
    [Test]
    public void ChaseStep_Adjacent_GuestMatchesNative_NoStepLatchCleared()
    {
        var native = RunChaseStep(guest: false, npcX: 8, npcY: 6, targetX: 8, targetY: 7, hasContact: true, sprinting: true);
        var guest = RunChaseStep(guest: true, npcX: 8, npcY: 6, targetX: 8, targetY: 7, hasContact: true, sprinting: true);
        Assert.That(guest, Is.EqualTo(native), "at melee range a guest and native behave identically (face, don't step)");
        Assert.That(native.X, Is.EqualTo(8), "adjacent => no step");
        Assert.That(native.Y, Is.EqualTo(6), "adjacent => no step");
        Assert.That(native.Dir, Is.EqualTo(Direction.Down), "the mob faces the target it's about to swing at");
        Assert.That(native.Sprinting, Is.False, "reaching melee clears the sprint latch");
        Assert.That(native.Sp, Is.EqualTo(20), "no step => no SP drain");
    }

    // Target on an ADJACENT map: the NPC steps across the seam this tick. A native converts into a guest on the
    // far map; a guest hops to the far map. This is the exact ### 7 case (cross-seam stepping on the legs pass) —
    // the resulting crossed guest must be at the same far-map position whether it started native or guest.
    [Test]
    public void ChaseStep_CrossSeam_GuestMatchesNative()
    {
        var native = RunCrossSeamStep(guest: false);
        var guest = RunCrossSeamStep(guest: true);
        Assert.That(guest, Is.EqualTo(native), "a native crossing a seam (converting to a guest) must land where a guest crossing does AND carry the same SP");
        Assert.That(native.Map, Is.EqualTo(1), "the NPC should have crossed from map 2 onto map 1");
        Assert.That(native.X, Is.EqualTo(15), "it lands on the far map's right edge, one tile across the seam");
        Assert.That(native.Y, Is.EqualTo(6));
        Assert.That(native.Sp, Is.EqualTo(19), "the crossing SPRINT drained 1 SP (started 20) — it rode onto the guest, not the vacated slot");
    }

    // ── BRAIN-LEVEL COMBAT PARITY (RunForAllMaps) — melee damage through the real 500ms brain ──
    // Damage is RNG (crit chance + ±10% variance), so a single hit can't be compared; instead we drive MANY
    // brain ticks of a native and of an equivalent guest, each adjacent to the same player, and assert the two
    // damage DISTRIBUTIONS match (same mean; same NpcRecord + same NpcAttackPlayer path => same formula). This
    // proves the brain drives a guest's melee exactly like a native's, end to end.
    [Test]
    public void MeleeDamage_GuestMatchesNative_ThroughBrain()
    {
        const int N = 4000;
        var native = MeleeSamples(guest: false, N);
        var guest = MeleeSamples(guest: true, N);
        Assert.That(native.count, Is.EqualTo(N), "the native should land N melee hits through the brain");
        Assert.That(guest.count, Is.EqualTo(N), "the guest should land N melee hits through the brain");
        Assert.That(native.mean, Is.GreaterThan(0));
        // Identical formula + identical NpcRecord => the same true mean; over N samples the estimates match tightly.
        Assert.That(guest.mean, Is.EqualTo(native.mean).Within(native.mean * 0.04),
            $"guest mean melee damage ({guest.mean:0.0}) must match the native's ({native.mean:0.0}) — same brain path");
    }

    // Drive N brain ticks of a native (or guest) adjacent to a player and collect the per-hit damage. Resets the
    // melee cooldown, NPC SP (stable crit chance), and player HP each tick; advances the AI clock so the guest's
    // LastAiTick guard doesn't skip it. Player SP=0 so it never blocks/dodges (every swing lands).
    static (double mean, int min, int max, int count) MeleeSamples(bool guest, int n)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 0;
        npc.Spd = 10;

        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = 8;
        pc.Y = 7;
        pc.Level = 10;
        pc.MaxHp = 100_000;
        pc.Hp = 100_000;
        pc.Sp = 0;
        world.MapObservers[Map].Add(TargetIdx);

        long tick = 1_000_000;
        MapNpcRecord rec;
        if (!guest)
        {
            var mn = world.MapNpcs[Map, 1];
            mn.Num = NpcNum;
            mn.X = 8;
            mn.Y = 6;
            mn.Dir = Direction.Down;  // faces the player at (8,7): swings, doesn't turn-then-wait
            mn.Hp = 9999;
            mn.Mp = 50;
            mn.Sp = 20;
            mn.Target = TargetIdx;
            mn.HasMadeContact = true;
            mn.ChaseTargetKey = TargetIdx;
            rec = mn;
        }
        else
        {
            var t = new TraversalNpcRecord
            {
                Num = NpcNum, SpawnMapNum = Map, SpawnSlot = 1, CurrentMapNum = Map,
                X = 8, Y = 6, Dir = Direction.Down, Hp = 9999, Mp = 50, Sp = 20,
                Target = TargetIdx, HasMadeContact = true, ChaseTargetKey = TargetIdx,
            };
            world.MapTraversalNpcs[Map].Add(t);
            rec = t;
        }

        var samples = new List<int>(n);
        int guard = 0;
        while (samples.Count < n && guard++ < n * 20)
        {
            pc.Hp = 100_000;
            rec.AttackTimer = 0;                    // clear the melee cooldown so it swings this tick
            rec.Sp = 20;                            // fresh SP => stable crit opportunity every tick
            rec.CombatExpiresAt = tick + 10_000_000; // stay engaged as the AI clock advances (keeps a guest from going home)
            ai.RunForAllMaps(tick);
            int dmg = 100_000 - pc.Hp;
            if (dmg > 0) samples.Add(dmg);          // (player SP=0 so hits always land; guard anyway)
            tick += 1_000;                          // advance so a guest's LastAiTick guard processes it next tick too
        }
        return (samples.Average(), samples.Min(), samples.Max(), samples.Count);
    }

    // Same idea as the melee test but for a SPELL-primary caster (Int >> Str) holding at cast range: the brain
    // drives NpcCastSpellOnPlayer for a native and a guest alike, so their spell-damage distributions must match.
    [Test]
    public void SpellDamage_GuestMatchesNative_ThroughBrain()
    {
        const int N = 3000;
        var native = SpellSamples(guest: false, N);
        var guest = SpellSamples(guest: true, N);
        Assert.That(native.count, Is.EqualTo(N), "the native caster should land N spell hits through the brain");
        Assert.That(guest.count, Is.EqualTo(N), "the guest caster should land N spell hits through the brain");
        Assert.That(native.mean, Is.GreaterThan(0));
        Assert.That(guest.mean, Is.EqualTo(native.mean).Within(native.mean * 0.04),
            $"guest mean spell damage ({guest.mean:0.0}) must match the native's ({native.mean:0.0}) — same cast path");
    }

    // Drive N brain ticks of a spell-primary caster (native or guest) holding at cast range and collect per-cast
    // damage. Resets mana (always affords the cast), SP (stable crit), the weave rising-edge, position (a rare
    // melee/kite beat may nudge it), and player HP each tick; advances the AI clock for the guest's LastAiTick.
    static (double mean, int count) SpellSamples(bool guest, int n)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mage";
        npc.Behavior = NpcBehavior.AttackOnSight;
        // 🔴 PURE caster (Str 0), so RollCastModality's pCast is Int/(Int+Str) = 1.0 and the weave picks magic
        // every beat. Str 1 also cast every beat here, but only by accident: the fixture pins Mp above the
        // NPC's actual max, and the Int-primary mana taper is uncapped, so pCast computed ABOVE 1.0. Clamp
        // that taper in production — a one-line, obviously-correct change — and this parity run silently
        // becomes a 2.4%-per-beat coin flip.
        npc.Str = 0;
        npc.Def = 10;
        npc.Int = 40;
        npc.Spd = 10;

        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = 8;
        pc.Y = 9;
        pc.Level = 10;
        pc.MaxHp = 100_000;
        pc.Hp = 100_000;
        pc.Sp = 0;  // gap 3: inside R=5, not melee
        world.MapObservers[Map].Add(TargetIdx);

        long tick = 1_000_000;
        MapNpcRecord rec;
        if (!guest)
        {
            var mn = world.MapNpcs[Map, 1];
            mn.Num = NpcNum;
            mn.X = 8;
            mn.Y = 6;
            mn.Dir = Direction.Down;
            mn.Hp = 9999;  // alive (NpcCastSpellOnPlayer refuses Hp<=0)
            mn.Target = TargetIdx;
            mn.HasMadeContact = true;
            mn.ChaseTargetKey = TargetIdx;
            rec = mn;
        }
        else
        {
            var t = new TraversalNpcRecord
            {
                Num = NpcNum, SpawnMapNum = Map, SpawnSlot = 1, CurrentMapNum = Map,
                X = 8, Y = 6, Dir = Direction.Down, Hp = 9999, Target = TargetIdx, HasMadeContact = true, ChaseTargetKey = TargetIdx,
            };
            world.MapTraversalNpcs[Map].Add(t);
            rec = t;
        }

        var samples = new List<int>(n);
        int guard = 0;
        while (samples.Count < n && guard++ < n * 40)
        {
            pc.Hp = 100_000;
            rec.X = 8;
            rec.Y = 6;  // hold at cast range (a rare melee/kite beat may have nudged it)
            rec.Mp = world.EffectiveNpcMaxMp(npc);   // full, never OVER full - see NpcVitalCeilingTests
            rec.Sp = 20;  // always afford the cast; fresh SP for a stable spell-crit chance
            rec.AttackTimer = 0;
            rec.WeaveWasReady = false;  // cast-ready + a fresh weave roll this tick
            rec.WantsKite = false;
            rec.MeleeKiteAttempts = 0;
            rec.CombatExpiresAt = tick + 10_000_000;
            ai.RunForAllMaps(tick);
            int dmg = 100_000 - pc.Hp;
            if (dmg > 0) samples.Add(dmg);              // skip the occasional melee/kite beat (P(cast)=Int/(Int+Str)~0.98)
            tick += 1_000;
        }
        return (samples.Average(), samples.Count);
    }

    // ── TARGET-SELECTION PARITY — the shared aggro re-evaluation (highest DEF-weighted contributor) ──
    // Natives SCAN for a fresh target (Find*, covered by NpcTargetAcquisitionTests); a guest inherits its target
    // and doesn't re-scan (a deliberate difference). The selection logic BOTH run is the aggro re-eval: from a
    // ledger of contributors, pick the highest DEF-weighted one. SelectAggroTargetEx reads the inherited ledger +
    // Num off the base record, so a native and a guest with an identical ledger must pick the identical target.
    [Test]
    public void AggroSelection_GuestMatchesNative()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var combat = BuildCombatSystem(world, pm);
        world.Npcs[NpcNum].Behavior = NpcBehavior.AttackOnSight;

        // Two contributors in the NPC's observable area: a tanky, lower-damage attacker and a squishy, higher-
        // damage one — so the DEF-weighted pick is non-trivial (whichever wins, both records must agree on it).
        RegisterContributor(world, pm, index: 5, x: 8, y: 5, level: 10, def: 80);
        RegisterContributor(world, pm, index: 6, x: 8, y: 7, level: 10, def: 5);

        var native = world.MapNpcs[Map, 1];
        native.Num = NpcNum;
        native.X = 8;
        native.Y = 6;
        native.Hp = 100;
        native.DamageByPlayer[5] = 100;
        native.DamageByPlayer[6] = 130;

        var guest = new TraversalNpcRecord { Num = NpcNum, SpawnMapNum = Map, SpawnSlot = 1, CurrentMapNum = Map, X = 8, Y = 6, Hp = 100 };
        guest.DamageByPlayer[5] = 100;
        guest.DamageByPlayer[6] = 130;

        int nativePick = InvokeAggroPick(combat, native);
        int guestPick = InvokeAggroPick(combat, guest);
        Assert.That(guestPick, Is.EqualTo(nativePick), "a guest and a native must pick the same aggro target from an identical ledger");
        Assert.That(nativePick, Is.AnyOf(5, 6), "the NPC should aggro one of its two contributors");
    }

    static CombatSystem BuildCombatSystem(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    static void RegisterContributor(GameWorld world, PlayerManager pm, int index, int x, int y, int level, int def)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = level;
        pc.Def = def;
        pc.Hp = 500;
        pc.MaxHp = 500;
        world.MapObservers[Map].Add(index);
    }

    // SelectAggroTargetEx is private and returns an AggroPick struct; reflect the call and read its Player field.
    static int InvokeAggroPick(CombatSystem combat, MapNpcRecord mn)
    {
        var method = typeof(CombatSystem).GetMethod("SelectAggroTargetEx", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null, "SelectAggroTargetEx must exist (shared aggro selection)");
        object pick = method!.Invoke(combat, new object[] { Map, mn })!;
        var pt = pick.GetType();
        return (int)(pt.GetProperty("Player")?.GetValue(pick) ?? pt.GetField("Player")!.GetValue(pick))!;
    }

    // ── CASTER ARCHETYPE — kite/hold-at-range only for INT-primary (Int>Str); melee-primary casts FROM melee ──
    // Only a pure caster (Str=0) or INT-primary hybrid (Int>Str) kites + holds at cast range.  A Str>=Int mob does
    // NOT kite — it fights AT melee range and casts from there (steady DPS, no kite-then-reclose gap).  Mixed builds
    // also COMMIT to a modality for a few beats, and an INT-primary hybrid tapers its cast rate by current mana.

    [Test]
    public void MeleeHybrid_ClosesToMelee_DoesNotHoldAtCastRange()
    {
        // Str>=Int at cast range, with mana + a cast beat, must CLOSE IN — not hold at range like an INT-primary
        // kiter (compare ManaCaster_HoldsAtCastRange below, which holds at y=6 for the default Int>Str build).
        int y = CasterLegsStepY(guest: false, mp: 9999, weaveCast: true, str: 40, intel: 20);
        Assert.That(y, Is.EqualTo(7), "a Str>=Int mob casts from melee — it closes in, it does not hold at cast range");
    }

    [Test]
    public void MeleeHybrid_CastsFromMelee_DoesNotKite()
    {
        var (wantsKite, dmg) = CasterInMeleeOnCastBeat(str: 40, intel: 20);
        Assert.That(wantsKite, Is.False, "a Str>=Int mob casts from melee range — it must NOT kite (retreat) to reopen distance");
        Assert.That(dmg, Is.GreaterThan(0), "it should cast (deal damage) from melee range instead of standing off");
    }

    [Test]
    public void CasterHybrid_KitesFromMelee()
    {
        var (wantsKite, _) = CasterInMeleeOnCastBeat(str: 20, intel: 40);
        Assert.That(wantsKite, Is.True, "an Int>Str mob kites (retreats to reopen spell range) when a cast beat finds it in melee");
    }

    [Test]
    public void WeaveModality_CommitsForSeveralBeats_NotEveryBeat()
    {
        // A balanced mob (Str==Int) rolls 50-50, so WITHOUT the commitment it would flip modality every time the
        // roll came up the other way. The commitment holds one modality for NpcWeaveCommitMin..MaxBeats.
        //
        // 🔴 The roll is PINNED (WeaveRolls), not sampled. Every other brain-driven test in the suite pins its
        // mob into a deterministic band instead — Int 0 always melees, Str 0 with Int above 0 always casts — but
        // this test is about the weave itself, so it has to enter the rolling band. That makes it the one place
        // the IRandomSource seam is load-bearing: with the modality alternating and the commit length fixed, the
        // run length is exact, and the assertion is an equality rather than a threshold over a sampled average.
        const int Commit = 4;   // inside NpcWeaveCommitMin..MaxBeats
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm, new WeaveRolls(Commit));
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 20;
        npc.Spd = 10;  // balanced: P(cast)=0.5, no mana taper (Str>=Int)
        RegisterTarget(world, pm, 8, 9);

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Dir = Direction.Down;
        mn.Hp = 9999;
        mn.Sp = 20;
        mn.Target = TargetIdx;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = TargetIdx;

        long tick = 1_000_000;
        var modality = new List<bool>();
        for (int i = 0; i < 300; i++)
        {
            mn.X = 8;
            mn.Y = 6;
            mn.Mp = world.EffectiveNpcMaxMp(npc);
            mn.Sp = 20;
            mn.AttackTimer = 0;
            mn.WeaveWasReady = false;  // force a fresh beat each tick
            mn.CombatExpiresAt = tick + 10_000_000;
            ai.RunForAllMaps(tick);
            modality.Add(mn.WeaveCastThisBeat);
            tick += 1_000;
        }

        // Run lengths, dropping the trailing partial run the loop cut off mid-commitment.
        var runs = new List<int>();
        int run = 1;
        for (int i = 1; i < modality.Count; i++)
        {
            if (modality[i] == modality[i - 1]) { run++; continue; }
            runs.Add(run);
            run = 1;
        }

        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.Not.Empty, "the weave must still switch modality over time (not frozen)");
            Assert.That(runs, Is.All.EqualTo(Commit),
                $"every committed run must last exactly the rolled {Commit} beats — got [{string.Join(", ", runs.Distinct())}]");
            Assert.That(modality.Distinct().Count(), Is.EqualTo(2), "both modalities must appear");
        });
    }

    [Test]
    public void ManaTaper_IntPrimaryCastsMoreAtHighMana_ThanLowMana()
    {
        // An Int>Str hybrid tapers P(cast) by its mana fraction: cast more at high mana, melee more at low (a soft
        // ramp above the hard OOM cutoff). Measure the underlying cast-roll rate at full vs low (but affordable) mana.
        double highMana = WeaveCastRate(str: 20, intel: 60, mpFraction: 1.0);
        double lowMana = WeaveCastRate(str: 20, intel: 60, mpFraction: 0.3);
        Assert.That(highMana, Is.GreaterThan(lowMana + 0.2),
            $"an INT-primary hybrid must cast more at high mana ({highMana:0.00}) than at low mana ({lowMana:0.00})");
    }

    // Place a mob adjacent to the player on a forced (committed) CAST beat; return (did it kite?, damage dealt).
    static (bool wantsKite, int dmg) CasterInMeleeOnCastBeat(int str, int intel)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = str;
        npc.Def = 10;
        npc.Int = intel;
        npc.Spd = 10;

        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = 8;
        pc.Y = 7;
        pc.Level = 10;
        pc.MaxHp = 100_000;
        pc.Hp = 100_000;
        pc.Sp = 0;  // adjacent to (8,6)
        world.MapObservers[Map].Add(TargetIdx);

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Dir = Direction.Down;
        mn.Hp = 9999;
        mn.Sp = 20;
        mn.Mp = world.EffectiveNpcMaxMp(npc);
        mn.Target = TargetIdx;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = TargetIdx;
        mn.WeaveCastThisBeat = true;
        mn.WeaveModalityBeatsLeft = 5;  // force a committed CAST beat (no re-roll this tick)
        mn.AttackTimer = 0;
        mn.CombatExpiresAt = 100_000_000;

        ai.RunForAllMaps(1_000_000);
        return (mn.WantsKite, 100_000 - pc.Hp);
    }

    // Fraction of ready beats a mob rolls "cast", with its mana pinned at mpFraction of max (re-rolling every beat
    // so we measure the underlying P(cast), not a commitment run).
    static double WeaveCastRate(int str, int intel, double mpFraction)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);
        var npc = world.Npcs[NpcNum];
        npc.Name = "mage";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = str;
        npc.Def = 10;
        npc.Int = intel;
        npc.Spd = 10;
        RegisterTarget(world, pm, 8, 9);   // gap 3: an Int>Str mob holds + casts in place, no move
        var pc = pm[TargetIdx].Char;
        pc.MaxHp = 100_000;
        pc.Hp = 100_000;
        pc.Sp = 0;  // survive the casts (avoid the death/EXP path)

        int mp = Math.Max((int)(StatFormulas.GetNpcMaxMp(npc) * mpFraction), 1);

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Dir = Direction.Down;
        mn.Hp = 9999;
        mn.Sp = 20;
        mn.Target = TargetIdx;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = TargetIdx;

        long tick = 1_000_000;
        int castBeats = 0, total = 0;
        for (int i = 0; i < 400; i++)
        {
            mn.X = 8;
            mn.Y = 6;
            mn.Mp = mp;  // hold mana at the target fraction
            mn.AttackTimer = 0;
            mn.WeaveWasReady = false;  // fresh beat
            mn.WeaveModalityBeatsLeft = 0;                  // re-roll every beat → measures the raw P(cast)
            mn.CombatExpiresAt = tick + 10_000_000;
            ai.RunForAllMaps(tick);
            if (mn.WeaveCastThisBeat) castBeats++;
            total++;
            tick += 1_000;
        }
        return (double)castBeats / total;
    }

    // ── OUT-OF-MANA CASTER — must close in for melee, never stand idle at cast range ──
    // Regression guard for the drift bug: CasterHoldsAtCastRange once kept a stale `Mp < 1` while the brain gated
    // on the real per-cast cost, so a caster holding 1..cost-1 MP passed the legs' gate yet couldn't afford a cast
    // — the brain wouldn't cast (hasMana false) and the legs held it at range, so it stood idle until MP trickled
    // back. Both gates now share NpcCanAffordCast (round(maxMp/20)). For this Int=40 caster the cost is ~10, so
    // Mp=1 is the exact trap (>= 1 but < cost); WeaveCastThisBeat=true is the stale cast-beat that latched the old
    // hold (an OOM caster's brain returns before re-rolling the weave).

    [Test]
    public void OomCaster_ClosesIn_NotHoldingAtCastRange([Values(false, true)] bool guest)
    {
        int y = CasterLegsStepY(guest, mp: 1, weaveCast: true);
        Assert.That(y, Is.EqualTo(7), $"an out-of-mana caster ({(guest ? "guest" : "native")}) must close one tile in for melee, not hold at cast range (y stayed 6)");
    }

    [Test]
    public void ManaCaster_HoldsAtCastRange_WhenItCanCast()
    {
        // Complement: with enough MP to afford a cast (and a cast beat) the caster HOLDS at range — the fix must
        // not turn a functional caster into one that blindly charges in.
        int y = CasterLegsStepY(guest: false, mp: 9999, weaveCast: true);
        Assert.That(y, Is.EqualTo(6), "a caster that CAN afford a cast holds at cast range (it casts) instead of closing in");
    }

    // End-to-end through BOTH passes: an OOM caster (kept below cost every tick, with the stale cast-beat that used
    // to freeze it) must close the distance and land a MELEE hit — never idle at range.
    [Test]
    public void OomCaster_ReachesMeleeAndAttacks_DoesNotIdle()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mage";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 40;
        npc.Spd = 10;  // a real caster (Int) that can also swing (Str)

        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = 8;
        pc.Y = 9;
        pc.Level = 10;
        pc.MaxHp = 100_000;
        pc.Hp = 100_000;
        pc.Sp = 0;
        world.MapObservers[Map].Add(TargetIdx);

        var mn = world.MapNpcs[Map, 1];
        mn.Num = NpcNum;
        mn.X = 8;
        mn.Y = 6;
        mn.Dir = Direction.Down;
        mn.Hp = 9999;
        mn.Sp = 20;
        mn.Target = TargetIdx;
        mn.HasMadeContact = true;
        mn.ChaseTargetKey = TargetIdx;

        long tick = 1_000_000;
        for (int i = 0; i < 40 && pc.Hp == 100_000; i++)
        {
            mn.Mp = 1;                    // keep it OOM (below cost) every tick — it can never cast, so it MUST melee
            mn.WeaveCastThisBeat = true;  // stale cast-beat: the exact state that would freeze it at cast range
            mn.NextMoveMs = 0;
            mn.AttackTimer = 0;
            mn.CombatExpiresAt = tick + 10_000_000;
            ai.RunMovement(tick);         // legs close the distance
            ai.RunForAllMaps(tick);       // brain swings when adjacent
            tick += 500;
        }
        Assert.That(mn.Y, Is.GreaterThan(6), "the out-of-mana caster must close the distance toward the player");
        Assert.That(pc.Hp, Is.LessThan(100_000), "an out-of-mana caster must reach melee and attack, not idle at cast range");
    }

    // Place a caster at cast range (gap 3) with the given MP + weave state, run one movement tick, return its Y
    // (6 = held at range, 7 = closed one tile in).
    static int CasterLegsStepY(bool guest, int mp, bool weaveCast, int str = 20, int intel = 40)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        var npc = world.Npcs[NpcNum];
        npc.Name = "mage";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = str;
        npc.Def = 10;
        npc.Int = intel;
        npc.Spd = 10;  // default Int>Str = INT-primary (kiter); pass str>=intel for a melee-primary
        RegisterTarget(world, pm, 8, 9);   // gap 3 from (8,6): inside R=5 cast range, clear LoS

        if (!guest)
        {
            var mn = world.MapNpcs[Map, 1];
            mn.Num = NpcNum;
            mn.X = 8;
            mn.Y = 6;
            mn.Dir = Direction.Down;
            mn.Hp = 9999;
            mn.Sp = 20;
            mn.Target = TargetIdx;
            mn.HasMadeContact = true;
            mn.ChaseTargetKey = TargetIdx;
            mn.Mp = mp;
            mn.WeaveCastThisBeat = weaveCast;
            mn.NextMoveMs = 0;
            ai.RunMovement(1_000_000);
            return mn.Y;
        }
        var t = new TraversalNpcRecord
        {
            Num = NpcNum, SpawnMapNum = Map, SpawnSlot = 1, CurrentMapNum = Map,
            X = 8, Y = 6, Dir = Direction.Down, Hp = 9999, Sp = 20,
            Target = TargetIdx, HasMadeContact = true, ChaseTargetKey = TargetIdx,
            Mp = mp, WeaveCastThisBeat = weaveCast, NextMoveMs = 0,
        };
        world.MapTraversalNpcs[Map].Add(t);
        ai.RunMovement(1_000_000);
        return t.Y;
    }

    // ── harness: build a REAL ai, place the actors, run one movement tick, read the resulting state ──

    static StepOutcome RunChaseStep(bool guest, int npcX, int npcY, int targetX, int targetY, bool hasContact, bool sprinting)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        // The NPC's template: a plain melee mob (Int=0 so it is not treated as a caster), enough SPD for SP.
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 0;
        npc.Spd = 10;

        RegisterTarget(world, pm, targetX, targetY);

        long now = 100_000;   // >> any AttackTimer/NextMoveMs we set to 0
        if (!guest)
        {
            var mn = world.MapNpcs[Map, 1];
            mn.Num = NpcNum;
            mn.X = npcX;
            mn.Y = npcY;
            mn.Dir = Direction.Up;
            mn.Hp = 9999;
            mn.Mp = 50;
            mn.Sp = 20;
            mn.Target = TargetIdx;
            mn.HasMadeContact = hasContact;
            mn.ChaseSprinting = sprinting;
            mn.ChaseTargetKey = TargetIdx;   // established mid-chase (matches mn.Target) so the step doesn't re-BeginEngagement and reset the latches
            mn.NextMoveMs = 0;
            mn.AttackTimer = 0;
            ai.RunMovement(now);
            return Read(mn);
        }
        else
        {
            var t = new TraversalNpcRecord
            {
                Num = NpcNum, SpawnMapNum = Map, SpawnSlot = 1, CurrentMapNum = Map,
                X = npcX, Y = npcY, Dir = Direction.Up, Hp = 9999, Mp = 50, Sp = 20,
                Target = TargetIdx, HasMadeContact = hasContact, ChaseSprinting = sprinting,
                ChaseTargetKey = TargetIdx,   // established mid-chase (see the native branch) — no re-BeginEngagement this tick
                NextMoveMs = 0, AttackTimer = 0,
            };
            world.MapTraversalNpcs[Map].Add(t);
            ai.RunMovement(now);
            return Read(t);
        }
    }

    static StepOutcome Read(MapNpcRecord mn) => new(mn.X, mn.Y, mn.Dir, mn.MoveType, mn.Sp, mn.ChaseSprinting);

    // Cross-seam scenario: NPC on map 2, target on map 1 (map 2's Left neighbor); one movement tick crosses it.
    // Position AND SP: a native converting to a guest across a seam must land at the same tile AND carry the same
    // SP as a guest hopping.  The crossing sprint's SP drain is applied BEFORE the step (see AdvanceNativeChaseStep
    // / FinishChaseStep) so it rides onto the new guest, exactly as a guest crossing drains — no 1-SP freebie for a
    // native's first seam cross.
    readonly record struct CrossOutcome(int Map, int X, int Y, Direction Dir, double Sp);

    static CrossOutcome RunCrossSeamStep(bool guest)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        // Link map 2 (the NPC's start) and map 1 (the target) as right/left neighbors. Crossing into the LOWER-
        // numbered map (already ticked this RunMovement pass) avoids a same-pass second step, isolating the cross.
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;

        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Str = 20;
        npc.Def = 10;
        npc.Int = 0;
        npc.Spd = 10;

        // Target on map 1 near its right edge (world-close to the seam); the player observes both maps.
        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = 1;
        pc.X = 13;
        pc.Y = 6;
        pc.Level = 10;
        world.MapObservers[1].Add(TargetIdx);
        world.MapObservers[2].Add(TargetIdx);

        long now = 100_000;
        if (!guest)
        {
            var mn = world.MapNpcs[2, 1];
            mn.Num = NpcNum;
            mn.X = 0;
            mn.Y = 6;
            mn.Dir = Direction.Up;
            mn.Hp = 9999;
            mn.Mp = 50;
            mn.Sp = 20;
            mn.Target = TargetIdx;
            mn.HasMadeContact = true;
            mn.ChaseTargetKey = TargetIdx;
            mn.NextMoveMs = 0;
            mn.AttackTimer = 0;
            ai.RunMovement(now);
        }
        else
        {
            var t = new TraversalNpcRecord
            {
                Num = NpcNum, SpawnMapNum = 2, SpawnSlot = 1, CurrentMapNum = 2,
                X = 0, Y = 6, Dir = Direction.Up, Hp = 9999, Mp = 50, Sp = 20,
                Target = TargetIdx, HasMadeContact = true, ChaseTargetKey = TargetIdx,
                NextMoveMs = 0, AttackTimer = 0,
            };
            world.MapTraversalNpcs[2].Add(t);
            ai.RunMovement(now);
        }

        // The NPC is now a guest on map 1 (a native converts via NativeNpcCrossBorder; a guest hops via
        // MoveGuestToMap). Exactly one traversal guest should exist on map 1 either way.
        var list = world.MapTraversalNpcs[1];
        Assert.That(list.Count, Is.EqualTo(1), "exactly one NPC should have crossed the seam onto map 1");
        var c = list[0];
        return new CrossOutcome(c.CurrentMapNum, c.X, c.Y, c.Dir, c.Sp);
    }

    static void RegisterTarget(GameWorld world, PlayerManager pm, int x, int y)
    {
        var sp = pm[TargetIdx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = 10;
        world.MapObservers[Map].Add(TargetIdx);   // observed map => RunMovement processes it; also a valid target
    }

    // A REAL NpcAiSystem: real Combat/Movement/Blood/Spawn, a no-op dispatcher, and null for the sub-systems the
    // AI paths under test never reach (items/joinLeave/shop). This runs BOTH the fast movement pass AND the 500ms
    // brain (RunForAllMaps): SpawnSystem.CheckNpcRespawn never calls SpawnNpc on a fresh map (no map NPC slot
    // definitions), and the melee/cast paths only touch _items/_joinLeave on a KILL (tests keep the target alive).
    // If a future edit makes these paths touch a nulled system, this throws — a useful "you added a dependency" signal.
    static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm, IRandomSource? rng = null)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        var spawn = new SpawnSystem(world, pm, dispatcher);
        return new NpcAiSystem(world, pm, dispatcher, combat, movement, spawn, items: null!, blood, rng: rng);
    }

    /// <summary>Pins the melee-vs-magic weave: the modality roll alternates and the commit length is fixed, so
    /// the run structure the weave produces is exact instead of sampled.
    ///
    /// <para>Safe to alternate blindly because <c>Rng.NextDouble()</c> has exactly ONE call site in the whole AI
    /// — <c>RollCastModality</c> — so no other roll can shift the phase. The ranged overload is CLAMPED into its
    /// caller's range: it also serves the wander stride, and returning a fixed value blind would hand some other
    /// caller a number outside its own bounds.</para></summary>
    sealed class WeaveRolls(int commit) : IRandomSource
    {
        private bool _cast;

        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => Math.Clamp(commit, minInclusive, maxExclusive - 1);
        public long NextInt64(long minInclusive, long maxExclusive) => minInclusive;

        // Balanced mob => pCast 0.5, so 0.0 rolls a cast beat and (just under) 1.0 rolls a melee beat.
        public double NextDouble()
        {
            _cast = !_cast;
            return _cast ? 0.0 : Math.BitDecrement(1.0);
        }
    }

    // No-op packet dispatcher — the chase step emits sync/dir packets we don't need to observe here.
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}

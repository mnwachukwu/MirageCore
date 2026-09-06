namespace Mirage.Shared.Records;

// Not sealed: TraversalNpcRecord inherits this so the AI can operate on a chasing NPC's
// full state polymorphically while it visits a neighbor map.
public class MapNpcRecord
{
    public int Num { get; set; }
    public int Target { get; set; }        // player index; 0 = no target
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int Sp { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    // Two-layer world: which logical layer this NPC occupies (transient runtime state; not persisted). Seeded
    // at spawn, recomputed on movement / seam crossings, reseeded on respawn. See LayerLogic/WorldLayer.
    public WorldLayer Layer { get; set; }
    public long SpawnWait { get; set; }
    public long AttackTimer { get; set; }
    public int MeleeKiteAttempts { get; set; }  // consecutive "want to cast but in melee" ticks; reset on a non-melee tick or a bail-out cast
    public long CombatExpiresAt { get; set; }   // 0 = never in combat; future tick = in combat until then

    // Seamless chase: true while this home slot's NPC is away visiting a neighbor map.
    // Blocks respawn into the slot until the traveler returns or dies.
    public bool IsReservedSlot { get; set; }

    public int JanitorTarget { get; set; }  // 1-MaxMapItems = claimed dropped item slot; 0 = none

    // NPC target encoded by stable identity (SpawnMap, SpawnSlot) so it survives native↔guest
    // transitions.  Mutually exclusive with Target (player); both > 0 is a bug.  0 means none.
    public int NpcTargetSpawnMap { get; set; }
    public int NpcTargetSpawnSlot { get; set; }

    // Runtime movement (not persisted)
    public int XOffset { get; set; }
    public int YOffset { get; set; }
    public MovementType Moving { get; set; }
    public bool Attacking { get; set; }

    // Wander stride (runtime; not persisted).  Idle NPCs amble in committed strides — a heading plus a
    // tile count that can bend at right angles mid-stride — so ambient movement reads as deliberate
    // walking rather than isolated twitches.  Driven by NpcAiSystem.WanderStep.  Unused while chasing.
    public Direction WanderDir { get; set; }
    public int WanderStepsLeft { get; set; }       // tiles left in the current stride; 0 = idle

    // Run-chase state (runtime; not persisted).
    // MoveType = the pace for the NEXT physical step, read by the step primitives (TryNativeStep /
    // TryApplyGuestStep).  Defaults to Walking; the fast movement pass sets it to Running (SP permitting)
    // just around a chase-step, then resets it — so wander / janitor / kite / cross all stay Walking.
    public MovementType MoveType { get; set; } = MovementType.Walking;
    // Per-NPC step-clock: earliest tick (Environment.TickCount64) this NPC may take its next chase-step
    // (the SPD-scaled run cadence).  The fast movement pass gates on this; 0 = ready.
    public long NextMoveMs { get; set; }
    // A caster wants to RETREAT (kite) this cycle: the brain sets it after taking the first retreat step so the
    // fast legs pass CONTINUES the retreat at run cadence.  Cleared each brain magic eval and when a legs
    // retreat is cornered.  Distinguishes "brain handled me by KITING (legs, keep moving me away)" from "brain
    // handled me by casting/holding (legs, leave me alone)" — see the magic-push sites and TryLegsKite.
    public bool WantsKite { get; set; }

    // Per-beat melee-vs-magic weave (runtime; not persisted).  On each ready 1s combat beat an Int>0 NPC
    // rolls cast(true)/melee(false) with P(cast)=Int/(Int+Str) — see NpcAiSystem.TryNpcMagicActionCore.
    // WeaveCastThisBeat is LATCHED (rolled once per beat on the rising edge of castReady) so the 500ms brain
    // and the faster legs pass agree within a beat: the legs pass reads it via CasterHoldsAtCastRange to
    // hold-at-range (cast beat) vs close-in (melee beat).  WeaveWasReady carries the previous eval's castReady
    // for that rising-edge detection.  Defaults (false) are safe — an undecided tick closes in, never idles.
    public bool WeaveCastThisBeat { get; set; }
    public bool WeaveWasReady { get; set; }

    // Modality-commitment counter (runtime; not persisted).  A mixed NPC re-rolls WeaveCastThisBeat only when this
    // hits 0, then commits to that modality for NpcWeaveCommitMinBeats..MaxBeats ready beats — so it casts for
    // a short run, then melees for a short run, instead of flickering every beat.  0 = re-roll on the next beat.
    public int WeaveModalityBeatsLeft { get; set; }

    // Run-stamina hysteresis latch (runtime; not persisted).  Set when a run drains SP to 0; while set the
    // NPC walks until SP rebuilds to Constants.NpcRunReservoirFraction of its max, then it clears and the NPC
    // may sprint again.  Stops the run/walk flicker (and slide-snap) of burning each SP-regen trickle the
    // instant it lands.  Applies to both chase and kite runs — see NpcAiSystem.NpcCanRun.
    public bool RunReservoirLow { get; set; }

    // Per-engagement approach commitment (runtime; not persisted).  Rolled once per fresh chase target in
    // BeginEngagement: RushCommitted (won the NpcApproachRushChancePct "charge" roll) lets an AoS mob RUN the
    // opening approach; otherwise the AoS mob walks in until HasMadeContact turns true (it reached the target
    // once) — non-AoS chasers run the opening approach freely.  After contact the run/walk decision passes to
    // the distance hysteresis (see ChaseSprinting below).  See NpcAiSystem.NpcWantsChaseRun.  Both carry across
    // the native↔guest seam.
    public bool RushCommitted { get; set; }
    public bool HasMadeContact { get; set; }

    // Re-close sprint latch (runtime; not persisted).  Lifecycle mirrors HasMadeContact: init false in
    // BeginEngagement/BeginRushEngagement, carried across the native↔guest seam, cleared at the chase
    // steppers' adjacency early-returns.  Set true once a re-closing melee mob opens
    // Constants.NpcChaseSprintGapTiles; while set it sprints (SP permitting) until it regains melee, which
    // clears it back to a walk — so it bursts stamina instead of gluing.  Only non-guard, non-caster chasers
    // consult it — see NpcAiSystem.NpcWantsChaseRun.
    public bool ChaseSprinting { get; set; }

    /// <summary>Sized by the CREATOR, not by <see cref="Constants.MaxPlayers"/>.
    ///
    /// <para>One of these records exists per map NPC slot, and the world allocates every slot on every map
    /// up front — 21,000 of them. Two arrays each at the protocol ceiling would be ~84 MB of mostly-zero
    /// ints for a server that will never see more than a handful of players. <c>GameWorld</c> passes its
    /// configured player limit instead, which is the largest index any of these can ever take.</para>
    ///
    /// <para>Defaults to the ceiling so a test or a one-off construction still gets a correct record
    /// without knowing about configuration.</para></summary>
    public MapNpcRecord(int playerSlots = Constants.MaxPlayers)
    {
        DamageByPlayer = new int[playerSlots + 1];
        WarnHitsByPlayer = new int[playerSlots + 1];
    }

    // Damage contribution tracking — 1-based by player index; cleared when NPC leaves combat or respawns
    public int[] DamageByPlayer { get; }

    // Guard grace-warning tally — 1-based by player index; counts "Watch it!" warnings already
    // issued to that attacker in the current combat window.  Cleared alongside DamageByPlayer via
    // ClearDamageCredit so the existing combat-exit / respawn cleanup grants a fresh grace window.
    public int[] WarnHitsByPlayer { get; }

    // Parallel NPC contributor ledger — lazy-allocated, null in the common no-NPC-source case.
    // At most a handful of entries per fight (one or two guards, maybe a different-kind AoS mob),
    // so list+linear scan beats Dictionary overhead and keeps the zero-allocation hot path.
    public List<NpcDamageEntry>? DamageByNpc { get; set; }
    public bool WasInCombat { get; set; }

    /// <summary>Zero every entry in <see cref="DamageByPlayer"/> and clear <see cref="DamageByNpc"/>.
    /// Called when combat ends, the NPC respawns, or is converted to a traversal guest — anywhere
    /// the kill-credit ledger should restart.  Clears the list contents but keeps the list object
    /// so capacity is reused across kills of the same NPC slot.</summary>
    public void ClearDamageCredit()
    {
        Array.Clear(DamageByPlayer, 0, DamageByPlayer.Length);
        Array.Clear(WarnHitsByPlayer, 0, WarnHitsByPlayer.Length);
        DamageByNpc?.Clear();
    }

    /// <summary>Hand the ENTIRE combat/aggro ledger to <paramref name="dest"/> in one shot — kill-credit
    /// (<see cref="DamageByPlayer"/>), the guard grace tally (<see cref="WarnHitsByPlayer"/>), AND the NPC
    /// contributor list (<see cref="DamageByNpc"/>).  These MUST travel together across a map seam: the guard
    /// grace-skip in SelectAggroTargetEx weighs a player's DamageByPlayer against their WarnHitsByPlayer, so
    /// carrying one without the other silently breaks grace — a guard that chased a mob across a border then
    /// aggroed a player who had only spent "Watch it!" warnings on it.  DamageByNpc is reference-transferred
    /// (heap list); a hand-off caller nulls its own afterward so the two records don't share it.</summary>
    public void CopyCombatLedgerTo(MapNpcRecord dest)
    {
        // Bounded by the SHORTER of the two. Every record in one world is sized alike, so this is the same
        // length in practice — it is here so a record built with the default ceiling (a test, a one-off)
        // cannot overrun a world-sized one.
        int n = Math.Min(DamageByPlayer.Length, dest.DamageByPlayer.Length);
        Array.Copy(DamageByPlayer, dest.DamageByPlayer, n);
        Array.Copy(WarnHitsByPlayer, dest.WarnHitsByPlayer, n);
        dest.DamageByNpc = DamageByNpc;
    }

    /// <summary>Record damage from an NPC source onto this victim's ledger.  Lazy-allocates the list
    /// on the first NPC hit (cap 2 — typical fight has 1–2 NPC contributors).  Increments the existing
    /// entry on repeat hits from the same source so each (spawnMap, spawnSlot) appears at most once.</summary>
    public void AddNpcDamage(int spawnMap, int spawnSlot, int dmg)
    {
        DamageByNpc ??= new List<NpcDamageEntry>(2);
        for (int i = 0; i < DamageByNpc.Count; i++)
        {
            var e = DamageByNpc[i];
            if (e.SpawnMap == spawnMap && e.SpawnSlot == spawnSlot)
            {
                DamageByNpc[i] = e with { Damage = e.Damage + dmg };
                return;
            }
        }
        DamageByNpc.Add(new NpcDamageEntry(spawnMap, spawnSlot, dmg));
    }

    /// <summary>Remove any NPC-damage credit this ledger holds for the given source identity.  Called
    /// from the death sweep (and the guest-returns-home sweep) so combat credit a now-dead NPC earned
    /// against other NPCs is voided — a future respawn into the same (spawnMap, spawnSlot) can't inherit
    /// it and steal aggro.  At most one entry matches (AddNpcDamage dedups per source).</summary>
    public void RemoveNpcDamageBySource(int spawnMap, int spawnSlot)
    {
        if (DamageByNpc is null) return;
        for (int i = DamageByNpc.Count - 1; i >= 0; i--)
        {
            if (DamageByNpc[i].SpawnMap == spawnMap && DamageByNpc[i].SpawnSlot == spawnSlot)
                DamageByNpc.RemoveAt(i);
        }
    }

    /// <summary>True iff <see cref="CombatExpiresAt"/> sits strictly in the future.
    /// `CombatExpiresAt == 0` means "never entered combat" — the zero check guards against
    /// a stale 0 reading as "in combat forever" once the loop's TickCount64 advances.</summary>
    public bool IsInCombat(long now) => CombatExpiresAt > 0 && now < CombatExpiresAt;

    /// <summary>Universal NPC identity — (SpawnMap, SpawnSlot) so a native at home and a guest abroad
    /// resolve to the same key.  Native default uses its current (mapNum, slot); TraversalNpcRecord
    /// overrides to return its permanent SpawnMapNum/SpawnSlot.</summary>
    public virtual (int SpawnMap, int SpawnSlot) GetSpawnIdentity(int mapNum, int slot) => (mapNum, slot);

    // Last tick the NPC took a damaging action against its current target — updated on acquisition,
    // melee attack landed, successful pathing step toward target, AND successful cast.  Any path
    // that actually engages or progresses the fight counts; only true "can't act on the target"
    // ticks (no melee reach, no cast — typically out-of-mana AoS NPCs pinned by impassable terrain)
    // leave this stamp unchanged.  Drives the AoS-only unreachable give-up timer: if an AoS NPC
    // goes longer than the configured threshold without ANY damaging action, it drops the lock.
    // 0 = no progress recorded yet (target not yet acquired).
    public long LastReachedTargetMs { get; set; }

    /// <summary>Stamp <see cref="LastReachedTargetMs"/> to <paramref name="now"/> — the NPC took
    /// a damaging action this tick (acquired the target, landed a melee swing, completed a chase
    /// step, or fired a cast).  Resets the AoS unreachable give-up clock.  Anything that doesn't
    /// deal damage or progress toward the target (failed BFS step with no fallback action, sitting
    /// out-of-mana, etc.) deliberately doesn't call this so the timer can tick down.</summary>
    public void MarkReachedTarget(long now) => LastReachedTargetMs = now;

    // ── Chase stall tracking ───────────────────────────────────────────────────
    // Progress state for the chase stepper.  ChaseBestWorldDist/ChaseStallTicks flag a chase that has
    // made no headway for Constants.NpcChaseStallTicks.  The stepper uses "stalled" for two things: it
    // gates the straight-line momentum shortcut off (so a stuck chaser re-plans), and — ONLY when the
    // target is UNREACHABLE (BFS returns no path) — it lets the best-effort walk refuse the exact
    // reversal of its last step, so the chaser closes on the wall instead of pacing back and forth
    // mirroring a sealed moving target.  A REACHABLE target is always followed (no hold), so a chaser is
    // never frozen on the wrong side of an idle target.  See the chase steppers in NpcAiSystem
    // (sim-validated as StallFixF in Simulations/OscSim); occupied attack-slots are masked in the BFS
    // itself so a lined-up trailer routes to an open flank.

    /// <summary>Smallest world-distance to the current chase target reached so far; int.MaxValue
    /// until the first measured step.  A step that beats it clears <see cref="ChaseStallTicks"/>.</summary>
    public int ChaseBestWorldDist { get; set; } = int.MaxValue;

    /// <summary>Consecutive chase steps that failed to beat <see cref="ChaseBestWorldDist"/>.
    /// At <see cref="Constants.NpcChaseStallTicks"/> the chaser is considered stalled.</summary>
    public int ChaseStallTicks { get; set; }

    /// <summary>Identity the stall counters are measured against — the player index for a player
    /// target, or the negated <see cref="EncodeNpcId"/> for an NPC target.  A change resets the
    /// counters so each fresh chase starts clean.  0 = none.</summary>
    public int ChaseTargetKey { get; set; }

    /// <summary>Last cardinal direction stepped while chasing — powers the momentum shortcut, and once
    /// stalled against an UNREACHABLE target lets the best-effort walk refuse the exact reversal (anti
    /// wall-pacing).  <see cref="ChaseHasLastStep"/> distinguishes "no step yet" from <see cref="Direction.Up"/>
    /// (the zero enum value).</summary>
    public Direction ChaseLastStepDir { get; set; }
    public bool ChaseHasLastStep { get; set; }

    /// <summary>Restart chase-stall tracking — called when the chase target changes so a fresh
    /// pursuit isn't immediately judged stalled against the previous target's distances.</summary>
    public void ResetChaseStall()
    {
        ChaseBestWorldDist = int.MaxValue;
        ChaseStallTicks = 0;
        ChaseHasLastStep = false;
    }

    /// <summary>Start-of-engagement roll for a fresh chase target: decide whether an AoS chaser COMMITS to
    /// running the opening approach (<see cref="Constants.NpcApproachRushChancePct"/>) or conserves SP and
    /// walks in, and clear <see cref="HasMadeContact"/> so the "run once engaged" gate re-arms.  Called from
    /// the chase steppers on a target change; the guest handoff copies the result instead of re-rolling.</summary>
    public void BeginEngagement()
    {
        RushCommitted = Random.Shared.Next(100) < Constants.NpcApproachRushChancePct;
        HasMadeContact = false;
        ChaseSprinting = false;
    }

    /// <summary>The NPC was DRAWN INTO combat (attacked) rather than spotting a target on its own, so it commits
    /// to RUNNING the approach immediately — no cautious walk-in, no charge roll (being engaged is different from
    /// initiating).  Sets <see cref="RushCommitted"/> and, for a genuinely fresh target, CLAIMS the chase
    /// engagement (advances <see cref="ChaseTargetKey"/> + resets stall) so the legs pass's
    /// <see cref="BeginEngagement"/> won't fire on this target and re-roll the rush back to a walk-in.  Called
    /// from the attacked-aggro paths after the target is set.  Idempotent when already engaging this target.</summary>
    public void BeginRushEngagement()
    {
        RushCommitted = true;
        int key = Target > 0 ? Target : -EncodeNpcId(NpcTargetSpawnMap, NpcTargetSpawnSlot);
        if (key != ChaseTargetKey)   // fresh engagement — claim it so the legs pass leaves the rush intact
        {
            ChaseTargetKey = key;
            HasMadeContact = false;
            ChaseSprinting = false;
            ResetChaseStall();
        }
    }

    // Last player index this NPC said its AttackSay to.  Survives combat-expiry (so a player who
    // chips away from behind cover doesn't re-trigger the line on every fresh acquisition); cleared
    // only on respawn/death/border-vacate so a brand-new NPC greets each attacker once.
    public int LastAttackSayTarget { get; set; }

    // Parallel dedup for NPC targets — encoded as spawnMap * NpcIdStride + spawnSlot so a single int
    // suffices.  0 = no NPC AttackSay issued yet.  Same lifecycle as LastAttackSayTarget.
    public int LastAttackSayNpcTarget { get; set; }

    // Stride is large enough that no (map, slot) pair can collide with another (Constants.MaxMapNpcs
    // is far below 100,000), so the encoded id is unique per identity.  Public so any combat / AI
    // code that needs to encode an NPC id uses the same key.
    public const int NpcIdStride = 100_000;
    public static int EncodeNpcId(int spawnMap, int spawnSlot)
        => spawnSlot > 0 ? spawnMap * NpcIdStride + spawnSlot : 0;
}

/// <summary>One NPC contributor's damage on a victim NPC.  Keyed by stable (SpawnMap, SpawnSlot)
/// identity so a native that became a guest mid-fight still matches its earlier hits.</summary>
public readonly record struct NpcDamageEntry(int SpawnMap, int SpawnSlot, int Damage);

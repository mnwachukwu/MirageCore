using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The caster brain: whether to cast this beat, what to cast it at, and the kiting that
/// backs a caster out of melee to keep its range advantage.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    /// <summary>Per-tick magic decision for an aggressive NPC against a player target.  Thin wrapper
    /// around <see cref="TryNpcMagicActionCore"/> with player-victim args.  Returns true if a magic
    /// action consumed the tick (cast, kite step, or hold-at-range), false to fall through to melee.</summary>
    private bool TryNpcMagicAction(int mapNum, int slot, MapNpcRecord mn, int target, PlayerRecord vp, long now)
        => TryNpcMagicActionCore(mapNum, slot, mn, vp.Map, vp.X, vp.Y, now, target, 0, null);

    /// <summary>NPC-victim variant, used by the NPC-vs-NPC combat step.  Same gates and same
    /// kite/cast/hold behavior; dispatches to <see cref="CombatSystem.NpcCastSpellOnNpc"/> instead
    /// of <see cref="CombatSystem.NpcCastSpellOnPlayer"/>.</summary>
    private bool TryNpcMagicActionVsNpc(int mapNum, int slot, MapNpcRecord mn, int victimMap, int victimSlot, MapNpcRecord victimMn, long now)
        => TryNpcMagicActionCore(mapNum, slot, mn, victimMap, victimMn.X, victimMn.Y, now, 0, victimSlot, victimMn);

    /// <summary>Shared cast-decision body for both player-victim and NPC-victim paths.  Int=0 NPCs
    /// short-circuit before any cast logic runs and behave exactly like pure-melee NPCs.  All victim
    /// references are primitive coords (cross-seam aware via <see cref="WorldCoordHelper.ToWorldRelative"/>);
    /// the dispatch into the actual cast method branches on which victim type is non-zero.</summary>
    private bool TryNpcMagicActionCore(int mapNum, int slot, MapNpcRecord mn, int victimMap, int victimX, int victimY, long now,
                                        int playerVictimIdx, int npcVictimSlot, MapNpcRecord? npcVictimMn)
    {
        var npc = _world.Npcs[mn.Num];
        if (npc.Int <= 0) return false;
        // Recomputed below — set true only if THIS eval decides to kite (the legs pass reads it to continue the
        // retreat; the magic-push sites skip the "hold the legs off" push while it's set).
        mn.WantsKite = false;

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var tw = grid.ToWorldRelative(victimMap, victimX, victimY);
        if (tw is null) return false;
        int tgtWX = tw.Value.worldX, tgtWY = tw.Value.worldY;

        // Two-plane connect gate: the NPC only reaches a target its layer connects to — same plane, or a ramp
        // bridging them.  Gates BOTH the cross-layer cast (hasLoS, below) and the melee-range/bail-out logic
        // (inMelee), mirroring the player's cast + melee layer gate — so a ground NPC can't cast up at a bridged
        // player it isn't connected to.  Same-layer targets always connect, so flat-map behavior is unchanged.
        var castGrid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var victimLayer = npcVictimMn?.Layer ?? _pm[playerVictimIdx].Char.Layer;
        bool layerConnects = LayerLogic.LayerConnects(new ServerTileView(_world, castGrid), npcWX, npcWY, mn.Layer, tgtWX, tgtWY, victimLayer);

        int casterSize = npc.EffectiveSize;
        int victimSize = npcVictimMn is not null ? _world.Npcs[npcVictimMn.Num].EffectiveSize : 1;
        // Edge to edge, matching the melee gate: an oversize body is pinned when its EDGE touches, not when its
        // anchor sits one tile off — which for a size-3 caster never happens, so it never counted itself pinned.
        bool inMelee = layerConnects && WorldCoordHelper.AreFootprintsAdjacent(npcWX, npcWY, casterSize, tgtWX, tgtWY, victimSize);

        // Any tick we're NOT in melee resets the bail-out counter — the NPC successfully broke
        // off (even briefly), so retreat attempts start over fresh next time it's pinned.
        if (!inMelee) mn.MeleeKiteAttempts = 0;

        // hasMana gates on the ACTUAL SubHp cast cost via NpcCanAffordCast (the real pool-fraction, not just >= 1):
        // a caster below that threshold genuinely can't cast, so the beat falls through to MELEE (the !hasMana
        // return below) instead of reaching a cast branch that refuses the cast (Mp < cost, inside NpcCastSpell*)
        // yet still CONSUMES the beat.  Shared with CasterHoldsAtCastRange so the cast decision and the legs'
        // hold-at-range decision agree — a caster that runs its pool near-empty MELEES instead of standing idle.
        bool hasMana = NpcCanAffordCast(mn, npc);
        // Heavy Wind doubles the cast-to-cast cooldown (there is no post-cast move lock to scale).
        long windMult = _world.WeatherOn(mapNum) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        // castReady uses AttackTimer (shared with melee) for player-parity cooldown beats.
        bool castReady = AiCadence.Elapsed(now, mn.AttackTimer, Constants.SpellCastCooldownMs * windMult);

        // Out of mana — fall through to the close-distance / melee logic.  Mana regenerates; on the next
        // ready tick the NPC re-enters this branch.  There is no post-cast move lock, so a caster is free
        // to kite / reposition the instant it has cast (see Constants.SpellCastCooldownMs).
        if (!hasMana) return false;

        // Melee-vs-magic WEAVE, committed for a short run of beats (see RollCastModality + WeaveModalityBeatsLeft)
        // so it doesn't flicker cast↔melee every beat.  The chosen modality latches in WeaveCastThisBeat (rolled
        // on the rising edge of castReady) so the 500ms brain and the 100ms legs pass agree within the beat.  A
        // MELEE beat returns false here so the brain's melee fall-through closes in and swings — the same path an
        // Int=0 or out-of-mana NPC uses.
        bool freshBeat = castReady && !mn.WeaveWasReady;
        mn.WeaveWasReady = castReady;
        if (freshBeat)
        {
            // Commit to whichever modality we roll for a short run of beats (NpcWeaveCommitMin..MaxBeats) before
            // re-rolling — re-rolling every beat read as twitchy/mechanical cast↔melee flicker.
            if (mn.WeaveModalityBeatsLeft <= 0)
            {
                mn.WeaveCastThisBeat = RollCastModality(mn, npc);
                mn.WeaveModalityBeatsLeft = Rng.Next(Constants.NpcWeaveCommitMinBeats, Constants.NpcWeaveCommitMaxBeats + 1);
            }
            mn.WeaveModalityBeatsLeft--;
        }
        if (castReady && !mn.WeaveCastThisBeat) return false;

        // Compute spell-range + LoS once — used both for the "cast at range" path and the
        // "hold at range during cooldown / between-roll" path below.
        bool inSpellRange = WorldCoordHelper.IsInSpellRange(npcWX, npcWY, casterSize, tgtWX, tgtWY, victimSize);
        bool hasLoS = false;
        if (inSpellRange && layerConnects)
        {
            // Cross-layer cast: ramp tiles on the line are walls (can't cast through a ramp to a target behind/under it).
            hasLoS = WorldCoordHelper.HasClearSpellLineOfSight(npcWX, npcWY, tgtWX, tgtWY,
                                                               new WorldLosPredicate(_world, castGrid, mn.Layer, blockRamps: mn.Layer != victimLayer));
        }

        // A CAST beat (the weave above returned melee beats early).  Handle the three cast positions:
        // retreat-then-cast if the target closed to melee, cast in place at safe range, or fall through
        // to close distance when out of range / LoS.  When Mp later drops to 0 the `hasMana` gate above
        // flips false and the NPC falls through to melee regardless of the roll.
        if (castReady)
        {
            if (inMelee)
            {
                // ARCHETYPE SPLIT: only an INT-PRIMARY caster (Int>Str, incl. a pure caster with Str=0) kites to
                // reopen spell range.  A Str>=Int (melee-primary or balanced) mob does NOT kite — it casts FROM
                // melee range, keeping melee distance so its DPS is steady whether it swings or casts (no kite-
                // then-reclose gap).  Either way, a cornered / kite-capped / melee-primary caster falls through to
                // the melee-range cast below.
                //
                // Prefer to retreat to spell range so the next cast lands from safety — but only up
                // to the kite cap AND only if an open retreat tile actually exists.  A cornered mage
                // (every in-range retreat direction blocked by a wall/occupancy) must NOT burn the
                // tick on a failed move: TryKiteStepAwayFromTarget returns false and we fall straight
                // through to the bail-out cast below.  Counting only *successful* steps toward the cap
                // keeps its meaning "the target keeps following my retreat" rather than "I keep
                // failing to move against a wall" — so a wall-pinned mage never latches the cap by
                // standing still, and resumes kiting the instant a retreat tile opens up.
                if (npc.Int > npc.Str && mn.MeleeKiteAttempts < Constants.NpcMeleeKiteMaxAttempts)
                {
                    // Kiting is RUN-paced when the caster has stamina (walk when drained), like a chase — so it
                    // can actually gain range on a running attacker — and is NEVER gated by SP (always retreat
                    // when the AI wants distance).  The brain takes the FIRST retreat here; on success it flags
                    // WantsKite so the fast legs pass CONTINUES the retreat at run cadence (short NextMoveMs)
                    // instead of one tile per 500ms brain tick.  A cornered caster (no open retreat) falls
                    // through to the bail-out melee cast below.
                    bool kiteRunning = NpcCanRun(mapNum, mn);
                    mn.MoveType = kiteRunning ? MovementType.Running : MovementType.Walking;
                    // Apply the retreat bookkeeping BEFORE the step: if this retreat crosses a map seam it converts
                    // the native into a fresh traversal-guest record, and NativeNpcCrossBorder copies WantsKite +
                    // MeleeKiteAttempts + the already-spent Sp across so the guest keeps kiting instead of reverting
                    // to a chase.  A cornered retreat makes no move (no cross; native stays live), so the speculative
                    // bookkeeping is rolled back below.  For a within-map step this ordering is behavior-identical to
                    // applying it after — each field still lands iff the NPC actually moved.
                    int spBeforeKite = mn.Sp;
                    mn.WantsKite = true;
                    mn.MeleeKiteAttempts++;
                    if (kiteRunning) mn.Sp = Math.Max(mn.Sp - NpcRunSpDrain(mapNum), 0);
                    if (TryKiteStepAwayFromTarget(mapNum, slot, mn, npcWX, npcWY, tw, victimSize))
                    {
                        mn.MoveType = MovementType.Walking;   // reset the live native's one-shot pace; no-op on a crossed (dead) record
                        mn.NextMoveMs = now + (long)MathF.Round(kiteRunning ? MovementFormulas.NpcRunMsPerTile(npc.Spd) : MovementFormulas.NpcWalkMsPerTile);
                        return true;
                    }
                    // Cornered: undo the speculative bookkeeping and fall through to the bail-out cast below.
                    mn.Sp = spBeforeKite;
                    mn.MeleeKiteAttempts--;
                    mn.WantsKite = false;
                    mn.MoveType = MovementType.Walking;
                }
                // Bail-out: either the target is pinning us (kite cap reached) or we're cornered (no
                // open retreat).  Either way, don't stand still — cast at melee range and accept the
                // retaliation.  Don't reset MeleeKiteAttempts here — leaving it at the cap latches the
                // bail-out so each subsequent castReady tick also melee-casts instead of restarting
                // the kite cycle.  The !inMelee reset above handles natural disengagement: once the
                // target actually moves away, attempts falls to 0 and the next melee encounter gets a
                // fresh kite cycle.  Without the latch, two pinning targets produce a visible ~3-4 s
                // dance (3 kites → bail-out → reset → 3 kites again).
                FaceNpcToward(mapNum, slot, mn, victimMap, victimX, victimY);
                DispatchNpcCast(mapNum, slot, mn, playerVictimIdx, victimMap, npcVictimSlot, npcVictimMn);
                return true;
            }
            // Not in melee, ready to cast: an INT-PRIMARY caster casts in place at spell range; a Str>=Int mob does
            // NOT park at range — it closes to melee first (falls through), then casts from melee the next beat.
            if (npc.Int > npc.Str && inSpellRange && hasLoS)
            {
                // At safe distance with a clear line — cast now.  Face target so the client
                // batches Dir+Cast into the same frame.
                FaceNpcToward(mapNum, slot, mn, victimMap, victimX, victimY);
                DispatchNpcCast(mapNum, slot, mn, playerVictimIdx, victimMap, npcVictimSlot, npcVictimMn);
                return true;
            }
            // Ready to cast but out of spell range / no LoS / a melee-primary mob — fall through to close-distance.
            return false;
        }

        // Already at safe spell range with LoS AND mana on a CAST beat — an INT-PRIMARY caster HOLDS POSITION so a
        // cooldown-locked tick doesn't walk it back into melee.  A Str>=Int mob does NOT hold (it fights AT melee),
        // and on a MELEE beat neither holds — both fall through to close in and swing instead of parking at range.
        if (npc.Int > npc.Str && !inMelee && inSpellRange && hasLoS && mn.WeaveCastThisBeat)
        {
            FaceNpcToward(mapNum, slot, mn, victimMap, victimX, victimY);
            return true;
        }

        return false;
    }

    /// <summary>Branch to the appropriate <c>NpcCastSpell*</c> overload based on which victim
    /// argument is set.  Exactly one of <paramref name="playerVictimIdx"/> > 0 or
    /// <paramref name="npcVictimMn"/> non-null is expected to hold.</summary>
    private void DispatchNpcCast(int mapNum, int slot, MapNpcRecord mn, int playerVictimIdx, int victimMap, int npcVictimSlot, MapNpcRecord? npcVictimMn)
    {
        if (playerVictimIdx > 0) _combat.NpcCastSpellOnPlayer(mapNum, slot, mn, playerVictimIdx, _pathNow);
        else if (npcVictimMn is not null) _combat.NpcCastSpellOnNpc(mapNum, slot, mn, victimMap, npcVictimSlot, npcVictimMn);
    }

    /// <summary>One zig-zag retreat step that stays inside the R=5 spell circle AND keeps LoS to
    /// target.  Tries EVERY retreat direction before giving up — straight-away, then both
    /// perpendiculars — with a ~33% chance to lead with a perpendicular for zigzag unpredictability;
    /// only the toward-target direction is excluded (it closes distance).  Validates each candidate
    /// position against IsInSpellRange + HasClearSpellLineOfSight on the projected post-step world
    /// coords, so the NPC never kites itself out of range or behind a wall — the whole point is to
    /// cast again next tick.  Returns true iff the NPC actually stepped; false only when ALL three
    /// retreat directions are out of range, LoS-blocked, or terrain-blocked (truly cornered),
    /// letting the caller fall through to a bail-out cast instead of burning the tick.  Facing is
    /// the caller's responsibility on the false path (its bail-out cast faces the target).</summary>
    private bool TryKiteStepAwayFromTarget(int mapNum, int slot, MapNpcRecord mn,
                                            int npcWX, int npcWY,
                                            (int worldX, int worldY)? tw, int targetSize)
    {
        if (tw is null)
        {
            // Target slipped outside observable area — can't compute a kite direction.
            return false;
        }
        int tgtWX = tw.Value.worldX;
        int tgtWY = tw.Value.worldY;

        // Direction TOWARD target; away = its opposite.  Mirrors TryComputeWorldDeltas's logic
        // (longer-axis-first) without needing the full helper's out-params.  The three retreat
        // candidates are every cardinal EXCEPT toward: straight-away, and the two perpendiculars
        // (perpAway leaves the target's secondary-axis side; perpOther is its opposite).  Toward is
        // never a retreat — it closes distance and, in melee, walks into the target's tile.
        Direction towardDir = WorldCoordHelper.WorldDirectionFrom(npcWX, npcWY, tgtWX, tgtWY);
        Direction awayDir = OppositeDir(towardDir);
        Direction perpAwayDir = PerpAwayDir(towardDir, tgtWX - npcWX, tgtWY - npcWY);
        // Computed from the ORIGINAL perpAwayDir, before the zigzag swap below, so it's always the
        // other perpendicular — never accidentally OppositeDir(awayDir) == towardDir.
        Direction perpOtherDir = OppositeDir(perpAwayDir);

        // Zigzag: ~33% chance lead with the perpendicular before the straight-away step.  Keeps the
        // retreat path from being a predictable straight line the player can intercept on the next
        // step.  Only the two primary retreats reorder; perpOther is always the last-resort tile.
        Direction firstDir = awayDir, secondDir = perpAwayDir;
        if (Rng.Next(3) == 0)
            (firstDir, secondDir) = (perpAwayDir, awayDir);

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var los = new WorldLosPredicate(_world, grid, mn.Layer);

        // Exhaust every retreat direction before giving up: the two primary retreats (order
        // zig-zagged above), then the remaining perpendicular as a last resort — so a mage boxed in
        // on its preferred sides still slips free if ANY castable (in-range + LoS) tile is open,
        // and only truly cornered (all three blocked) falls through to the caller's bail-out cast.
        if (TryKiteInDirection(mapNum, slot, mn, firstDir, npcWX, npcWY, tgtWX, tgtWY, targetSize, los)) return true;
        if (TryKiteInDirection(mapNum, slot, mn, secondDir, npcWX, npcWY, tgtWX, tgtWY, targetSize, los)) return true;
        if (TryKiteInDirection(mapNum, slot, mn, perpOtherDir, npcWX, npcWY, tgtWX, tgtWY, targetSize, los)) return true;

        // Every castable retreat direction is blocked (cornered).  Report the miss so the caller
        // casts at melee range instead of standing still; its bail-out faces the target first.
        return false;
    }

    /// <summary>Legs-pass continuation of a caster's kite: the brain took the first retreat and flagged
    /// WantsKite; this keeps retreating at the SPD run cadence (walk when out of SP), draining SP, until the
    /// kite cap is hit (hold, so the brain casts) or the caster is cornered (clear the flag so the brain
    /// bail-casts next tick).  Mirrors the brain's kite branch but runs on the fast movement pass so the
    /// retreat actually outpaces a pursuer.  <paramref name="slot"/> is the native slot or guest list index.</summary>
    private void TryLegsKite(int mapNum, int slot, MapNpcRecord mn, int targetMap, int targetX, int targetY, long now, int targetSize = 1)
    {
        if (mn.MeleeKiteAttempts >= Constants.NpcMeleeKiteMaxAttempts) return;   // cap reached — hold for the brain's cast
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        int spd = _world.Npcs[mn.Num].Spd;                       // capture before the step — a native→guest cross zeroes mn.Num
        bool running = NpcCanRun(mapNum, mn);
        mn.MoveType = running ? MovementType.Running : MovementType.Walking;
        // Bookkeeping BEFORE the step so a native→guest seam-cross carries the spent SP + attempt count to the guest
        // via NativeNpcCrossBorder; WantsKite is already set (that's why the legs pass is running) and rides across
        // too.  Cornered → roll back and clear WantsKite so the brain bail-casts next tick.  A within-map or guest→
        // guest step keeps the same record, so this ordering is behavior-identical to applying it after.
        int spBeforeKite = mn.Sp;
        mn.MeleeKiteAttempts++;
        if (running) mn.Sp = Math.Max(mn.Sp - NpcRunSpDrain(mapNum), 0);
        if (!TryKiteStepAwayFromTarget(mapNum, slot, mn, npcWX, npcWY, tw, targetSize))
        {
            mn.Sp = spBeforeKite;
            mn.MeleeKiteAttempts--;
            mn.WantsKite = false;                                 // cornered mid-retreat → brain bail-casts next tick
            mn.MoveType = MovementType.Walking;
            return;
        }
        mn.MoveType = MovementType.Walking;
        mn.NextMoveMs = now + (long)MathF.Round(running ? MovementFormulas.NpcRunMsPerTile(spd) : MovementFormulas.NpcWalkMsPerTile);
    }

    private bool TryKiteInDirection(int mapNum, int slot, MapNpcRecord mn, Direction dir, int npcWX, int npcWY, int tgtWX, int tgtWY,
                                     int targetSize, WorldLosPredicate los)
    {
        var npc = _world.Npcs[mn.Num];
        var (ddx, ddy) = WorldCoordHelper.DirDelta(dir);
        int newWX = npcWX + ddx;
        int newWY = npcWY + ddy;
        // Footprint-aware: a big caster kites to where its BODY (not its anchor) sits in cast range of the target body.
        if (!WorldCoordHelper.IsInSpellRange(newWX, newWY, npc.EffectiveSize, tgtWX, tgtWY, targetSize)) return false;
        if (!WorldCoordHelper.HasClearSpellLineOfSight(newWX, newWY, tgtWX, tgtWY, los)) return false;
        return StepNpc(mapNum, slot, mn, dir);
    }

    private static Direction OppositeDir(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        _ => d,
    };

    /// <summary>Perpendicular-axis direction pointing away from target.  If the primary toward-
    /// target axis is horizontal, picks Up/Down based on target's Y; if vertical, picks Left/Right
    /// based on target's X.  When the perpendicular delta is zero (target on the same secondary
    /// axis) BOTH perpendiculars are equally "away", so pick one at RANDOM.  A fixed default here
    /// (a fixed Down when horizontal, Right when vertical) would funnel wall-pinned kiters toward
    /// the bottom-right corner: a caster shoved against a side wall could only sidestep Down, one
    /// against the top/bottom wall could only sidestep Right, so cornered deaths piled up in the
    /// lower-right.  Randomizing the tie alone (the caller still tries this perpendicular AND its
    /// opposite) removes the drift without changing which tiles the retreat can reach.</summary>
    private Direction PerpAwayDir(Direction primaryToward, int dx, int dy)
    {
        bool primaryHorizontal = primaryToward == Direction.Left || primaryToward == Direction.Right;
        if (primaryHorizontal)
            return dy > 0 ? Direction.Up : dy < 0 ? Direction.Down : RandomPerpendicular(primaryToward);
        return dx > 0 ? Direction.Left : dx < 0 ? Direction.Right : RandomPerpendicular(primaryToward);
    }
}

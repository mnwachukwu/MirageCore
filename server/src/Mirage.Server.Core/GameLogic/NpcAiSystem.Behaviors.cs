using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The per-map AI passes and the behavior brains they drive: the observed-map pass, the
/// cheaper unobserved upkeep, and the aggressive / janitor / wander routines an NPC runs
/// depending on what it is and whether anything is in reach.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Light AI for a map with no observers: a vacated town's guards still tidy player-dropped litter
    // so it's clean when someone returns.  No observers means no players in range, so there's nothing
    // to target and nothing to broadcast — we skip the whole aggressive/wander path and run only the
    // janitor sweep.  Non-Safe maps (where guards don't janitor) return immediately, and a single
    // litter check up front skips the per-guard item scan entirely on an already-clean town (the
    // common steady state) — so an idle vacated town costs one MaxMapItems scan per tick, not N.
    private void RunUnobservedUpkeep(int mapNum)
    {
        if (_world.MoralOf(mapNum) != MapMoral.Safe) return;
        if (!HasDroppedItems(mapNum)) return;
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num <= 0 || mn.IsReservedSlot) continue;
            if (_world.Npcs[mn.Num].Behavior != NpcBehavior.Guard) continue;
            if (mn.Target != 0 || mn.NpcTargetSpawnSlot != 0) continue;  // still winding down a chase; RunUnobservedCombat handles it
            RunJanitorAi(mapNum, slot, mn);
        }
    }

    // True if the map holds any voluntary-player-dropped item — a janitor's only concern.  Death drops
    // (PlayerDeathDropped) and NPC loot (NpcDropped) are deliberately excluded so corpses in safe
    // zones stay lootable.  One list scan, used to skip the per-guard litter search on a vacated
    // town that's already clean.
    private bool HasDroppedItems(int mapNum)
    {
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
            if (list[i].Source == ItemSource.PlayerDropped) return true;
        return false;
    }

    // Combat upkeep for a map with no observers.  A native NPC here can still hold a target whose
    // player just LEFT or WARPED away — a true warp un-observes this map, which would otherwise freeze
    // the chaser the instant its quarry teleported out of sight.  Per NPC: if its combat has lapsed,
    // reset it (restore vitals, drop the target — the chase ends); otherwise the lock is still live, so
    // keep driving the pursuit (the warp-follow path) so a guard follows a fleeing player out through a
    // town's warp for the rest of the combat timer.  One pass handles both, so no extra per-tick scan.
    private void RunUnobservedCombat(int mapNum, long now)
    {
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num == 0) continue;
            if (!mn.WasInCombat && mn.Target == 0 && mn.NpcTargetSpawnSlot == 0) continue;  // idle — nothing combat-related to do
            bool nowInCombat = mn.IsInCombat(now);
            if (mn.WasInCombat && !nowInCombat)
            {
                var npc = _world.Npcs[mn.Num];
                mn.WasInCombat = false;
                mn.CombatExpiresAt = 0;
                mn.Hp = _world.EffectiveNpcMaxHp(npc);
                mn.Mp = _world.EffectiveNpcMaxMp(npc);
                mn.Sp = _world.EffectiveNpcMaxSp(npc);
                mn.ClearDamageCredit();
                mn.Target = 0;
                mn.NpcTargetSpawnMap = 0;
                mn.NpcTargetSpawnSlot = 0;
                mn.LastAttackSayNpcTarget = 0;
                // LastReachedTargetMs intentionally not reset — see the matching note in
                // RunAiForMap's combat-exit block.  Acquisition restamps it when a target is set.
            }
            else if (mn.Target > 0)
            {
                // The target is necessarily off this unobserved map, so this drives the warp-follow
                // (combat is refreshed inside only for a relentless pursuer; non-relentless ones lapse
                // above when the timer runs out).  Drop the lock if the player has left the game.
                if (_pm[mn.Target].IsPlaying)
                    NativeChaseAcrossBorder(mapNum, slot, mn, mn.Target, now);
                else
                    DropNativeTarget(mapNum, slot, mn);
            }
            // No need to drive NpcTarget pursuit here — an unobserved map has no players to attract
            // hostile NPCs into viewport in the first place; if an NPC-target lingers it's because
            // the victim NPC despawned or chased a player elsewhere, and the combat timer will lapse.
        }
    }
    // Brain pass for one observed map: regen, combat-exit cleanup, then a per-behavior branch —
    // AttackOnSight scans for players and falls back to a hostile NPC, AttackWhenAttacked only retaliates,
    // Guard prioritizes PKs then hostile NPCs then litter, Stationary does nothing, and Friendly wanders.
    private void RunAiForMap(int mapNum, long now, bool regenTick)
    {
        for (int slot = 1; slot <= Constants.MaxMapNpcs; slot++)
        {
            var mn = _world.MapNpcs[mapNum, slot];
            if (mn.Num <= 0) continue;

            var npc = _world.Npcs[mn.Num];

            // HP regen — only fires once the regen interval has elapsed.
            if (regenTick)
                RegenNpcVitals(mapNum, mn, npc, now);

            // Combat-exit: WasInCombat is set immediately in MarkNpcCombat; cleared here when timer lapses.
            bool nowInCombat = mn.IsInCombat(now);
            if (mn.WasInCombat && !nowInCombat)
            {
                mn.WasInCombat = false;
                mn.CombatExpiresAt = 0;  // reset so stale expiry doesn't short-circuit future target propagation
                // AttackOnSight and AttackWhenAttacked both disengage when combat expires: neither
                // refreshes the timer by chasing (see IsRelentlessPursuit), so a target that breaks
                // contact for the window is let go. CombatExpiresAt is already 0 here, so
                // RunAggressiveAi can't detect expiry — handle the target drop now instead.
                if (mn.Target > 0 && npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)
                {
                    mn.Target = 0;
                    SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = false });
                }
                // Guards drop non-PK/non-PvP targets on expiry; CombatExpiresAt is 0 here so the
                // RunAggressiveAi dropOnExpiry path can't fire — same reason as AWA above.
                if (npc.Behavior == NpcBehavior.Guard && mn.Target > 0)
                {
                    int tgt = mn.Target;
                    bool targetIsPk = _pm[tgt].IsPlaying && _pm[tgt].Char.IsPk(NowUtc);
                    bool targetIsActivePvpAggr = _pm[tgt].IsPlaying && _pm[tgt].PvpAttackerUntil > now;
                    if (!targetIsPk && !targetIsActivePvpAggr)
                    {
                        mn.Target = 0;
                        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = false });
                    }
                }
                // NPC-target unconditionally drops on combat expiry — no PK exception for NPC targets.
                if (mn.NpcTargetSpawnSlot > 0)
                {
                    mn.NpcTargetSpawnMap = 0;
                    mn.NpcTargetSpawnSlot = 0;
                    SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = mn.Target != 0 });
                }
                mn.LastAttackSayNpcTarget = 0;
                // LastReachedTargetMs is left as it lies. Every target — player and NPC alike — has
                // been dropped by this point, and acquisition (TryAcquire*, AlertNpc, ...) calls
                // MarkReachedTarget, so the next lock starts on a fresh clock whatever this holds.
                // It feeds the unreachable give-up gate, which is a shorter fuse for a mob that
                // cannot act at all rather than a second copy of this one.
                ResetNativeNpc(mn, mapNum, slot, npc);
            }

            switch (npc.Behavior)
            {
                case NpcBehavior.AttackOnSight:
                    // AoS mobs commit to whatever they acquire — only aggro flips it.  If we already
                    // have an NpcTarget, pass onlyWhenAttacked=true so RunAggressiveAi's player-
                    // acquisition scan is suppressed (so a passing player can't pre-empt the chase).
                    // ReEvaluateAggro still runs each tick, so damage-driven flips
                    // (SetNpcAggroFromNpc / AlertNpcFromNpc) can still hand the AoS over to a player
                    // contributor on its ledger — that's the intended way to lose an NPC target.
                    // Safe-zone aggro rule: if standing on a safe map with a guard in viewport, drop
                    // non-guard targets and lock onto the nearest guard.  Without a guard nearby the
                    // mob behaves normally (harmful to the dragger).
                    EnforceSafeZoneAggroRule(mn, mapNum, slot, now);
                    RunAggressiveAi(mapNum, slot, mn, now, onlyWhenAttacked: mn.NpcTargetSpawnSlot > 0);
                    // No player target found → try a different-kind hostile NPC within Range.
                    if (mn.Target == 0 && mn.NpcTargetSpawnSlot == 0)
                        TryAcquireAosNpcTarget(mapNum, slot, mn, now);
                    if (mn.NpcTargetSpawnSlot > 0)
                        RunNpcVsNpcStep(mapNum, slot, mn, now);
                    break;

                case NpcBehavior.AttackWhenAttacked:
                    // AWA retaliates against whoever attacks it, safe zones included;
                    // it is exempt from the safe-zone guard-redirect rule.  Guard-assisted safe-zone
                    // kills instead pay the player no EXP/loot (see CombatSystem.ExecuteNpcDamage) so
                    // guards still can't be used to tank-farm dragged mobs in town.
                    RunAggressiveAi(mapNum, slot, mn, now, true);
                    // AWA mobs never initiate NPC combat — they retaliate via aggro flip in
                    // SetNpcAggroFromNpc when struck.  Still need to pursue the NPC target once flipped.
                    if (mn.NpcTargetSpawnSlot > 0)
                        RunNpcVsNpcStep(mapNum, slot, mn, now);
                    break;

                case NpcBehavior.Guard:
                    RunAggressiveAi(mapNum, slot, mn, now, false);  // PK/PvP scan + chase
                    // PK pre-empt for the NPC-target case: a guard chasing a wolf must drop it the
                    // moment a PK appears (PK always wins).  The existing PK swap inside RunAggressiveAi
                    // only fires when Target > 0 (player target held), so the NpcTarget case needs an
                    // explicit check here.
                    if (mn.Target == 0 && mn.NpcTargetSpawnSlot > 0)
                    {
                        int pkTarget = FindGuardTarget(mapNum, mn, now);
                        if (pkTarget > 0)
                        {
                            mn.NpcTargetSpawnMap = 0;
                            mn.NpcTargetSpawnSlot = 0;
                            mn.Target = pkTarget;
                            mn.MarkReachedTarget(now);
                            _combat.MarkNpcCombat(mapNum, slot, now);
                            SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
                            if (mn.LastAttackSayTarget != pkTarget && !string.IsNullOrWhiteSpace(npc.AttackSay))
                            {
                                mn.LastAttackSayTarget = pkTarget;
                                _dispatcher.SendLocalizedChatTo(pkTarget, ServerStrings.CombatSystem_NpcSays,
                                    new ChatMetadata(GameColor.Npc, ChatChannel.Say),
                                    ("NpcName", npc.TrimmedName), ("Say", npc.AttackSay.TrimEnd()));
                                _dispatcher.SendTo(pkTarget, PacketBuilder.NpcChatBubble(mapNum, slot, npc.AttackSay.TrimEnd(), kind: 0));
                            }
                        }
                    }
                    // Idle (no PK, no current NPC chase) → scan for a hostile NPC chasing any player in viewport.
                    if (mn.Target == 0 && mn.NpcTargetSpawnSlot == 0)
                        TryAcquireGuardNpcTarget(mapNum, slot, mn, now);
                    // Pursue NPC target if set (PK chase still goes through RunAggressiveAi above).
                    if (mn.NpcTargetSpawnSlot > 0)
                        RunNpcVsNpcStep(mapNum, slot, mn, now);
                    // Janitor only when completely idle.
                    if (mn.Target == 0 && mn.NpcTargetSpawnSlot == 0 && _world.MoralOf(mapNum) == MapMoral.Safe)
                        RunJanitorAi(mapNum, slot, mn);
                    break;

                case NpcBehavior.Stationary:
                    break;

                default: // Friendly — amble in committed strides (see WanderStep).
                    WanderStep(mapNum, slot, mn);
                    break;
            }
        }
    }

    private void RunAggressiveAi(int mapNum, int slot, MapNpcRecord mn, long now, bool onlyWhenAttacked)
    {
        var npc = _world.Npcs[mn.Num];
        // Per-tick aggro re-eval — runs BEFORE the existing acquisition / chase logic so a flipped
        // target is what the rest of the function operates on.  Catches contributor disappearance
        // (logout, out-of-area) without a damage event.  No-op when the ledger is empty (an idle-
        // acquired target hasn't been hit yet) so a fresh acquisition doesn't immediately drop.
        if (mn.Target > 0 || mn.NpcTargetSpawnSlot > 0)
            _combat.ReEvaluateAggro(mapNum, slot, mn);

        // If no target, find one (attack-on-sight searches all players; guards search whole-map PK-only; attack-when-attacked waits to be hit)
        if (mn.Target == 0 && !onlyWhenAttacked)
        {
            mn.Target = npc.Behavior == NpcBehavior.Guard
                ? FindGuardTarget(mapNum, mn, now)
                : FindLowestLevelPlayer(mapNum, mn, npc.Range);
            // Combat target acquired — release any janitor claim so the item is free for another guard.
            if (mn.Target > 0 && mn.JanitorTarget > 0)
                mn.JanitorTarget = 0;
            if (mn.Target > 0)
            {
                mn.MarkReachedTarget(now);
                _combat.MarkNpcCombat(mapNum, slot, now);
                SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
                if (mn.LastAttackSayTarget != mn.Target && !string.IsNullOrWhiteSpace(npc.AttackSay))
                {
                    mn.LastAttackSayTarget = mn.Target;
                    _dispatcher.SendLocalizedChatTo(mn.Target, ServerStrings.CombatSystem_NpcSays,
                        new ChatMetadata(GameColor.Npc, ChatChannel.Say),
                        ("NpcName", npc.TrimmedName), ("Say", npc.AttackSay.TrimEnd()));
                    _dispatcher.SendTo(mn.Target, PacketBuilder.NpcChatBubble(mapNum, slot, npc.AttackSay.TrimEnd(), kind: 0));
                }
            }
        }

        if (mn.Target > 0)
        {
            int target = mn.Target;
            long nowUtcForGrace = NowUtc;
            bool combatExpired = mn.CombatExpiresAt > 0 && now >= mn.CombatExpiresAt;
            bool targetIsPk = _pm[target].IsPlaying && _pm[target].Char.IsPk(nowUtcForGrace)
                                           && _pm[target].PkGraceUntilUtc <= nowUtcForGrace;
            bool targetIsActivePvpAggr = _pm[target].PvpAttackerUntil > now;
            bool dropOnExpiry = npc.Behavior switch
            {
                NpcBehavior.AttackOnSight => false,
                NpcBehavior.Guard => !targetIsPk && !targetIsActivePvpAggr,
                _ => true,
            };
            // Note: a target that moved to a *different* observable map is NOT dropped here — it is
            // chased across the border below.  Only an absent player or expired combat drops it.
            if (!_pm[target].IsPlaying
                || (dropOnExpiry && combatExpired))
            {
                // If combat is still live and this NPC searches proactively, try to grab a new target
                // before going idle (e.g. guard retargets to another PK while the first player fled).
                bool nowInCombat = mn.IsInCombat(now);
                if (!onlyWhenAttacked && nowInCombat)
                {
                    mn.Target = npc.Behavior == NpcBehavior.Guard
                        ? FindGuardTarget(mapNum, mn, now)
                        : FindLowestLevelPlayer(mapNum, mn, npc.Range);
                }
                else
                {
                    mn.Target = 0;
                }
                if (!onlyWhenAttacked)
                    SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = mn.Target > 0 });
                if (mn.Target == 0) return;
                target = mn.Target;
                mn.MarkReachedTarget(now);  // re-acquired a fresh target — restart the unreachable clock
                if (mn.LastAttackSayTarget != target && !string.IsNullOrWhiteSpace(npc.AttackSay))
                {
                    mn.LastAttackSayTarget = target;
                    _dispatcher.SendLocalizedChatTo(target, ServerStrings.CombatSystem_NpcSays,
                        new ChatMetadata(GameColor.Npc, ChatChannel.Say),
                        ("NpcName", npc.TrimmedName), ("Say", npc.AttackSay.TrimEnd()));
                    _dispatcher.SendTo(target, PacketBuilder.NpcChatBubble(mapNum, slot, npc.AttackSay.TrimEnd(), kind: 0));
                }
            }

            // Guard PK priority: switch to a PK/PvP target immediately if the current one has lower priority.
            if (npc.Behavior == NpcBehavior.Guard)
            {
                bool currentIsPriority = (_pm[target].Char.IsPk(nowUtcForGrace) && _pm[target].PkGraceUntilUtc <= nowUtcForGrace)
                                         || _pm[target].PvpAttackerUntil > now;
                if (!currentIsPriority)
                {
                    int pkTarget = FindGuardTarget(mapNum, mn, now);
                    if (pkTarget > 0 && pkTarget != target)
                    {
                        mn.Target = pkTarget;
                        target = pkTarget;
                        mn.MarkReachedTarget(now);  // fresh target — restart the unreachable clock (Guards don't actually check it, but keep state consistent)
                        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
                        if (mn.LastAttackSayTarget != target && !string.IsNullOrWhiteSpace(npc.AttackSay))
                        {
                            mn.LastAttackSayTarget = target;
                            _dispatcher.SendLocalizedChatTo(target, ServerStrings.CombatSystem_NpcSays,
                                new ChatMetadata(GameColor.Npc, ChatChannel.Say),
                                ("NpcName", npc.TrimmedName), ("Say", npc.AttackSay.TrimEnd()));
                            _dispatcher.SendTo(target, PacketBuilder.NpcChatBubble(mapNum, slot, npc.AttackSay.TrimEnd(), kind: 0));
                        }
                    }
                }
            }

            var vp = _pm[target].Char;

            // ── NPC magic decision ────────────────────────────────────────────────
            // Implicit from the Int stat (no behavior flag): an Int=0 NPC short-circuits and never
            // casts.  An Int>0 NPC WEAVES melee and magic per ready beat — P(cast)=Int/(Int+Str), so a
            // Str-dominant mob mostly swings and an Int-dominant one mostly casts (see TryNpcMagicActionCore).
            // A cast beat is gated further by safe spell range + LoS (cast in place) or melee range (retreat,
            // then bail out and cast after 3 failed retreats so the player can't kite it indefinitely).  A
            // melee beat — or MP dropping to 0 — returns false and falls through to the melee logic below.
            if (TryNpcMagicAction(mapNum, slot, mn, target, vp, now))
            {
                // Brain handled this NPC this cycle.  If it CAST/HELD (not WantsKite), hold the legs off until
                // the next brain decision.  If it KITED (WantsKite), leave NextMoveMs alone — the brain set a
                // short run-cadence clock so the legs CONTINUE the retreat (so the caster gains range fast).
                if (!mn.WantsKite) mn.NextMoveMs = now + Constants.AiTickIntervalMs;
                return;
            }

            // AoS unreachable give-up: an AoS NPC that hasn't reached its target for too long drops
            // the lock AND fully resets (vitals restored, contributor ledger cleared, spawn-packet
            // refresh).  Reaches are counted from melee landing or a successful chase step that
            // closed world-distance to target — sliding along a wall doesn't count — so an AoS that
            // can't connect eventually times out instead of camping indefinitely.
            if (ShouldGiveUpUnreachableAosTarget(mn, now))
            {
                DropNativeTarget(mapNum, slot, mn);
                ResetNativeNpc(mn, mapNum, slot, npc);
                return;
            }

            // Strike first if the target is adjacent — including one tile across a map seam, where
            // the NPC can't step onto the player's occupied tile to "come to it", so only a cross-
            // seam swing can connect (otherwise a player standing just over the border is unhittable).
            if (_combat.CanNpcAttackPlayer(mapNum, slot, target, _pathNow))
            {
                var faceDir = FaceTargetDir(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, vp.Map, vp.X, vp.Y, mn.Dir);
                if (mn.Dir != faceDir)
                {
                    // The legs pass turns a freshly-arrived mob to face its target (post-slide, promptly); this
                    // is the fallback for the rare tick the brain beats it there or the target just sidestepped.
                    // Turn now — never mid-slide — and swing next tick.  No deliberate beat: the turn just
                    // precedes the strike.
                    if (now < mn.NextMoveMs) return;              // still sliding into place — finish the move first
                    BroadcastNpcDir(mapNum, slot, faceDir);
                    return;
                }
                _combat.NpcAttackPlayer(mapNum, slot, target, _pathNow);
                mn.AttackTimer = now;
                return;
            }

            // The target left the face, but a wide body is likely still pressed against by others. It is
            // already facing them and its beat is ready, so it swings at what is standing there rather than
            // turning away to chase — the cleave covers the whole edge, and the edge is what decides.
            if (_world.Npcs[mn.Num].EffectiveSize > 1 && _combat.FirstVictimOnFace(mapNum, mn, _pathNow) is { } onFace)
            {
                if (onFace.Npc is { } faceNpc)
                    _combat.NpcAttackNpc(mapNum, slot, mn, onFace.NpcMap, onFace.NpcSlot, faceNpc, _pathNow);
                else
                    _combat.NpcAttackPlayer(mapNum, slot, onFace.PlayerIndex, _pathNow);
                mn.AttackTimer = now;
                return;
            }

            // Not adjacent and on another observable map → chase across the border (the NPC becomes
            // a traversal guest), or drop the target if its map is no longer reachable.
            if (vp.Map != mapNum)
            {
                NativeChaseAcrossBorder(mapNum, slot, mn, target, now, legsStep: true);   // legs pass runs the cross-seam run/walk step
                return;
            }

            // Same map, not adjacent — close the distance.  AoS and Guard are both relentless: the
            // chase itself refreshes combat, so they don't time out mid-pursuit just because they
            // haven't landed a hit recently.  AWA (and any other yield-able behavior) skips this
            // refresh, so combat lapses naturally and they disengage.
            if (IsRelentlessPursuit(npc, target, now))
                _combat.MarkNpcCombat(mapNum, slot, now);
            // The chase-STEP — same-map AND cross-seam — runs on the fast legs pass (RunMovement →
            // AdvanceNativeChaseStep) at the NPC's SPD-scaled run pace, not on this 500ms brain tick.
            // Everything else for a chasing NPC (acquire, magic, give-up, attack, cross-border warp-follow +
            // combat refresh) belongs here on the brain.
        }
        // Wander only when fully idle.  An NPC chasing an NPC target (NpcTargetSpawnSlot > 0)
        // falls through here when called with onlyWhenAttacked=true; without the slot check it
        // would randomly wander mid-chase, disrupting the BFS pursuit RunNpcVsNpcStep runs right
        // after — visible as two NPCs "dancing" instead of closing on each other cleanly.
        else if (mn.NpcTargetSpawnSlot == 0)
        {
            WanderStep(mapNum, slot, mn);
        }
    }

    private void RunJanitorAi(int mapNum, int slot, MapNpcRecord mn)
    {
        // Step 1: service an existing claim.
        if (mn.JanitorTarget > 0)
        {
            var mi = _world.MapItemBySlot(mapNum, mn.JanitorTarget);
            if (mi is null || mi.Source != ItemSource.PlayerDropped)
            {
                mn.JanitorTarget = 0;  // stale — fall through to search
            }
            else if (mn.X == mi.X && mn.Y == mi.Y && mn.Layer == mi.Layer)
            {
                // Reached the item — on its own layer (a bridge-top drop is cleared from the deck, not from
                // under it) — clear it and save.
                int targetSlot = mn.JanitorTarget;
                mn.JanitorTarget = 0;
                _items.RemoveMapItem(mapNum, targetSlot);
                _items.EnqueueSaveDroppedItems(mapNum);
                return;
            }
            else
            {
                // Route toward the litter on ITS layer — the layer-aware BFS climbs a ramp to reach a bridge-top
                // drop, so a ground guard still tidies fringe litter instead of being blind to it.
                StepNpcTowardObservableArea(mapNum, slot, mn, mapNum, mi.X, mi.Y, mi.Layer);
                return;
            }
        }

        // Step 2: find an unclaimed dropped item anywhere on the map.
        var list = _world.MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
        {
            var mi = list[i];
            if (mi.Num == 0 || mi.Source != ItemSource.PlayerDropped) continue;
            // Check that no other guard on this map has already claimed this slot id.
            bool claimed = false;
            for (int g = 1; g <= Constants.MaxMapNpcs; g++)
            {
                if (g == slot) continue;
                if (_world.MapNpcs[mapNum, g].JanitorTarget == mi.Slot)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed) continue;
            mn.JanitorTarget = mi.Slot;
            return;
        }
    }

    // ── Wander (committed-stride ambling) ───────────────────────────────────────
    // Idle NPCs (native or guest) stroll in strides instead of taking isolated random steps: on a
    // NpcWanderStartChancePerTick roll, commit to a heading and a length
    // of NpcWanderStride{Min,Max}Tiles, then walk it one tile per AI tick (gapless at the tick-matched NPC
    // walk-slide).  Mid-stride each step may bend a right angle (NpcWanderTurnChancePerStep) — never a
    // reversal — so paths form Ls and gentle zigzags, not dead-straight lines.  Confined to the map by
    // CanNpcMove's bounds check, so an NPC never wanders across a border (only the chase code turns a native
    // into a traversal guest).  Polymorphic over native slots and traversal guests via the same step / face
    // primitives the chase steppers use.
    private void WanderStep(int mapNum, int slot, MapNpcRecord mn)
    {
        if (mn.WanderStepsLeft > 0)
        {
            // Mid-stride: occasionally turn a right angle so the stroll bends instead of running dead straight.
            if (Rng.Next(Constants.NpcWanderTurnChancePerStep) == 0)
                mn.WanderDir = RandomPerpendicular(mn.WanderDir);
            TakeWanderStep(mapNum, slot, mn);
            return;
        }
        // Idle — begin a fresh stride on the 1-in-N cadence; otherwise keep loitering this tick.
        if (Rng.Next(Constants.NpcWanderStartChancePerTick) != 0) return;
        mn.WanderDir = (Direction)Rng.Next(Constants.NumDirections);
        mn.WanderStepsLeft = Rng.Next(Constants.NpcWanderStrideMinTiles, Constants.NpcWanderStrideMaxTiles + 1);
        TakeWanderStep(mapNum, slot, mn);   // first step keeps the freshly-picked heading (no turn)
    }

    // One stride step in mn.WanderDir.  Clear tile: step and decrement.  Blocked: end the stride early and
    // face the obstacle (a failed wander step still turns the NPC).  Native vs guest via the same
    // step / face primitives as the chase steppers.
    private void TakeWanderStep(int mapNum, int slot, MapNpcRecord mn)
    {
        bool moved;
        if (mn is TraversalNpcRecord t)
        {
            moved = TryApplyGuestStep(mapNum, t, mn.WanderDir);
        }
        else if (_movement.CanNpcMove(mapNum, slot, mn.WanderDir))
        {
            _movement.NpcMove(mapNum, slot, mn.WanderDir, MovementType.Walking);
            moved = true;
        }
        else
        {
            moved = false;
        }

        if (moved)
        {
            mn.WanderStepsLeft--;
        }
        else
        {
            mn.WanderStepsLeft = 0;
            if (mn is TraversalNpcRecord tg) BroadcastTraversalFacing(tg, mn.WanderDir);
            else BroadcastNpcDir(mapNum, slot, mn.WanderDir);
        }
    }

    // A random 90° turn from dir (never a 180° reversal): Up/Down bend to Left/Right and vice-versa.
    private Direction RandomPerpendicular(Direction dir) =>
        dir is Direction.Up or Direction.Down
            ? (Rng.Next(2) == 0 ? Direction.Left : Direction.Right)
            : (Rng.Next(2) == 0 ? Direction.Up : Direction.Down);
}

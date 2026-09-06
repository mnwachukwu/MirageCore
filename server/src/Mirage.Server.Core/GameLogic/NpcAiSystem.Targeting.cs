using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Choosing and letting go of a target: the player and NPC scanners, guard and
/// attack-on-sight acquisition, the unreachable give-up clock, the safe-zone rule that makes a
/// mob defer to guards, and the full reset a native runs when a chase ends.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Seamless world: an aggressive NPC sees players within Range across the whole 9-map observable area,
    // using world-tile distance from its own position (the center cell).  A player spotted on a
    // neighbor map is acquired here; the chase logic then walks the NPC to the border to engage.
    // Iterates MapObservers[mapNum] — the pre-maintained set of players who can see this map (which is
    // exactly the players in the observable area) — instead of the whole 1,000-slot roster.
    // LoS gate: a player behind a wall or closed door is skipped — the loop keeps scanning, so the
    // winner is the lowest-level player who is BOTH in range AND visible (not "lowest, bail if blocked").
    // Reachability gate: a candidate that passes LoS but has no walkable BFS path (e.g. behind an
    // NpcAvoid wall) is also skipped — without this, the AoS would lock on, fail to close, give up,
    // and instantly reacquire the same unreachable player forever.  Lowest-level REACHABLE player
    // wins (nearest breaks equal-level ties); if none reachable, the NPC stays idle until a path
    // opens or a reachable player wanders into range.  Once acquired, the chase BFS routes around
    // walls as before.
    private int FindLowestLevelPlayer(int mapNum, MapNpcRecord mn, int range)
    {
        var npc = _world.Npcs[mn.Num];
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var los = new WorldLosPredicate(_world, grid, mn.Layer);
        int best = 0, bestLevel = int.MaxValue, bestDist = int.MaxValue;
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            if (_pm[i].Char.Dead) continue;  // never acquire a corpse: it would re-lock every idle beat and re-run the death path
            if (_pm[i].Char.GodMode) continue;    // nor an observer, which nothing can see and nothing can reach
            var p = _pm[i].Char;
            // Beneath its notice: a mob far enough under the player does not start anything. Struck, it
            // still retaliates through AlertNpc, which is a different path entirely.
            if (p.Level - StatFormulas.NpcLevel(npc) > Constants.NpcAggroIgnoreLevelGap) continue;
            var gp = grid.PositionOf(p.Map);
            if (gp is null) continue;  // defensive: observer that left the area mid-tick
            var (pwx, pwy) = grid.ToWorld(gp.Value.col, gp.Value.row, p.X, p.Y);
            // Range is measured from the BODY, so a big NPC notices you at the same distance on every side.
            if (!WorldCoordHelper.AreFootprintsWithin(npcWX, npcWY, npc.EffectiveSize, pwx, pwy, 1, range)) continue;
            int d = WorldCoordHelper.FootprintManhattan(npcWX, npcWY, npc.EffectiveSize, pwx, pwy, 1);
            // Lowest level wins; nearest breaks equal-level ties.  Cheap cutoff before LoS/BFS work:
            // skip anyone who can't beat the current (level, distance) best.
            if (p.Level > bestLevel) continue;
            if (p.Level == bestLevel && d >= bestDist) continue;
            if (!WorldCoordHelper.HasClearSpellLineOfSight(npcWX, npcWY, pwx, pwy, los)) continue;
            if (FindStepTowardObservableArea(mapNum, mn.X, mn.Y, mn.Layer, p.Map, p.X, p.Y, p.Layer, npc) is null)
                continue;
            best = i;
            bestLevel = p.Level;
            bestDist = d;
        }
        return best;
    }

    // Guards scan the whole 9-map observable area — Range does not apply. Targets confirmed PK players and
    // active PvP initiators wherever they are in the observable area.  Iterates MapObservers[mapNum]
    // (the pre-maintained observable-area set) instead of the whole 1,000-slot roster.  Picks the PK/PvP
    // player NEAREST the guard (world-tile Manhattan); equidistant ties keep the first-seen player.
    private int FindGuardTarget(int mapNum, MapNpcRecord guard, long now)
    {
        long nowUtc = NowUtc;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (gWX, gWY) = grid.CenterToWorld(guard.X, guard.Y);
        int best = 0, bestDist = int.MaxValue;
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            if (_pm[i].Char.Dead) continue;  // a dead PK player is still PK (death doesn't clear PkExpiryUtc) — don't guard-target the corpse
            if (_pm[i].Char.GodMode) continue;    // nor an observer, whatever its PK history says
            var p = _pm[i].Char;
            var gp = grid.PositionOf(p.Map);
            if (gp is null) continue;
            bool effectivelyPk = p.IsPk(nowUtc) && _pm[i].PkGraceUntilUtc <= nowUtc;
            if (!effectivelyPk && _pm[i].PvpAttackerUntil <= now) continue;
            var (pwx, pwy) = grid.ToWorld(gp.Value.col, gp.Value.row, p.X, p.Y);
            int d = WorldCoordHelper.WorldManhattan(gWX, gWY, pwx, pwy);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Guards scan their 16×12 viewport for hostile NPCs chasing a player.  Iterates the
    /// 9-map observable area's native slots AND guest list — a wolf that pursued a player into the
    /// guard's viewport is fair game.  Eligibility: AoS/AWA behavior AND Target > 0 (currently
    /// chasing a player; an idle AWA mob does not qualify).  No LoS gate — matches PK-scan precedent
    /// (guards see hostiles through walls; BFS routes them around after acquisition).  Picks the
    /// nearest such hostile (world-tile Manhattan); returns (0, 0) when nothing eligible is in range.</summary>
    private (int SpawnMap, int SpawnSlot) FindGuardNpcTarget(int mapNum, int guardSlot, MapNpcRecord guard)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (gWX, gWY) = grid.CenterToWorld(guard.X, guard.Y);
        (int SpawnMap, int SpawnSlot) best = (0, 0);
        int bestDist = int.MaxValue;

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                // Native NPCs on this cell.
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    if (m == mapNum && s == guardSlot) continue;
                    var other = _world.MapNpcs[m, s];
                    if (other.Num <= 0 || other.Hp <= 0) continue;
                    var beh = _world.Npcs[other.Num].Behavior;
                    if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                    if (other.Target <= 0) continue;  // must currently be chasing a PLAYER
                    var (oWX, oWY) = grid.ToWorld(col, row, other.X, other.Y);
                    if (!WorldCoordHelper.IsWithinViewport(gWX, gWY, oWX, oWY)) continue;
                    int d = WorldCoordHelper.WorldManhattan(gWX, gWY, oWX, oWY);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = other.GetSpawnIdentity(m, s);
                    }
                }
                // Guests visiting this cell.
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0 || gt.Hp <= 0) continue;
                    var beh = _world.Npcs[gt.Num].Behavior;
                    if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                    if (gt.Target <= 0) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (!WorldCoordHelper.IsWithinViewport(gWX, gWY, oWX, oWY)) continue;
                    int d = WorldCoordHelper.WorldManhattan(gWX, gWY, oWX, oWY);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = gt.GetSpawnIdentity(m, 0);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>AoS mobs scan their <see cref="NpcRecord.Range"/> for a hostile NPC that is neither
    /// the same kind nor a same-group ally (see <see cref="GameWorld.AreNpcsAllied"/>: wolves don't fight other
    /// wolves, and a tagged pack doesn't infight).  Eligibility otherwise: AoS/AWA behavior —
    /// Friendly/Stationary/Guard are never targeted (behavior gate).  LoS gated like
    /// <see cref="FindLowestLevelPlayer"/> — can't see a goblin through a wall.  Picks the NEAREST
    /// eligible hostile (world-tile Manhattan); the distance check runs before the expensive LoS +
    /// reachability gates, so those only fire for a candidate that could beat the current best.
    /// Returns (0, 0) when nothing eligible is in range.</summary>
    private (int SpawnMap, int SpawnSlot) FindAosNpcTarget(int mapNum, int attackerSlot, MapNpcRecord attacker)
    {
        var attackerNpc = _world.Npcs[attacker.Num];
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (aWX, aWY) = grid.CenterToWorld(attacker.X, attacker.Y);
        var los = new WorldLosPredicate(_world, grid, attacker.Layer);
        int range = attackerNpc.Range;
        (int SpawnMap, int SpawnSlot) best = (0, 0);
        int bestDist = int.MaxValue;

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    if (m == mapNum && s == attackerSlot) continue;
                    var other = _world.MapNpcs[m, s];
                    if (other.Num <= 0 || other.Hp <= 0) continue;
                    if (_world.AreNpcsAllied(attacker.Num, other.Num)) continue;  // same-kind or same-group peace
                    var beh = _world.Npcs[other.Num].Behavior;
                    if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, other.X, other.Y);
                    // Both sides can be oversize here, so range and nearness are measured edge to edge.
                    int otherSize = _world.Npcs[other.Num].EffectiveSize;
                    if (!WorldCoordHelper.AreFootprintsWithin(aWX, aWY, attackerNpc.EffectiveSize, oWX, oWY, otherSize, range)) continue;
                    int d = WorldCoordHelper.FootprintManhattan(aWX, aWY, attackerNpc.EffectiveSize, oWX, oWY, otherSize);
                    if (d >= bestDist) continue;  // can't beat the current nearest; skip before LoS/BFS
                    if (!WorldCoordHelper.HasClearSpellLineOfSight(aWX, aWY, oWX, oWY, los)) continue;
                    // Reachability gate — same rationale as FindLowestLevelPlayer.  An LoS-visible
                    // mob behind an NpcAvoid wall passes the LoS check but has no walkable path;
                    // locking on would loop give-up → instant reacquire forever.
                    if (FindStepTowardObservableArea(mapNum, attacker.X, attacker.Y, attacker.Layer, m, other.X, other.Y, other.Layer, attackerNpc,
                                                 targetSize: _world.Npcs[other.Num].EffectiveSize) is null)
                        continue;
                    bestDist = d;
                    best = other.GetSpawnIdentity(m, s);
                }
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0 || gt.Hp <= 0) continue;
                    if (_world.AreNpcsAllied(attacker.Num, gt.Num)) continue;  // same-kind or same-group peace
                    var beh = _world.Npcs[gt.Num].Behavior;
                    if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (Math.Abs(oWX - aWX) > range || Math.Abs(oWY - aWY) > range) continue;
                    int d = WorldCoordHelper.WorldManhattan(aWX, aWY, oWX, oWY);
                    if (d >= bestDist) continue;
                    if (!WorldCoordHelper.HasClearSpellLineOfSight(aWX, aWY, oWX, oWY, los)) continue;
                    if (FindStepTowardObservableArea(mapNum, attacker.X, attacker.Y, attacker.Layer, m, gt.X, gt.Y, gt.Layer, attackerNpc,
                                                 targetSize: _world.Npcs[gt.Num].EffectiveSize) is null)
                        continue;
                    bestDist = d;
                    best = gt.GetSpawnIdentity(m, 0);
                }
            }
        }

        return best;
    }

    /// <summary>Acquire a hostile NPC target for an idle guard via <see cref="FindGuardNpcTarget"/>.
    /// On hit: set NpcTarget fields, release janitor claim, mark combat, broadcast target packet,
    /// emit AttackSay bubble to observers, and propagate to same-Num comrade guards via
    /// <see cref="CombatSystem.PropagateGuardAggro"/>.</summary>
    private void TryAcquireGuardNpcTarget(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var (spawnMap, spawnSlot) = FindGuardNpcTarget(mapNum, slot, mn);
        if (spawnSlot <= 0) return;
        mn.NpcTargetSpawnMap = spawnMap;
        mn.NpcTargetSpawnSlot = spawnSlot;
        mn.MarkReachedTarget(now);
        if (mn.JanitorTarget > 0) mn.JanitorTarget = 0;
        _combat.MarkNpcCombat(mapNum, slot, now);
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
        _combat.EmitNpcAttackSayBubbleToObservers(mapNum, slot, mn, spawnMap, spawnSlot);
        _combat.PropagateGuardAggro(mapNum, mn, new CombatSystem.GuardTargetSpec(0, spawnMap, spawnSlot), overwrite: false);
    }

    /// <summary>Acquire a different-kind hostile NPC target for an idle AoS mob via
    /// <see cref="FindAosNpcTarget"/>.  No comrade propagation (AoS mobs don't squad up like guards).
    /// AttackSay bubble fires same as for player-target acquisition, but broadcast to observers
    /// (not private to any player).</summary>
    private void TryAcquireAosNpcTarget(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var (spawnMap, spawnSlot) = FindAosNpcTarget(mapNum, slot, mn);
        if (spawnSlot <= 0) return;
        mn.NpcTargetSpawnMap = spawnMap;
        mn.NpcTargetSpawnSlot = spawnSlot;
        mn.MarkReachedTarget(now);
        _combat.MarkNpcCombat(mapNum, slot, now);
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
        _combat.EmitNpcAttackSayBubbleToObservers(mapNum, slot, mn, spawnMap, spawnSlot);
    }

    /// <summary>Per-tick brain step for a native NPC with an NpcTarget set.  Mirrors the player-target
    /// block in <see cref="RunAggressiveAi"/>: validate victim, cast-decision via
    /// <see cref="TryNpcMagicActionVsNpc"/>, strike if adjacent.  Closing the distance is NOT done here —
    /// that runs on the fast legs pass (<see cref="AdvanceNativeNpcChaseStep"/>).
    /// Drops the NpcTarget cleanly (with broadcast) when the victim has died, despawned, or moved
    /// outside the attacker's 3×3 observable area.  Refreshes combat each step for guards (mirrors
    /// the PK pursuit refresh).</summary>
    private void RunNpcVsNpcStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var npc = _world.Npcs[mn.Num];
        int victimSpawnMap = mn.NpcTargetSpawnMap;
        int victimSpawnSlot = mn.NpcTargetSpawnSlot;
        if (victimSpawnSlot <= 0) return;

        var resolved = _combat.ResolveNpcByIdentity(victimSpawnMap, victimSpawnSlot);
        if (resolved is null)
        {
            DropNpcTarget(mapNum, slot, mn);
            return;
        }
        var (victimMap, victimSlot, victimMn) = resolved.Value;

        // Must still be inside attacker's 3×3 observable area — no warp follow for NPC targets.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        if (grid.PositionOf(victimMap) is null)
        {
            DropNpcTarget(mapNum, slot, mn);
            return;
        }

        // AoS unreachable give-up applies to NPC targets too — same 10s rule, drop the lock AND
        // fully reset (vitals + spawn-packet refresh).
        if (ShouldGiveUpUnreachableAosTarget(mn, now))
        {
            DropNpcTarget(mapNum, slot, mn);
            ResetNativeNpc(mn, mapNum, slot, npc);
            return;
        }

        // Cast-decision (Int NPCs) — may consume the tick with kite/cast/hold.
        if (TryNpcMagicActionVsNpc(mapNum, slot, mn, victimMap, victimSlot, victimMn, now))
        {
            if (!mn.WantsKite) mn.NextMoveMs = now + Constants.AiTickIntervalMs;  // push unless kiting (legs continues the retreat)
            return;
        }

        // Adjacent (incl. cross-seam) → strike.  Turn to face first (the legs pass does this on arrival; brain
        // fallback here), but never mid-slide, and with no deliberate beat (see the player-target path).
        if (_combat.CanNpcAttackNpc(mapNum, mn, victimMap, victimMn, now))
        {
            var faceDir = FaceTargetDir(mapNum, mn.X, mn.Y, _world.Npcs[mn.Num].EffectiveSize, victimMap, victimMn.X, victimMn.Y, mn.Dir);
            if (mn.Dir != faceDir)
            {
                if (now < mn.NextMoveMs) return;                  // still sliding into place — finish the move first
                BroadcastNpcDir(mapNum, slot, faceDir);
                return;
            }
            _combat.NpcAttackNpc(mapNum, slot, mn, victimMap, victimSlot, victimMn, now);
            mn.AttackTimer = now;
            return;
        }

        // The target left the face, but a wide body is likely still pressed against by others. It is already
        // facing them and its beat is ready, so it swings at what is standing there rather than turning away
        // to chase — the cleave covers the whole edge, and the edge is what decides, not the one target.
        if (_world.Npcs[mn.Num].EffectiveSize > 1 && _combat.FirstVictimOnFace(mapNum, mn, now) is { } onFace)
        {
            if (onFace.Npc is { } faceNpc)
                _combat.NpcAttackNpc(mapNum, slot, mn, onFace.NpcMap, onFace.NpcSlot, faceNpc, now);
            else
                _combat.NpcAttackPlayer(mapNum, slot, onFace.PlayerIndex, now);
            mn.AttackTimer = now;
            return;
        }

        // Not adjacent — close the distance.  AoS and Guard refresh combat each step (relentless);
        // AWA stays yield-able and lets combat lapse if it can't land hits.
        if (npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.Guard)
            _combat.MarkNpcCombat(mapNum, slot, now);
        // The chase-STEP — same-map AND cross-seam — runs entirely on the fast legs pass
        // (AdvanceNativeNpcChaseStep) at run/walk pace; the brain does not step here.
    }

    /// <summary>Clear an NPC's NpcTarget (e.g. victim died or fled the observable area) and notify
    /// observers.  <c>HasTarget</c> reflects any remaining player Target so the combat outline
    /// matches actual state.</summary>
    private void DropNpcTarget(int mapNum, int slot, MapNpcRecord mn)
    {
        mn.NpcTargetSpawnMap = 0;
        mn.NpcTargetSpawnSlot = 0;
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = mn.Target != 0 });
    }

    /// <summary>AoS-only give-up gate: true when an AttackOnSight NPC has held its current target
    /// for longer than <see cref="NpcAosUnreachableGiveUpMs"/> without successfully reaching it
    /// (melee landed or chase step succeeded).  <see cref="MapNpcRecord.LastReachedTargetMs"/> is
    /// stamped on acquisition + on every reach event, so the typical chasing NPC keeps resetting
    /// this clock.  Casts at unreachable targets don't update the stamp, so a pure ranged poker
    /// eventually times out.  Other behaviors (AWA, Guard) have their own retention rules and
    /// aren't checked here.</summary>
    private bool ShouldGiveUpUnreachableAosTarget(MapNpcRecord mn, long now)
        => _world.Npcs[mn.Num].Behavior == NpcBehavior.AttackOnSight
           && mn.LastReachedTargetMs > 0
           && now - mn.LastReachedTargetMs > NpcAosUnreachableGiveUpMs;

    /// <summary>Safe-zone aggro rule for AoS: an AoS NPC chasing a PLAYER on a safe map with a Guard in
    /// viewport drops the player and locks onto the nearest visible guard. Without a guard in viewport it
    /// behaves normally, and the player has to escape or lure it toward a guarded area.
    ///
    /// <para>THE RULE PROTECTS PLAYERS, NOT NPCS. A mob whose aggro is purely NPC-driven is left alone, so
    /// a brawl spilling into a guarded safe zone plays out normally —
    /// <see cref="FindGuardNpcTarget"/> agrees, engaging only a hostile that is itself chasing a player.
    /// AWA is excluded too: it retaliates even in safe zones, and farming is blocked instead by
    /// guard-assisted kills denying EXP and loot (see <see cref="CombatSystem.ExecuteNpcDamage"/>).</para>
    ///
    /// <para>Called per-tick from the AoS AI loop, native and guest. Paired with
    /// <see cref="CombatSystem.SelectAggroTargetEx"/>, which applies the same player-only gate so
    /// damage-driven aggro flips cannot re-pick the player.</para></summary>
    private void EnforceSafeZoneAggroRule(MapNpcRecord mn, int currentMapNum, int slot, long now)
    {
        var npc = _world.Npcs[mn.Num];
        if (npc.Behavior != NpcBehavior.AttackOnSight) return;
        if (_world.MoralOf(currentMapNum) != MapMoral.Safe) return;
        // The redirect shields PLAYERS, so it only fires to peel a mob off a player it is chasing.
        // Target and NpcTarget are mutually exclusive, so Target > 0 means "chasing a player" and
        // implies there is no NPC target to preserve; Target == 0 means the mob is idle or brawling
        // with another NPC, and we leave that fight alone (guards don't police NPC-vs-NPC in town).
        if (mn.Target <= 0) return;

        var (guardSpawnMap, guardSpawnSlot) = FindGuardInViewport(mn, currentMapNum);
        if (guardSpawnSlot <= 0) return;  // no guard nearby — normal targeting (player on their own)

        // Peel off the player and lock onto the guard so the mob doesn't immediately reacquire the
        // player (RunAggressiveAi's player scan is suppressed while an NpcTarget is set).
        mn.Target = 0;
        mn.NpcTargetSpawnMap = guardSpawnMap;
        mn.NpcTargetSpawnSlot = guardSpawnSlot;
        mn.MarkReachedTarget(now);
        _combat.MarkNpcCombat(mn, now);  // initiate / refresh combat so AWA doesn't immediately yield

        if (mn is TraversalNpcRecord tg)
        {
            BroadcastTraversalState(tg);
        }
        else
        {
            SendToMap(_world, currentMapNum,
                new NpcTargetPacket { MapNum = currentMapNum, NpcSlot = slot, HasTarget = true });
        }
    }

    /// <summary>Find the nearest Guard inside <paramref name="mn"/>'s viewport (16×12 tiles).
    /// Returns the Guard's stable (spawnMap, spawnSlot) identity, or (0, 0) if none.  Both native
    /// AND guest Guards count — a guard chasing a PK into a town remains a viable redirect target.
    /// Used by the safe-zone aggro rule to redirect dragged mobs onto guards proactively rather
    /// than waiting for the guard's own scan or a damage event.  The chase logic that follows uses
    /// <see cref="CombatSystem.ResolveNpcByIdentity"/>, which resolves both natives and guests by
    /// spawn identity, so the returned (spawnMap, spawnSlot) works for either.</summary>
    private (int SpawnMap, int SpawnSlot) FindGuardInViewport(MapNpcRecord mn, int currentMapNum)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, currentMapNum);
        var (mnWX, mnWY) = grid.CenterToWorld(mn.X, mn.Y);
        int bestSpawnMap = 0, bestSpawnSlot = 0, bestDist = int.MaxValue;
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var other = _world.MapNpcs[m, s];
                    if (other.Num <= 0 || other.Hp <= 0) continue;
                    if (_world.Npcs[other.Num].Behavior != NpcBehavior.Guard) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, other.X, other.Y);
                    if (!WorldCoordHelper.IsWithinViewport(mnWX, mnWY, oWX, oWY)) continue;
                    int d = WorldCoordHelper.WorldManhattan(mnWX, mnWY, oWX, oWY);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        (bestSpawnMap, bestSpawnSlot) = other.GetSpawnIdentity(m, s);
                    }
                }
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0 || gt.Hp <= 0) continue;
                    if (_world.Npcs[gt.Num].Behavior != NpcBehavior.Guard) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (!WorldCoordHelper.IsWithinViewport(mnWX, mnWY, oWX, oWY)) continue;
                    int d = WorldCoordHelper.WorldManhattan(mnWX, mnWY, oWX, oWY);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        (bestSpawnMap, bestSpawnSlot) = gt.GetSpawnIdentity(m, 0);
                    }
                }
            }
        }

        return (bestSpawnMap, bestSpawnSlot);
    }

    /// <summary>Full reset for a native NPC that just gave up a chase: restore vitals, clear the
    /// damage ledger, and broadcast a spawn-packet refresh so observers see the HP bar refill.
    /// Position stays put — the native is already on its home map.  Guests use
    /// <see cref="ReturnTraversalHome"/> instead, which also relocates them back to spawn.
    /// <para><b>This is what makes an open world safe.</b> There is deliberately NO cross-map entry
    /// restriction anywhere in the chase code — a hostile NPC will follow a player across a border or
    /// through a warp into a town, because seamless pursuit is the point.  The drag-to-town exploit
    /// (lure a mob onto a safe map and let the guards and aggro do the work) is closed here instead of
    /// by refusing entry: pursuit only refreshes combat while the NPC is genuinely engaged
    /// (<see cref="IsRelentlessPursuit"/>), so once the target stops trading blows the combat window
    /// lapses — <c>CombatSystem.CombatDurationMs</c>, 10 s — and the NPC drops its target and resets in
    /// the same step.  An AoS pinned on a target it cannot reach gives up on the same 10 s budget via
    /// <see cref="ShouldGiveUpUnreachableAosTarget"/>.  A mob therefore cannot be parked in a foreign
    /// town: it either fights or it goes home.</para></summary>
    private void ResetNativeNpc(MapNpcRecord mn, int mapNum, int slot, NpcRecord npc)
    {
        mn.Hp = _world.EffectiveNpcMaxHp(npc);
        mn.Mp = _world.EffectiveNpcMaxMp(npc);
        mn.Sp = _world.EffectiveNpcMaxSp(npc);
        mn.ClearDamageCredit();
        mn.WeaveCastThisBeat = false;   // clear the weave latch + commitment so a reused slot re-rolls fresh on its first ready beat
        mn.WeaveWasReady = false;
        mn.WeaveModalityBeatsLeft = 0;
        SendToMap(_world, mapNum, new NpcSpawnPacket
        {
            MapNum = mapNum,
            NpcSlot = slot,
            Num = mn.Num,
            X = mn.X,
            Y = mn.Y,
            Dir = mn.Dir,
            MaxHp = _world.EffectiveNpcMaxHp(npc),
            MaxMp = _world.EffectiveNpcMaxMp(npc),
            MaxSp = _world.EffectiveNpcMaxSp(npc),
            Layer = mn.Layer,
        });
    }
}

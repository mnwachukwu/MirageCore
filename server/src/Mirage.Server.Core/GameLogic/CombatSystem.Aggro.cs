using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Who is fighting whom, and how that survives the world moving underneath it: player
/// and NPC target assignment, target maintenance across NPC map changes and despawns, guard
/// aggro propagation, and the weighted pick that chooses an NPC's next target.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Player→NPC target maintenance across NPC map changes ─────────────────────
    // The seamless world vacates/reuses both native slots and traversal identities, so a player's
    // lock must move with the NPC and be cleared when that instance ends — otherwise a stale target
    // re-binds to a new entity that reuses the slot/identity.

    /// <summary>Transfers any native-slot lock to the guest's identity when an NPC crosses a seam,
    /// so the player keeps tracking the same monster as it becomes a traversal guest.</summary>
    public void TransferTargetsToTraversal(int fromMap, int npcSlot, int toMap)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType == 1 && sp.Target == npcSlot && sp.TargetMap == fromMap)
            {
                sp.TargetType = 3;
                sp.Target = 0;
                sp.TargetSpawnMap = fromMap;
                sp.TargetSpawnSlot = npcSlot;
                sp.TargetMap = toMap;
            }
        }
    }

    /// <summary>Clears every player's lock on a native NPC slot (the NPC died or left the slot).</summary>
    public void DropPlayerTargetsOnNpcSlot(int mapNum, int npcSlot)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType == 1 && sp.Target == npcSlot && sp.TargetMap == mapNum)
            {
                sp.Target = 0;
                sp.TargetType = 0;
                sp.TargetMap = 0;
            }
        }
    }

    /// <summary>Clears every player's lock on a traversal guest identity (it died or returned home).</summary>
    public void DropPlayerTargetsOnTraversal(int spawnMap, int spawnSlot)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType == 3 && sp.TargetSpawnMap == spawnMap && sp.TargetSpawnSlot == spawnSlot)
            {
                sp.Target = 0;
                sp.TargetType = 0;
                sp.TargetMap = 0;
                sp.TargetSpawnSlot = 0;
            }
        }
    }

    /// <summary>Tagged target descriptor for guard comrade-alert propagation.  Either a player index
    /// (<see cref="Player"/> > 0) or an NPC identity (<see cref="NpcSpawnSlot"/> > 0); all-zeros
    /// means "clear" (used when an aggro re-eval found no surviving contributor, so woken comrades
    /// should drop too).</summary>
    public readonly record struct GuardTargetSpec(int Player, int NpcSpawnMap, int NpcSpawnSlot)
    {
        public bool IsPlayer => Player > 0;
        public bool IsNpc => NpcSpawnSlot > 0;
        public bool HasTarget => IsPlayer || IsNpc;
    }

    /// <summary>Wake (or clear) same-Num comrade guards whose own viewport contains
    /// <paramref name="alertedMn"/> — the universal sight gate is "could this comrade see the engaged
    /// guard on its own screen if it were a player."  Iterates the alerted guard's 9-map observable
    /// area with proper world-coord conversion, so comrades on adjacent maps are covered.  Guests
    /// are individuals — never
    /// propagate from a TraversalNpcRecord, never include guest comrades.  Self is always skipped.
    ///
    /// <para><paramref name="overwrite"/> controls whether comrades already targeting something keep
    /// it: <c>false</c> (AlertNpc-style initial wake) only touches idle comrades; <c>true</c>
    /// (SetNpcAggro-style mid-fight aggro flip) overwrites all same-Num comrades in viewport so the
    /// squad re-syncs to the alerted guard's new target.</para></summary>
    public void PropagateGuardAggro(int alertedMap, MapNpcRecord alertedMn, GuardTargetSpec spec, bool overwrite)
    {
        var alertedNpc = _world.Npcs[alertedMn.Num];
        if (alertedNpc.Behavior != NpcBehavior.Guard) return;
        // A guest guard propagates too (parity): it's at the center of the grid built around its CURRENT map,
        // and the comrades it wakes are native slots on that grid — its own transient list index is irrelevant
        // because the self-skip below is by object identity.

        long now = Environment.TickCount64;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, alertedMap);
        var (alertedWX, alertedWY) = grid.CenterToWorld(alertedMn.X, alertedMn.Y);
        string say = string.IsNullOrWhiteSpace(alertedNpc.AttackSay) ? string.Empty : alertedNpc.AttackSay.TrimEnd();
        int encodedNpc = spec.IsNpc ? MapNpcRecord.EncodeNpcId(spec.NpcSpawnMap, spec.NpcSpawnSlot) : 0;

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                {
                    var other = _world.MapNpcs[m, s];
                    if (ReferenceEquals(other, alertedMn)) continue;  // skip the alerter itself (a guest is never in the array)
                    if (other.Num != alertedMn.Num) continue;
                    // Initial-wake call: don't touch already-engaged comrades.  Aggro-flip call: overwrite.
                    if (!overwrite && (other.Target != 0 || other.NpcTargetSpawnSlot != 0)) continue;

                    var (otherWX, otherWY) = grid.ToWorld(col, row, other.X, other.Y);
                    if (!WorldCoordHelper.IsWithinViewport(otherWX, otherWY, alertedWX, alertedWY)) continue;

                    other.Target = spec.Player;
                    other.NpcTargetSpawnMap = spec.NpcSpawnMap;
                    other.NpcTargetSpawnSlot = spec.NpcSpawnSlot;
                    if (!spec.HasTarget) continue;  // silent clear — matches existing behavior

                    other.MarkReachedTarget(now);  // woken comrade starts with a fresh unreachable clock
                    MarkNpcCombat(m, s, now);
                    SendToMap(_world, m, new NpcTargetPacket { MapNum = m, NpcSlot = s, HasTarget = true });

                    if (say.Length == 0) continue;
                    if (spec.IsPlayer)
                    {
                        if (other.LastAttackSayTarget == spec.Player) continue;
                        other.LastAttackSayTarget = spec.Player;
                        SendMsg(spec.Player, ServerStrings.CombatSystem_NpcSays, GameColor.Npc, ChatChannel.Say, ("NpcName", alertedNpc.TrimmedName), ("Say", say));
                        _dispatcher.SendTo(spec.Player, PacketBuilder.NpcChatBubble(m, s, say, kind: 0));
                    }
                    else
                    {
                        // Per-combat-session dedup — see EmitNpcAttackSayBubbleToObservers for rationale.
                        if (other.LastAttackSayNpcTarget != 0) continue;
                        other.LastAttackSayNpcTarget = encodedNpc;
                        SendToMap(_world, m, PacketBuilder.NpcChatBubble(m, s, say, kind: 0));
                    }
                }
            }
        }
    }

    /// <summary>NPC-source twin of <see cref="AlertNpc"/>: locks the victim onto the attacker NPC
    /// when a 0-damage hit landed (all damage absorbed by protection).  Mirrors the player-melee
    /// behavior — a swing that did nothing still flips an idle target onto the attacker, so two
    /// NPCs with mismatched stats don't just stand there ignoring each other.  No-op if the victim
    /// already has any target (player or NPC); for guard victims, comrade-propagates same as the
    /// damaged-aggro path.  Spell casts at 0 damage do NOT call this (matches player-spell behavior
    /// in <see cref="ExecuteNpcDamage"/> which also doesn't alert on 0-damage casts).</summary>
    private void AlertNpcFromNpc(int victimMap, int victimSlot, MapNpcRecord victimMn, int attackerSpawnMap, int attackerSpawnSlot)
    {
        var victimNpc = _world.Npcs[victimMn.Num];
        if (victimMn.Target != 0 || victimMn.NpcTargetSpawnSlot != 0) return;
        long alertNow = Environment.TickCount64;
        if (victimMn is TraversalNpcRecord tg)
        {
            // Guest: lock onto the NPC attacker, broadcast state, broadcast AttackSay bubble
            // addressed by spawn identity.  No propagation (guests are individuals).
            victimMn.NpcTargetSpawnMap = attackerSpawnMap;
            victimMn.NpcTargetSpawnSlot = attackerSpawnSlot;
            victimMn.BeginRushEngagement();   // drawn into combat at range → sprint to close, skip the AoS walk-in
            victimMn.MarkReachedTarget(alertNow);
            SendTraversalState(tg);
            EmitNpcAttackSayBubbleToObservers(victimMap, victimSlot, victimMn, attackerSpawnMap, attackerSpawnSlot);
            return;
        }
        victimMn.NpcTargetSpawnMap = attackerSpawnMap;
        victimMn.NpcTargetSpawnSlot = attackerSpawnSlot;
        victimMn.BeginRushEngagement();   // drawn into combat at range → sprint to close, skip the AoS walk-in
        victimMn.MarkReachedTarget(alertNow);
        SendToMap(_world, victimMap, new NpcTargetPacket { MapNum = victimMap, NpcSlot = victimSlot, HasTarget = true });
        EmitNpcAttackSayBubbleToObservers(victimMap, victimSlot, victimMn, attackerSpawnMap, attackerSpawnSlot);
        PropagateGuardAggro(victimMap, victimMn,
            new GuardTargetSpec(0, attackerSpawnMap, attackerSpawnSlot), overwrite: false);
    }

    // Sets NPC target to attacker when no damage was dealt (block/dodge/0-after-mit on melee, or a
    // hostile spell whose magnitude couldn't penetrate magic mitigation).  For guards, propagates to
    // idle same-Num guards whose viewport contains the alerted guard (cross-seam aware via
    // PropagateGuardAggro).  No-ops if already targeted.  Public so the spell path can route 0-dmg
    // hostile casts through here for melee parity.
    public void AlertNpc(int mapNum, int npcSlot, MapNpcRecord mn, int attacker)
    {
        var npcRec = _world.Npcs[mn.Num];
        // Grace fires BEFORE the Target!=0 bail so each player burns their own budget regardless
        // of who the guard is currently fighting — a guard already targeting Alice still warns
        // Bob on his first swing. A blocked / dodged / 0-damage swing still counts as a full
        // swing; 10% override is trivially false at dmg=0 so the counter path always wins here.
        if (ConsumeGuardGrace(attacker, mapNum, npcSlot, mn, dmg: 0)) return;
        if (mn.Target != 0) return;
        long alertNow = Environment.TickCount64;
        if (mn is TraversalNpcRecord tg)
        {
            // Guests are individuals — no guard-post propagation; just lock onto the attacker.
            mn.Target = attacker;
            mn.BeginRushEngagement();   // drawn into combat at range → sprint to close, skip the AoS walk-in
            mn.MarkReachedTarget(alertNow);
            SendTraversalState(tg);
            if (mn.LastAttackSayTarget != attacker && !string.IsNullOrWhiteSpace(npcRec.AttackSay))
            {
                mn.LastAttackSayTarget = attacker;
                SendMsg(attacker, ServerStrings.CombatSystem_NpcSays, GameColor.Npc, ChatChannel.Say, ("NpcName", npcRec.TrimmedName), ("Say", npcRec.AttackSay.TrimEnd()));
                _dispatcher.SendTo(attacker, PacketBuilder.TraversalNpcChatBubble(tg.SpawnMapNum, tg.SpawnSlot, npcRec.AttackSay.TrimEnd(), kind: 0));
            }
            return;
        }
        mn.Target = attacker;
        mn.BeginRushEngagement();   // drawn into combat at range → sprint to close, skip the AoS walk-in
        mn.MarkReachedTarget(alertNow);
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = npcSlot, HasTarget = true });
        if (mn.LastAttackSayTarget != attacker && !string.IsNullOrWhiteSpace(npcRec.AttackSay))
        {
            mn.LastAttackSayTarget = attacker;
            SendMsg(attacker, ServerStrings.CombatSystem_NpcSays, GameColor.Npc, ("NpcName", npcRec.TrimmedName), ("Say", npcRec.AttackSay.TrimEnd()));
            _dispatcher.SendTo(attacker, PacketBuilder.NpcChatBubble(mapNum, npcSlot, npcRec.AttackSay.TrimEnd(), kind: 0));
        }
        PropagateGuardAggro(mapNum, mn, new GuardTargetSpec(attacker, 0, 0), overwrite: false);
    }

    private void SetNpcAggro(int mapNum, int npcSlot, MapNpcRecord mn, int attacker)
    {
        if (mn is TraversalNpcRecord tg)
        {
            // Re-point at the strongest on-map contributor, but never drop to 0 (that would send the
            // guest home mid-fight) — keep its current chase target if no on-map contributor is found.
            int pick = SelectAggroTarget(mapNum, mn);
            if (pick > 0 && pick != mn.Target)
            {
                mn.Target = pick;
                mn.BeginRushEngagement();   // drawn into combat → sprint to close, skip the AoS walk-in
                SendTraversalState(tg);
            }
            return;
        }
        int best = SelectAggroTarget(mapNum, mn);
        mn.Target = best;
        if (best > 0)
        {
            long now = Environment.TickCount64;
            mn.BeginRushEngagement();   // drawn into combat → sprint to close, skip the AoS walk-in
            mn.MarkReachedTarget(now);
            MarkNpcCombat(mapNum, npcSlot, now);
            SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = npcSlot, HasTarget = true });
        }
        // Hit guard's AttackSay was already sent by the AttackNpc / damage path; propagation handles
        // comrades' AttackSay (deduped via LastAttackSayTarget).  best == 0 propagates the clear so
        // comrades drop in sync.
        PropagateGuardAggro(mapNum, mn, new GuardTargetSpec(best, 0, 0), overwrite: true);
    }

    /// <summary>Tagged "who wins aggro" — a player index, an NPC identity, or none.
    /// Player > 0 means a player pick; NpcSpawnSlot > 0 means an NPC pick; all-zeros means nothing.
    /// At most one of the two pick fields is set; <see cref="Damage"/> is the winner's RAW (unweighted)
    /// ledger damage — kept > 0 for any live contributor so callers' pick.Damage<=0 liveness gate works.</summary>
    public readonly record struct AggroPick(int Player, int NpcSpawnMap, int NpcSpawnSlot, int Damage);

    private int SelectAggroTarget(int mapNum, MapNpcRecord mn)
        => SelectAggroTargetEx(mapNum, mn).Player;

    /// <summary>True when at least one Guard NPC stands inside <paramref name="mn"/>'s viewport
    /// (16×12 tiles centered on it).  Both native and guest Guards count.  Gates the safe-zone
    /// aggro rule: an AoS NPC on a safe map redirects to guards only when at least one is visible
    /// (AWA is exempt — it retaliates against its attacker even in town).  Without a guard in
    /// viewport the NPC behaves normally and the player has to escape or lure it toward a guarded
    /// area.</summary>
    public bool HasGuardInViewport(MapNpcRecord mn, int currentMapNum)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, currentMapNum);
        var (mnWX, mnWY) = grid.CenterToWorld(mn.X, mn.Y);
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
                    if (WorldCoordHelper.IsWithinViewport(mnWX, mnWY, oWX, oWY)) return true;
                }
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0 || gt.Hp <= 0) continue;
                    if (_world.Npcs[gt.Num].Behavior != NpcBehavior.Guard) continue;
                    var (oWX, oWY) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (WorldCoordHelper.IsWithinViewport(mnWX, mnWY, oWX, oWY)) return true;
                }
            }
        }

        return false;
    }

    /// <summary>Chooses which contributor on <paramref name="mn"/>'s damage ledger wins aggro.  Damage is
    /// DEF-weighted (a higher-DEF attacker's damage counts for more — see <see cref="AggroWeight"/>) and the
    /// result is STICKY: the current target is retained unless a DIFFERENT contributor's weighted score beats
    /// it by <see cref="AggroStickinessFactor"/>, so aggro doesn't ping-pong between similar attackers on every
    /// hit while a decisive lead still bails out.  Incumbent-aware (reads mn.Target / mn.NpcTargetSpawn*).
    /// The returned <see cref="AggroPick.Damage"/> is the winner's RAW (unweighted) ledger damage.</summary>
    private AggroPick SelectAggroTargetEx(int mapNum, MapNpcRecord mn)
    {
        // An attacker may be on a NEIGHBOR map (cross-seam melee / spell), so a candidate is any
        // contributor still inside the NPC's observable 9-map region — not just its own map.  (At hit
        // time the attacker is in range, so this never excludes them; it only drops contributors who
        // have since left the area.)  Grid built once.
        // Safe-zone AoS rule: when an AoS NPC stands on a safe map AND has a Guard in viewport,
        // ignore PLAYER contributors so a dragged mob can't be pulled onto (or kept on) a player in
        // town — the AI redirects it to the guard instead (EnforceSafeZoneAggroRule).  NPC
        // contributors are still honored, so an NPC-vs-NPC brawl that wanders into a safe zone plays
        // out normally and the guard stays out of it unless a player is threatened.  AWA is excluded:
        // it retaliates against its attacker even in safe zones, and guard-assisted safe-zone
        // kills deny the player EXP/loot instead (see ExecuteNpcDamage).  Without a guard nearby the
        // NPC behaves normally and the player is on their own.
        var npcRec = _world.Npcs[mn.Num];
        // guardMode is DERIVED here, never caller-supplied — it is always exactly this NPC's own behavior.
        // A parameter would only let a caller (e.g. a copy-pasted guest branch) pass a value that DISAGREES
        // with reality and silently switch off the guard grace-skip + PK-bias below — the guest-guard bug
        // where a seam-chasing guard hunted a player who had only spent grace warnings on it.  One source of
        // truth makes that whole class of mistake unrepresentable.
        bool guardMode = npcRec.Behavior == NpcBehavior.Guard;
        bool safeZoneAos = npcRec.Behavior == NpcBehavior.AttackOnSight
                           && _world.MoralOf(mapNum) == MapMoral.Safe
                           && HasGuardInViewport(mn, mapNum);
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        long nowUtc = NowUtc;
        int bestPlayer = 0, bestPlayerDmg = 0;
        double bestPlayerScore = 0.0;
        // Incumbent (current target) capture — its weighted score / raw damage if it is still a live,
        // unfiltered contributor.  Feeds the stickiness margin at the end so aggro doesn't ping-pong
        // between similar contributors every hit.  Stays invalid if the current target has left the
        // ledger / region or was filtered out (grace, safe-zone), which correctly lets a challenger win.
        double curScore = 0.0;
        int curDmg = 0;
        bool curValid = false;
        if (!safeZoneAos)
        {
            bool hasPk = false;
            if (guardMode)
            {
                for (int i = 1; i <= _pm.Slots; i++)
                {
                    if (mn.DamageByPlayer[i] > 0 && _pm[i].IsPlaying
                        && grid.PositionOf(_pm[i].Char.Map) is not null && _pm[i].Char.IsPk(nowUtc))
                    {
                        hasPk = true;
                        break;
                    }
                }
            }

            for (int i = 1; i <= _pm.Slots; i++)
            {
                if (mn.DamageByPlayer[i] == 0) continue;
                if (!_pm[i].IsPlaying || grid.PositionOf(_pm[i].Char.Map) is null) continue;
                if (guardMode && hasPk && !_pm[i].Char.IsPk(nowUtc)) continue;
                // Guards in active grace with this player ignore their damage for target picking —
                // the warnings are still being spent, so the player shouldn't drive an aggro flip
                // away from the guard's current target (NPC chase, other PK, etc.).  Once grace
                // breaks (counter > limit via the 4th swing or the 10% override), they become a
                // real contributor and rejoin the comparison.
                if (guardMode && mn.WarnHitsByPlayer[i] > 0 && mn.WarnHitsByPlayer[i] <= GuardGraceWarnLimit) continue;
                // DEF-weighted score (see AggroWeight): a higher-DEF attacker's damage counts for more.
                // Track the leader's raw damage separately for the returned AggroPick / liveness gate.
                double score = mn.DamageByPlayer[i] * AggroWeight(_pm[i].Char.Def);
                if (score > bestPlayerScore)
                {
                    bestPlayer = i;
                    bestPlayerDmg = mn.DamageByPlayer[i];
                    bestPlayerScore = score;
                }
                if (i == mn.Target)
                {
                    curScore = score;
                    curDmg = mn.DamageByPlayer[i];
                    curValid = true;
                }
            }
        }

        // NPC contributors — must still be alive AND inside the 9-map observable area.  Guards' PK
        // bias is players-only (NPCs are never PK), so when guardMode + hasPk we still consider NPCs
        // independently and the tie/comparison below decides who wins.  Safe-zone AoS does NOT filter
        // this pool: NPCs fight each other freely even in a guarded town — only players are dropped
        // (above), so the guard-redirect never hijacks an NPC-vs-NPC fight.  No raw-damage early-skip
        // here: under DEF weighting a lower-damage but higher-DEF NPC can outscore one that hit harder,
        // so every live contributor must be scored.
        int bestNpcMap = 0, bestNpcSlot = 0, bestNpcDmg = 0;
        double bestNpcScore = 0.0;
        if (mn.DamageByNpc is { } list)
        {
            for (int j = 0; j < list.Count; j++)
            {
                var e = list[j];
                var resolved = ResolveNpcByIdentity(e.SpawnMap, e.SpawnSlot);
                if (resolved is null) continue;
                var (cMap, _, rec) = resolved.Value;
                if (grid.PositionOf(cMap) is null) continue;
                double score = e.Damage * AggroWeight(_world.Npcs[rec.Num].Def);
                if (score > bestNpcScore)
                {
                    bestNpcMap = e.SpawnMap;
                    bestNpcSlot = e.SpawnSlot;
                    bestNpcDmg = e.Damage;
                    bestNpcScore = score;
                }
                if (e.SpawnMap == mn.NpcTargetSpawnMap && e.SpawnSlot == mn.NpcTargetSpawnSlot)
                {
                    curScore = score;
                    curDmg = e.Damage;
                    curValid = true;
                }
            }
        }

        // Objective best across pools — tie or player-strict-higher → player keeps it (favors player
        // retention per design rule 1).  Comparison is on DEF-weighted scores; AggroPick.Damage carries
        // the winner's RAW damage so the pick.Damage<=0 liveness gate keeps its meaning.
        int winPlayer, winNpcMap, winNpcSlot, winDmg;
        double winScore;
        if (bestPlayerScore >= bestNpcScore)
        {
            winPlayer = bestPlayer;
            winNpcMap = 0;
            winNpcSlot = 0;
            winDmg = bestPlayerDmg;
            winScore = bestPlayerScore;
        }
        else
        {
            winPlayer = 0;
            winNpcMap = bestNpcMap;
            winNpcSlot = bestNpcSlot;
            winDmg = bestNpcDmg;
            winScore = bestNpcScore;
        }

        // Stickiness (hysteresis): keep the incumbent unless a DIFFERENT contributor's weighted score
        // beats the incumbent's by AggroStickinessFactor.  Small back-and-forth leads between evenly
        // matched attackers don't flip the target every hit; a decisive lead (big hit / sustained
        // higher weighted damage) still bails out.  curValid == false (incumbent gone or filtered) falls
        // straight through to the challenger.
        bool winnerIsIncumbent = (winPlayer > 0 && winPlayer == mn.Target)
            || (winNpcSlot > 0 && winNpcMap == mn.NpcTargetSpawnMap && winNpcSlot == mn.NpcTargetSpawnSlot);
        if (curValid && !winnerIsIncumbent && winScore <= curScore * AggroStickinessFactor)
        {
            return mn.Target > 0
                ? new AggroPick(mn.Target, 0, 0, curDmg)
                : new AggroPick(0, mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot, curDmg);
        }

        return new AggroPick(winPlayer, winNpcMap, winNpcSlot, winDmg);
    }

    /// <summary>Resolve an NPC's stable (SpawnMap, SpawnSlot) identity to its current live record.
    /// Tries the home slot first; if reserved (native is currently away as a guest), scans the
    /// game-wide guest lists.  Returns null when the NPC is dead, despawned, or never spawned.
    /// Game-wide scan is cheap — typical guest count is in the low single digits.</summary>
    public (int CurrentMap, int CurrentSlot, MapNpcRecord Record)? ResolveNpcByIdentity(int spawnMap, int spawnSlot)
    {
        if (spawnMap <= 0 || spawnSlot <= 0 || spawnMap > _world.Limits.Maps || spawnSlot > Constants.MaxMapNpcs) return null;
        var native = _world.MapNpcs[spawnMap, spawnSlot];
        if (native.Num > 0 && !native.IsReservedSlot) return (spawnMap, spawnSlot, native);
        if (native.IsReservedSlot)
        {
            for (int m = 1; m <= _world.Limits.Maps; m++)
            {
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var t = guests[g];
                    if (t.SpawnMapNum == spawnMap && t.SpawnSlot == spawnSlot && t.Num > 0 && t.Hp > 0)
                        return (m, 0, t);  // CurrentSlot=0 → caller must use the record directly (it's a guest)
                }
            }
        }
        return null;
    }

    internal void ClearPlayerNpcContributions(int playerIndex, int mapNum)
    {
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            _world.MapNpcs[mapNum, i].DamageByPlayer[playerIndex] = 0;
    }

    // Drop every NPC's lock on a player who just died, so nothing keeps hounding the respawn.  The
    // death map alone is not enough: (a) a native on any of the 8 neighbor maps can hold a cross-seam
    // target, so sweep the victim's whole 3×3 region; and (b) a chasing GUEST that followed the player
    // here lives OUTSIDE the slot arrays — if it isn't reset it keeps its target through the death and
    // immediately re-chases the corpse's respawn.  Clearing a guest's target makes RunTraversalAi send
    // it home on its next tick.
    private void ClearNpcTargetsFor(int deathMap, int playerIndex)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, deathMap);
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = grid[col, row];
                if (m <= 0) continue;
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var mn = _world.MapNpcs[m, i];
                    if (mn.Target != playerIndex) continue;
                    mn.Target = 0;
                    SendToMap(_world, m, new NpcTargetPacket { MapNum = m, NpcSlot = i, HasTarget = false });
                }
            }
        }
        // Guests are few (only active chasers), so a game-wide sweep is cheap and guarantees none keeps
        // a stale lock regardless of which map it wandered onto.
        for (int m = 1; m <= _world.Limits.Maps; m++)
        {
            var guests = _world.MapTraversalNpcs[m];
            for (int g = 0; g < guests.Count; g++)
            {
                if (guests[g].Target == playerIndex)
                    guests[g].Target = 0;
            }
        }
    }
}

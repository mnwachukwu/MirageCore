using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>NPC versus NPC: guard-on-monster and monster-on-monster fights, the aggro handoff
/// a hit creates between them, and the re-evaluation that decides who an NPC turns on next.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── NPC vs NPC ────────────────────────────────────────────────────────────

    /// <summary>Adjacency + cooldown gate for an NPC swinging at another NPC.  Cross-seam aware via
    /// world-tile math.  No safe-zone gate (aggressive NPCs can't spawn on safe maps, so the only
    /// relevant case is a safe-map guard striking a non-safe-map mob, which is allowed).</summary>
    public bool CanNpcAttackNpc(int attackerMap, MapNpcRecord attackerMn, int victimMap, MapNpcRecord victimMn, long now)
    {
        long windMult = _world.WeatherOn(attackerMap) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (!AiCadence.Elapsed(now, attackerMn.AttackTimer, Constants.NpcAttackCooldownMs * windMult)) return false;
        return NpcInMeleeRangeOfNpc(attackerMap, attackerMn, victimMap, victimMn);
    }

    /// <summary>The REACH half of <see cref="CanNpcAttackNpc"/> — everything but the swing cooldown. See
    /// <see cref="NpcInMeleeRangeOfPlayer"/> for why the chase step must ask this one and not the other.</summary>
    public bool NpcInMeleeRangeOfNpc(int attackerMap, MapNpcRecord attackerMn, int victimMap, MapNpcRecord victimMn)
    {
        if (attackerMn.Num <= 0 || attackerMn.Hp <= 0) return false;
        if (victimMn.Num <= 0 || victimMn.Hp <= 0) return false;

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, attackerMap);
        var (aWX, aWY) = grid.CenterToWorld(attackerMn.X, attackerMn.Y);
        var vw = grid.ToWorldRelative(victimMap, victimMn.X, victimMn.Y);
        if (vw is null) return false;
        // Edge to edge, since either side may be oversize: two size-3 bodies touching sit 3 tiles apart anchor
        // to anchor. Both sides are footprints here, unlike the player gate where the attacker is always size 1.
        int attackerSize = _world.Npcs[attackerMn.Num].EffectiveSize;
        int victimSize = _world.Npcs[victimMn.Num].EffectiveSize;
        if (!WorldCoordHelper.AreFootprintsAdjacent(aWX, aWY, attackerSize, vw.Value.worldX, vw.Value.worldY, victimSize))
            return false;
        // Two-layer connect: the attacker and the adjacent victim connect across layers only where a ramp bridges
        // them — a guard on the ground can't hit a mob up on the bridge (or vice-versa) unless one is on a ramp.
        return LayerLogic.LayerConnects(new ServerTileView(_world, grid), aWX, aWY, attackerMn.Layer, vw.Value.worldX, vw.Value.worldY, victimMn.Layer);
    }

    /// <summary>NPC melee on another NPC.  Mirrors <see cref="NpcAttackPlayer"/> end-to-end: full
    /// <see cref="CombatFormulas.NpcMeleeBaseDamage"/> swing minus victim's <see cref="CombatFormulas.NpcProtection"/>
    /// (no shield/dodge — NPCs don't have either), crit roll via the shared NPC-crit triad, and a
    /// broadcast <see cref="NpcAttackPacket"/> addressed by universal identity so a guest attacker
    /// (<paramref name="attackerSlot"/> = 0) spawns the swoosh + sparks exactly like a native.  The
    /// "stronger against players" identity is preserved by HP favor and by the
    /// fact that players don't get implicit gear baked into their stat block — both sides cancel
    /// in matched NPC-vs-NPC, so the curve mirrors player-vs-NPC at matched gear (~30% net throughput
    /// at matched stats).</summary>
    public void NpcAttackNpc(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, int victimMap, int victimSlot, MapNpcRecord victimMn, long now)
    {
        var attackerNpc = _world.Npcs[attackerMn.Num];
        if (!CanNpcAttackNpc(attackerMap, attackerMn, victimMap, victimMn, now)) return;

        // A large NPC swings once and strikes everything on the tiles past its leading edge, exactly as it
        // does against players (NpcAttackPlayerFootprint). A body three tiles wide that could only ever
        // damage one of the two enemies pressed against that same face was reading as a bug, because it is.
        if (attackerNpc.EffectiveSize > 1)
        {
            NpcAttackNpcFootprint(attackerMap, attackerSlot, attackerMn, attackerNpc, victimMap, victimSlot, victimMn, now);
            return;
        }

        MarkNpcCombat(attackerMn, now);

        attackerMn.AttackTimer = now;
        attackerMn.Attacking = true;
        attackerMn.MarkReachedTarget(now);  // melee landed — physical reach
        // Swing broadcast by universal identity (native home slot or guest spawn slot) — same event parity as
        // NpcAttackPlayer, so a guest attacker's swoosh + sparks render just like a native's.  Broadcast BEFORE
        // the victim's block/dodge roll so the swoosh shows whatever the outcome (parity with EngageNpc's swing).
        var (aSpawnMap, aSpawnSlot) = attackerMn.GetSpawnIdentity(attackerMap, attackerSlot);
        if (aSpawnSlot > 0)
            SendToMap(_world, attackerMap, new NpcAttackPacket { MapNum = aSpawnMap, NpcSlot = aSpawnSlot });

        ApplyNpcMeleeHitOnNpc(attackerMap, attackerSlot, attackerMn, attackerNpc, victimMap, victimSlot, victimMn, now);
    }

    /// <summary>
    /// One swing from a large NPC, striking every eligible NPC standing on the tiles just past its leading
    /// edge — the same rule <see cref="NpcAttackPlayerFootprint"/> applies to players.
    ///
    /// <para>🔴 Victims are matched by FOOTPRINT, not by anchor. The player strip compares the victim's
    /// single tile; an NPC victim can be three wide, so anchor equality would both miss a body straddling
    /// the strip and count a wide one once per tile it covers. Candidates are walked once and tested for
    /// intersection, which dedupes by construction.</para>
    ///
    /// <para>Allies are never caught — <see cref="AreNpcsAllied"/> is the same rule the AI acquires by, so a
    /// warband does not mince itself on a wide swing. A GUARD additionally spares anything it would not have
    /// picked a fight with: the primary victim is always struck, but a bystander has to be aggressive in its
    /// own right, mirroring the guard exemption on the player strip.</para>
    /// </summary>
    private void NpcAttackNpcFootprint(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, NpcRecord attackerNpc,
                                       int victimMap, int victimSlot, MapNpcRecord victimMn, long now)
    {
        MarkNpcCombat(attackerMn, now);
        attackerMn.AttackTimer = now;
        attackerMn.Attacking = true;
        attackerMn.MarkReachedTarget(now);

        var (aSpawnMap, aSpawnSlot) = attackerMn.GetSpawnIdentity(attackerMap, attackerSlot);
        if (aSpawnSlot > 0)
            SendToMap(_world, attackerMap, new NpcAttackPacket { MapNum = aSpawnMap, NpcSlot = aSpawnSlot });

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, attackerMap);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(attackerMn.X, attackerMn.Y);
        int size = attackerNpc.EffectiveSize;
        var strip = WorldCoordHelper.LeadingEdgeTiles(aWX, aWY, size, attackerMn.Dir);
        var (edx, edy) = WorldCoordHelper.DirDelta(attackerMn.Dir);

        // Who is standing on the three tiles — asked of the tiles, not of every roster in the world.
        foreach (var body in SweepTiles(in grid, in strip, view, attackerMn.Layer, edx, edy))
        {
            var other = body.Npc;
            if (other is null) continue;                       // players are the melee-on-player path's business
            if (ReferenceEquals(other, attackerMn)) continue;

            bool isPrimary = ReferenceEquals(other, victimMn);
            if (!isPrimary)
            {
                // Allies are never caught — the same rule the AI acquires by, so a warband does not mince
                // itself. A GUARD additionally spares anything it would not have picked a fight with.
                if (_world.AreNpcsAllied(attackerMn.Num, other.Num)) continue;
                if (attackerNpc.Behavior == NpcBehavior.Guard)
                {
                    var beh = _world.Npcs[other.Num].Behavior;
                    if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                }
            }

            ApplyNpcMeleeHitOnNpc(attackerMap, attackerSlot, attackerMn, attackerNpc,
                                  body.NpcMap, body.NpcSlot, other, now);
        }
    }

    /// <summary>Resolves one melee hit on a single NPC from an already-stamped swing: the victim's block/dodge
    /// rolls, the attacker's crit, and damage.  Does NOT touch the cooldown or the swing FX — the caller does
    /// that once per swing — so it runs once per struck victim and a wide swing costs one beat, not three.
    /// Mirrors <see cref="ApplyNpcMeleeHitOnPlayer"/> on the player side.</summary>
    private void ApplyNpcMeleeHitOnNpc(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, NpcRecord attackerNpc,
                                       int victimMap, int victimSlot, MapNpcRecord victimMn, long now)
    {
        if (victimMn.Num <= 0 || victimMn.Hp <= 0) return;
        var victimNpc = _world.Npcs[victimMn.Num];
        MarkNpcCombat(victimMn, now);
        var (aSpawnMap, aSpawnSlot) = attackerMn.GetSpawnIdentity(attackerMap, attackerSlot);

        // Victim NPC blocks/dodges the incoming melee exactly like a player-attacked NPC does — a 50/50 pick
        // between block and dodge, then the SP-gated CanNpcBlock/CanNpcDodge roll.  On success: drain the
        // victim's SP, float Blocked/Dodged over it, flip its aggro onto the attacker, and eat the swing.
        // Mirrors EngageNpc (player melee) and TryNpcNegateMagicCore (NPC-vs-NPC magic).
        bool tryBlock = Rng.Next(2) == 0;
        if (WindTearsItAway(attackerMap))
        {
            BroadcastCombatText(victimMap, isNpc: true, index: victimSlot, CombatTextKind.Miss, victimMn.X, victimMn.Y);
            AlertNpcFromNpc(victimMap, victimSlot, victimMn, aSpawnMap, aSpawnSlot);
            return;
        }

        if (tryBlock && CanNpcBlock(victimMn, victimMap))
        {
            victimMn.Sp = Math.Max(victimMn.Sp - NpcSpBlockOrCrit(victimNpc, victimMap), 0);
            BroadcastCombatText(victimMap, isNpc: true, index: victimSlot, CombatTextKind.Block, victimMn.X, victimMn.Y);
            AlertNpcFromNpc(victimMap, victimSlot, victimMn, aSpawnMap, aSpawnSlot);
            return;
        }
        if (!tryBlock && CanNpcDodge(victimMn, victimMap))
        {
            victimMn.Sp = Math.Max(victimMn.Sp - NpcSpDodge(victimNpc, victimMap), 0);
            BroadcastCombatText(victimMap, isNpc: true, index: victimSlot, CombatTextKind.Dodge, victimMn.X, victimMn.Y);
            AlertNpcFromNpc(victimMap, victimSlot, victimMn, aSpawnMap, aSpawnSlot);
            return;
        }

        int prot = CombatFormulas.NpcProtection(victimNpc);
        bool wasCrit = CanNpcCritical(attackerMn, attackerMap);
        int damage;
        if (wasCrit)
        {
            attackerMn.Sp = Math.Max(attackerMn.Sp - NpcSpBlockOrCrit(attackerNpc, attackerMap), 0);
            int raw = CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(attackerNpc.Str));
            int crit = CombatFormulas.CritDamage(raw);
            damage = CombatFormulas.ResolveDamage(crit, prot);
        }
        else
        {
            damage = CombatFormulas.ResolveDamage(CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(attackerNpc.Str)), prot);
        }

        if (damage > 0)
        {
            ExecuteNpcVsNpcDamage(attackerMap, attackerSlot, attackerMn, victimMap, victimSlot, victimMn, damage, wasCrit, isSpell: false);
        }
        else
        {
            // Protection ate the swing — record no damage, but float a gray "0" over the victim
            // (Show Combat Numbers gates it) and still alert the victim so an idle NPC flips onto the
            // attacker, matching the player-vs-NPC AlertNpc behavior on 0-damage swings.
            BroadcastCombatText(victimMap, isNpc: true, index: victimSlot, CombatTextKind.ZeroHit, victimMn.X, victimMn.Y);
            AlertNpcFromNpc(victimMap, victimSlot, victimMn, aSpawnMap, aSpawnSlot);
        }
    }

    /// <summary>Shared damage-application body for both NPC melee (<see cref="NpcAttackNpc"/>) and
    /// NPC magic (NpcCastSpellOnNpc).  Standalone — not folded into <see cref="ExecuteNpcDamage"/>
    /// because that path is heavy on player-only concerns (SendMsg to attacker, etc.).  Records the
    /// attacker into <see cref="MapNpcRecord.DamageByNpc"/>, runs the shared EXP/loot/cleanup on
    /// kill, or flips the victim's aggro on non-lethal hits.</summary>
    private void ExecuteNpcVsNpcDamage(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, int victimMap, int victimSlot, MapNpcRecord victimMn, int damage, bool wasCrit, bool isSpell)
    {
        var attackerNpc = _world.Npcs[attackerMn.Num];
        _ = isSpell;  // reserved for future per-source flavor; currently unused on NPC-victim path
        var victimNpc = _world.Npcs[victimMn.Num];
        var (aSpawnMap, aSpawnSlot) = attackerMn.GetSpawnIdentity(attackerMap, attackerSlot);

        // Blood: deposit on the victim NPC's tile, sized by damage vs its effective max HP; a kill always splats.
        _blood.Deposit(victimMap, victimMn.X, victimMn.Y, Constants.BloodDepositStrength(damage, _world.EffectiveNpcMaxHp(victimNpc), victimMn.Hp), victimNpc.EffectiveSize, victimMn.Layer);

        if (damage >= victimMn.Hp)
        {
            // Credit kill-blow before any clears so loot suppression sees the full contribution.
            victimMn.AddNpcDamage(aSpawnMap, aSpawnSlot, victimMn.Hp);
            // Safe-zone NPC-vs-NPC kill: no EXP and no loot, even for human contributors who did
            // more damage.  Guards exist for protection only — players can't use guard help to
            // farm dragged mobs in towns.  The cleanup + death broadcast still fire so the NPC
            // disappears cleanly and the contributor ledger is cleared as usual.
            if (_world.MoralOf(victimMap) != MapMoral.Safe)
            {
                AwardExpForKill(victimMap, victimMn);
                ResolveAndSpawnLoot(victimMap, victimMn);
            }
            ResetNpcCombatLedger(victimMn);
            BroadcastNpcDeathAndCleanup(victimMap, victimMn, victimSlot, damage, wasCrit);
        }
        else
        {
            victimMn.Hp -= damage;
            victimMn.AddNpcDamage(aSpawnMap, aSpawnSlot, damage);
            if (victimMn is TraversalNpcRecord t)
                SendTraversalState(t, damage: damage, isCrit: wasCrit);
            else
                SendToMap(_world, victimMap, new NpcDamagePacket { MapNum = victimMap, NpcSlot = victimSlot, Damage = damage, IsCrit = wasCrit });

            SetNpcAggroFromNpc(victimMap, victimSlot, victimMn, aSpawnMap, aSpawnSlot);
        }
    }

    /// <summary>Re-evaluate a victim NPC's aggro after taking damage from an NPC source.  Symmetric
    /// to <see cref="SetNpcAggro"/> but the contributor universe now includes NPC sources via
    /// <see cref="SelectAggroTargetEx"/>.  Tie keeps the player (favoring player retention).  When
    /// the victim is itself a guard, comrades within viewport propagate to the new target (player or
    /// NPC).  The <paramref name="attackerSpawnMap"/>/<paramref name="attackerSpawnSlot"/> args are
    /// unused for the pick (SelectAggroTargetEx reads the full ledger) but kept in the signature for
    /// call-site clarity and future hooks (e.g. "always retarget the most recent hitter on tie").</summary>
    private void SetNpcAggroFromNpc(int victimMap, int victimSlot, MapNpcRecord victimMn, int attackerSpawnMap, int attackerSpawnSlot)
    {
        _ = attackerSpawnMap;
        _ = attackerSpawnSlot;
        long now = Environment.TickCount64;
        if (victimMn is TraversalNpcRecord tg)
        {
            // Guest: re-point at highest contributor but don't drop to 0 — would send the guest home
            // mid-fight, dropping a still-live target.  Keep current chase if no live contributor.  (Guard
            // grace + PK-bias are honored automatically — SelectAggroTargetEx derives guardMode itself.)
            var pick = SelectAggroTargetEx(victimMap, victimMn);
            if (pick.Player > 0 && pick.Player != victimMn.Target)
            {
                victimMn.Target = pick.Player;
                victimMn.NpcTargetSpawnMap = 0;
                victimMn.NpcTargetSpawnSlot = 0;
                victimMn.MarkReachedTarget(now);
                SendTraversalState(tg);
            }
            else if (pick.NpcSpawnSlot > 0 &&
                     (pick.NpcSpawnMap != victimMn.NpcTargetSpawnMap || pick.NpcSpawnSlot != victimMn.NpcTargetSpawnSlot))
            {
                victimMn.Target = 0;
                victimMn.NpcTargetSpawnMap = pick.NpcSpawnMap;
                victimMn.NpcTargetSpawnSlot = pick.NpcSpawnSlot;
                victimMn.MarkReachedTarget(now);
                SendTraversalState(tg);
                EmitNpcAttackSayBubbleToObservers(victimMap, victimSlot, victimMn, pick.NpcSpawnMap, pick.NpcSpawnSlot);
            }
            if (victimMn.Target > 0 || victimMn.NpcTargetSpawnSlot > 0)
                victimMn.BeginRushEngagement();   // drawn into combat → sprint to close, skip the AoS walk-in
            return;
        }

        var pickN = SelectAggroTargetEx(victimMap, victimMn);
        if (pickN.Player > 0)
        {
            victimMn.Target = pickN.Player;
            victimMn.NpcTargetSpawnMap = 0;
            victimMn.NpcTargetSpawnSlot = 0;
            victimMn.MarkReachedTarget(now);
            MarkNpcCombat(victimMap, victimSlot, now);
            SendToMap(_world, victimMap, new NpcTargetPacket { MapNum = victimMap, NpcSlot = victimSlot, HasTarget = true });
            PropagateGuardAggro(victimMap, victimMn, new GuardTargetSpec(pickN.Player, 0, 0), overwrite: true);
        }
        else if (pickN.NpcSpawnSlot > 0)
        {
            victimMn.Target = 0;
            victimMn.NpcTargetSpawnMap = pickN.NpcSpawnMap;
            victimMn.NpcTargetSpawnSlot = pickN.NpcSpawnSlot;
            victimMn.MarkReachedTarget(now);
            MarkNpcCombat(victimMap, victimSlot, now);
            SendToMap(_world, victimMap, new NpcTargetPacket { MapNum = victimMap, NpcSlot = victimSlot, HasTarget = true });
            EmitNpcAttackSayBubbleToObservers(victimMap, victimSlot, victimMn, pickN.NpcSpawnMap, pickN.NpcSpawnSlot);
            PropagateGuardAggro(victimMap, victimMn, new GuardTargetSpec(0, pickN.NpcSpawnMap, pickN.NpcSpawnSlot), overwrite: true);
        }
        else
        {
            victimMn.Target = 0;
            victimMn.NpcTargetSpawnMap = 0;
            victimMn.NpcTargetSpawnSlot = 0;
        }
        if (victimMn.Target > 0 || victimMn.NpcTargetSpawnSlot > 0)
            victimMn.BeginRushEngagement();   // drawn into combat → sprint to close, skip the AoS walk-in
    }

    /// <summary>NPC magic attack on another NPC.  Mirrors <see cref="NpcCastSpellOnPlayer"/>'s
    /// power-roll / cost-scaling / cooldown semantics — same shape, just with an NPC victim.  Damage
    /// uses <see cref="CombatFormulas.NpcProtection"/> (the single universal MIT — the victim resists
    /// magic exactly as it resists melee); crit chat line is broadcast-omitted (no specific player to
    /// private-message).  Caller is responsible for the range/LoS gate.</summary>
    public void NpcCastSpellOnNpc(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, int victimMap, int victimSlot, MapNpcRecord victimMn)
    {
        var attackerNpc = _world.Npcs[attackerMn.Num];
        if (victimMn.Num <= 0 || victimMn.Hp <= 0) return;
        if (attackerMn.Num <= 0 || attackerMn.Hp <= 0) return;
        // Trivial pool-fraction cast cost (GetSubHpSpellMpCost, mirror of NpcCastSpellOnPlayer) — the same /20 as
        // the player's; in-combat regen out-paces it so a caster sustains, only meleeing if it truly can't afford.
        int mpCost = CombatFormulas.GetSubHpSpellMpCost(_world.EffectiveNpcMaxMp(attackerNpc));
        if (attackerMn.Mp < mpCost) return;

        long now = Environment.TickCount64;
        MarkNpcCombat(attackerMn, now);
        MarkNpcCombat(victimMn, now);

        if (WindTearsItAway(attackerMap))
        {
            BroadcastCombatText(attackerMap, isNpc: true, index: attackerSlot, CombatTextKind.Miss, attackerMn.X, attackerMn.Y);
            attackerMn.Mp -= mpCost;
            attackerMn.AttackTimer = now;
            return;
        }

        attackerMn.Mp -= mpCost;
        attackerMn.AttackTimer = now;
        attackerMn.Attacking = true;
        attackerMn.MarkReachedTarget(now);  // cast counts as reach — any damage action keeps the AoS give-up clock alive
        // Identify the caster by its universal (SpawnMap, SpawnSlot) so a guest attacker resolves too.  A guest
        // VICTIM likewise has no native slot the observer can resolve, so address it by identity via TargetType 3
        // (traversal) — otherwise the bolt can't find it and bursts on the caster.  A native victim keeps its slot.
        var (casterMap, casterSlot) = attackerMn.GetSpawnIdentity(attackerMap, attackerSlot);
        if (casterSlot > 0)
        {
            var castFx = victimMn is TraversalNpcRecord g
                ? new NpcCastPacket { MapNum = casterMap, NpcSlot = casterSlot, TargetType = 3, SpawnMap = g.SpawnMapNum, SpawnSlot = g.SpawnSlot }
                : new NpcCastPacket { MapNum = casterMap, NpcSlot = casterSlot, TargetType = 1, Target = victimSlot, TargetMap = victimMap };
            SendToMap(_world, attackerMap, castFx);
        }

        ApplyNpcSpellHitOnNpc(attackerMap, attackerSlot, attackerMn, attackerNpc, victimMap, victimSlot, victimMn, now);
        SplashNpcSpellOnNpcs(attackerMap, attackerSlot, attackerMn, attackerNpc, victimMap, victimMn, now);
    }

    /// <summary>Resolves one cast landing on a single NPC: that victim's block/dodge, the attacker's crit, and
    /// damage.  Does NOT spend mana, stamp the beat or send the bolt — the caller does that once per cast — so a
    /// splashed body costs the caster nothing extra.  Mirrors <see cref="ApplyNpcSpellHitOnPlayer"/>.</summary>
    private void ApplyNpcSpellHitOnNpc(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, NpcRecord attackerNpc,
                                       int victimMap, int victimSlot, MapNpcRecord victimMn, long now)
    {
        if (victimMn.Num <= 0 || victimMn.Hp <= 0) return;
        var victimNpc = _world.Npcs[victimMn.Num];
        MarkNpcCombat(victimMn, now);

        // A symmetric +-10% Vary around NpcSpellBaseMagnitude(Int) — the SAME Vary as NPC melee, rolled per
        // victim so a splash is not one number copied across three bodies.
        int magnitude = Math.Max(1, CombatFormulas.Vary(CombatFormulas.NpcSpellBaseMagnitude(attackerNpc.Int)));
        int prot = CombatFormulas.NpcProtection(victimNpc);

        // The victim blocks/dodges at the same rate as against a physical hit; that already floated Block/Dodge,
        // so return before the damage path rather than letting it also float a ZeroHit.
        if (TryNpcNegateMagicCore(victimMap, victimSlot, victimMn, victimNpc) != MagicNegation.None) return;

        bool wasCrit = CanNpcSpellCritical(attackerMn, attackerMap);
        if (wasCrit)
        {
            attackerMn.Sp = Math.Max(attackerMn.Sp - NpcSpBlockOrCrit(attackerNpc, attackerMap), 0);
            magnitude = CombatFormulas.CritDamage(magnitude);
        }

        ExecuteNpcVsNpcDamage(attackerMap, attackerSlot, attackerMn, victimMap, victimSlot, victimMn,
                              CombatFormulas.ResolveDamage(magnitude, prot), wasCrit, isSpell: true);
    }

    /// <summary>The rest of a wide caster's break: the NPCs standing either side of where the bolt landed,
    /// PERPENDICULAR to the caster's facing — the same span its melee cleave sweeps, so a 2x2 catches 3 tiles
    /// and a 3x3 catches 5.  A body cannot dodge the splash by picking a side to stand on.
    ///
    /// <para>The impact tile is the caller's; this skips it.  Allies and the guard exemption are the melee
    /// cleave's rules exactly — a warband does not mince itself on its own caster.</para></summary>
    private void SplashNpcSpellOnNpcs(int attackerMap, int attackerSlot, MapNpcRecord attackerMn, NpcRecord attackerNpc,
                                      int victimMap, MapNpcRecord victimMn, long now)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, attackerMap);
        var cell = WorldCoordHelper.GridPosition(in grid, victimMap);
        if (cell is null) return;

        var (iWX, iWY) = grid.ToWorld(cell.Value.col, cell.Value.row, victimMn.X, victimMn.Y);
        var run = SplashRun(iWX, iWY, attackerMn.Dir, attackerNpc.EffectiveSize);
        var view = new ServerTileView(_world, grid);

        foreach (var body in SweepTiles(in grid, in run, view, victimMn.Layer, 0, 0))
        {
            var other = body.Npc;
            if (other is null) continue;
            if (ReferenceEquals(other, attackerMn) || ReferenceEquals(other, victimMn)) continue;
            if (_world.AreNpcsAllied(attackerMn.Num, other.Num)) continue;
            if (attackerNpc.Behavior == NpcBehavior.Guard)
            {
                var beh = _world.Npcs[other.Num].Behavior;
                if (beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
            }

            ApplyNpcSpellHitOnNpc(attackerMap, attackerSlot, attackerMn, attackerNpc,
                                  body.NpcMap, body.NpcSlot, other, now);
        }
    }

    /// <summary>Per-tick aggro re-evaluation for an NPC currently in combat (Target or NpcTarget set).
    /// Asks <see cref="SelectAggroTargetEx"/> for the current highest contributor, and swaps the
    /// NPC's target fields if it changed.  Catches the "contributor disappeared without a damage
    /// event" case — e.g. a player logged out, an attacking NPC died, or a chase took someone out
    /// of the 9-map area — which a purely event-driven aggro flip would miss.  No-op if the ledger
    /// is empty (NPC has a target it acquired via scan but hasn't been hit yet) so an idle-acquired
    /// target doesn't get dropped on the very next tick.  Returns true if the target changed.</summary>
    public bool ReEvaluateAggro(int mapNum, int npcSlot, MapNpcRecord mn)
    {
        var pick = SelectAggroTargetEx(mapNum, mn);
        if (pick.Damage <= 0) return false;  // no live contributor — keep whatever the AI most recently set

        bool alreadyOnPlayer = pick.Player > 0 && pick.Player == mn.Target;
        bool alreadyOnNpc = pick.NpcSpawnSlot > 0
                            && pick.NpcSpawnMap == mn.NpcTargetSpawnMap
                            && pick.NpcSpawnSlot == mn.NpcTargetSpawnSlot;
        if (alreadyOnPlayer || alreadyOnNpc) return false;

        long now = Environment.TickCount64;
        if (pick.Player > 0)
        {
            mn.Target = pick.Player;
            mn.NpcTargetSpawnMap = 0;
            mn.NpcTargetSpawnSlot = 0;
        }
        else
        {
            mn.Target = 0;
            mn.NpcTargetSpawnMap = pick.NpcSpawnMap;
            mn.NpcTargetSpawnSlot = pick.NpcSpawnSlot;
        }
        mn.MarkReachedTarget(now);  // aggro flip = fresh engagement, restart the unreachable clock
        if (mn is TraversalNpcRecord tg)
        {
            // Mirror the native tail below, addressed by identity (slot 0 = no native slot): refresh combat,
            // broadcast the new target, say the attack line on an NPC target, and sync nearby comrade guards.
            MarkNpcCombat(tg, now);
            SendTraversalState(tg);
            if (pick.NpcSpawnSlot > 0)
                EmitNpcAttackSayBubbleToObservers(mapNum, 0, tg, pick.NpcSpawnMap, pick.NpcSpawnSlot);
            PropagateGuardAggro(mapNum, tg, new GuardTargetSpec(pick.Player, pick.NpcSpawnMap, pick.NpcSpawnSlot), overwrite: true);
            return true;
        }
        MarkNpcCombat(mapNum, npcSlot, now);
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = npcSlot, HasTarget = true });
        if (pick.NpcSpawnSlot > 0)
            EmitNpcAttackSayBubbleToObservers(mapNum, npcSlot, mn, pick.NpcSpawnMap, pick.NpcSpawnSlot);
        // Guards keep their squad in sync via PropagateGuardAggro — same as SetNpcAggro flip path.
        PropagateGuardAggro(mapNum, mn,
            new GuardTargetSpec(pick.Player, pick.NpcSpawnMap, pick.NpcSpawnSlot), overwrite: true);
        return true;
    }

    /// <summary>Broadcast an NPC's AttackSay bubble to every player observing the speaker's map, so a
    /// viewport-visible observer sees it now and one who walks in later picks it up from the client's
    /// per-NPC bubble cache. No-op for an empty AttackSay.
    ///
    /// <para>Deduped PER COMBAT SESSION via <see cref="MapNpcRecord.LastAttackSayNpcTarget"/>, which
    /// resets only on combat exit, cross-border, death or spawn. Once any AttackSay fires this session the
    /// speaker stays silent even if aggro flips between NPC targets, so a ping-pong A→B→A cannot leak a
    /// re-fire on the third emit. The stored value is effectively a "said it this session" flag, kept for
    /// consistency with the player-target dedup.</para>
    ///
    /// <para>Native speakers address by (mapNum, npcSlot); traversal guests carry their permanent
    /// (SpawnMap, SpawnSlot) identity so the client can find them in TraversalNpcs.</para></summary>
    public void EmitNpcAttackSayBubbleToObservers(int mapNum, int npcSlot, MapNpcRecord mn, int victimSpawnMap, int victimSpawnSlot)
    {
        var npcRec = _world.Npcs[mn.Num];
        if (string.IsNullOrWhiteSpace(npcRec.AttackSay)) return;
        if (mn.LastAttackSayNpcTarget != 0) return;  // already said this combat session
        mn.LastAttackSayNpcTarget = MapNpcRecord.EncodeNpcId(victimSpawnMap, victimSpawnSlot);
        NpcChatBubblePacket bubble;
        if (npcSlot > 0)
            bubble = PacketBuilder.NpcChatBubble(mapNum, npcSlot, npcRec.AttackSay.TrimEnd(), kind: 0);
        else if (mn is TraversalNpcRecord tg)
            bubble = PacketBuilder.TraversalNpcChatBubble(tg.SpawnMapNum, tg.SpawnSlot, npcRec.AttackSay.TrimEnd(), kind: 0);
        else
            return;
        SendToMap(_world, mapNum, bubble);
    }
}

using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>NPC versus player: melee and spell attacks an NPC lands on a character, including
/// the oversize-footprint reach rules and the damage application that can end in a death.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── NPC vs Player ─────────────────────────────────────────────────────────

    // `now` is the caller's monotonic tick, threaded in rather than sampled here — the convention
    // IClock's summary states, and the one thing on this path that was breaking it. Sampling
    // Environment.TickCount64 inside these methods stamped MapNpcRecord.LastReachedTargetMs from a
    // different clock than ShouldGiveUpUnreachableAosTarget reads it with, so the AoS give-up timer
    // measured the gap between two unrelated time bases. In the live server the two coincide (the
    // game loop's AI tick passes Environment.TickCount64), which is why it never showed in play —
    // it only surfaced under an injected clock, i.e. in a test, and then only on a machine whose
    // uptime happened to fall on the wrong side of the injected value.
    public bool CanNpcAttackPlayer(int mapNum, int npcSlot, int victimIndex, long now)
    {
        if (!SlotValidation.IsValidNpcSlot(npcSlot)) return false;
        return CanNpcAttackPlayer(mapNum, _world.MapNpcs[mapNum, npcSlot], victimIndex, now);
    }

    /// <summary>
    /// Object-based form usable by both native slot NPCs and traversal (chasing) NPCs.  Adjacency
    /// is checked in world space, so an NPC can strike a player one tile away even across a map seam
    /// — including the case where the player stands on the very tile the NPC would step onto, which
    /// it can't (occupied), so "coming to it" is impossible and only a cross-seam swing connects.
    /// Mirrors player→target cross-seam melee.  Map moral is deliberately NOT a gate: a hostile NPC can
    /// strike a player standing just inside a safe map (see the note at the end of the body).
    /// </summary>
    public bool CanNpcAttackPlayer(int mapNum, MapNpcRecord mapNpc, int victimIndex, long now)
    {
        long windMult = _world.WeatherOn(mapNum) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (!AiCadence.Elapsed(now, mapNpc.AttackTimer, Constants.NpcAttackCooldownMs * windMult)) return false;
        return NpcInMeleeRangeOfPlayer(mapNum, mapNpc, victimIndex);
    }

    /// <summary>
    /// The REACH half of <see cref="CanNpcAttackPlayer"/> — everything but the swing cooldown.
    ///
    /// <para>🔴 This is what the legs pass asks, and the distinction is the whole point. "Can I swing right
    /// now" and "am I standing close enough" are different questions, and an NPC that takes the first for the
    /// second walks away between swings: the cooldown starts the instant it hits, so for the next second the
    /// attack gate says no and the chase step reads that as "not there yet". It steps in, the cooldown ends,
    /// it swings, it steps again — the beat-to-beat shuffle in a fight that should be a mob standing still and
    /// hitting. Oversize bodies make it obvious because their step lands on a tile they cannot occupy.</para>
    /// </summary>
    public bool NpcInMeleeRangeOfPlayer(int mapNum, MapNpcRecord mapNpc, int victimIndex)
    {
        if (!_pm[victimIndex].IsPlaying) return false;
        if (_pm[victimIndex].GettingMap) return false;
        if (_pm[victimIndex].Char.Dead) return false;  // a corpse can't be attacked/damaged/killed; this gate is above MarkPlayerCombat so it also blocks the combat re-stamp
        if (_pm[victimIndex].Char.GodMode) return false;    // an observer is not there to be swung at
        if (mapNpc.Num <= 0 || mapNpc.Hp <= 0) return false;

        var vp = _pm[victimIndex].Char;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var tw = grid.ToWorldRelative(vp.Map, vp.X, vp.Y);
        if (tw is null) return false;  // victim is outside the NPC's 9-map region
        var (npcWX, npcWY) = grid.CenterToWorld(mapNpc.X, mapNpc.Y);
        int size = _world.Npcs[mapNpc.Num].EffectiveSize;
        if (size <= 1)
        {
            if (WorldCoordHelper.WorldManhattan(npcWX, npcWY, tw.Value.worldX, tw.Value.worldY) != 1) return false;
        }
        else
        {
            // Large NPC: the victim must sit on a tile just past the leading edge in the direction the NPC
            // will face (FootprintFacingToward, matching FaceTargetDir) — so this gate and the strike strip in
            // NpcAttackPlayer always agree, and the NPC never "attacks" a corner tile it would then miss. The
            // facing is measured from the BODY, so every tile touching it is reachable from some edge.
            var faceDir = WorldCoordHelper.FootprintFacingToward(npcWX, npcWY, size, tw.Value.worldX, tw.Value.worldY);
            if (!WorldCoordHelper.LeadingEdgeTiles(npcWX, npcWY, size, faceDir).Contains(tw.Value.worldX, tw.Value.worldY))
                return false;
        }

        // Two-plane connect gate (mirrors the player's MeleeLayerConnects): an NPC on the ground can't melee a
        // player up on the fringe — or vice-versa — unless the fringe endpoint is on a ramp.  Without this an NPC
        // directly beneath a bridged player could still swing while the player couldn't hit back (the reported
        // asymmetry).  Same-layer neighbors always connect, so flat-map melee is unchanged.
        if (!LayerLogic.LayerConnects(new ServerTileView(_world, grid), npcWX, npcWY, mapNpc.Layer,
                tw.Value.worldX, tw.Value.worldY, vp.Layer))
        {
            return false;
        }

        // No safe-zone block on attacks: a hostile NPC can melee a player who's one tile across the
        // seam into a safe map.  Without this, a player could stand one tile inside the safe zone
        // and farm the NPC with zero retaliation — the seam becomes a no-cost barrier.  Spell casts
        // already cross seams unrestricted, so removing the melee block just closes the asymmetry.
        // The player can still escape by walking deeper into the safe zone, past adjacency range.
        return true;
    }

    public void NpcAttackPlayer(int mapNum, int npcSlot, int victimIndex, long now)
    {
        if (!SlotValidation.IsValidNpcSlot(npcSlot)) return;
        NpcAttackPlayer(mapNum, _world.MapNpcs[mapNum, npcSlot], npcSlot, victimIndex, now);
    }

    /// <summary>
    /// Object-based NPC melee on a player.  Pass <paramref name="npcSlot"/> = 0 for a traversal guest; the swing
    /// is broadcast (NpcAttackPacket) for natives AND guests alike, addressed by the record's universal
    /// <see cref="MapNpcRecord.GetSpawnIdentity"/>, so the client spawns the swoosh + sparks the same either way.
    /// </summary>
    public void NpcAttackPlayer(int mapNum, MapNpcRecord mapNpc, int npcSlot, int victimIndex, long now)
    {
        if (!CanNpcAttackPlayer(mapNum, mapNpc, victimIndex, now)) return;
        var npcRec = _world.Npcs[mapNpc.Num];

        // A large NPC swings once but strikes every player on the tiles just past its leading edge (the
        // faced direction).  Factored out so the size-1 path below stays byte-for-byte the original.
        if (npcRec.EffectiveSize > 1)
        {
            NpcAttackPlayerFootprint(mapNum, mapNpc, npcSlot, npcRec, victimIndex, now);
            return;
        }

        MarkNpcCombat(mapNpc, now);
        MarkPlayerCombat(victimIndex, now, asAttacker: false);
        BreakGraceForCombat(victimIndex, involvesPlayerOrGuard: npcRec.Behavior == NpcBehavior.Guard);
        var vp = _pm[victimIndex].Char;

        // A swing that reaches a target costs the beat, strikes the pose, counts as reach and shows the
        // swoosh — whatever it then resolves to (see HandleAttack). Reach is closing the distance to swing,
        // which a defended swing did as much as a landed one, and the give-up clock runs off it.
        mapNpc.AttackTimer = now;
        mapNpc.Attacking = true;
        mapNpc.MarkReachedTarget(now);
        // The swing is an EVENT addressed by the attacker's universal identity (GetSpawnIdentity = home slot
        // for a native, spawn slot for a guest), so observers spawn the crescent swoosh + sparks for guests
        // exactly as for natives.  The Attacking flag in a guest's state packet drives only the sprite POSE,
        // not the one-shot FX — same event-parity pattern as the NPC cast bolt.
        var (swingMap, swingSlot) = mapNpc.GetSpawnIdentity(mapNum, npcSlot);
        if (swingSlot > 0)
            SendToMap(_world, mapNum, new NpcAttackPacket { MapNum = swingMap, NpcSlot = swingSlot });

        if (WindTearsItAway(mapNum))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_AttackerMissed, GameColor.BrightCyan, ("AttackerName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Miss);
            return;
        }

        if (CanPlayerBlock(vp))
        {
            int shieldSlot = vp.ShieldSlot;
            string shieldName = _world.Items[vp.Inv[shieldSlot].Num].TrimmedName;
            SendMsg(victimIndex, ServerStrings.CombatSystem_YourShieldBlockedNpc, GameColor.BrightCyan, ("ShieldName", shieldName), ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Block);
            DegradeArmor(victimIndex, shieldSlot);
            DrainSpForBlock(victimIndex);
            return;
        }
        if (CanPlayerDodge(vp))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_YouDodgedNpc, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Dodge);
            DrainSpForDodge(victimIndex);
            return;
        }

        // NPC melee applies its full STR against the player's protection — no halving.
        int prot = GetPlayerProtection(victimIndex);
        bool wasCrit = CanNpcCritical(mapNpc, mapNum);
        int damage;
        if (wasCrit)
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            int raw = CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(npcRec.Str));
            int crit = CombatFormulas.CritDamage(raw);
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(crit, prot, npcRec.IsBoss);
            SendMsg(victimIndex, ServerStrings.CombatSystem_NpcSwingsMight, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        }
        else
        {
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(npcRec.Str)), prot, npcRec.IsBoss);
        }

        ApplyNpcDamageToPlayer(mapNum, npcRec, victimIndex, damage, wasCrit, isSpell: false);
    }

    /// <summary>Whether a guard may strike <paramref name="index"/> on its own account — the same test the
    /// guard AI acquires by (<c>NpcAiSystem.Behaviors</c>): a PK past their grace, or an active PvP
    /// aggressor. Anyone else is a bystander.</summary>
    private bool IsGuardFairGame(int index, long now)
    {
        if (!_pm[index].IsPlaying) return false;
        long nowUtc = NowUtc;
        bool isPk = _pm[index].Char.IsPk(nowUtc) && _pm[index].PkGraceUntilUtc <= nowUtc;
        return isPk || _pm[index].PvpAttackerUntil > now;
    }

    /// <summary>
    /// Large-NPC melee: ONE swing (cooldown + swoosh FX stamped once) that strikes every tile just past the
    /// NPC's leading edge in its facing direction, damaging each player standing there.  The caller already
    /// gated (CanNpcAttackPlayer) that the primary victim is on that leading edge; any other players on the
    /// strip are caught here too.  The strip is resolved in world space, so it can already reach across a
    /// seam.  Magic is unaffected (separate path).
    /// </summary>
    private void NpcAttackPlayerFootprint(int mapNum, MapNpcRecord mapNpc, int npcSlot, NpcRecord npcRec, int victimIndex, long now)
    {
        MarkNpcCombat(mapNpc, now);
        mapNpc.AttackTimer = now;
        mapNpc.Attacking = true;
        mapNpc.MarkReachedTarget(now);  // the swing landed on its leading edge — physical reach

        // Broadcast the single swing once, addressed by the attacker's universal identity (native slot or
        // guest spawn slot), exactly like the size-1 path.
        var (swingMap, swingSlot) = mapNpc.GetSpawnIdentity(mapNum, npcSlot);
        if (swingSlot > 0)
            SendToMap(_world, mapNum, new NpcAttackPacket { MapNum = swingMap, NpcSlot = swingSlot });

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(mapNpc.X, mapNpc.Y);
        var strip = WorldCoordHelper.LeadingEdgeTiles(aWX, aWY, npcRec.EffectiveSize, mapNpc.Dir);
        var (edx, edy) = WorldCoordHelper.DirDelta(mapNpc.Dir);   // strip tile is one step in Dir from the NPC's front row
        // Who is standing on the strip — asked of the tiles, not of every roster in the world.
        foreach (var body in SweepTiles(in grid, in strip, view, mapNpc.Layer, edx, edy))
        {
            int i = body.PlayerIndex;
            if (i <= 0) continue;                  // other NPCs are the melee-on-npc path's business
            // A GUARD spares bystanders. An ordinary large mob swings at everything on the strip — it does
            // not care who is standing there — but a guard is not trying to kill innocents, and a wide guard
            // cutting down the crowd behind its actual target would make the safest tiles in the world the
            // most dangerous.
            //
            // The primary victim is always struck: the guard acquired them legitimately, which includes the
            // case of someone who attacked the guard itself and is neither PK nor flagged. Everyone ELSE on
            // the strip has to be fair game in their own right.
            if (npcRec.Behavior == NpcBehavior.Guard && i != victimIndex && !IsGuardFairGame(i, now)) continue;
            ApplyNpcMeleeHitOnPlayer(mapNum, mapNpc, npcRec, i, now);
        }
    }

    /// <summary>Resolves one melee hit on a single player from an already-stamped large-NPC swing: per-victim
    /// combat marks, block/dodge rolls, crit, and damage.  Does NOT touch cooldown/swing FX (the caller does
    /// that once per swing), so it runs once per struck player.  Mirrors the size-1 per-victim resolution.</summary>
    private void ApplyNpcMeleeHitOnPlayer(int mapNum, MapNpcRecord mapNpc, NpcRecord npcRec, int victimIndex, long now)
    {
        if (!_pm[victimIndex].IsPlaying || _pm[victimIndex].GettingMap || _pm[victimIndex].Char.Dead) return;
        MarkPlayerCombat(victimIndex, now, asAttacker: false);
        BreakGraceForCombat(victimIndex, involvesPlayerOrGuard: npcRec.Behavior == NpcBehavior.Guard);
        var vp = _pm[victimIndex].Char;

        // A swing that reaches a target costs the beat whatever it resolves to — see HandleAttack.
        mapNpc.AttackTimer = now;

        if (WindTearsItAway(mapNum))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_AttackerMissed, GameColor.BrightCyan, ("AttackerName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Miss);
            return;
        }

        if (CanPlayerBlock(vp))
        {
            int shieldSlot = vp.ShieldSlot;
            string shieldName = _world.Items[vp.Inv[shieldSlot].Num].TrimmedName;
            SendMsg(victimIndex, ServerStrings.CombatSystem_YourShieldBlockedNpc, GameColor.BrightCyan, ("ShieldName", shieldName), ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Block);
            DegradeArmor(victimIndex, shieldSlot);
            DrainSpForBlock(victimIndex);
            return;
        }
        if (CanPlayerDodge(vp))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_YouDodgedNpc, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Dodge);
            DrainSpForDodge(victimIndex);
            return;
        }

        int prot = GetPlayerProtection(victimIndex);
        bool wasCrit = CanNpcCritical(mapNpc, mapNum);
        int damage;
        if (wasCrit)
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            int raw = CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(npcRec.Str));
            int crit = CombatFormulas.CritDamage(raw);
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(crit, prot, npcRec.IsBoss);
            SendMsg(victimIndex, ServerStrings.CombatSystem_NpcSwingsMight, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        }
        else
        {
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(CombatFormulas.Vary(CombatFormulas.NpcMeleeBaseDamage(npcRec.Str)), prot, npcRec.IsBoss);
        }
        ApplyNpcDamageToPlayer(mapNum, npcRec, victimIndex, damage, wasCrit, isSpell: false);
    }

    /// <summary>NPC magic attack on a player.  Mirrors <see cref="NpcAttackPlayer"/>'s shape but uses
    /// Int for the damage stat, the player's magic protection instead of physical, and broadcasts
    /// <see cref="NpcCastPacket"/> (or sets <c>Attacking</c> for traversal guests).  Crit roll uses
    /// <see cref="CanNpcSpellCritical"/> (the INT mirror of melee's <see cref="CanNpcCritical"/>) plus the
    /// same <see cref="CombatFormulas.SpCostForBlockOrCrit"/> + <see cref="CombatFormulas.CritDamage"/>, with a distinct
    /// "casts with overwhelming force!" chat line.  Cost is the same trivial pool-fraction as the player's,
    /// via <see cref="CombatFormulas.GetSubHpSpellMpCost"/> (in-combat regen out-paces it, so the caster sustains).  Caller owns the cooldown gate
    /// (<see cref="MapNpcRecord.AttackTimer"/>) and range/LoS checks — this method just applies
    /// the effect once the AI has decided to cast.</summary>
    public void NpcCastSpellOnPlayer(int mapNum, int npcSlot, MapNpcRecord mapNpc, int victimIndex, long now)
    {
        var npcRec = _world.Npcs[mapNpc.Num];
        if (!_pm[victimIndex].IsPlaying) return;
        if (_pm[victimIndex].GettingMap) return;
        if (_pm[victimIndex].Char.Dead) return;  // a corpse can't be attacked/damaged/killed; above MarkPlayerCombat so no combat re-stamp
        if (_pm[victimIndex].Char.GodMode) return;    // an observer is not there to be cast at
        if (mapNpc.Num <= 0 || mapNpc.Hp <= 0) return;
        // NPC cast cost is the same trivial pool-fraction as the player's (GetSubHpSpellMpCost = round(maxMp/20)).
        // NPCs pay no reagent, and in-combat MP regen out-paces this drain, so a caster SUSTAINS its spell attack
        // like a player instead of bursting out.  If it genuinely can't afford a cast (e.g. Snow cuts max MP to
        // near zero) it falls back to melee rather than idling — mana is a distant ceiling, not a per-cast gate.
        int mpCost = CombatFormulas.GetSubHpSpellMpCost(_world.EffectiveNpcMaxMp(npcRec));
        if (mapNpc.Mp < mpCost) return;

        MarkNpcCombat(mapNpc, now);
        MarkPlayerCombat(victimIndex, now, asAttacker: false);
        BreakGraceForCombat(victimIndex, involvesPlayerOrGuard: npcRec.Behavior == NpcBehavior.Guard);

        if (WindTearsItAway(mapNum))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_AttackerSpellMissed, GameColor.BrightCyan, ("AttackerName", npcRec.TrimmedName));
            // Over the TARGET, like a block or a dodge — and charged the same, bolt included.
            BroadcastCombatText(_pm[victimIndex].Char.Map, isNpc: false, index: victimIndex, CombatTextKind.Miss);
            ChargeNpcCast(mapNum, npcSlot, mapNpc, victimIndex, mpCost, now);
            return;
        }

        // Spell magnitude MIRRORS NPC melee: a symmetric +-10% Vary around NpcSpellBaseMagnitude(Int) (the same
        // Vary as P-DMG).  Full magnitude every cast — the trivial pool-fraction mpCost (computed above) lets the
        // caster sustain its damage instead of OOMing, so there is no MP cap on the roll.
        int magnitude = Math.Max(1, CombatFormulas.Vary(CombatFormulas.NpcSpellBaseMagnitude(npcRec.Int)));

        // Damage: rolled magnitude minus player's mitigation (single universal MIT — magic and physical
        // resisted identically, and this hit wears the player's gear like a melee hit).  The +-10% Vary above
        // is the whole variance (mirror of melee); crit applies CritDamage to the rolled magnitude.
        int prot = GetPlayerProtection(victimIndex);
        var victimForBlock = _pm[victimIndex].Char;   // capture shield name before negation (a break would clear the slot)
        string blockShieldName = victimForBlock.ShieldSlot > 0 ? _world.Items[victimForBlock.Inv[victimForBlock.ShieldSlot].Num].TrimmedName : "";
        var negation = TryPlayerNegateMagic(victimIndex);   // shield → block, no shield → dodge (mirror of melee)
        bool negated = negation != MagicNegation.None;
        bool wasCrit = !negated && CanNpcSpellCritical(mapNpc, mapNum);
        int damage;
        if (negation == MagicNegation.Blocked)
        {
            damage = 0;
            SendMsg(victimIndex, ServerStrings.CombatSystem_YourShieldBlockedSpellNpc, GameColor.BrightCyan, ("ShieldName", blockShieldName), ("NpcName", npcRec.TrimmedName));
        }
        else if (negation == MagicNegation.Dodged)
        {
            damage = 0;
            SendMsg(victimIndex, ServerStrings.CombatSystem_YouDodgedNpc, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        }
        else if (wasCrit)
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            int crit = CombatFormulas.CritDamage(magnitude);
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(crit, prot, npcRec.IsBoss);
            SendMsg(victimIndex, ServerStrings.CombatSystem_NpcCastsForce, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        }
        else
        {
            damage = CombatFormulas.ResolveNpcVsPlayerDamage(magnitude, prot, npcRec.IsBoss);
        }

        ChargeNpcCast(mapNum, npcSlot, mapNpc, victimIndex, mpCost, now);

        // A blocked/dodged spell already showed the Block/Dodge float + its message; skip the damage path
        // so it doesn't also fire the zero-damage "didn't phase" taunt (reserved for truly over-mitigated hits).
        if (!negated)
            ApplyNpcDamageToPlayer(mapNum, npcRec, victimIndex, damage, wasCrit, isSpell: true);

        // A wide caster's spell breaks over the tiles either side of where it landed — see the splash.
        if (npcRec.EffectiveSize > 1)
            SplashNpcSpellOnPlayers(mapNum, mapNpc, npcRec, victimIndex, now);
    }

    /// <summary>
    /// The spell breaking either side of where it landed, for a caster wider than one tile.
    ///
    /// <para>🔴 A body that cleaves three tiles wide in melee should not become a single-tile threat by
    /// raising a hand. The splash runs PERPENDICULAR to the line the spell travelled and reaches
    /// <c>size - 1</c> tiles each way, so a 2x2 caster covers 3 tiles and a 3x3 covers 5 — the same width
    /// its body would sweep. It is also what stops a player choosing the safe side of a friend to stand on:
    /// there is no side of the impact that is not in it.</para>
    ///
    /// <para>The impact tile itself is already resolved by the caller, so this starts one tile out. The cast
    /// is charged and broadcast once, by the caller — a splash is one spell, not several.</para>
    /// </summary>
    private void SplashNpcSpellOnPlayers(int mapNum, MapNpcRecord mapNpc, NpcRecord npcRec, int victimIndex, long now)
    {
        var vp = _pm[victimIndex].Char;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var cell = WorldCoordHelper.GridPosition(in grid, vp.Map);
        if (cell is null) return;

        var (iWX, iWY) = grid.ToWorld(cell.Value.col, cell.Value.row, vp.X, vp.Y);
        var run = SplashRun(iWX, iWY, mapNpc.Dir, npcRec.EffectiveSize);
        var view = new ServerTileView(_world, grid);

        // Everything caught in the break, asked of the tiles. (0,0) for the step-back: a spell breaks where
        // it landed, so there is no attacker's front row to measure the plane from — the impact's own plane
        // is what it spreads across.
        foreach (var body in SweepTiles(in grid, in run, view, vp.Layer, 0, 0))
        {
            if (body.PlayerIndex <= 0 || body.PlayerIndex == victimIndex) continue;
            ApplyNpcSpellHitOnPlayer(mapNum, mapNpc, npcRec, body.PlayerIndex, now);
        }
    }

    /// <summary>The tiles a wide caster's spell breaks across: the impact plus <c>size - 1</c> either side,
    /// PERPENDICULAR to the caster's facing — the same axis its melee cleave sweeps, so a 2x2 covers 3 tiles
    /// and a 3x3 covers 5.
    ///
    /// <para>🔴 Take the axis from the caster's FACING, not from anchor-to-victim. A wide body's anchor is
    /// its top-left corner, so a victim pressed against its left face can sit further away in y than in x,
    /// and an anchor-derived axis fans the spell ALONG its flight instead of across it — no splash at all
    /// where a player is standing.</para></summary>
    private static TileRun SplashRun(int impactWX, int impactWY, Direction facing, int size)
    {
        int reach = size - 1;
        return facing is Direction.Left or Direction.Right
            ? new TileRun(impactWX, impactWY - reach, 0, 1, size + reach)
            : new TileRun(impactWX - reach, impactWY, 1, 0, size + reach);
    }

    /// <summary>Resolves one already-cast spell on a single splash victim: their own combat marks, negation
    /// roll, crit and damage. Charges nothing and broadcasts no bolt — the caller did that once for the whole
    /// cast — so this runs once per caught player. Mirrors the primary victim's resolution above.</summary>
    private void ApplyNpcSpellHitOnPlayer(int mapNum, MapNpcRecord mapNpc, NpcRecord npcRec, int victimIndex, long now)
    {
        if (!_pm[victimIndex].IsPlaying || _pm[victimIndex].GettingMap) return;
        if (_pm[victimIndex].Char.Dead || _pm[victimIndex].Char.GodMode) return;

        MarkPlayerCombat(victimIndex, now, asAttacker: false);
        BreakGraceForCombat(victimIndex, involvesPlayerOrGuard: npcRec.Behavior == NpcBehavior.Guard);

        if (WindTearsItAway(mapNum))
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_AttackerSpellMissed, GameColor.BrightCyan, ("AttackerName", npcRec.TrimmedName));
            BroadcastCombatText(_pm[victimIndex].Char.Map, isNpc: false, index: victimIndex, CombatTextKind.Miss);
            return;
        }

        int magnitude = Math.Max(1, CombatFormulas.Vary(CombatFormulas.NpcSpellBaseMagnitude(npcRec.Int)));
        int prot = GetPlayerProtection(victimIndex);
        var victimForBlock = _pm[victimIndex].Char;
        string blockShieldName = victimForBlock.ShieldSlot > 0 ? _world.Items[victimForBlock.Inv[victimForBlock.ShieldSlot].Num].TrimmedName : "";
        var negation = TryPlayerNegateMagic(victimIndex);
        bool wasCrit = negation == MagicNegation.None && CanNpcSpellCritical(mapNpc, mapNum);

        switch (negation)
        {
            case MagicNegation.Blocked:
                SendMsg(victimIndex, ServerStrings.CombatSystem_YourShieldBlockedSpellNpc, GameColor.BrightCyan, ("ShieldName", blockShieldName), ("NpcName", npcRec.TrimmedName));
                return;
            case MagicNegation.Dodged:
                SendMsg(victimIndex, ServerStrings.CombatSystem_YouDodgedNpc, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
                return;
        }

        if (wasCrit)
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            magnitude = CombatFormulas.CritDamage(magnitude);
            SendMsg(victimIndex, ServerStrings.CombatSystem_NpcCastsForce, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        }

        ApplyNpcDamageToPlayer(mapNum, npcRec, victimIndex,
            CombatFormulas.ResolveNpcVsPlayerDamage(magnitude, prot, npcRec.IsBoss), wasCrit, isSpell: true);
    }

    /// <summary>Spends the mana, stamps the beat and broadcasts the bolt for one NPC cast — everything a cast
    /// pays whatever it resolves to, so a torn cast costs exactly what a landed one does.
    /// <para>AttackTimer gates both the swing (via <see cref="CanNpcAttackPlayer"/>) and the cast-decision roll
    /// (via TryNpcMagicAction): after a cast the NPC waits NpcAttackCooldownMs before swinging AND
    /// SpellCastCooldownMs before casting again — the same 1-second beat the player has between any combat
    /// actions. The caster rides its universal (SpawnMap, SpawnSlot) — for a native that's its home map+slot;
    /// for a guest it's the home identity, not the transient list index, so the observer can resolve it.</para></summary>
    private void ChargeNpcCast(int mapNum, int npcSlot, MapNpcRecord mapNpc, int victimIndex, int mpCost, long now)
    {
        mapNpc.Mp -= mpCost;
        mapNpc.AttackTimer = now;
        mapNpc.Attacking = true;
        mapNpc.MarkReachedTarget(now);  // cast counts as reach — any damage action keeps the AoS give-up clock alive
        var (casterMap, casterSlot) = mapNpc.GetSpawnIdentity(mapNum, npcSlot);
        if (casterSlot <= 0) return;
        SendToMap(_world, mapNum, new NpcCastPacket
        {
            MapNum = casterMap, NpcSlot = casterSlot,
            TargetType = 0, Target = victimIndex, TargetMap = _pm[victimIndex].Char.Map,
        });
    }

    /// <summary>Shared damage-application body for both NPC melee (<see cref="NpcAttackPlayer"/>)
    /// and NPC magic (<see cref="NpcCastSpellOnPlayer"/>).  Handles the zero-damage taunt, the
    /// regular hit broadcast, and the full death-penalty / warp / vital-reset path on lethal
    /// damage.  <paramref name="isSpell"/> swaps the "hit" verb to "blasted" and the "hit"
    /// noun to "spell" in the messages so spell hits read as spells, not swings.</summary>
    private void ApplyNpcDamageToPlayer(int mapNum, NpcRecord npcRec, int victimIndex, int damage, bool wasCrit, bool isSpell)
    {
        // Belt-and-suspenders: the single shared NPC→player damage chokepoint never touches a corpse or an
        // observer, so no future caller (DoT/AoE/trap) can reach either through a path that skipped the gates.
        if (_pm[victimIndex].Char.Dead) return;
        if (_pm[victimIndex].Char.GodMode) return;
        // NPC-vs-player damage disfavor: on-level mobs are +20% HP (favor) AND hit players softer, so PvE fights
        // stay impactful without spiking a squishy build down.  PvE-only (player→NPC / NPC→NPC stay full mirror),
        // applied here post-mitigation.  Guard on damage>0 so a fully-phased-out hit stays 0.  The kill-EXP danger
        // term mirrors this multiplier (ExpFormulas.ExpForKill) so EXP prices the softened threat.
        if (damage > 0)
            damage = (int)Math.Round(damage * Constants.NpcVsPlayerDamageMultiplier, MidpointRounding.AwayFromZero);

        // Night boost: NPCs hit players harder after dark (binary Night). Single chokepoint for melee and
        // spell, applied post-mitigation so the boosted number is what's shown, subtracted, and lethality-checked.
        // Stacks on top of the disfavor (effective night factor = disfavor x night multiplier).
        if (_world.TimePhase == TimePhase.Night && damage > 0)
            damage = (int)Math.Round(damage * Constants.NpcNightDamageMultiplier, MidpointRounding.AwayFromZero);

        var vp = _pm[victimIndex].Char;
        string attackNoun = isSpell ? "spell" : "hit";
        string hitKey = isSpell ? ServerStrings.CombatSystem_NpcHitYouMagic : ServerStrings.CombatSystem_NpcHitYouPhysical;

        if (damage == 0)
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_NpcAttackNoPhase, GameColor.BrightBlue, ("NpcName", npcRec.TrimmedName), ("AttackNoun", attackNoun));
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.ZeroHit);
            return;
        }

        // Blood: deposit on the victim's tile (still their pre-warp tile even on a kill); a kill always splats.
        _blood.Deposit(vp.Map, vp.X, vp.Y, Constants.BloodDepositStrength(damage, vp.MaxHp, vp.Hp), layer: vp.Layer);

        if (damage >= vp.Hp)
        {
            vp.Hp = 0;
            SendToMapBut(_world, vp.Map, victimIndex, PacketBuilder.SendHp(victimIndex, 0, vp.MaxHp, showFloat: true, isCrit: wasCrit, damage: damage));
            _dispatcher.SendTo(victimIndex, PacketBuilder.SendHp(victimIndex, 0, vp.MaxHp));
            SendMsg(victimIndex, hitKey, GameColor.BrightRed, ("NpcName", npcRec.TrimmedName), ("Damage", damage));
            // Kill-feed flavor by relative strength — same NpcLevel-vs-player gap as the on-target
            // "trivial readout": a much stronger mob "slaughtered" the player, a much weaker one is a
            // careless death, anything in between is a plain kill. Pure flavor; penalties below unchanged.
            int npcGap = StatFormulas.NpcLevel(npcRec) - vp.Level;
            string npcKilledKey =
                npcGap >= Constants.NpcStrengthTierGap ? ServerStrings.CombatSystem_PlayerSlaughteredByNpc :
                npcGap <= -Constants.NpcStrengthTierGap ? ServerStrings.CombatSystem_PlayerCarelessDeathByNpc :
                ServerStrings.CombatSystem_PlayerKilledByNpc;
            _dispatcher.SendLocalizedChatToAll(npcKilledKey,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                ("VictimName", vp.TrimmedName), ("NpcName", npcRec.TrimmedName));

            ClearNpcTargetsFor(mapNum, victimIndex);
            ClearPlayerNpcContributions(victimIndex, mapNum);

            if (vp.Level >= 10)
            {
                if (vp.IsPk(NowUtc))
                {
                    SendMsg(victimIndex, ServerStrings.CombatSystem_PkDeathPenalty, GameColor.BrightRed, ChatChannel.System);
                    // Caster parity BEFORE drops (destroy prepared-spell-priced reagents; the rest drop).
                    DestroyCasterDeathReagents(victimIndex, 20);
                    // Drops run BEFORE durability damage so a piece that breaks on death still gets the
                    // equipped drop CHANCE rather than being force-dropped as unequipped (see
                    // DegradeEquipped) — order is load-bearing, don't swap.
                    DropNonEquippedInventory(victimIndex);
                    DropRandomEquipped(victimIndex);
                    DegradeEquipped(victimIndex, 20);

                    long loss = ApplyExpLoss(victimIndex, ExpFormulas.DeathExpLossPk(vp.Level));
                    // Silent at 0: the operator switched EXP loss off, so there is no penalty to report.
                    if (loss > 0)
                        SendMsg(victimIndex, ServerStrings.CombatSystem_ExpLoss, GameColor.BrightRed, ChatChannel.Rewards, ("Loss", loss));
                    // PK flag is NOT cleared by an NPC kill — only a player kill removes it.
                }
                else
                {
                    // Caster parity BEFORE drops (destroy prepared-spell-priced reagents; the rest drop).
                    DestroyCasterDeathReagents(victimIndex, 10);
                    // Drops run BEFORE durability damage so a piece that breaks on death is still treated
                    // as equipped for this death's drop rules (a broken piece is then unequipped + kept)
                    // — order is load-bearing, don't swap.
                    DropRandomNonEquippedInventory(victimIndex);
                    DegradeEquipped(victimIndex, 10);

                    long loss = ApplyExpLoss(victimIndex, ExpFormulas.DeathExpLossNormal(vp.Level));
                    if (loss > 0)
                        SendMsg(victimIndex, ServerStrings.CombatSystem_ExpLoss, GameColor.BrightRed, ChatChannel.Rewards, ("Loss", loss));
                }
            }
            else
            {
                SendMsg(victimIndex, ServerStrings.CombatSystem_Sub10DeathSpared, GameColor.BrightBlue, ChatChannel.System);
            }

            // Death ends combat like a natural expiry — capture before zeroing (see the PvP death path).
            bool victimWasInCombat = _pm[victimIndex].WasInCombat;
            _pm[victimIndex].CombatExpiresAt = 0;
            _pm[victimIndex].WasInCombat = false;
            _pm[victimIndex].PvpAttackerUntil = 0;
            _pm[victimIndex].AttackTimer = 0;   // respawn act-ready (mirrored client-side in ClearMapState)
            BroadcastPlayerDeathFx(victimIndex);   // death animation at the tile the player fell on
            if (victimWasInCombat)   // combat-exit notice, AFTER the death broadcast, matching a natural expiry
                SendMsg(victimIndex, ServerStrings.RegenerationSystem_CombatEnded, GameColor.BrightGreen, ChatChannel.System);
            // Enter the timed dead state instead of respawning; EnterDeadState's broadcast
            // carries aggressorUntilUtc=0, which also clears any non-PK aggressor flash.
            EnterDeadState(victimIndex);
            if (_pm[victimIndex].IsGhost)
                _joinLeave.ClearGhost(victimIndex);
        }
        else
        {
            vp.Hp -= damage;
            SendToMap(_world, vp.Map, PacketBuilder.SendHp(victimIndex, vp.Hp, vp.MaxHp, showFloat: true, isCrit: wasCrit, msSinceCombat: VictimCombatStamp(victimIndex)));
            SendMsg(victimIndex, hitKey, GameColor.BrightRed, ("NpcName", npcRec.TrimmedName), ("Damage", damage));
        }
    }
}

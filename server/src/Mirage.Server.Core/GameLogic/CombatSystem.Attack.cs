using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The melee entry point: a player's attack keypress, and the NPC engagement it
/// triggers. Resolves what is in front of the attacker, then hands off to the vs-player or
/// vs-NPC path.</summary>
public sealed partial class CombatSystem : GameSystem
{
    public void HandleAttack(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't attack (covers player-vs-player and player-vs-NPC)
        // An observer swings at nothing: no packet, no beat, no whiff. Said out loud, or the key would
        // simply feel broken.
        if (_pm[index].Char.GodMode) { SayGodModeRefusal(index); return; }
        var ap = _pm[index].Char;
        long now = Environment.TickCount64;

        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (i == index) continue;
            if (!CanAttackPlayer(index, i)) continue;

            // Target found: broadcast with InCombat so observers show the attacker in combat.
            SendToMapBut(_world, ap.Map, index, new PlayerAttackPacket { Index = index, InCombat = true });
            MarkPlayerCombat(index, now, asAttacker: true);
            MarkPlayerCombat(i, now, asAttacker: false);
            MarkPvpInitiator(index, i, now);
            _dispatcher.SendTo(index, new PlayerAttackPacket { Index = index, InCombat = true });
            AssignPlayerTarget(index, i);
            var vp = _pm[i].Char;
            // A swing that reaches a target costs the beat whatever it resolves to — blocked, dodged,
            // torn off course, mitigated to nothing, or landed. Stamped here rather than in each branch so
            // no outcome can be added later that forgets to pay.
            _pm[index].AttackTimer = now;

            if (WindTearsItAway(ap.Map))
            {
                SendMsg(index, ServerStrings.CombatSystem_YourAttackMissed, GameColor.BrightCyan);
                SendMsg(i, ServerStrings.CombatSystem_AttackerMissed, GameColor.BrightCyan, ("AttackerName", ap.TrimmedName));
                BroadcastCombatText(vp.Map, isNpc: false, index: i, CombatTextKind.Miss);
            }
            else if (CanPlayerBlock(vp))
            {
                int shieldSlot = vp.ShieldSlot;  // always > 0 — CanPlayerBlock requires it
                string shieldName = _world.Items[vp.Inv[shieldSlot].Num].TrimmedName;
                SendMsg(index, ServerStrings.CombatSystem_VictimShieldBlocked, GameColor.BrightCyan, ("VictimName", vp.TrimmedName), ("ShieldName", shieldName));
                SendMsg(i, ServerStrings.CombatSystem_YourShieldBlocked, GameColor.BrightCyan, ("ShieldName", shieldName), ("AttackerName", ap.TrimmedName));
                BroadcastCombatText(vp.Map, isNpc: false, index: i, CombatTextKind.Block);
                // A shield degrades on a successful block — blocking is what wears it.
                DegradeArmor(i, shieldSlot, _pm[index].Char.Map);
                DrainSpForBlock(i);
            }
            else if (CanPlayerDodge(vp))
            {
                SendMsg(index, ServerStrings.CombatSystem_VictimDodged, GameColor.BrightCyan, ("VictimName", vp.TrimmedName));
                SendMsg(i, ServerStrings.CombatSystem_YouDodged, GameColor.BrightCyan, ("AttackerName", ap.TrimmedName));
                BroadcastCombatText(vp.Map, isNpc: false, index: i, CombatTextKind.Dodge);
                DrainSpForDodge(i);
            }
            else if (CanPlayerCritical(ap))
            {
                DrainSpForCrit(index);
                int raw = GetPlayerDamage(index, _pm[i].Char.Map);
                int crit = CombatFormulas.CritDamage(raw);
                int damage = CombatFormulas.ResolveDamage(CombatFormulas.Vary(crit), GetPlayerProtection(i, _pm[index].Char.Map), CombatFormulas.PvpDamageMultiplier);
                SendMsg(index, ServerStrings.CombatSystem_YouSurge, GameColor.BrightCyan);
                SendMsg(i, ServerStrings.CombatSystem_AttackerMightPvp, GameColor.BrightCyan, ("AttackerName", ap.TrimmedName));
                if (damage > 0)
                {
                    AttackPlayer(index, i, damage, isCrit: true);
                }
                else
                {
                    SendMsg(index, ServerStrings.CombatSystem_AttackDoesNothing, GameColor.BrightRed);
                    SendMsg(i, ServerStrings.CombatSystem_AttackNoPhase, GameColor.BrightBlue, ("AttackerName", ap.TrimmedName));
                    BroadcastCombatText(vp.Map, isNpc: false, index: i, CombatTextKind.ZeroHit);
                }
            }
            else
            {
                int raw = GetPlayerDamage(index, _pm[i].Char.Map);
                int damage = CombatFormulas.ResolveDamage(CombatFormulas.Vary(raw), GetPlayerProtection(i, _pm[index].Char.Map), CombatFormulas.PvpDamageMultiplier);
                if (damage > 0)
                {
                    AttackPlayer(index, i, damage);
                }
                else
                {
                    SendMsg(index, ServerStrings.CombatSystem_AttackDoesNothing, GameColor.BrightRed);
                    SendMsg(i, ServerStrings.CombatSystem_AttackNoPhase, GameColor.BrightBlue, ("AttackerName", ap.TrimmedName));
                    BroadcastCombatText(vp.Map, isNpc: false, index: i, CombatTextKind.ZeroHit);
                }
            }
            return;
        }

        // NPCs: scan every observable map for an NPC in the faced tile (cross-map melee), checking
        // both native slot NPCs and visiting traversal guests.  World-space adjacency in CanAttackNpc
        // means at most the one faced tile can match.
        Span<int> observed = stackalloc int[9];
        int observedCount = _world.ObservedMapsInto(ap.Map, observed);
        for (int oi = 0; oi < observedCount; oi++)
        {
            int mapNum = observed[oi];
            for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            {
                if (!CanAttackNpc(index, mapNum, _world.MapNpcs[mapNum, i], i, out bool rebuffed))
                {
                    if (rebuffed) return;   // faced a Friendly/Stationary NPC — its rebuff say fired; suppress the whiff swing
                    continue;
                }
                EngageNpc(index, mapNum, _world.MapNpcs[mapNum, i], i, now);
                return;
            }
            var guests = _world.MapTraversalNpcs[mapNum];
            for (int j = 0; j < guests.Count; j++)
            {
                if (!CanAttackNpc(index, mapNum, guests[j], 0, out bool rebuffedGuest))
                {
                    if (rebuffedGuest) return;   // faced a Friendly/Stationary guest — its rebuff say fired; no whiff swing
                    continue;
                }
                EngageNpc(index, mapNum, guests[j], 0, now);  // slot 0 → traversal path
                return;
            }
        }

        // Empty swing — no adjacent target. Broadcast the whiff animation to observers AND the attacker
        // themselves (mirroring the hit path's observer + self send) so everyone sees the miss. InCombat
        // stays false: it keeps the attacker's combat bar off, and the client keys the crescent's sparks off
        // it too, so a whiff shows the blade-arc alone (no sparks) while a hit shows the arc plus sparks.
        SendToMapBut(_world, ap.Map, index, new PlayerAttackPacket { Index = index });
        _dispatcher.SendTo(index, new PlayerAttackPacket { Index = index });
    }

    // Resolves one player melee swing against a specific NPC record (native slot or traversal guest,
    // npcSlot 0).  Handles the block/dodge/crit cascade and applies damage via the object path.
    private void EngageNpc(int index, int mapNum, MapNpcRecord mapNpcR, int npcSlot, long now)
    {
        var ap = _pm[index].Char;
        var npcRec = _world.Npcs[mapNpcR.Num];

        // Target found: broadcast with InCombat so observers show the attacker in combat.
        SendToMapBut(_world, ap.Map, index, new PlayerAttackPacket { Index = index, InCombat = true });
        MarkPlayerCombat(index, now, asAttacker: true);
        MarkNpcCombat(mapNpcR, now);
        _dispatcher.SendTo(index, new PlayerAttackPacket { Index = index, InCombat = true });
        AssignNpcTarget(index, mapNum, mapNpcR, npcSlot);

        // A swing that reaches a target costs the beat whatever it resolves to — see HandleAttack.
        _pm[index].AttackTimer = now;

        if (WindTearsItAway(ap.Map))
        {
            SendMsg(index, ServerStrings.CombatSystem_YourAttackMissed, GameColor.BrightCyan);
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.Miss, mapNpcR.X, mapNpcR.Y);
            AlertNpc(mapNum, npcSlot, mapNpcR, index);
            return;
        }

        bool tryBlock = Rng.Next(2) == 0;
        if (tryBlock && CanNpcBlock(mapNpcR, mapNum))
        {
            mapNpcR.Sp = Math.Max(mapNpcR.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            SendMsg(index, ServerStrings.CombatSystem_NpcBlocked, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.Block, mapNpcR.X, mapNpcR.Y);
            AlertNpc(mapNum, npcSlot, mapNpcR, index);
            return;
        }
        if (!tryBlock && CanNpcDodge(mapNpcR, mapNum))
        {
            mapNpcR.Sp = Math.Max(mapNpcR.Sp - NpcSpDodge(npcRec, mapNum), 0);
            SendMsg(index, ServerStrings.CombatSystem_NpcDodged, GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.Dodge, mapNpcR.X, mapNpcR.Y);
            AlertNpc(mapNum, npcSlot, mapNpcR, index);
            return;
        }

        bool wasCrit = CanPlayerCritical(ap);
        int damage;
        if (wasCrit)
        {
            DrainSpForCrit(index);
            int raw = GetPlayerDamage(index);
            int crit = CombatFormulas.CritDamage(raw);
            damage = CombatFormulas.ResolvePlayerVsNpcDamage(CombatFormulas.Vary(crit), CombatFormulas.NpcProtection(npcRec));
            SendMsg(index, ServerStrings.CombatSystem_YouSurge, GameColor.BrightCyan);
        }
        else
        {
            int raw = GetPlayerDamage(index);
            damage = CombatFormulas.ResolvePlayerVsNpcDamage(CombatFormulas.Vary(raw), CombatFormulas.NpcProtection(npcRec));
        }

        if (damage > 0)
        {
            StrikeNpc(index, mapNum, mapNpcR, npcSlot, damage, isCrit: wasCrit);
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_AttackDoesNothing, GameColor.BrightRed);
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.ZeroHit, mapNpcR.X, mapNpcR.Y);
            AlertNpc(mapNum, npcSlot, mapNpcR, index);
        }
    }
}

using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The reaction gates — block, dodge, critical, and magic negation — for players and
/// NPCs alike. Each is a stamina-priced roll; the drains themselves live in .Costs.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Block / dodge / critical helpers ─────────────────────────────────────
    // No hidden gate. The returned chance value IS the actual combat probability (rolled against
    // Constants.ChancePercentRollSides). At the current Constants.ChanceScaleFactor = 1:
    //   player block/crit/spellcrit 35%, player dodge 15%, NPC block/crit 25%, NPC dodge 10%.
    // Bump ChanceScaleFactor to 10 to reread the same caps as per-mille (3.5% / 1.5% / 2.5% / 1.0%).
    // NPC caps stay lower than players' because NPC DEF also drives HP/EXP.

    /// <summary>Heavy Wind prevents all stamina-based combat procs (block/dodge/crit) from occurring.</summary>
    private bool StaminaProcsAllowed(int map) => _world.WeatherOn(map) != WeatherType.HeavyWind;

    /// <summary>Whether the weather tears this attack or cast off course. Rolled on the ATTACKER's map and
    /// checked BEFORE any block, dodge or crit, so a blown swing drains the defender nothing — it never
    /// arrived. That ordering is what keeps it separate from the proc cascade rather than a fourth branch
    /// of it, and it is why a miss floats its own text instead of reusing Block or Dodge.</summary>
    public bool WindTearsItAway(int attackerMap) =>
        WeatherEffects.MissChancePercent(_world.WeatherOn(attackerMap)) > Rng.Percent();

    /// <summary>Shield equipped + SP gate + not Heavy Wind + RNG roll vs PlayerBlockChancePerMille.</summary>
    private bool CanPlayerBlock(PlayerRecord p) =>
        p.Sp > 0 && p.ShieldSlot > 0 && StaminaProcsAllowed(p.Map)
        && CombatFormulas.PlayerBlockChancePerMille(p.Def, p.Level) > Rng.PerMille();

    /// <summary>Result of a player's attempt to negate an incoming spell — the mirror of melee block/dodge.</summary>
    public enum MagicNegation { None, Blocked, Dodged }

    /// <summary>Player magic negation — the EXACT mirror of melee negation: with a shield the player BLOCKS the
    /// spell (same Def/Level chance and SP cost as a melee block; the block wears the shield), and with NO shield
    /// the player DODGES it (same chance and SP cost as a melee dodge).  Floats Block/Dodge and drains SP; returns
    /// which (if any) fired so the caller can send the matching message.  Magic *damage* that lands still never
    /// wears gear — only the block does, exactly as in melee.</summary>
    public MagicNegation TryPlayerNegateMagic(int victimIndex, int opponentMap = 0)
    {
        var vp = _pm[victimIndex].Char;
        if (CanPlayerBlock(vp))
        {
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Block);
            DegradeArmor(victimIndex, vp.ShieldSlot, opponentMap);
            DrainSpForBlock(victimIndex);
            return MagicNegation.Blocked;
        }
        if (CanPlayerDodge(vp))
        {
            BroadcastCombatText(vp.Map, isNpc: false, index: victimIndex, CombatTextKind.Dodge);
            DrainSpForDodge(victimIndex);
            return MagicNegation.Dodged;
        }
        return MagicNegation.None;
    }

    /// <summary>Player-vs-player spell negation: runs <see cref="TryPlayerNegateMagic"/> on the victim and, on a
    /// block or dodge, sends both players the matching message (spell-deflect messages for a block, the shared
    /// melee dodge messages for a dodge).  Returns true if negated (the caller then skips the spell's damage/drain).</summary>
    public bool TryPlayerNegateMagicFromPlayer(int attackerIndex, int victimIndex)
    {
        var vp = _pm[victimIndex].Char;
        string shieldName = vp.ShieldSlot > 0 ? _world.Items[vp.Inv[vp.ShieldSlot].Num].TrimmedName : "";
        var result = TryPlayerNegateMagic(victimIndex, _pm[attackerIndex].Char.Map);
        if (result == MagicNegation.None) return false;
        var ap = _pm[attackerIndex].Char;
        if (result == MagicNegation.Blocked)
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_YourShieldBlockedSpell, GameColor.BrightCyan, ("ShieldName", shieldName), ("AttackerName", ap.TrimmedName));
            SendMsg(attackerIndex, ServerStrings.CombatSystem_VictimShieldBlockedSpell, GameColor.BrightCyan, ("VictimName", vp.TrimmedName), ("ShieldName", shieldName));
        }
        else
        {
            SendMsg(victimIndex, ServerStrings.CombatSystem_YouDodged, GameColor.BrightCyan, ("AttackerName", ap.TrimmedName));
            SendMsg(attackerIndex, ServerStrings.CombatSystem_VictimDodged, GameColor.BrightCyan, ("VictimName", vp.TrimmedName));
        }
        return true;
    }

    /// <summary>No shield + SP gate + not Heavy Wind + RNG roll vs PlayerDodgeChancePerMille.</summary>
    private bool CanPlayerDodge(PlayerRecord p) =>
        p.Sp > 0 && p.ShieldSlot == 0 && StaminaProcsAllowed(p.Map)
        && CombatFormulas.PlayerDodgeChancePerMille(p.Def, p.Level) > Rng.PerMille();

    /// <summary>Weapon equipped + SP gate + not Heavy Wind + RNG roll vs PlayerCriticalChancePerMille.</summary>
    private bool CanPlayerCritical(PlayerRecord p) =>
        p.Sp > 0 && p.WeaponSlot > 0 && StaminaProcsAllowed(p.Map)
        && CombatFormulas.PlayerCriticalChancePerMille(p.Str, p.Level) > Rng.PerMille();

    /// <summary>SP gate + not Heavy Wind + RNG roll vs SpellCriticalChancePerMille.</summary>
    public bool CanSpellCritical(PlayerRecord p) =>
        p.Sp > 0 && StaminaProcsAllowed(p.Map)
        && CombatFormulas.SpellCriticalChancePerMille(p.Int, p.Level) > Rng.PerMille();

    private bool CanNpcCritical(MapNpcRecord mapNpc, int map) =>
        mapNpc.Sp > 0 && StaminaProcsAllowed(map)
        && CombatFormulas.NpcCriticalChancePerMille(_world.Npcs[mapNpc.Num].Str) > Rng.PerMille();

    /// <summary>NPC spell crit — the INT mirror of <see cref="CanNpcCritical"/> (STR melee crit).</summary>
    private bool CanNpcSpellCritical(MapNpcRecord mapNpc, int map) =>
        mapNpc.Sp > 0 && StaminaProcsAllowed(map)
        && CombatFormulas.NpcSpellCriticalChancePerMille(_world.Npcs[mapNpc.Num].Int) > Rng.PerMille();

    private bool CanNpcBlock(MapNpcRecord mapNpc, int map) =>
        mapNpc.Sp > 0 && StaminaProcsAllowed(map)
        && CombatFormulas.NpcBlockChancePerMille(_world.Npcs[mapNpc.Num].Def) > Rng.PerMille();

    /// <summary>Independent of block.</summary>
    private bool CanNpcDodge(MapNpcRecord mapNpc, int map) =>
        mapNpc.Sp > 0 && StaminaProcsAllowed(map)
        && CombatFormulas.NpcDodgeChancePerMille(_world.Npcs[mapNpc.Num].Def) > Rng.PerMille();

    /// <summary>Core NPC magic negation — the EXACT mirror of NPC melee negation: a 50/50 coin flip picks block
    /// vs dodge, then the SP-gated <see cref="CanNpcBlock"/>/<see cref="CanNpcDodge"/> roll decides.  On success:
    /// drain the NPC's SP and float Block/Dodge; return which fired.  No messaging/aggro — the player-facing and
    /// NPC-vs-NPC callers add those.</summary>
    private MagicNegation TryNpcNegateMagicCore(int mapNum, int npcSlot, MapNpcRecord mapNpc, NpcRecord npcRec)
    {
        bool tryBlock = Rng.Next(2) == 0;
        if (tryBlock && CanNpcBlock(mapNpc, mapNum))
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpBlockOrCrit(npcRec, mapNum), 0);
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.Block, mapNpc.X, mapNpc.Y);
            return MagicNegation.Blocked;
        }
        if (!tryBlock && CanNpcDodge(mapNpc, mapNum))
        {
            mapNpc.Sp = Math.Max(mapNpc.Sp - NpcSpDodge(npcRec, mapNum), 0);
            BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.Dodge, mapNpc.X, mapNpc.Y);
            return MagicNegation.Dodged;
        }
        return MagicNegation.None;
    }

    /// <summary>An NPC negates an incoming PLAYER spell (<see cref="TryNpcNegateMagicCore"/>) and, on success,
    /// messages the caster and aggros onto them.  Returns true if negated (caller skips the spell's damage/drain).</summary>
    public bool TryNpcNegateMagic(int mapNum, int npcSlot, MapNpcRecord mapNpc, NpcRecord npcRec, int attackerIndex)
    {
        var result = TryNpcNegateMagicCore(mapNum, npcSlot, mapNpc, npcRec);
        if (result == MagicNegation.None) return false;
        SendMsg(attackerIndex,
            result == MagicNegation.Blocked ? ServerStrings.CombatSystem_NpcBlockedSpell : ServerStrings.CombatSystem_NpcDodged,
            GameColor.BrightCyan, ("NpcName", npcRec.TrimmedName));
        AlertNpc(mapNum, npcSlot, mapNpc, attackerIndex);
        return true;
    }
}

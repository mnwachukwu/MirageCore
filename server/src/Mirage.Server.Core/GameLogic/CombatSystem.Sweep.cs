using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Which body on an NPC's leading edge its next swing lands on.</summary>
public sealed partial class CombatSystem : GameSystem
{
    /// <summary>Someone an NPC may strike on the edge it is FACING right now — a player, a native NPC or a
    /// visiting guest, whichever the tiles hold.</summary>
    public readonly record struct FaceVictim(int PlayerIndex, int NpcMap, int NpcSlot, MapNpcRecord? Npc);

    /// <summary>The first body on this NPC's leading edge that it may swing at, or null when the edge is
    /// empty or its beat is not ready.
    ///
    /// <para>🔴 A wide NPC's swing covers a whole edge, but its TARGET is one body. When that body steps off
    /// the edge, the swing gate refuses and the two enemies still pressed against the face go unhit while the
    /// NPC turns away to chase the one that left. This asks the edge instead of the target, so the beat lands
    /// on whoever is actually standing there.</para>
    ///
    /// <para>Allies, the guard exemption and the observer/corpse rules are the same ones the swing itself
    /// applies, so nothing is returned here that the strike would then refuse.</para></summary>
    public FaceVictim? FirstVictimOnFace(int mapNum, MapNpcRecord attackerMn, long now)
    {
        if (attackerMn.Num <= 0 || attackerMn.Hp <= 0) return null;
        var attackerNpc = _world.Npcs[attackerMn.Num];

        long windMult = _world.WeatherOn(mapNum) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (!TickCadence.Elapsed(now, attackerMn.AttackTimer, Constants.NpcAttackCooldownMs * windMult)) return null;

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(attackerMn.X, attackerMn.Y);
        var strip = WorldCoordHelper.LeadingEdgeTiles(aWX, aWY, attackerNpc.EffectiveSize, attackerMn.Dir);
        var (edx, edy) = WorldCoordHelper.DirDelta(attackerMn.Dir);

        _queries.SweepTiles(in grid, in strip, attackerMn.Layer, edx, edy, _swept);
        foreach (var body in _swept)
        {
            if (body.Npc is { } other)
            {
                if (ReferenceEquals(other, attackerMn)) continue;
                if (_world.AreNpcsAllied(attackerMn.Num, other.Num)) continue;
                var beh = _world.Npcs[other.Num].Behavior;
                if (attackerNpc.Behavior == NpcBehavior.Guard
                    && beh != NpcBehavior.AttackOnSight && beh != NpcBehavior.AttackWhenAttacked) continue;
                return new FaceVictim(0, body.NpcMap, body.NpcSlot, other);
            }

            int i = body.PlayerIndex;
            if (i <= 0 || !_pm[i].IsPlaying || _pm[i].GettingMap) continue;
            if (_pm[i].Char.Dead || _pm[i].Char.GodMode) continue;
            if (attackerNpc.Behavior == NpcBehavior.Guard && !IsGuardFairGame(i, now)) continue;
            return new FaceVictim(i, 0, 0, null);
        }
        return null;
    }
}

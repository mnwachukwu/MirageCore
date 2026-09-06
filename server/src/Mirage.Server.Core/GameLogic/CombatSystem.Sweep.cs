using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// What a wide attack lands on: the bodies standing on a run of tiles.
///
/// <para>🔴 ASKED TILE BY TILE, never by walking rosters. A sweep that iterates the native NPC slots, then
/// the guest list, then the player set has to be taught about every kind of body that can stand on a map,
/// and silently misses the ones it was never taught — which is exactly how a three-tile swing came to pass
/// straight through a visiting NPC. A cleave covers three tiles; the honest question is "who is on these
/// three tiles", and it is asked once, here, for melee and magic alike.</para>
///
/// <para>Bodies are DEDUPED: an oversize victim covering two tiles of the run is one body and takes one
/// hit. And a gap in the run costs the tiles past it nothing — every tile is asked independently, because
/// a sweep along an edge is not a spear down a line.</para>
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>One body caught by a sweep: an NPC (with the map + slot to address it by) or a player.</summary>
    private readonly record struct SweptBody(MapNpcRecord? Npc, int NpcMap, int NpcSlot, int PlayerIndex);

    // Reused across sweeps — the game loop is single-threaded, and a swing should not allocate.
    private readonly List<SweptBody> _swept = new();

    /// <summary>Everything standing on <paramref name="run"/>, deduped, in tile order.
    ///
    /// <para><paramref name="srcDx"/>/<paramref name="srcDy"/> step from a run tile BACK to the attacker's
    /// own front row, so each tile is layer-connect tested from where the blow actually comes from — a
    /// ground body does not reach a victim up on a bridge without a ramp. Pass (0,0) for a sweep that has no
    /// direction to step back along (a spell breaking where it landed), and the impact tile's own layer is
    /// used instead.</para>
    ///
    /// <para>The returned list is REUSED — read it before the next sweep.</para></summary>
    private List<SweptBody> SweepTiles(in MapGrid grid, in TileRun run, ServerTileView view,
                                       WorldLayer attackerLayer, int srcDx, int srcDy)
    {
        _swept.Clear();
        for (int i = 0; i < run.Count; i++)
        {
            var (wx, wy) = run[i];
            var (tMap, tx, ty) = grid.ResolveWorldTile(wx, wy);
            if (tMap <= 0) continue;

            if (_world.NpcCoveringLocal(tMap, tx, ty) is { } hit)
            {
                if (Connects(view, wx, wy, attackerLayer, srcDx, srcDy, hit.Npc.Layer)
                    && !AlreadySwept(hit.Npc))
                    _swept.Add(new SweptBody(hit.Npc, tMap, hit.Slot, 0));
            }

            // Players are one tile each, so the map's observer set is the cheapest way to find whoever is
            // standing here — everyone on a map observes it.
            foreach (int p in _world.MapObservers[tMap])
            {
                if (!_pm[p].IsPlaying) continue;
                var pc = _pm[p].Char;
                if (pc.Map != tMap || pc.X != tx || pc.Y != ty) continue;
                if (!Connects(view, wx, wy, attackerLayer, srcDx, srcDy, pc.Layer)) continue;
                _swept.Add(new SweptBody(null, 0, 0, p));
            }
        }
        return _swept;
    }

    private static bool Connects(ServerTileView view, int wx, int wy, WorldLayer attackerLayer,
                                 int srcDx, int srcDy, WorldLayer victimLayer)
        => (srcDx == 0 && srcDy == 0)
            ? attackerLayer == victimLayer
            : LayerLogic.LayerConnects(view, wx - srcDx, wy - srcDy, attackerLayer, wx, wy, victimLayer);

    private bool AlreadySwept(MapNpcRecord npc)
    {
        for (int i = 0; i < _swept.Count; i++)
            if (ReferenceEquals(_swept[i].Npc, npc)) return true;
        return false;
    }

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
        if (!AiCadence.Elapsed(now, attackerMn.AttackTimer, Constants.NpcAttackCooldownMs * windMult)) return null;

        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(attackerMn.X, attackerMn.Y);
        var strip = WorldCoordHelper.LeadingEdgeTiles(aWX, aWY, attackerNpc.EffectiveSize, attackerMn.Dir);
        var (edx, edy) = WorldCoordHelper.DirDelta(attackerMn.Dir);

        foreach (var body in SweepTiles(in grid, in strip, view, attackerMn.Layer, edx, edy))
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

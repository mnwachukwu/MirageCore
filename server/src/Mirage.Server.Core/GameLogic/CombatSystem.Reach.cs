using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Whether an attacker can reach a target at all: the seam-aware facing tests for a player
/// and for an NPC footprint, and the two-plane connect gate. Shared by the PvP and player-vs-NPC paths.</summary>
public sealed partial class CombatSystem : GameSystem
{
    /// <summary>
    /// True when an actor on <paramref name="actorMap"/> facing <paramref name="dir"/> from
    /// (ax,ay) is one tile — in world space — from a target at (tx,ty) on <paramref name="targetMap"/>.
    /// Returns false when the target's map isn't observable from the actor's (so it can't be melee'd).
    /// This is what lets melee land across a seamless border.
    /// </summary>
    private bool IsFacingTargetAcrossMaps(int actorMap, Direction dir, int ax, int ay, int targetMap, int tx, int ty)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var tw = grid.ToWorldRelative(targetMap, tx, ty);
        if (tw is null) return false;
        var (awx, awy) = grid.CenterToWorld(ax, ay);
        return WorldCoordHelper.IsAdjacentInDir(awx, awy, dir, tw.Value.worldX, tw.Value.worldY);
    }

    /// <summary>Footprint-aware melee-facing test for a player attacking an NPC: true when the tile the
    /// attacker faces (one step in <paramref name="dir"/> from (ax,ay), in world space) lands on any tile of
    /// the NPC's SxS footprint.  For a size-1 NPC this is exactly <see cref="IsFacingTargetAcrossMaps"/>
    /// against its single tile.</summary>
    private bool IsFacingNpcAcrossMaps(int actorMap, Direction dir, int ax, int ay, int npcMap, MapNpcRecord mapNpc, int size)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var nw = grid.ToWorldRelative(npcMap, mapNpc.X, mapNpc.Y);
        if (nw is null) return false;
        var (awx, awy) = grid.CenterToWorld(ax, ay);
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        return WorldCoordHelper.FootprintContains(nw.Value.worldX, nw.Value.worldY, size, awx + dx, awy + dy);
    }

    /// <summary>Two-layer melee connect: after 2-D adjacency is confirmed, the attacker (on
    /// <paramref name="actorLayer"/> at (ax,ay)) and the adjacent target one tile in <paramref name="dir"/> (on
    /// <paramref name="targetLayer"/>) must connect across layers per <see cref="LayerLogic.LayerConnects"/> —
    /// same layer always; across layers only when one of them stands on a ramp (a person on a ramp can melee an
    /// adjacent ground OR fringe entity; a plain ground and a plain fringe entity never reach each other).
    /// Seam-aware.</summary>
    private bool MeleeLayerConnects(int actorMap, int ax, int ay, WorldLayer actorLayer, Direction dir, WorldLayer targetLayer)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, actorMap);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = grid.CenterToWorld(ax, ay);
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        return LayerLogic.LayerConnects(view, aWX, aWY, actorLayer, aWX + dx, aWY + dy, targetLayer);
    }
}

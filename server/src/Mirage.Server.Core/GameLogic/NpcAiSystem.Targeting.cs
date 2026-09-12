using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Choosing and letting go of whoever an NPC has noticed: the player and NPC scanners, the
/// give-up clock, and the full reset a native runs when it lets go.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    // Seamless world: a noticing NPC sees players within Range across the whole 9-map observable area,
    // using world-tile distance from its own position (the center cell).  A player spotted on a
    // neighbor map is acquired here; the chase logic then walks the NPC to the border.
    // Iterates MapObservers[mapNum] — the pre-maintained set of players who can see this map (which is
    // exactly the players in the observable area) — instead of the whole 1,000-slot roster.
    // LoS gate: a player behind a wall or closed door is skipped — the loop keeps scanning, so the
    // winner is the lowest-level player who is BOTH in range AND visible (not "lowest, bail if blocked").
    // Reachability gate: a candidate that passes LoS but has no walkable BFS path (e.g. behind an
    // NpcAvoid wall) is also skipped — without this, a pursuer would lock on, fail to close, give up,
    // and instantly reacquire the same unreachable player forever.  Lowest-level REACHABLE player
    // wins (nearest breaks equal-level ties); if none reachable, the NPC stays idle until a path
    // opens or a reachable player wanders into range.
    private int FindNoticeablePlayer(int mapNum, MapNpcRecord mn, int range)
    {
        var npc = _world.Npcs[mn.Num];
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (npcWX, npcWY) = grid.CenterToWorld(mn.X, mn.Y);
        var los = new WorldLosPredicate(_world, grid, mn.Layer);
        int best = 0, bestDist = int.MaxValue;
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            if (_pm[i].Char.Dead) continue;  // never notice a corpse: it would re-lock every idle beat
            if (_pm[i].Char.GodMode) continue;    // nor an observer, which nothing can see and nothing can reach
            var p = _pm[i].Char;
            var gp = grid.PositionOf(p.Map);
            if (gp is null) continue;  // defensive: observer that left the area mid-tick
            var (pwx, pwy) = grid.ToWorld(gp.Value.col, gp.Value.row, p.X, p.Y);
            // Range is measured from the BODY, so a big NPC notices you at the same distance on every side.
            if (!WorldCoordHelper.AreFootprintsWithin(npcWX, npcWY, npc.EffectiveSize, pwx, pwy, 1, range)) continue;
            int d = WorldCoordHelper.FootprintManhattan(npcWX, npcWY, npc.EffectiveSize, pwx, pwy, 1);
            // Nearest wins. A cheap cutoff before the LoS and BFS work below.
            if (d >= bestDist) continue;
            if (!WorldCoordHelper.HasClearSpellLineOfSight(npcWX, npcWY, pwx, pwy, los)) continue;
            if (FindStepTowardObservableArea(mapNum, mn.X, mn.Y, mn.Layer, p.Map, p.X, p.Y, p.Layer, npc) is null)
                continue;
            best = i;
            bestDist = d;
        }
        return best;
    }

    /// <summary>Scans the 9-map observable area for the nearest NPC this one would notice: within
    /// <see cref="NpcRecord.Range"/> and NOT kin (see <see cref="GameWorld.AreNpcsKin"/> — wolves
    /// ignore other wolves, and a tagged pack keeps to itself). Natives and traversal guests both
    /// count. LoS gated like <see cref="FindNoticeablePlayer"/>, and reachability gated for the same
    /// reason; the distance check runs before both, so those only fire for a candidate that could beat
    /// the current best. Returns (0, 0) when nothing eligible is in range.</summary>
    private (int SpawnMap, int SpawnSlot) FindNoticeableNpc(int mapNum, int selfSlot, MapNpcRecord self)
    {
        var selfNpc = _world.Npcs[self.Num];
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (aWX, aWY) = grid.CenterToWorld(self.X, self.Y);
        var los = new WorldLosPredicate(_world, grid, self.Layer);
        int range = selfNpc.Range;
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
                    if (m == mapNum && s == selfSlot) continue;
                    var other = _world.MapNpcs[m, s];
                    if (other.Num <= 0) continue;
                    if (_world.AreNpcsKin(self.Num, other.Num)) continue;  // same-kind or same-group indifference
                    var (oWX, oWY) = grid.ToWorld(col, row, other.X, other.Y);
                    // Both sides can be oversize here, so range and nearness are measured edge to edge.
                    int otherSize = _world.Npcs[other.Num].EffectiveSize;
                    if (!WorldCoordHelper.AreFootprintsWithin(aWX, aWY, selfNpc.EffectiveSize, oWX, oWY, otherSize, range)) continue;
                    int d = WorldCoordHelper.FootprintManhattan(aWX, aWY, selfNpc.EffectiveSize, oWX, oWY, otherSize);
                    if (d >= bestDist) continue;  // can't beat the current nearest; skip before LoS/BFS
                    if (!WorldCoordHelper.HasClearSpellLineOfSight(aWX, aWY, oWX, oWY, los)) continue;
                    if (FindStepTowardObservableArea(mapNum, self.X, self.Y, self.Layer, m, other.X, other.Y, other.Layer, selfNpc,
                                                 targetSize: otherSize) is null)
                        continue;
                    bestDist = d;
                    best = other.GetSpawnIdentity(m, s);
                }
                var guests = _world.MapTraversalNpcs[m];
                for (int g = 0; g < guests.Count; g++)
                {
                    var gt = guests[g];
                    if (gt.Num <= 0) continue;
                    if (_world.AreNpcsKin(self.Num, gt.Num)) continue;  // same-kind or same-group indifference
                    var (oWX, oWY) = grid.ToWorld(col, row, gt.X, gt.Y);
                    if (Math.Abs(oWX - aWX) > range || Math.Abs(oWY - aWY) > range) continue;
                    int d = WorldCoordHelper.WorldManhattan(aWX, aWY, oWX, oWY);
                    if (d >= bestDist) continue;
                    if (!WorldCoordHelper.HasClearSpellLineOfSight(aWX, aWY, oWX, oWY, los)) continue;
                    if (FindStepTowardObservableArea(mapNum, self.X, self.Y, self.Layer, m, gt.X, gt.Y, gt.Layer, selfNpc,
                                                 targetSize: _world.Npcs[gt.Num].EffectiveSize) is null)
                        continue;
                    bestDist = d;
                    best = gt.GetSpawnIdentity(m, 0);
                }
            }
        }

        return best;
    }

    /// <summary>Notice a non-kin NPC via <see cref="FindNoticeableNpc"/> and lock onto it.</summary>
    private void TryNoticeNpc(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var (spawnMap, spawnSlot) = FindNoticeableNpc(mapNum, slot, mn);
        if (spawnSlot <= 0) return;
        mn.NpcTargetSpawnMap = spawnMap;
        mn.NpcTargetSpawnSlot = spawnSlot;
        mn.MarkReachedTarget(now);
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = true });
    }

    /// <summary>Per-tick brain step for a native NPC that has noticed another NPC. Closing the gap is
    /// NOT done here — that runs on the fast legs pass (<see cref="AdvanceNativeNpcChaseStep"/>). This
    /// only drops the lock cleanly (with broadcast) when the other body has died, despawned, or moved
    /// outside this one's 3x3 observable area.</summary>
    private void RunNoticedNpcStep(int mapNum, int slot, MapNpcRecord mn, long now)
    {
        var npc = _world.Npcs[mn.Num];
        if (mn.NpcTargetSpawnSlot <= 0) return;

        var resolved = _queries.ResolveNpc(mn.NpcTargetSpawnMap, mn.NpcTargetSpawnSlot);
        if (resolved is null)
        {
            DropNpcTarget(mapNum, slot, mn);
            return;
        }
        var (otherMap, _, _) = resolved.Value;

        // Must still be inside the 3x3 observable area — an NPC is never warp-followed.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        if (grid.PositionOf(otherMap) is null)
        {
            DropNpcTarget(mapNum, slot, mn);
            return;
        }

        if (ShouldGiveUpUnreachedTarget(mn, now))
        {
            DropNpcTarget(mapNum, slot, mn);
            ResetNativeNpc(mn, mapNum, slot, npc);
        }
    }

    /// <summary>Clear an NPC's NpcTarget and notify observers. <c>HasTarget</c> reflects any remaining
    /// player Target so the client outline matches actual state.</summary>
    private void DropNpcTarget(int mapNum, int slot, MapNpcRecord mn)
    {
        mn.NpcTargetSpawnMap = 0;
        mn.NpcTargetSpawnSlot = 0;
        SendToMap(_world, mapNum, new NpcTargetPacket { MapNum = mapNum, NpcSlot = slot, HasTarget = mn.Target != 0 });
    }

    /// <summary>Give-up gate for a <see cref="NpcBehavior.Pursue"/> NPC: true once it has held its
    /// lock for longer than <see cref="NpcUnreachedGiveUpMs"/> without once reaching what it is after.
    /// <see cref="MapNpcRecord.LastReachedTargetMs"/> is stamped on acquisition and on every chase step
    /// that closed world-distance, so an NPC that is genuinely closing keeps resetting this clock and
    /// only one that cannot act on its quarry at all times out.
    ///
    /// <para><b>This is what keeps an open world safe.</b> There is deliberately no cross-map entry
    /// restriction anywhere in the chase code — a pursuer follows a player across a border or through
    /// a warp, because seamless pursuit is the point. What stops a mob being parked somewhere it does
    /// not belong is this clock: it either reaches its quarry or it goes home.</para></summary>
    private bool ShouldGiveUpUnreachedTarget(MapNpcRecord mn, long now)
        => _world.Npcs[mn.Num].Behavior == NpcBehavior.Pursue
           && mn.LastReachedTargetMs > 0
           && now - mn.LastReachedTargetMs > NpcUnreachedGiveUpMs;

    /// <summary>Full reset for a native NPC that just let go: clears the damage ledger and broadcasts a
    /// spawn-packet refresh so observers re-read it. Position stays put — the native is already on its
    /// home map. Guests use <see cref="ReturnTraversalHome"/> instead, which also relocates them back to
    /// spawn.</summary>
    private void ResetNativeNpc(MapNpcRecord mn, int mapNum, int slot, NpcRecord npc)
    {
        mn.ClearDamageCredit();
        SendToMap(_world, mapNum, new NpcSpawnPacket
        {
            MapNum = mapNum,
            NpcSlot = slot,
            Num = mn.Num,
            X = mn.X,
            Y = mn.Y,
            Dir = mn.Dir,
            Layer = mn.Layer,
        });
    }
}

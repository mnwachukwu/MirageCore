using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>Choosing a target: Tab cycling, click and hover resolution down to an entity, and the
/// tile/footprint math that backs both.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Tab targeting ─────────────────────────────────────────────────────────

    // Mirrors a click on the local player's tile: lock the tab-target onto self and
    // tell the server, so beneficial self-cast spells work with no mouse.
    private void TargetSelf()
    {
        var state = _ctx.State;
        if (state.MyIndex <= 0) return;
        var me = state.Me;
        _tabTarget = new TargetRef(TargetKind.Player, state.MyIndex, 0);
        _ctx.Sender.SendSearch(me.X, me.Y, state.CenterMapNum, 2, state.MyIndex, 0);
    }

    private void CycleTabTarget(bool reverse = false)
    {
        var state = _ctx.State;
        bool safeMap = state.MoralOf(state.Map) == MapMoral.Safe;
        var me = state.Me;
        int myWX = _ctx.State.MapTilesX + me.X;  // local player sits at the center cell
        int myWY = _ctx.State.MapTilesY + me.Y;
        var list = new List<(TargetRef Ref, int DistSq)>();

        void TryAddNpc(TargetRef r, int worldX, int worldY, int size, NpcBehavior behavior, WorldLayer layer)
        {
            if (behavior is not (NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)) return;
            if (!WorldCoordHelper.IsInSpellRange(myWX, myWY, 1, worldX, worldY, size)) return;   // footprint-aware (Tab picks a big NPC by its body)
            // Skip targets the player couldn't actually cast on — the FULL layer-aware LoS gate (same-layer or a
            // ramp bridge, then walls/doors), matching the server's HasLineOfSight. So Tab won't land on a target
            // across a plane it can't reach (e.g. up on a bridge you're not on), same as the grayed arrow.
            if (!ClientLineOfSight.HasClearFromLocalPlayer(state, worldX, worldY, layer)) return;
            int dx = worldX - myWX, dy = worldY - myWY;
            list.Add((r, dx * dx + dy * dy));
        }

        // Native NPCs on every loaded cell (center + neighbors).
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int cellMap = state.NeighborMapNums[col, row];
                if (cellMap <= 0) continue;
                var npcs = state.NpcsForMap(cellMap);
                if (npcs is null) continue;
                int offX = col * _ctx.State.MapTilesX, offY = row * _ctx.State.MapTilesY;
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = npcs[i];
                    if (n.Num == 0 || n.Num > state.Limits.Npcs) continue;
                    var def = state.NpcDefs[n.Num];
                    if (def is null) continue;
                    TryAddNpc(new TargetRef(TargetKind.Npc, i, cellMap), offX + n.X, offY + n.Y, def.EffectiveSize, def.Behavior, n.Layer);
                }
            }
        }

        // Visiting (chasing) guests, placed at their current cell.
        foreach (var t in state.TraversalNpcs.Values)
        {
            if (t.Num == 0 || t.Num > state.Limits.Npcs) continue;
            var def = state.NpcDefs[t.Num];
            if (def is null) continue;
            var off = CellOffsetForMapClient(t.CurrentMapNum);
            if (off is null) continue;
            TryAddNpc(new TargetRef(TargetKind.Traversal, t.SpawnMapNum, t.SpawnSlot), off.Value.ox + t.X, off.Value.oy + t.Y, def.EffectiveSize, def.Behavior, t.Layer);
        }

        if (!_skipPlayersWithTabTarget && !safeMap)
        {
            for (int i = 1; i <= state.PlayerSlots; i++)
            {
                if (i == state.MyIndex) continue;
                var p = state.Players[i];
                if (string.IsNullOrEmpty(p.Name)) continue;
                var off = CellOffsetForMapClient(p.Map);
                if (off is null) continue;
                int wx = off.Value.ox + p.X, wy = off.Value.oy + p.Y;
                if (!WorldCoordHelper.IsInSpellRange(myWX, myWY, wx, wy)) continue;
                if (!ClientLineOfSight.HasClearFromLocalPlayer(state, wx, wy, p.Layer)) continue;
                int dx = wx - myWX, dy = wy - myWY;
                list.Add((new TargetRef(TargetKind.Player, i, 0), dx * dx + dy * dy));
            }
        }

        if (list.Count == 0)
        {
            _tabTarget = default;
            return;
        }
        list.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

        var keys = list.Select(t => t.Ref).ToList();
        int cur = keys.IndexOf(_tabTarget);
        if (keys.Count == 1 && cur == 0) return;
        _tabTarget = reverse
            ? keys[(cur - 1 + keys.Count) % keys.Count]
            : keys[(cur + 1) % keys.Count];

        // Replicate what a tile click does — tell the server our new target.
        if (ResolveTargetTile(_tabTarget, out int emap, out int ex, out int ey))
        {
            var pr = ToSearchProposal(_tabTarget, _ctx.State.MyIndex);
            _ctx.Sender.SendSearch(ex, ey, emap, pr.type, pr.id, pr.map);
        }
    }

    // Sprite-pixel hit-test: returns the entity whose rendered sprite rect contains the click
    // world pixel.  An entity at tile (tx,ty) with interp offset (XOff,YOff) occupies the
    // world-pixel rect [tx*PicX + XOff, +PicX) × [ty*PicY + YOff, +PicY) — so a moving entity
    // mid-step occupies pixels on both its source and destination tiles, and a click on either
    // tile (or anywhere along its slide path) lands it.  Priority order matches the server's
    // legacy click scan: self, then other players, then native NPCs, then traversal guests.
    // Pixel-in-footprint hit test (world pixels vs an entity's top-left tile + its sub-tile slide offset). Shared by
    // the single-target finder and the stacked-NPC finder so both agree on what a click covers.
    private static bool HitFootprint(float wx, float wy, int worldTileX, int worldTileY, float xOff, float yOff, int sizeTiles = 1)
    {
        float px = worldTileX * Constants.PicX + xOff;
        float py = worldTileY * Constants.PicY + yOff;
        float ext = sizeTiles * Constants.PicX;   // size-2/3 NPCs are clickable across their whole SxS body
        return wx >= px && wx < px + ext && wy >= py && wy < py + ext;
    }

    private TargetRef FindEntityAtPixel(float wx, float wy)
    {
        bool Hit(float hx, float hy, int worldTileX, int worldTileY, float xOff, float yOff, int sizeTiles = 1)
            => HitFootprint(hx, hy, worldTileX, worldTileY, xOff, yOff, sizeTiles);

        var state = _ctx.State;
        var me = state.Me;
        if (state.MyIndex > 0
            && Hit(wx, wy, _ctx.State.MapTilesX + me.X, _ctx.State.MapTilesY + me.Y, me.XOffset, me.YOffset))
        {
            return new TargetRef(TargetKind.Player, state.MyIndex, 0);
        }

        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            if (i == state.MyIndex) continue;
            var p = state.Players[i];
            if (string.IsNullOrEmpty(p.Name)) continue;
            var off = CellOffsetForMapClient(p.Map);
            if (off is null) continue;
            if (Hit(wx, wy, off.Value.ox + p.X, off.Value.oy + p.Y, p.XOffset, p.YOffset))
                return new TargetRef(TargetKind.Player, i, 0);
        }

        // Stacked NPCs (same tile, different planes): a click targets the one on the PLAYER'S layer, so you hit
        // what you're standing with; a cross-layer hit is only a fallback if nothing on your plane overlaps.
        TargetRef? npcFallback = null;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                int mapNum = state.NeighborMapNums[c, r];
                if (mapNum <= 0) continue;
                var npcs = state.NpcsForMap(mapNum);
                if (npcs is null) continue;
                int cellOx = c * _ctx.State.MapTilesX;
                int cellOy = r * _ctx.State.MapTilesY;
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = npcs[i];
                    if (n.Num == 0 || n.Num > state.Limits.Npcs) continue;
                    if (Hit(wx, wy, cellOx + n.X, cellOy + n.Y, n.XOffset, n.YOffset, state.NpcDefs[n.Num]?.EffectiveSize ?? 1))
                    {
                        var hit = new TargetRef(TargetKind.Npc, i, mapNum);
                        if (n.Layer == me.Layer) return hit;
                        npcFallback ??= hit;
                    }
                }
            }
        }

        foreach (var t in state.TraversalNpcs.Values)
        {
            if (t.Num <= 0 || t.Num > state.Limits.Npcs) continue;
            var off = CellOffsetForMapClient(t.CurrentMapNum);
            if (off is null) continue;
            if (Hit(wx, wy, off.Value.ox + t.X, off.Value.oy + t.Y, t.XOffset, t.YOffset, state.NpcDefs[t.Num]?.EffectiveSize ?? 1))
            {
                var hit = new TargetRef(TargetKind.Traversal, t.SpawnMapNum, t.SpawnSlot);
                if (t.Layer == me.Layer) return hit;
                npcFallback ??= hit;
            }
        }

        return npcFallback ?? default;
    }

    // Every NPC (native + traversal guest) whose footprint the cursor is over — BOTH planes. Right-click uses this so
    // stacked NPCs (one on the ground, one on the deck above) each get their own labeled menu; left-click stays on
    // the layer-preferred single finder above.
    private List<TargetRef> FindNpcsAtPixel(float wx, float wy)
    {
        var state = _ctx.State;
        var hits = new List<TargetRef>();
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                int mapNum = state.NeighborMapNums[c, r];
                if (mapNum <= 0) continue;
                var npcs = state.NpcsForMap(mapNum);
                if (npcs is null) continue;
                int cellOx = c * _ctx.State.MapTilesX, cellOy = r * _ctx.State.MapTilesY;
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = npcs[i];
                    if (n.Num == 0 || n.Num > state.Limits.Npcs) continue;
                    if (HitFootprint(wx, wy, cellOx + n.X, cellOy + n.Y, n.XOffset, n.YOffset, state.NpcDefs[n.Num]?.EffectiveSize ?? 1))
                        hits.Add(new TargetRef(TargetKind.Npc, i, mapNum));
                }
            }
        }

        foreach (var t in state.TraversalNpcs.Values)
        {
            if (t.Num <= 0 || t.Num > state.Limits.Npcs) continue;
            var off = CellOffsetForMapClient(t.CurrentMapNum);
            if (off is null) continue;
            if (HitFootprint(wx, wy, off.Value.ox + t.X, off.Value.oy + t.Y, t.XOffset, t.YOffset, state.NpcDefs[t.Num]?.EffectiveSize ?? 1))
                hits.Add(new TargetRef(TargetKind.Traversal, t.SpawnMapNum, t.SpawnSlot));
        }
        return hits;
    }

    // Converts a TargetRef into the proposal triple sent in SearchPacket.  Self is signaled
    // explicitly (Type=2) so the server never has to compare click coords to entity tiles.
    private static (byte type, int id, int map) ToSearchProposal(TargetRef t, int myIndex) => t.Kind switch
    {
        TargetKind.Player when t.A == myIndex => ((byte)2, t.A, 0),
        TargetKind.Player => ((byte)0, t.A, 0),
        TargetKind.Npc => ((byte)1, t.A, t.B),
        TargetKind.Traversal => ((byte)3, t.B, t.A),
        _ => ((byte)255, 0, 0),
    };

    // Resolves a target's current map + local tile, or false if it no longer exists.
    private bool ResolveTargetTile(TargetRef t, out int mapNum, out int x, out int y)
    {
        var state = _ctx.State;
        mapNum = 0;
        x = 0;
        y = 0;
        switch (t.Kind)
        {
            case TargetKind.Player:
                if (!SlotValidation.IsValidPlayerSlot(t.A)) return false;
                var p = state.Players[t.A];
                if (string.IsNullOrEmpty(p.Name)) return false;
                mapNum = p.Map;
                x = p.X;
                y = p.Y;
                return true;
            case TargetKind.Npc:
                var npcs = state.NpcsForMap(t.B);
                if (npcs is null || !SlotValidation.IsValidNpcSlot(t.A)) return false;
                var n = npcs[t.A];
                if (n.Num == 0) return false;
                mapNum = t.B;
                x = n.X;
                y = n.Y;
                return true;
            case TargetKind.Traversal:
                if (!state.TraversalNpcs.TryGetValue((t.A, t.B), out var g) || g.Num == 0) return false;
                mapNum = g.CurrentMapNum;
                x = g.X;
                y = g.Y;
                return true;
            default:
                return false;
        }
    }

    // Footprint size (tiles) of a spell target — players are 1; an NPC/guest yields its EffectiveSize.  Used to
    // center the projectile impact (and the deferred number/blood) on an oversize NPC's body, not its anchor.
    private int TargetFootprintSize(TargetRef t)
    {
        var state = _ctx.State;
        int num = t.Kind switch
        {
            TargetKind.Npc => state.NpcsForMap(t.B) is { } npcs && SlotValidation.IsValidNpcSlot(t.A) ? npcs[t.A].Num : 0,
            TargetKind.Traversal => state.TraversalNpcs.TryGetValue((t.A, t.B), out var g) ? g.Num : 0,
            _ => 0,   // player / none → size 1
        };
        return FootprintOfNpcNum(num);
    }

    /// <summary>How many tiles a side an NPC type covers. 0 (a player, or nothing resolved) is one tile.</summary>
    private int FootprintOfNpcNum(int num) => num > 0 ? _ctx.State.NpcDefs[num]?.EffectiveSize ?? 1 : 1;

    /// <summary>The footprint of the entity a popup belongs to, found from what a combat packet carries:
    /// a player index, an NPC slot, or — for a traversal guest, which has no slot — the tile it stands on.
    ///
    /// <para>🔴 An NPC's position is its TOP-LEFT anchor, and its body grows from there toward +x/+y. Text
    /// placed at the anchor of a 3x3 creature sits over its shoulder, a tile off centre, which is what
    /// makes a readout look like it belongs to something else.</para></summary>
    public int PopupFootprint(bool isNpc, int npcSlot, int mapNum, int lx, int ly)
    {
        if (!isNpc) return 1;
        var state = _ctx.State;
        if (npcSlot != 0)
            return FootprintOfNpcNum(state.NpcsForMap(mapNum) is { } npcs && SlotValidation.IsValidNpcSlot(npcSlot)
                ? npcs[npcSlot].Num : 0);

        // A guest is keyed by where it CAME from, which a combat packet does not carry — so find the body
        // standing on the tile the packet named instead.
        foreach (var g in state.TraversalNpcs.Values)
            if (g.CurrentMapNum == mapNum && g.X == lx && g.Y == ly) return FootprintOfNpcNum(g.Num);
        return 1;
    }

    // World-tile offset of the grid cell holding a given map number, or null if it isn't loaded.
    private (int ox, int oy)? CellOffsetForMapClient(int mapNum)
    {
        if (mapNum <= 0) return null;
        var nums = _ctx.State.NeighborMapNums;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (nums[c, r] == mapNum)
                    return (c * _ctx.State.MapTilesX, r * _ctx.State.MapTilesY);
            }
        }

        return null;
    }

    private TargetRef ComputeHoveredEntity()
    {
        var mp = _lastInput.MousePosition;
        if (mp.X < 0 || mp.X >= Camera.ViewW || mp.Y < 0 || mp.Y >= Camera.ViewH) return default;
        for (int zi = 0; zi < _zOrder.Count; zi++)
            if (PanelIsOpen(_zOrder[zi]) && PanelContainsMouse(_zOrder[zi], mp)) return default;
        // Hover spans the full 3x3 region: the client doesn't distinguish "your map" from "next
        // map over" for bars/tooltips. Reuse the click-targeting hit-test so a mouseover on a
        // neighbor-map NPC (or a chasing traversal guest) gets the same identity a click would.
        return FindEntityAtPixel(mp.X + _camera.CameraX, mp.Y + _camera.CameraY);
    }
}

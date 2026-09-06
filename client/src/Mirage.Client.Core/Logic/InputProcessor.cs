using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Translates an <see cref="InputSnapshot"/> into C→S packets.
/// Called each frame from the Shell when chat is unfocused and no overlay panel is open.
/// </summary>
public static class InputProcessor
{
    /// <summary>Translates one frame of input.
    ///
    /// <para><paramref name="onTick"/> gates the ACTION sends only — attack and pick-up — so a rapid tap
    /// cannot fire faster than the tick rate. Movement is deliberately outside it: a step already waits for
    /// the previous slide to finish, which is a finer and more accurate limit than a fixed tick, and the
    /// server's own move-credit budget is what actually bounds the pace.</para>
    ///
    /// <para>🔴 Gating movement on the tick quantises every step UP to the next tick boundary. At the base
    /// 200 ms tile that is invisible — it lands exactly on one — but any faster run finishes its slide
    /// mid-tick and then stands still waiting for the next one, so SPD buys a stutter instead of speed.</para></summary>
    public static void Process(
        InputSnapshot input,
        ClientState state,
        ClientPacketSender sender,
        long nowMs,
        bool onTick = true)
    {
        if (!state.InGame || state.GettingMap) return;

        ProcessMovement(input, state, sender, nowMs);
        if (!onTick) return;
        ProcessAttack(input, state, sender, nowMs);
        ProcessPickUp(input, sender);
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    /// <summary>The beat an arrival owes before the next step may be taken.
    ///
    /// <para>🔴 One figure, not a step at the current pace. What the beat has to outlast is a TAP — about a
    /// tenth of a second — and that is the same length whether the player is walking or running. Deriving
    /// it from pace makes arriving at a walk feel different from arriving at a run for no reason the player
    /// can see, and a walk step (400 ms) is long enough that the pause reads as the game hanging.</para>
    ///
    /// <para>Tuned by feel rather than computed: it is the shortest pause that a tap cannot get through.</para></summary>
    private const float ArrivalBeatMs = 200f;

    private static void ProcessMovement(InputSnapshot input, ClientState state, ClientPacketSender sender, long nowMs)
    {
        var me = state.Me;

        // Only start a new move when the previous animation has fully settled.
        if (me.XOffset != 0 || me.YOffset != 0) return;

        // An arrival is a step with no slide to wait out, so it owes the beat one directly — see
        // ClientState.ArrivedAtMs.
        if (state.ArrivedAtMs != 0)
        {
            if (nowMs - state.ArrivedAtMs < ArrivalBeatMs) return;
            state.ArrivedAtMs = 0;
        }

        // The dominant direction is already resolved by press-order in the Shell (see
        // MovementInputStack) — most-recently-pressed still-held key wins.
        Direction? dir = input.Move;

        if (dir is null)
        {
            // Optimistic local update so a held face input doesn't re-fire the packet every tick
            // while waiting on the server's SendPlayerDir echo (which never reaches the sender anyway).
            if (input.DirFace is Direction face && face != me.Dir)
            {
                me.Dir = face;
                sender.SendPlayerDir(face);
            }
            return;
        }

        int nx = me.X, ny = me.Y;
        switch (dir.Value)
        {
            case Direction.Up:
                ny--;
                break;
            case Direction.Down:
                ny++;
                break;
            case Direction.Left:
                nx--;
                break;
            case Direction.Right:
                nx++;
                break;
        }

        bool inBounds = state.Map.Contains(nx, ny);

        // Two-layer world: the logical layer this step lands on (sticky / ramp-gated), resolved by LayerLogic
        // over the 3x3 tile view exactly as the server's CanPlayerWalkOnTile does — so a predicted step onto a
        // ramp/bridge picks the SAME layer the server will (no rubber-band), and occupancy is filtered to the
        // resulting layer so a fringe walker isn't blocked by a ground actor beneath (or vice-versa).  Defaults
        // to the current layer; CanEnter flips it only across a ramp.
        WorldLayer newLayer = me.Layer;

        if (inBounds)
        {
            // CanEnter false => walking off a deck edge (fringe footprint doesn't fit): treat as blocked.
            bool blocked = !LayerLogic.CanEnter(new ClientTileView(state), state.MapTilesX + nx,
                                                 state.MapTilesY + ny, 1, me.Layer, dir.Value, out newLayer);
            // A deck edge resolves no legal transition, so god mode keeps the plane it is already on.
            if (blocked && me.GodMode) newLayer = me.Layer;
            if (!blocked)
            {
                // Tile attribute at the RESULTING layer (a fringe railing blocks a fringe walker, not one below).
                // Door state is still per-map 2D (fringe/ground share it) — a documented deferral that matches
                // the server's CanPlayerWalkOnTile, so it never rubber-bands.
                var attrType = LayerLogic.AttrFor(state.Map.Tile[nx, ny], newLayer).Type;
                blocked = attrType == TileType.Blocked ||
                          (attrType == TileType.Key && !state.TempTile[nx, ny, (int)newLayer]);
            }

            // Another player standing on the target tile AND SAME LAYER also blocks movement — except in safe
            // zones, where players pass through each other.  PK-flagged movers are the lone exception to the
            // safe-zone exemption.  A grace-period PKer counts as effectively non-PK and keeps the pass-through.
            long nowUtcLocal = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool effectivelyPk = me.IsPk(nowUtcLocal) && me.PkGraceUntilUtc <= nowUtcLocal;
            if (!blocked && (effectivelyPk || state.MoralOf(state.Map) != MapMoral.Safe))
            {
                for (int i = 1; i <= state.PlayerSlots; i++)
                {
                    var p = state.Players[i];
                    if (p == me || string.IsNullOrEmpty(p.Name) || p.Dead) continue;   // a corpse is walked over
                    // state.Players holds every visible player including those on neighbor maps —
                    // filter by map so a same-coords sprite on a different map can't false-block us.
                    if (p.Map == me.Map && p.X == nx && p.Y == ny && p.Layer == newLayer)
                    {
                        blocked = true;
                        break;
                    }
                }
            }

            // A live NPC on the target tile AND SAME LAYER blocks movement — footprint-aware (a large NPC blocks
            // its whole SxS body; natives, chasing guests, and a body spilling in across a seam all count),
            // mirroring the server so a predicted step onto a big NPC's body is never rubber-banded.
            if (!blocked && state.IsTileNpcBlocked(state.CenterMapNum, nx, ny, newLayer))
                blocked = true;

            // God mode walks through walls, closed doors, NPCs and other players, matching the server's
            // CanPlayerWalkOnTile. The prediction has to agree: a step it refuses sends no move packet at
            // all, so the server-side exemption is never reached.
            if (me.GodMode) blocked = false;

            if (blocked)
            {
                // Face the blocked direction and broadcast it; no movement occurs.
                if (me.Dir != dir.Value)
                {
                    me.Dir = dir.Value;
                    sender.SendPlayerDir(dir.Value);
                }
                return;
            }
        }
        else
        {
            // Map edge: a move proceeds only if an adjacent map exists in this direction AND
            // the tile we'd cross into is walkable — so crossing into a neighbor's wall is
            // blocked locally (instant, like any in-map wall) instead of round-tripping.
            bool hasAdjacentMap = dir.Value switch
            {
                Direction.Up => state.Map.Up > 0,
                Direction.Down => state.Map.Down > 0,
                Direction.Left => state.Map.Left > 0,
                Direction.Right => state.Map.Right > 0,
                _ => false
            };

            // 🔴 And only if that neighbor is actually LOADED. The prediction below shifts the grid on
            // the spot, so the cell it crosses into becomes the center map — an empty one makes
            // ClientState.Map null, which every draw path treats as impossible. The declaration and the
            // delivery are separate: a warp empties the grid and the server refills it a packet at a
            // time, so stepping straight off a fresh map's edge can arrive before the cell does.
            // Refusing here costs the frames until it lands; crossing anyway costs the process.
            bool neighborLoaded = state.NeighborToward(dir.Value) is not null;

            if (!hasAdjacentMap || !neighborLoaded || EdgeDestBlocked(state, dir.Value, me.X, me.Y, out newLayer))
            {
                if (me.Dir != dir.Value)
                {
                    me.Dir = dir.Value;
                    sender.SendPlayerDir(dir.Value);
                }
                return;
            }

            // Adjacent map exists and the destination is walkable — PREDICT the seamless cross now
            // (shift the grid, place us on the neighbor's edge, animate the step) so there's no
            // round-trip pause.  Record the from-map for reconciliation: the server's SeamlessCross
            // confirms it, while a self move-correction (rejection) reverts us via a reload.
            int fromMap = state.CenterMapNum;
            int fromRev = state.Map.Revision;
            var crossMovement = (input.Running && (me.Sp > 0 || me.GodMode)) ? MovementType.Running : MovementType.Walking;
            me.Dir = dir.Value;
            sender.SendPlayerMove(dir.Value, crossMovement);

            state.ShiftGrid(dir.Value);
            (me.X, me.Y) = dir.Value switch
            {
                Direction.Up => (me.X, state.Map.Height - 1),
                Direction.Down => (me.X, 0),
                Direction.Left => (state.Map.Width - 1, me.Y),
                Direction.Right => (0, me.Y),
                _ => (me.X, me.Y),
            };
            me.Map = state.CenterMapNum;
            me.PrevLayer = me.Layer;   // pre-cross layer for the cross-layer slide-occlusion fix
            me.Layer = newLayer;   // two-layer world: carry the layer across the seam (a bridge continues)
            // Same step-animation offset a normal move uses (slide in from the tile we left).
            me.XOffset = dir.Value switch { Direction.Left => Constants.PicX, Direction.Right => -Constants.PicX, _ => 0 };
            me.YOffset = dir.Value switch { Direction.Up => Constants.PicY, Direction.Down => -Constants.PicY, _ => 0 };
            me.Moving = crossMovement;
            state.BeginPendingCross(fromMap, fromRev, state.CenterMapNum);
            return;
        }

        var movement = (input.Running && (me.Sp > 0 || me.GodMode)) ? MovementType.Running : MovementType.Walking;
        sender.SendPlayerMove(dir.Value, movement);
        me.PredictMove(dir.Value, nx, ny, movement, newLayer);
    }

    // True when the tile we'd cross into on the neighbor map is a wall, a locked door, or
    // NPC/player-occupied — mirroring the center map's collision check.  Returns false (allow the
    // move) when that neighbor isn't loaded yet; the server is authoritative and will correct.
    private static bool EdgeDestBlocked(ClientState state, Direction dir, int meX, int meY, out WorldLayer newLayer)
    {
        var me = state.Me;
        newLayer = me.Layer;   // default: carry the layer (used on the "neighbor not loaded → allow" path)
        var (col, row, dx, dy) = dir switch
        {
            Direction.Up => (1, 0, meX, state.NeighborMaps[1, 0]?.Height - 1 ?? 0),
            Direction.Down => (1, 2, meX, 0),
            Direction.Left => (0, 1, state.NeighborMaps[0, 1]?.Width - 1 ?? 0, meY),
            Direction.Right => (2, 1, 0, meY),
            _ => (1, 1, 0, 0)
        };
        var map = state.NeighborMaps[col, row];
        if (map is null) return false;

        // Resolve the resulting layer over the 3x3 view (same gate as the in-map step) and reject a deck-edge
        // walk-off; then read the neighbor tile's attribute AT that layer.
        int destWX = col * state.MapTilesX + dx;
        int destWY = row * state.MapTilesY + dy;
        if (!LayerLogic.CanEnter(new ClientTileView(state), destWX, destWY, 1, me.Layer, dir, out newLayer))
        {
            if (!me.GodMode) return true;
            newLayer = me.Layer;
        }
        // Same pass-through as the in-map step: nothing on the far tile holds an observer.
        if (me.GodMode) return false;
        var attrType = LayerLogic.AttrFor(map.Tile[dx, dy], newLayer).Type;
        if (attrType == TileType.Blocked) return true;
        if (attrType == TileType.Key && !state.NeighborTempTiles[col, row][dx, dy, (int)newLayer]) return true; // locked door on the resolved layer
        // Footprint- and seam-aware NPC block on the neighbor tile at the resulting layer, mirroring the
        // center-map check (natives + chasing guests + a large body spilling across the seam).
        if (state.IsTileNpcBlocked(state.NeighborMapNums[col, row], dx, dy, newLayer)) return true;

        // Player block across the seam — same-layer, same pass-through rule.  Pass-through applies when either
        // side of the crossing is a safe zone (the mover's source map OR the neighbor destination map), unless
        // the mover is PK-flagged.  A grace-period PKer counts as effectively non-PK and keeps the pass-through.
        long nowUtcLocal = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool effectivelyPk = me.IsPk(nowUtcLocal) && me.PkGraceUntilUtc <= nowUtcLocal;
        bool playersPassThrough = !effectivelyPk && (state.MoralOf(state.Map) == MapMoral.Safe || state.MoralOf(map) == MapMoral.Safe);
        if (!playersPassThrough)
        {
            int destMapNum = state.NeighborMapNums[col, row];
            for (int i = 1; i <= state.PlayerSlots; i++)
            {
                var p = state.Players[i];
                if (p == me || string.IsNullOrEmpty(p.Name) || p.Dead) continue;   // a corpse is walked over
                if (p.Map == destMapNum && p.X == dx && p.Y == dy && p.Layer == newLayer) return true;
            }
        }
        return false;
    }

    // ── Attack ────────────────────────────────────────────────────────────────

    // Hold-to-attack: while the attack key is held,
    // an attack fires each time the cooldown window elapses.
    private static void ProcessAttack(InputSnapshot input, ClientState state, ClientPacketSender sender, long _)
    {
        if (!input.Attack) return;

        // Melee aimed at an NPC never swings AT it:
        //  • An interactable NPC (keeper / quest / conversation) opens its menu instead — talk-first, fired ONCE
        //    per press (edge); the held key is swallowed so it neither respams the interact nor whiffs an anim.
        //    Auto lets the server route it (conversation, else quest menu if actionable, else shop/inn); it
        //    re-validates range + slot, so a slightly-off client guess is harmless. No AttackPacket → no swing.
        //  • A non-combat NPC (Friendly / Stationary) can't be damaged — attacking it only triggers its AttackSay
        //    rebuff. Still SEND the attack so the server issues that say, but SUPPRESS the swing (the server also
        //    skips the whiff broadcast for a friendly rebuff, so no swing plays anywhere — see CombatSystem).
        // Only a genuine combat target (or empty air) plays the swing.
        bool suppressSwing = false;
        if (TryFindFacingNpc(state, out int map, out int slot, out int num, out bool layerConnects))
        {
            if (state.NpcKeeperShop[num] != 0 || state.NpcQuestGlyph[num] != 0 || state.NpcConvGlyph[num] != 0)
            {
                // Interaction only reaches a plane the player's own connects to (the server's gate agrees), so a
                // keeper on the bridge is reachable from the ramp foot but not from the ground below it. A refusal
                // is flagged for the Shell to voice, once per press, and still no swing — a keeper is no target.
                if (input.AttackPressed)
                {
                    if (layerConnects) sender.SendNpcInteract(map, slot);
                    else state.NpcInteractWrongLayer = true;
                }
                return;   // no SendAttack, no Attacking — no swing animation
            }
            // Only an NPC the server would actually let us reach suppresses the swing. Across disconnected planes
            // its melee gate rejects the NPC before it can rebuff, so there's no AttackSay to wait for and the
            // swing should play into thin air (a whiff) rather than be swallowed — which read as a dropped key.
            if (layerConnects && state.NpcDefs[num]?.Behavior is NpcBehavior.Friendly or NpcBehavior.Stationary)
                suppressSwing = true;
        }

        long tickNow = Environment.TickCount64;
        // Heavy Wind doubles the attack cooldown server-side; mirror it locally to stay in lockstep.
        long windMult = state.Weather == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (tickNow - state.Me.AttackTimer < Constants.PlayerAttackCooldownMs * windMult) return;

        sender.SendAttack();
        state.Me.AttackTimer = tickNow;
        if (!suppressSwing) state.Me.Attacking = true;   // a friendly rebuff sends the attack but plays no swing
    }

    // The native-slot NPC whose footprint covers the tile directly in FRONT of the local player, or false if
    // none. Cross-map aware: the front tile is resolved in world space so a seam-adjacent NPC on a neighbor map
    // is found. Keeper NPCs (Friendly/Stationary) are stationary and never traverse, so guests aren't scanned.
    //
    // LAYER-AWARE: with the two-layer world a bridge NPC (fringe) and a wanderer beneath it (ground) can share the
    // front tile, so an NPC on the PLAYER'S layer wins outright — otherwise the melee could resolve the wrong one
    // (a ground wanderer under the keeper you're facing on the deck) and whiff into a swing. A cross-layer hit is
    // still kept as a fallback so a lone NPC on the other plane resolves; <paramref name="layerConnects"/> says
    // whether the planes actually meet there, because interaction refuses a disconnected NPC where a swing whiffs.
    // The connect test mirrors the server's melee gate exactly — player tile to FACED tile (not the NPC's anchor,
    // which for an oversize body sits elsewhere), so the two agree on a large NPC straddling a ramp.
    private static bool TryFindFacingNpc(ClientState state, out int mapNum, out int slot, out int num, out bool layerConnects)
    {
        mapNum = slot = num = 0;
        layerConnects = false;
        var me = state.Me;
        var (dx, dy) = WorldCoordHelper.DirDelta(me.Dir);
        int frontWX = state.MapTilesX + me.X + dx;
        int frontWY = state.MapTilesY + me.Y + dy;

        bool found = false;                       // a cross-layer fallback hit is recorded; a same-layer hit returns immediately
        var foundLayer = WorldLayer.Ground;       // the fallback's plane, tested for a ramp connect once the scan ends
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = (col == 1 && row == 1) ? state.CenterMapNum : state.NeighborMapNums[col, row];
                if (m <= 0) continue;
                var npcs = (col == 1 && row == 1) ? state.MapNpcs : state.NeighborNpcs[col, row];
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = npcs[i];
                    if (n.Num <= 0 || n.Num > state.Limits.Npcs) continue;
                    int size = state.NpcDefs[n.Num]?.EffectiveSize ?? 1;
                    var (awx, awy) = state.ToWorld(col, row, n.X, n.Y);
                    if (!WorldCoordHelper.FootprintContains(awx, awy, size, frontWX, frontWY)) continue;
                    if (n.Layer == me.Layer)
                    {
                        mapNum = m;
                        slot = i;
                        num = n.Num;
                        layerConnects = true;
                        return true;
                    }
                    if (!found)
                    {
                        mapNum = m;
                        slot = i;
                        num = n.Num;
                        foundLayer = n.Layer;
                        found = true;
                    }  // remember, keep scanning for a same-layer hit
                }
            }
        }
        // A ramp bridges the planes down its mount axis, so a keeper on the deck IS reachable from the ramp's foot.
        if (found) layerConnects = ClientLineOfSight.LayerConnectsFromLocalPlayer(state, frontWX, frontWY, foundLayer);
        return found;
    }

    // ── Pick up item ──────────────────────────────────────────────────────────

    private static void ProcessPickUp(InputSnapshot input, ClientPacketSender sender)
    {
        if (input.PickUp) sender.SendMapGetItem();
    }
}

// ── Input snapshot (Shell fills this from MonoGame each frame) ────────────────

/// <summary>
/// Immutable snapshot of input state for one frame.
/// The Shell creates this; Core consumes it — no MonoGame dependency in Core.
/// </summary>
public sealed class InputSnapshot
{
    /// <summary>The movement direction for this tick, already resolved by press-order
    /// (last-pressed still-held key wins), or null when no movement key is held.</summary>
    public Direction? Move { get; init; }
    public bool Running { get; init; }
    public bool Attack { get; init; }
    // Fresh press this frame (edge) — so the melee-key interact fires once per press, not every held frame.
    public bool AttackPressed { get; init; }
    public bool PickUp { get; init; }

    /// <summary>If the player is facing a direction without moving (for dir-change packets).</summary>
    public Direction? DirFace { get; init; }
}

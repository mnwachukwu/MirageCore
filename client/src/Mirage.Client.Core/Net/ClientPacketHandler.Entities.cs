using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>Everything that moves: map items, player and NPC spawn/move/face/despawn, and the
/// traversal guests that cross map borders.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Map entities ──────────────────────────────────────────────────────────

    private void HandleSendPlayerData(SendPlayerDataPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.Name = p.Name;
        player.Sprite = p.Sprite;
        player.SpriteSheet = p.SpriteSheet;
        player.X = p.X;
        player.Y = p.Y;
        player.Dir = p.Dir;
        player.Layer = p.Layer;
        player.Map = p.Map;
        player.Level = p.Level;
        player.Class = p.Class;
        player.Sex = p.Sex;
        player.Access = p.Access;
        player.PkExpiryUtc = p.PkExpiryUtc;
        player.PkGraceUntilUtc = p.GraceUntilUtc;
        player.AggressorUntilUtc = p.AggressorUntilUtc;
        // Nullable on the wire for the same reason the guild fields below are: absent means unchanged.
        if (p.GodMode.HasValue) player.GodMode = p.GodMode.Value;
        // Guild fields are nullable on the wire: only guild-aware broadcasts carry them, so a null
        // means "unchanged" — keep the cached value rather than wiping it on an ordinary broadcast.
        if (p.GuildId.HasValue) player.GuildId = p.GuildId.Value;
        if (p.GuildRank.HasValue) player.GuildRank = p.GuildRank.Value;
        if (p.GuildName is not null) player.GuildName = p.GuildName;
        if (p.GuildOpen.HasValue) player.GuildOpen = p.GuildOpen.Value;
        if (p.GuildColor.HasValue) player.GuildColor = p.GuildColor.Value;
        if (p.GuildShowRank.HasValue) player.GuildShowRank = p.GuildShowRank.Value;
        if (p.GuildStanding.HasValue) player.GuildStanding = p.GuildStanding.Value;
        // Death state: non-nullable, so every broadcast carries the current value. Drives the
        // corpse render (other players) and the death panel (yourself).
        player.Dead = p.Dead;
        player.RespawnReadyUtc = p.RespawnReadyUtc;
        // The overhead quest-glyph class filter keys off the LOCAL player's class — relight the glyphs when it
        // (re)loads, in case player data arrives after the quest push.
        if (p.Index == _state.MyIndex) _state.RefreshQuestGlyphs();
    }

    // Slim per-hit refresh of the aggressor expiry. The full SendPlayerData carries the off→on
    // edge transition; this packet keeps the timer aligned during a sustained fight without
    // re-broadcasting the heavier player record.
    private void HandleAggressorRefresh(AggressorRefreshPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        _state.Players[p.Index].AggressorUntilUtc = p.AggressorUntilUtc;
    }

    private void HandleLeftGame(LeftGamePacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        if (p.Index == _state.MyIndex)
        {
            // Shell handles our own disconnect via AlertMessage or transport close.
            return;
        }
        _state.Players[p.Index] = new PlayerRecord();
    }

    private void HandleLeaveMap(LeaveMapPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        if (p.Index == _state.MyIndex) return; // our map change handled by CheckForMap
        _state.Players[p.Index] = new PlayerRecord();
    }

    private void HandlePlayerXY(PlayerXYPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        if (p.Index == _state.MyIndex && _state.PendingCrossToMap != 0)
        {
            RevertPendingCross();
            return;
        }
        var player = _state.Players[p.Index];
        player.X = p.X;
        player.Y = p.Y;
    }

    private void HandleSendPlayerMove(SendPlayerMovePacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        // A position correction for us while a predicted cross is pending means the server rejected
        // the cross — revert by reloading the map we came from.
        if (p.Index == _state.MyIndex && _state.PendingCrossToMap != 0)
        {
            RevertPendingCross();
            return;
        }
        var player = _state.Players[p.Index];

        if (p.MapNum > 0 && p.MapNum != player.Map)
        {
            // Another player crossed a seam: slide the sprite one tile over the border (world delta when
            // both maps are loaded, else the cross Dir) instead of popping it, then re-home it to the new
            // map so it renders on the right grid cell.
            if (TryWorldStepOffset(player.Map, player.X, player.Y, p.MapNum, p.X, p.Y, out float wx, out float wy))
            {
                player.XOffset = wx;
                player.YOffset = wy;
            }
            else
            {
                (player.XOffset, player.YOffset) = p.Dir switch
                {
                    Direction.Up => (0f, (float)Constants.PicY),
                    Direction.Down => (0f, -(float)Constants.PicY),
                    Direction.Left => ((float)Constants.PicX, 0f),
                    Direction.Right => (-(float)Constants.PicX, 0f),
                    _ => (0f, 0f),
                };
            }
            player.Map = p.MapNum;
        }
        else
        {
            // Same-map step: start offset = one tile opposite the move so the sprite slides into the new tile.
            int dx = p.X - player.X;
            int dy = p.Y - player.Y;
            player.XOffset = -dx * Constants.PicX;
            player.YOffset = -dy * Constants.PicY;
        }

        player.X = p.X;
        player.Y = p.Y;
        player.Dir = p.Dir;
        player.PrevLayer = player.Layer;   // pre-step layer for the cross-layer slide-occlusion fix
        player.Layer = p.Layer;
        player.Moving = p.Movement;
    }

    private void HandleSendPlayerDir(SendPlayerDirPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        _state.Players[p.Index].Dir = p.Dir;
    }

    // ── NPC ───────────────────────────────────────────────────────────────────

    // True when the packet targets the current center map (so center-only UI events fire).
    private bool IsCenter(int mapNum) => mapNum == _state.CenterMapNum;

    private void HandleNpcSpawn(NpcSpawnPacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        var n = npcs[p.NpcSlot];
        n.Num = p.Num;
        n.Hp = n.MaxHp = p.MaxHp;
        n.Mp = n.MaxMp = p.MaxMp;
        n.Sp = n.MaxSp = p.MaxSp;
        n.X = p.X;
        n.Y = p.Y;
        n.Dir = p.Dir;
        n.Layer = p.Layer;
        n.XOffset = 0;
        n.YOffset = 0;
        n.Moving = 0;
        n.LastCombatMs = 0;
        n.HasTarget = false;
        if (IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
    }

    private void HandleNpcMove(NpcMovePacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        var n = npcs[p.NpcSlot];
        int dx = p.X - n.X;
        int dy = p.Y - n.Y;
        n.XOffset = -dx * Constants.PicX;
        n.YOffset = -dy * Constants.PicY;
        n.X = p.X;
        n.Y = p.Y;
        n.Dir = p.Dir;
        n.PrevLayer = n.Layer;   // pre-step layer for the cross-layer slide-occlusion fix
        n.Layer = p.Layer;
        n.Moving = p.Movement;
        if (IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
    }

    private void HandleNpcDir(NpcDirPacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        npcs[p.NpcSlot].Dir = p.Dir;
        if (IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
    }

    /// <summary>Resolve an NPC addressed by its universal identity (mapNum, slot) to the live client record —
    /// a native at its home slot on an observed map, OR a guest in <see cref="ClientState.TraversalNpcs"/>
    /// (keyed by spawn identity, its home slot vacated). Returns null if neither resolves. <paramref name="currentMap"/>
    /// is the map the NPC is physically on (a guest's visited map — where its FX must render); <paramref name="isNative"/>
    /// is true only for a resolved home-slot native (only then does the slot-indexed center-map notify apply).
    /// EVERY NPC event handler routes through this so a guest is never treated differently from a native — the
    /// structural close on the recurring "guests act differently" class of bug.</summary>
    private ClientMapNpc? ResolveNpc(int mapNum, int slot, out int currentMap, out bool isNative)
    {
        currentMap = mapNum;
        isNative = false;
        var home = _state.NpcsForMap(mapNum);
        if (home is not null && SlotValidation.IsValidNpcSlot(slot) && home[slot].Num > 0)
        {
            isNative = true;
            return home[slot];
        }
        if (_state.TraversalNpcs.TryGetValue((mapNum, slot), out var g) && g.Num > 0)
        {
            currentMap = g.CurrentMapNum;
            return g;
        }
        return null;
    }

    private void HandleNpcDead(NpcDeadPacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        var n = npcs[p.NpcSlot];
        // Death-blow number floats on any observed map; invoked before the slot is cleared so the
        // handler can still read the NPC's position (the VitalDelta handler runs synchronously).
        if (n.Num > 0 && p.Damage > 0)
            VitalDelta?.Invoke(p.NpcSlot, -p.Damage, VitalType.Hp, true, p.IsCrit, p.MapNum);
        // Hand the shell the pre-clear render state so it can hold a delayed-death sprite until a killing bolt lands.
        if (n.Num > 0 && _state.NpcDefs[n.Num] is { } deadDef)
        {
            EntityDied?.Invoke(new EntityDeathFx(new TargetRef(TargetKind.Npc, p.NpcSlot, p.MapNum),
                deadDef.Sprite, p.MapNum, n.X, n.Y, n.XOffset, n.YOffset, n.Dir, deadDef.EffectiveSize));
        }

        // Preserve any active chat bubble across the death so "last words" still drift away rather
        // than vanishing in place — common when a one-shot kill on a guard arrives in the same batch
        // as that guard's AttackSay (or its propagation to a neighboring guard). Head bubble is
        // eagerly demoted to a drifter on death; the slot stays in the array with Num=0 so the
        // sprite vanishes, but the tick keeps cleaning drifters and the renderer emits them at the
        // last-known tile position until the float window elapses.
        string? bubbleText = n.ChatBubbleText;
        int bubbleColor = n.ChatBubbleColor;
        var bubbleDrifters = n.ChatBubbleDrifters;
        bool hasBubble = bubbleText != null || (bubbleDrifters is { Count: > 0 });
        int lastX = n.X, lastY = n.Y;

        npcs[p.NpcSlot] = new ClientMapNpc();
        if (hasBubble)
        {
            var newN = npcs[p.NpcSlot];
            if (bubbleText != null)
            {
                bubbleDrifters ??= new List<NpcChatBubbleDrifter>(4);
                bubbleDrifters.Add(new NpcChatBubbleDrifter(bubbleText, bubbleColor, Environment.TickCount64));
            }
            newN.X = lastX;
            newN.Y = lastY;
            newN.ChatBubbleDrifters = bubbleDrifters;
        }
        if (IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
    }

    private void HandleNpcTarget(NpcTargetPacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        npcs[p.NpcSlot].HasTarget = p.HasTarget;
    }

    // Full state of a traversal (chasing) NPC, addressed by its permanent (SpawnMapNum, SpawnSlot)
    // identity.  Creates it on first sight, moves/updates it otherwise, and removes the now-vacated
    // home-slot native NPC so the same sprite isn't drawn twice on the home map.
    private void HandleTraversalNpc(TraversalNpcPacket p)
    {
        // Floating combat number (works for both a non-lethal hit and the kill blow).
        if (p.Damage != 0)
            NpcWorldDamage?.Invoke(p.CurrentMapNum, p.X, p.Y, -p.Damage, p.IsCrit, p.SpawnMapNum, p.SpawnSlot);

        // Kill blow: drop the guest after its number floats; the native NPC respawns at home later.
        if (p.Dead)
        {
            // Hand the shell the guest's render state first so it can hold a delayed-death sprite until a bolt lands.
            if (_state.TraversalNpcs.TryGetValue((p.SpawnMapNum, p.SpawnSlot), out var dyingTn)
                && _state.NpcDefs[dyingTn.Num] is { } tnDef)
            {
                EntityDied?.Invoke(new EntityDeathFx(new TargetRef(TargetKind.Traversal, p.SpawnMapNum, p.SpawnSlot),
                    tnDef.Sprite, p.CurrentMapNum, p.X, p.Y, 0f, 0f, dyingTn.Dir, tnDef.EffectiveSize));
            }

            _state.TraversalNpcs.Remove((p.SpawnMapNum, p.SpawnSlot));
            return;
        }

        var key = (p.SpawnMapNum, p.SpawnSlot);
        bool isNew = !_state.TraversalNpcs.TryGetValue(key, out var t);
        // For a native→guest FIRST cross, remember where the native last stood on its (loaded) home map.
        // The slide can then be derived from the real world-tile delta below — robust like the !isNew
        // path — instead of trusting the packet's Stepped flag.  0 = no loaded native to anchor to.
        int fromMap = 0, fromX = 0, fromY = 0;
        if (isNew)
        {
            t = new ClientTraversalNpc { SpawnMapNum = p.SpawnMapNum, SpawnSlot = p.SpawnSlot };
            // First sight = a native NPC converting to a guest at the border.  Carry over its render
            // state (animated HP/MP/SP bars, combat-bar timer, in-flight attack) so the guest CONTINUES
            // the same sprite seamlessly instead of resetting — otherwise the bar snaps and the attack
            // frame drops for a frame on the native→guest handoff.  (A guest→guest cross reuses one
            // object and never flickers; this makes the FIRST hop match it.)
            if (p.SpawnSlot >= 1 && p.SpawnSlot <= Constants.MaxMapNpcs
                && _state.NpcsForMap(p.SpawnMapNum) is { } home && home[p.SpawnSlot].Num > 0)
            {
                var native = home[p.SpawnSlot];
                t.DispHp = native.DispHp;
                t.DispMp = native.DispMp;
                t.DispSp = native.DispSp;
                t.LastCombatMs = native.LastCombatMs;
                t.Attacking = native.Attacking;
                t.AttackTimer = native.AttackTimer;
                // Carry over any active chat bubble across the native→guest seam handoff so an
                // AttackSay (or any other bubble in flight) stays attached to the same sprite
                // instead of vanishing the instant the NPC steps over the border.
                t.ChatBubbleText = native.ChatBubbleText;
                t.ChatBubbleEndMs = native.ChatBubbleEndMs;
                t.ChatBubbleColor = native.ChatBubbleColor;
                t.ChatBubbleDrifters = native.ChatBubbleDrifters;
                fromMap = p.SpawnMapNum;
                fromX = native.X;
                fromY = native.Y;
            }
            _state.TraversalNpcs[key] = t;
        }

        // Slide the sprite for a one-tile step instead of popping it:
        //  • same-map step        → local tile delta (only on an actual tile change — see below);
        //  • seam step, both maps loaded → WORLD-tile delta.  This is the robust path: it reads the two
        //    positions directly, so it animates correctly even when the carrier packet had no Stepped
        //    flag (e.g. a region re-sync snapshot that wins the race against the individual cross packet);
        //  • seam step, old map NOT loaded (entered view from off-screen) → fall back to the Stepped flag
        //    + Dir, the only info available then;
        //  • otherwise (warp/teleport, fresh spawn, re-appearance) → snap, and re-snap the bar.
        if (!isNew && t!.CurrentMapNum == p.CurrentMapNum)
        {
            // Only (re)start a slide on an ACTUAL tile change.  A traversal NPC re-broadcasts its full
            // state on every action (attack, facing, idle), and a same-tile update here would reset the
            // offset to 0 — cutting off an in-flight slide.  Leaving it alone lets MovementProcessor finish.
            if (p.X != t.X || p.Y != t.Y)
            {
                t.XOffset = -(p.X - t.X) * Constants.PicX;
                t.YOffset = -(p.Y - t.Y) * Constants.PicY;
            }
        }
        else if (!isNew && TryWorldStepOffset(t!.CurrentMapNum, t.X, t.Y, p.CurrentMapNum, p.X, p.Y, out float wx, out float wy))
        {
            t.XOffset = wx;
            t.YOffset = wy;
        }
        else if (isNew && fromMap != 0 && TryWorldStepOffset(fromMap, fromX, fromY, p.CurrentMapNum, p.X, p.Y, out float nwx, out float nwy))
        {
            // First sight via an in-view seam STEP: derive the slide from the native's last home tile.
            // This is the robust path for the native→guest handoff: it animates correctly even when the
            // packet that CREATED the guest was a Stepped=false region snapshot that won the race against
            // the Stepped=true cross packet (happens when the player crosses the same seam in the same
            // frame as the NPC — i.e. while baiting a hub NPC out onto an arm).  When there's no loaded
            // native to anchor to (fromMap==0 — the guest merely scrolled into view), we fall through to
            // snap below, which is the correct behavior there.
            t!.XOffset = nwx;
            t.YOffset = nwy;
        }
        else if (p.Stepped)
        {
            // Trailing offset = the tile we came from, one step opposite Dir (matches the same-map formula).
            (t!.XOffset, t.YOffset) = p.Dir switch
            {
                Direction.Up => (0f, (float)Constants.PicY),
                Direction.Down => (0f, -(float)Constants.PicY),
                Direction.Left => ((float)Constants.PicX, 0f),
                Direction.Right => (-(float)Constants.PicX, 0f),
                _ => (0f, 0f),
            };
        }
        else
        {
            t!.XOffset = 0;
            t.YOffset = 0;
            t.DispHp = -1f;
        }

        t.CurrentMapNum = p.CurrentMapNum;
        t.Num = p.Num;
        t.X = p.X;
        t.Y = p.Y;
        t.Dir = p.Dir;
        t.PrevLayer = t.Layer;   // pre-step layer for the cross-layer slide-occlusion fix
        t.Layer = p.Layer;
        t.Moving = p.Movement;
        t.Hp = p.Hp;
        t.MaxHp = p.MaxHp;
        t.HasTarget = p.HasTarget;
        // Server-authoritative combat stamp converted to our clock — see TraversalNpcPacket.MsSinceCombat.
        if (p.MsSinceCombat != int.MaxValue) t.LastCombatMs = Environment.TickCount64 - p.MsSinceCombat;
        if (p.Attacking)
        {
            t.Attacking = true;
            t.AttackTimer = Environment.TickCount64;
            t.LastCombatMs = Environment.TickCount64;
        }

        // The server vacated the home slot when this NPC left — clear the local native copy so it
        // doesn't linger as a ghost on the home map.
        if (p.SpawnSlot >= 1 && p.SpawnSlot <= Constants.MaxMapNpcs)
        {
            var homeNpcs = _state.NpcsForMap(p.SpawnMapNum);
            if (homeNpcs is not null && homeNpcs[p.SpawnSlot].Num != 0)
            {
                homeNpcs[p.SpawnSlot] = new ClientMapNpc();
                if (IsCenter(p.SpawnMapNum)) MapNpcChanged?.Invoke(p.SpawnSlot);
            }
        }
    }

    // Trailing pixel offset (the tile the sprite slides FROM) for a single contiguous step between two
    // world tiles, when BOTH maps are loaded grid cells.  Returns false when a map isn't loaded (so the
    // caller falls back to the Stepped flag) or the tiles aren't exactly one apart (a warp/teleport →
    // snap).  Reads the positions directly, so it animates a seam cross regardless of which packet
    // carried it — robust against a region re-sync arriving without the Stepped flag.
    private bool TryWorldStepOffset(int fromMap, int fromX, int fromY, int toMap, int toX, int toY,
                                    out float ox, out float oy)
    {
        ox = oy = 0f;
        if (_state.CellForMap(fromMap) is not { } a) return false;
        if (_state.CellForMap(toMap) is not { } b) return false;
        int dx = (b.col * _state.MapTilesX + toX) - (a.col * _state.MapTilesX + fromX);
        int dy = (b.row * _state.MapTilesY + toY) - (a.row * _state.MapTilesY + fromY);
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;
        ox = -dx * Constants.PicX;
        oy = -dy * Constants.PicY;
        return true;
    }

    // A traversal NPC silently left the world (returned home) — drop it; the native NPC respawns
    // on its home slot via the normal NpcSpawn path.
    private void HandleNpcDespawn(NpcDespawnPacket p)
        => _state.TraversalNpcs.Remove((p.SpawnMapNum, p.SpawnSlot));
}

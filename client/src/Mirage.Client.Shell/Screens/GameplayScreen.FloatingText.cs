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

/// <summary>Floating combat numbers: spawning and shifting them, and the deferred-hit queue behind
/// them. A bolt's damage, death and blood are held until the projectile lands, so the number appears
/// when the hit does rather than when it was rolled; bolts on one target are claimed FIFO so several
/// stagger across their own arrivals. The public spawn/shift/clear entry points are what
/// <c>IClientEvents</c> drives from the network layer.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    /// <summary>VitalDelta path (slotted NPC or player) — build the target ref and defer-or-float.</summary>
    public void SpawnOrDeferVitalFloat(bool isNpc, int idx, int npcMap, int mapNum, int lx, int ly, float xoff, float yoff, string? text, Color color, float bloodIntensity)
        => DeferOrFloat(isNpc ? new TargetRef(TargetKind.Npc, idx, npcMap) : new TargetRef(TargetKind.Player, idx, 0),
            mapNum, lx, ly, xoff, yoff, text, color, bloodIntensity);

    /// <summary>Traversal-NPC path (positioned by world tile) — build the traversal ref and defer-or-float.</summary>
    public void SpawnOrDeferTraversalFloat(int spawnMap, int spawnSlot, int mapNum, int x, int y, string? text, Color color, float bloodIntensity)
        => DeferOrFloat(new TargetRef(TargetKind.Traversal, spawnMap, spawnSlot), mapNum, x, y, 0f, 0f, text, color, bloodIntensity);

    /// <summary>Float a vital number now, OR — when it belongs to an in-flight spell projectile — defer it
    /// until the bolt would land, so the number appears in sync with the visible impact. The world position
    /// is captured now, since the entity may die or despawn before release.</summary>
    private void DeferOrFloat(TargetRef target, int mapNum, int lx, int ly, float xoff, float yoff, string? text, Color color, float bloodIntensity)
    {
        long release = ClaimRelease(target);
        bool onScreen = TryEntityScreen(mapNum, lx, ly, xoff, yoff, out float sx, out float sy);
        int tsize = TargetFootprintSize(target);   // center the number/blood on an oversize NPC's body, not its anchor
        if (release > 0 && onScreen)
        {
            HoldBarFor(target, release); // hold the HP bar too, so it drops in sync with the bolt
            float cx = sx + tsize * Constants.PicX / 2f;
            float cy = sy - FloatTextGapAbove;
            // Defer BOTH the number and the blood burst to the bolt's arrival, so they land with the impact.
            _deferredFloats.Add(new DeferredFloat
            {
                WorldX = cx + _camera.CameraX, WorldY = cy + _camera.CameraY, Text = text, Color = color,
                ReleaseMs = release, BloodIntensity = bloodIntensity, Layer = LayerAtTile(mapNum, lx, ly),
            });
            return;
        }
        // Immediate (melee, or an unresolved/instant bolt): number now, blood burst now.
        if (text is not null) SpawnFloatingTextAtEntity(mapNum, lx, ly, xoff, yoff, text, color, tsize);
        if (bloodIntensity > 0f && _showBlood && onScreen)
            _particles.EmitBloodSplatter(sx + tsize * Constants.PicX / 2f + _camera.CameraX, sy - FloatTextGapAbove + _camera.CameraY, bloodIntensity, LayerAtTile(mapNum, lx, ly));
    }

    // Hold the target's HP bar (display) until the bolt lands, matching the deferred number. NPC + traversal
    // route through their ClientMapNpc; players are handled with the player-death work (server death signal).
    private void HoldBarFor(TargetRef target, long until)
    {
        switch (target.Kind)
        {
            case TargetKind.Npc:
                var npcs = _ctx.State.NpcsForMap(target.B);
                if (npcs is not null && target.A >= 1 && target.A <= Constants.MaxMapNpcs)
                    npcs[target.A].BarHoldUntilMs = Math.Max(npcs[target.A].BarHoldUntilMs, until);
                break;
            case TargetKind.Traversal:
                if (_ctx.State.TraversalNpcs.TryGetValue((target.A, target.B), out var tn))
                    tn.BarHoldUntilMs = Math.Max(tn.BarHoldUntilMs, until);
                break;
            case TargetKind.Player:
                if (target.A >= 1 && target.A <= Constants.MaxPlayers)
                    _ctx.State.Players[target.A].BarHoldUntilMs = Math.Max(_ctx.State.Players[target.A].BarHoldUntilMs, until);
                break;
        }
    }

    /// <summary>Delayed death: hold a killed entity's sprite in place until its killing spell bolt lands, so the
    /// body doesn't vanish before the visible projectile arrives. Works for NPCs, traversal guests, and other
    /// players. No-op for the LOCAL player (its own death must not lag) or a death with no in-flight hit.
    /// (Distinct from the combat-logoff "ghost" feature — different concept, different vocabulary.)</summary>
    public void OnEntityDied(EntityDeathFx fx)
    {
        if (fx.Target.Kind == TargetKind.Player && fx.Target.A == _ctx.State.MyIndex) return;
        long release = ClaimOrReuseRelease(fx.Target);
        if (release <= 0 || fx.SpriteRow < 0) return;
        if (!TryEntityScreen(fx.Map, fx.X, fx.Y, fx.XOff, fx.YOff, out float sx, out float sy)) return;
        _delayedDeaths.Add(new DelayedDeath
        {
            WorldX = sx + _camera.CameraX, WorldY = sy + _camera.CameraY, SpriteRow = fx.SpriteRow, Dir = fx.Dir, ReleaseMs = release, Size = fx.Size,
        });
    }

    private static bool SameTarget(TargetRef a, TargetRef b) => a.Kind == b.Kind && a.A == b.A && a.B == b.B;

    // Claim the EARLIEST unclaimed in-flight hit for this target (FIFO), returning its release time (0 if none).
    // Claiming marks it so N bolts on one target stagger their numbers/deaths across their distinct arrivals.
    private long ClaimRelease(TargetRef target)
    {
        long now = Environment.TickCount64;
        int best = -1;
        for (int i = 0; i < _pendingHits.Count; i++)
        {
            var h = _pendingHits[i];
            if (!h.Claimed && h.ReleaseMs > now && SameTarget(h.Target, target)
                && (best < 0 || h.ReleaseMs < _pendingHits[best].ReleaseMs))
            {
                best = i;
            }
        }
        if (best < 0) return 0;
        var claimed = _pendingHits[best];
        claimed.Claimed = true;
        _pendingHits[best] = claimed;
        _lastClaimTarget = target;
        _lastClaimRelease = claimed.ReleaseMs;
        _lastClaimTick = now;
        _lastClaimConsumed = false;
        return claimed.ReleaseMs;
    }

    // A death and its damage number are the SAME hit: the death reuses the number's just-claimed bolt (same
    // batch) rather than consuming a second one; otherwise it claims its own.
    private long ClaimOrReuseRelease(TargetRef target)
    {
        long now = Environment.TickCount64;
        if (!_lastClaimConsumed && _lastClaimRelease > now && SameTarget(_lastClaimTarget, target)
            && now - _lastClaimTick <= ClaimReuseWindowMs)
        {
            _lastClaimConsumed = true;
            return _lastClaimRelease;
        }
        return ClaimRelease(target);
    }

    // Release deferred hit numbers whose projectile has landed; expire stale pending hits. Called each frame.
    private void ReleaseDeferredHits()
    {
        long now = Environment.TickCount64;
        for (int i = _deferredFloats.Count - 1; i >= 0; i--)
        {
            if (now >= _deferredFloats[i].ReleaseMs)
            {
                var d = _deferredFloats[i];
                if (d.Text is not null) SpawnFloatingText(d.WorldX - _camera.CameraX, d.WorldY - _camera.CameraY, d.Text, d.Color);
                if (d.BloodIntensity > 0f && _showBlood) _particles.EmitBloodSplatter(d.WorldX, d.WorldY, d.BloodIntensity, d.Layer);
                _deferredFloats.RemoveAt(i);
            }
        }

        for (int i = _delayedDeaths.Count - 1; i >= 0; i--)
            if (now >= _delayedDeaths[i].ReleaseMs) _delayedDeaths.RemoveAt(i);
        for (int i = _pendingHits.Count - 1; i >= 0; i--)
            if (now >= _pendingHits[i].ReleaseMs + PendingHitGraceMs) _pendingHits.RemoveAt(i);
    }

    // ── Public access for IClientEvents wiring ────────────────────────────────

    /// <summary>Spawn floating text anchored over an entity on (mapNum, lx, ly) with its
    /// current interp offset (xoff, yoff). Returns false if the entity isn't on screen.
    /// Centralizes the screen-coord conversion + below-sprite flip + pic-center offset that
    /// the half-dozen damage/heal/exp/levelup spawn sites otherwise repeat.
    ///
    /// <para>🔴 <paramref name="size"/> is the subject's FOOTPRINT in tiles, and it has no default on
    /// purpose: an NPC's position is its top-left anchor, so text sized 1 over a 3x3 body lands on its
    /// shoulder and reads as belonging to something else. A caller over a player passes 1 and says so;
    /// a caller over an NPC asks <see cref="PopupFootprint"/>.</para></summary>
    public bool SpawnFloatingTextAtEntity(int mapNum, int lx, int ly, float xoff, float yoff,
                                          string text, Color color, int size)
    {
        if (!TryEntityScreen(mapNum, lx, ly, xoff, yoff, out float sx, out float sy)) return false;
        bool floatDown = sy < RenderCommandBuilder.BelowSpriteThreshold;
        // Center on the footprint and, when floating below, clear the whole body (size*PicY) - size 1 is unchanged.
        float cx = sx + size * Constants.PicX / 2f;
        float cy = floatDown ? sy + size * Constants.PicY + FloatTextGapBelow : sy - FloatTextGapAbove;
        SpawnFloatingText(cx, cy, text, color, floatDown);
        return true;
    }

    /// <summary>Slide every live floating text by (dx, dy) world pixels. Called on a seamless
    /// border crossing (see <see cref="ClientState.GridShifted"/>) — the loaded grid data slides
    /// one cell in the opposite direction of the cross, so any world-pixel coord we cached at
    /// spawn time becomes stale by that same amount. Without this, floats spawned just before a
    /// seam (e.g. the "Enter Combat" tag on a pursuing player) drift off-screen the instant the
    /// camera re-frames around the new center map.</summary>
    public void ShiftFloatingTexts(int dx, int dy)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_floatingTexts);
        for (int i = 0; i < span.Length; i++)
        {
            span[i].X += dx;
            span[i].Y += dy;
        }
    }

    /// <summary>Drop every live floating text. Called on a warp/teleport/full reload (see
    /// <see cref="ClientState.MapStateCleared"/>): floats are anchored in the old map's world-pixel
    /// frame, so without this they hang over the destination map until they age out.</summary>
    public void ClearFloatingTexts() => _floatingTexts.Clear();

    public void SpawnFloatingText(float x, float y, string text, Color color, bool floatDown = false)
    {
        // Callers pass a SCREEN position (computed from the entity at spawn time via WorldTileToScreen).
        // Add the camera back to recover the exact world pixel so it stays anchored to the gameworld as
        // the camera scrolls; the same-frame stacking check below is then consistently in world space too.
        x += _camera.CameraX;
        y += _camera.CameraY;
        const float xThreshold = 50f;
        const float yThreshold = 20f;
        const float yStep = 16f;
        // Only floats that pop in the SAME frame over the SAME spot are stacked: they would otherwise render
        // exactly on top of one another. Frame-mates are the ones still at Age == 0 (aging runs once per
        // frame in Update, so anything spawned this frame hasn't aged yet); earlier-frame floats have
        // Age > 0 and are already drift-separated, so each of those simply starts at the origin.
        int stackIndex = 0;
        foreach (var ft in _floatingTexts)
        {
            if (ft.Age == 0f && ft.FloatDown == floatDown
                && Math.Abs(ft.X - x) < xThreshold && Math.Abs(ft.Y - y) < yThreshold)
            {
                stackIndex++;
            }
        }
        // Fan the column opposite the drift (toward the sprite): down (+y) for normal above-sprite text,
        // up (-y) when the text is flipped below the sprite.
        float stackOffset = (floatDown ? -1f : 1f) * stackIndex * yStep;
        _floatingTexts.Add(new FloatingText(x, y, text, color, stackOffset, floatDown));
    }
}

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

/// <summary>Blood decals: the generated stain atlas, the accumulation pass into an offscreen field,
/// and the composite that lays it under the entities.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Blood-pool decals ──────────────────────────────────────────────────────
    private Texture2D? _bloodTex;
    private const int BloodVariants = 3;    // lumpy CORE variants in the atlas (the streak + droplet cells follow these)
    private const int BloodBlobSize = 96;   // px per atlas cell (resolution headroom for big draws)
    private const float BloodCoreRadius = 0.52f;   // central blob radius (normalized; 1 = cell half) — bigger core so pools read solid + merge
    // Droplets are stamped SEPARATELY from the core (not baked in): their COUNT ramps with the pool's amount and
    // they sit at FIXED positions so they don't slide as the pool grows/shrinks (see the draw loop).
    private const int BloodSatelliteMax = 30;         // droplet count at a FULL tile — ramps in lockstep with the blob size (shares BloodSizeFullAmount), so droplets ring the pool at every size, birth-anchored
    private const float BloodSatelliteMinPx = 1f;     // droplet diameter range (px) — per-droplet, FIXED (independent of pool size)
    private const float BloodSatelliteMaxPx = 4f;
    private const float BloodSatelliteDistMinFrac = 0.30f; // droplet distance as a FRACTION of the pool size (min) — so droplets sit just past the rim at ANY size, not a fixed px that's too far on a small pool / swallowed by a big one
    private const float BloodSatelliteDistMaxFrac = 0.48f; // + up to here, per-droplet — a tight cluster hugging the rim
    private const float BloodDropletFadeFloor = 0.6f;  // droplets are fully GONE once the stain's freshness falls to this — so the spatter clears while the pool is still dark (raise toward 1 to clear it even sooner)
    private const float BloodDropletFadePower = 2.5f;  // fade curve WITHIN the [floor..1] window — >1 so the spatter thins out faster than the pool, cueing a stain's freshness

    // One atlas texture holding, side by side: BloodVariants lumpy CORE blobs then a single DROPLET cell (a soft
    // dot), stamped as separate quads at draw time (see DrawBloodInfluence) and welded into one mass by the
    // MAX-blend accumulation buffer.  EVERY cell fades to fully transparent at its edges, so linear sampling at a
    // source-rect boundary can't bleed one cell's ink into the next (that bleed was showing as stray red lines at
    // blob edges).  Premultiplied white, tinted dark red at composite.  Deterministic (hashed shape, no RNG).
    private void EnsureBloodTextures(GraphicsDevice gd)
    {
        if (_bloodTex is not null) return;
        int sz = BloodBlobSize, cells = BloodVariants + 1, w = sz * cells;
        var px = new Color[w * sz];
        float c = sz / 2f;

        // Core variants: solid center, soft irregular rim (the lumpy blob that GROWS with the pool amount).
        for (int v = 0; v < BloodVariants; v++)
        {
            int ox = v * sz;
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x - c + 0.5f) / c;   // -1..1 (1 = cell half)
                    float dy = (y - c + 0.5f) / c;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float ang = MathF.Atan2(dy, dx);
                    float coreEdge = BloodCoreRadius * (1f + 0.16f * MathF.Sin(ang * 3f + v * 1.7f) + 0.10f * MathF.Sin(ang * 5f - v * 2.3f));
                    float a01;
                    // Solid out to 70% of the radius with a tight soft rim (outer 30%).  A mostly-solid core means a
                    // streak (drawn at full opacity) matches the core across most of its overlap instead of reading
                    // as a darker spoke punching through a wide faded rim — so streaks blend in.  Also reads bold.
                    if (dist >= coreEdge)
                    {
                        a01 = 0f;
                    }
                    else if (dist <= coreEdge * 0.70f)
                    {
                        a01 = 1f;
                    }
                    else
                    {
                        float r = (dist - coreEdge * 0.70f) / (coreEdge * 0.30f);
                        a01 = 1f - r * r * (3f - 2f * r);
                    }
                    byte a = (byte)(Math.Clamp(a01, 0f, 1f) * 255f);
                    px[y * w + ox + x] = new Color(a, a, a, a);   // premultiplied
                }
            }
        }

        // Droplet cell: a small soft round dot (solid inner half, soft rim), centered.  Stamped at a FIXED px
        // size for the detached satellite spatter.
        {
            int ox = BloodVariants * sz;
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x - c + 0.5f) / c, dy = (y - c + 0.5f) / c;
                    float d = MathF.Sqrt(dx * dx + dy * dy);           // 0 center .. 1 cell edge
                    float a01 = d >= 1f ? 0f : MathF.Min(1f, (1f - d) / 0.5f);   // solid inner half, soft rim
                    byte a = (byte)(Math.Clamp(a01, 0f, 1f) * 255f);
                    px[y * w + ox + x] = new Color(a, a, a, a);
                }
            }
        }

        _bloodTex = new Texture2D(gd, w, sz);
        _bloodTex.SetData(px);
    }

    // Draws the frame's blood-pool decals — a few stamps (core + streaks + droplets) per bloodied tile, all
    // from the single atlas (one texture bind) inside the existing world AlphaBlend batch, so they scroll with
    // the map and dim at night.  A stable per-tile hash picks the core variant, rotation, and scale jitter so a
    // spread multi-tile pool reads as one organic mass rather than a grid of circles.
    // Accumulate every blood pool into the offscreen bloodRT with MAX blend: overlapping blobs take the
    // higher influence (a smooth UNION), so adjacent pools merge into one contiguous mass with no additive
    // darkening at the seams.  Runs before the world batch because it swaps render targets; the world target
    // is re-bound + re-cleared afterward (nothing was drawn to it yet, so the re-clear is loss-free).
    // Two-layer world: accumulate only the pools on `layer` into `bloodRT`, so ground blood and fringe (bridge-
    // top) blood build separate fields that composite in their own passes (fringe over ground).  Skips the whole
    // render-target swap when this layer has no blood, so a flat map pays nothing for the second field.
    private bool AccumulateBloodField(SpriteBatch sb, RenderTarget2D bloodRT, Matrix transform, WorldLayer layer)
    {
        var blood = _renderFrame.Blood;
        bool any = false;
        for (int i = 0; i < blood.Count && !any; i++) if (blood[i].Layer == layer) any = true;
        if (!any) return false;

        EnsureBloodTextures(sb.GraphicsDevice);
        var gd = sb.GraphicsDevice;
        var saved = gd.GetRenderTargets();
        if (saved.Length == 0) return false;   // no world target bound (shouldn't happen) — skip rather than risk binding the backbuffer
        gd.SetRenderTarget(bloodRT);
        gd.Clear(Color.Transparent);
        sb.Begin(SpriteSortMode.Deferred, MirageGame.MaxLightBlend, SamplerState.LinearClamp, null, null, null, transform);
        for (int i = 0; i < blood.Count; i++) if (blood[i].Layer == layer) DrawBloodInfluence(sb, blood[i]);
        sb.End();
        gd.SetRenderTargets(saved);
        gd.Clear(Color.Black);   // the re-bind discarded the world target (nothing drawn to it yet); re-establish its clear
        return true;
    }

    // Stamps one tile's blood into the MAX-blend accumulation buffer.  Every blood tile gets a CORE POOL BLOB
    // (small at first, grows SLOWLY toward max under accumulation) plus DROPLETS (count ramps with the amount) for
    // a splatter look.  Streaks removed.  Opacity = freshness.  Seed drives a stable per-tile variant/rotation/
    // jitter so the look never shimmers frame-to-frame.
    private static float BloodHash(float n)
    {
        float s = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return s - MathF.Floor(s);
    }
    private void DrawBloodInfluence(SpriteBatch sb, in BloodDrawCmd cmd)
    {
        var tex = _bloodTex!;
        int sz = BloodBlobSize;
        float sat = Math.Clamp(cmd.Amount / Constants.BloodSizeFullAmount, 0f, 1f);
        float influence = cmd.Freshness;
        uint h = (uint)cmd.Seed;
        int variant = (int)(h % BloodVariants);
        float rot = ((h >> 3) % 360) * (MathF.PI / 180f);
        float jitter = 0.85f + ((h >> 12) % 31) / 100f;   // 0.85..1.15
        var tint = Color.White * influence;               // MAX-accumulate the influence; the blood color is applied at composite
        // A large NPC (Size>1) leaves ONE decal scaled to its whole body, CENTERED on the footprint (Size*32 px)
        // instead of its top-left anchor tile - so its splat reads as one pool, not Size*Size discrete tile blobs.
        float scale = cmd.Size < 1 ? 1f : cmd.Size;
        var pos = new Vector2(cmd.ScreenX + scale * Constants.PicX / 2f, cmd.ScreenY + scale * Constants.PicY / 2f);
        var center = new Vector2(sz / 2f, sz / 2f);

        // Core pool blob — grows with the amount, shrinks back as it dries; scaled up for a large NPC's body.
        float coreSize = MathHelper.Lerp(Constants.BloodDecalMinSizePx, Constants.BloodDecalMaxSizePx, sat) * jitter * scale;
        sb.Draw(tex, pos, new Rectangle(variant * sz, 0, sz, sz), tint, rot, center, coreSize / sz, SpriteEffects.None, 0f);

        // Droplets — always.  FIXED px size AND fixed absolute distance (both hashed per droplet, NOT scaled by the
        // pool), so a droplet never slides as blood accumulates or dries.  The COUNT ramps with saturation, so new
        // droplets appear on top of the ones already placed; the newest fades in with `dPartial` (no pop).
        var dropSrc = new Rectangle(BloodVariants * sz, 0, sz, sz);
        // Droplets fade to NOTHING by the time the stain's freshness drops to BloodDropletFadeFloor — well before
        // the pool itself "dries" — so a fresh stain shows its spray and an aged one is just the pool left.  Remap
        // freshness from [floor..1] onto [0..1] so it hits a hard 0 at the floor (not the asymptotic speck a bare
        // power curve leaves), then the power shapes the fade within that window.
        float dropFade = Math.Clamp((influence - BloodDropletFadeFloor) / (1f - BloodDropletFadeFloor), 0f, 1f);
        var dropTint = Color.White * MathF.Pow(dropFade, BloodDropletFadePower);
        float dCountF = BloodSatelliteMax * sat;   // droplet count ramps in lockstep with the blob SIZE (shared `sat` = amount / BloodSizeFullAmount)
        int dWhole = (int)MathF.Floor(dCountF);
        float dPartial = dCountF - dWhole;
        for (int k = 0; k <= dWhole; k++)
        {
            float dMul = k < dWhole ? 1f : dPartial;
            if (dMul <= 0.01f) break;
            float baseN = variant * 9.7f + k * 7.31f;
            float phi = rot + BloodHash(baseN) * MathF.Tau;
            // Distance is a fraction of the blob size AT THIS DROPLET'S BIRTH — the size-saturation the pool had
            // when the count first reached index k.  With the droplet ramp matched to the size ramp, that's simply
            // k/Max, a function of k alone, so the position is CONSTANT: as the pool grows, higher-index droplets
            // appear further out at the bigger rim while every droplet already placed stays put (and the growing
            // pool eventually laps over the innermost).
            float birthSize = MathHelper.Lerp(Constants.BloodDecalMinSizePx, Constants.BloodDecalMaxSizePx, k / (float)BloodSatelliteMax) * jitter * scale;
            float dist = birthSize * (BloodSatelliteDistMinFrac + BloodHash(baseN + 2.1f) * (BloodSatelliteDistMaxFrac - BloodSatelliteDistMinFrac));
            float dropPx = (BloodSatelliteMinPx + BloodHash(baseN + 4.3f) * (BloodSatelliteMaxPx - BloodSatelliteMinPx)) * scale;  // 1..4 px, per-droplet (scaled up for a big NPC)
            var sp = pos + new Vector2(MathF.Cos(phi), MathF.Sin(phi)) * dist;
            sb.Draw(tex, sp, dropSrc, dropTint * dMul, 0f, center, dropPx / sz, SpriteEffects.None, 0f);
        }
    }

    // Composite the merged blood field over the ground as one tinted 1:1 quad.  The viewport-sized dest rect
    // maps under the world transform to the full supersampled target, matching bloodRT 1:1.  Premultiplied
    // blood tint * MaxAlpha, so the union reads as solid dark red (with a soft rim from the blob falloff).
    private void CompositeBloodField(SpriteBatch sb, RenderTarget2D bloodRT)
    {
        var rgb = Constants.BloodTintRgb;
        var tint = new Color((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF)) * Constants.BloodMaxAlpha;
        sb.Draw(bloodRT, new Rectangle(0, 0, Camera.ViewW, Camera.ViewH), tint);
    }
}

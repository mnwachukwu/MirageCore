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

/// <summary>The night light map: ambient darkness, per-light halos, safe-zone and always-dark
/// regions, and the glow pass over it.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Light-source constants ─────────────────────────────────────────────────────────────────────

    // Halo geometry + flicker constants/curves now live in Mirage.Shared LightModel, shared verbatim with the
    // editor's night preview so the two can't drift (inner core = outer reach × LightModel.InnerRadiusFactor,
    // dim outer tint = core × LightModel.OuterDimFactor).
    private const int HalfTile = Constants.PicX / 2;
    // Per-light core color now travels on LightSourceCmd.Rgb (default torch = Core's TorchLightRgb); the halo
    // textures stay white-luminance and are tinted at draw. The dim outer tint = core × OuterDimFactor, the
    // inner core = the full color scaled by darkness and flicker — a single torch center still lands warm gold.
    // Safe-zone map light: a steady map-wide area light (no flicker), max-blended so contiguous safe maps
    // merge without seam glow. FULL WHITE = full daylight (×1.0 in the multiply). Because the town light map
    // is already at white, an entity's additive halo on top just clamps at 255 → invisible in town — so no
    // per-emitter suppression is needed; emitters only show where they spill into the darker wilderness.
    // Warm, slightly-dimmed daylight (below white) — a "lit town" look with a touch of evening warmth.
    // Because it's below white, an entity's additive halo could show through in town, so emitters instead
    // fade their own halo out by the safe-zone coverage (see TownLightCoverage) — full daylight would have
    // masked emitters for free, but the warm look is preferred. Raise toward white for a neutral daylit town.
    private static readonly Vector3 SafeZoneLightPeak = new(200f, 195f, 175f);

    // Flicker seeding + curves (Flame/Pulse) live in LightModel.FlickerFor, shared with the editor preview.

    // Unpack a packed 0xRRGGBB color to a straight-RGB 0..255 vector for tinting the white halo textures.
    private static Vector3 UnpackRgb(uint rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // Same, as a Color (used for the additive glow-seam draw).
    private static Color UnpackColor(uint rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>
    /// Builds the light map into the currently-bound light RT. EVERYTHING composites with MAX blend so no
    /// two light sources bleed into each other: safe-zone area lights, then entity halos. Each entity halo
    /// is a single baked sprite (inner+outer already fused), so max between sources never flattens a light's
    /// own glow. Call while the light RT is bound, with <paramref name="transform"/> =
    /// <c>Matrix.CreateScale(_worldSS)</c>. Composited over the world by multiply in Pass 2.
    /// </summary>
    public void DrawLightMap(SpriteBatch sb, Matrix transform, Texture2D boxTex, Texture2D outerTex,
        Texture2D innerTex, float totalSec, WorldLayer? layerFilter = null, Effect? maskFx = null)
    {
        // Between frames, and only between frames: reach masks are traced while the frame is built, so this
        // number holds still across the passes below and the second and third find nothing to let go of.
        int gen = RenderCommandBuilder.ReachGeneration;
        if (gen != _reachTexGeneration)
        {
            RecycleReachTextures();
            _reachTexGeneration = gen;
        }

        // Safe-zone map area lights FIRST, MAX-blended: contiguous safe cells' flat interiors tile with no
        // seam, skirts spill. (Emitters are excluded from the Lights list inside safe cells, so no halo
        // sits over a town — only the wilderness-side skirt meets halos, where additive fills cleanly.)
        if (_renderFrame.AlwaysLitMapLights.Count > 0)
        {
            sb.Begin(SpriteSortMode.Deferred, MirageGame.MaxLightBlend,
                SamplerState.LinearClamp, null, null, null, transform);
            var safeColor = ScaleGlow(SafeZoneLightPeak, 1f);
            foreach (var m in _renderFrame.AlwaysLitMapLights) DrawMapAreaBox(sb, boxTex, in m, safeColor);
            sb.End();
        }

        // Per-map light overrides SECOND, ALPHA-BLENDED: indoor maps stay lit (White), AlwaysDark maps
        // stay dark (NightAmbient) regardless of time of day. Indoors drawn before AlwaysDark so darkness
        // wins on co-located maps. Deliberately AFTER the safe-zone MAX pass: an explicit per-map override
        // must win over a neighboring safe map's light skirt. Otherwise a safe map adjacent to an
        // AlwaysDark map would MAX-blend its bright spill ~2 tiles into the dark cell, lighting the edge,
        // pushing the visible seam inward, and delaying torch visibility. Alpha-blending the solid override
        // (alpha 1 over the cell) here paints back over any bled-in safe light; the override's own skirt
        // then spills the correct way — darkness OUT of the dark map, light OUT of the indoor map.
        if (_renderFrame.IndoorsMapLights.Count > 0 || _renderFrame.AlwaysDarkMapLights.Count > 0)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, null, null, null, transform);
            foreach (var m in _renderFrame.IndoorsMapLights) DrawMapAreaBox(sb, boxTex, in m, Color.White);
            foreach (var m in _renderFrame.AlwaysDarkMapLights) DrawMapAreaBox(sb, boxTex, in m, MirageGame.NightAmbient);
            sb.End();
        }

        // Entity halos SECOND, ADDITIVE: static outer reach + flickering inner core. Overlapping halos (and
        // the safe skirt at a town border) sum and blend smoothly — no seams, no dips — clamping at white
        // (= fully lit) in the 8-bit light map, so nothing floods brighter than "no lighting effect".
        //
        // Two batches. A light with a reach mask carries its own texture and its own UV mapping, which are
        // effect parameters, so those go through LightMask.fx in Immediate mode — every Draw flushes, so the
        // parameters set just before it are the ones it uses. Everything else batches as before. Additive
        // accumulation is order-independent, so splitting the list changes nothing about the result.
        var masked = _lightBatchScratch;
        masked.Clear();
        sb.Begin(SpriteSortMode.Deferred, MirageGame.LightAccumBlend,
            SamplerState.LinearClamp, null, null, null, transform);
        foreach (var cmd in _renderFrame.Lights)
        {
            // Two light maps (occlusion split): a filtered pass builds only ONE layer's halos (ground vs fringe)
            // so ground-layer content is lit by the ground map and fringe content by the fringe map. Unfiltered
            // (layerFilter == null) is the single-map flat/daylight path — every halo, as before.
            if (layerFilter is not null && cmd.Layer != layerFilter) continue;
            if (maskFx is not null && cmd.Reach is not null) { masked.Add(cmd); continue; }
            DrawLightHalo(sb, outerTex, innerTex, cmd.ScreenX + HalfTile, cmd.ScreenY + HalfTile, cmd, totalSec, null);
        }
        sb.End();

        if (masked.Count == 0) return;
        sb.Begin(SpriteSortMode.Immediate, MirageGame.LightAccumBlend,
            SamplerState.LinearClamp, null, null, maskFx, transform);
        foreach (var cmd in masked)
        {
            maskFx!.Parameters["MaskTexture"].SetValue(
                ReachTexture(sb.GraphicsDevice, cmd.ReachRadius, cmd.Reach!));
            // A standing emitter reads only the first mask, but the sampler still has to be bound — and it
            // binds the same texture, so the pair costs one upload rather than two.
            maskFx.Parameters["IntoTexture"].SetValue(
                ReachTexture(sb.GraphicsDevice, cmd.ReachRadius, cmd.ReachInto ?? cmd.Reach!));
            sb.GraphicsDevice.SamplerStates[1] = SamplerState.LinearClamp;
            sb.GraphicsDevice.SamplerStates[2] = SamplerState.LinearClamp;
            DrawLightHalo(sb, outerTex, innerTex, cmd.ScreenX + HalfTile, cmd.ScreenY + HalfTile, cmd, totalSec, maskFx);
        }
        sb.End();
    }

    // A texture per distinct MASK, keyed by the mask array itself, backed by a pool of spares per texture
    // size. A mask array leaves the reach cache only when that cache drops wholesale, so within one
    // RenderCommandBuilder.ReachGeneration the same array is the same reach and the texture holding it stays
    // good. The UVs map a halo onto the whole texture, so a mask cannot share one with a mask of another size.
    private readonly List<LightSourceCmd> _lightBatchScratch = [];
    private readonly Dictionary<byte[], Texture2D> _reachTexOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, Stack<Texture2D>> _reachTexPool = [];
    private int _reachTexGeneration = -1;


    /// <summary>The texture holding one light's reach mask, opaque where the light gets through. Uploaded the
    /// first time a mask is seen and kept until the reach cache drops.
    ///
    /// <para>🔴 The upload is the expensive half of a light, and not for the bytes. Writing a texture the GPU
    /// is still reading makes the driver drain the pipeline, and the halo batch runs in
    /// <see cref="SpriteSortMode.Immediate"/>, so one upload per light is one sync point per light — a cost
    /// that scales with how many lit actors are on screen and reads as Draw throughput, not as a spike. Keyed
    /// per mask, a standing lamp uploads once and a street of them uploads nothing.</para>
    ///
    /// <para>One byte a texel, not four: the mask is a single bit and a radius-3 light is 56x56 of them, so a
    /// colour format spends three quarters of the upload on nothing.</para>
    ///
    /// <para>🔴 <see cref="SurfaceFormat.Alpha8"/> lands in a DIFFERENT CHANNEL per backend, so the format
    /// and the shader have to be changed together. On DesktopGL — which is every platform this ships on —
    /// MonoGame maps it to <c>GL_LUMINANCE</c>, which samples as (L,L,L,1), so <c>LightMask.fx</c> reads
    /// <c>.r</c>. A DirectX build would get <c>A8_UNORM</c>, which samples as (0,0,0,a): the mask would read
    /// as zero everywhere and every light in the game would go black. Obvious rather than subtle, but the
    /// fix is in the shader and nowhere near where it would be noticed.</para></summary>
    private Texture2D ReachTexture(GraphicsDevice gd, int radius, byte[] reach)
    {
        if (_reachTexOf.TryGetValue(reach, out var tex)) return tex;

        // 🔴 Never recycles here. A moving light binds both of its masks at once, so a texture reclaimed
        // partway through a pass could be handed straight back for that same light's second mask — one
        // sampler reading the other's content. Reclaiming happens between frames and nowhere else; a pass
        // that outruns the pool grows it instead.
        int side = LightOcclusion.MaskTexels(radius);
        var free = ReachTexPool(side);
        tex = free.Count > 0 ? free.Pop() : new Texture2D(gd, side, side, false, SurfaceFormat.Alpha8);

        tex.SetData(reach, 0, side * side);
        _reachTexOf[reach] = tex;
        return tex;
    }

    private Stack<Texture2D> ReachTexPool(int side)
    {
        if (!_reachTexPool.TryGetValue(side, out var free)) _reachTexPool[side] = free = new Stack<Texture2D>();
        return free;
    }

    /// <summary>Returns every held mask texture to its size's pool. Textures are recycled, never destroyed.</summary>
    private void RecycleReachTextures()
    {
        foreach (var tex in _reachTexOf.Values) ReachTexPool(tex.Width).Push(tex);
        _reachTexOf.Clear();
    }

    /// <summary>Fills the pool with every mask texture a scene could ask for, made up front.
    ///
    /// <para>Creating one costs a driver allocation, and a driver allocation in the middle of a frame is a
    /// stall of a hundred milliseconds or more — the price of a light at an unseen radius walking into view.
    /// <see cref="PrewarmedPerRadius"/> of each of the <see cref="MaxLightRadiusTiles"/> sizes costs about
    /// nine megabytes at load and covers every scene a viewport can hold.</para></summary>
    private void PrewarmReachTextures(GraphicsDevice gd)
    {
        for (int r = 0; r <= MaxLightRadiusTiles; r++)
        {
            int side = LightOcclusion.MaskTexels(r);
            var free = ReachTexPool(side);
            while (free.Count < PrewarmedPerRadius)
                free.Push(new Texture2D(gd, side, side, false, SurfaceFormat.Alpha8));
        }
    }

    /// <summary>The widest reach a light is built with. An NPC's scales with its footprint and the biggest
    /// body is three tiles, so this has headroom over anything the world can hold.</summary>
    private const int MaxLightRadiusTiles = 12;

    /// <summary>Mask textures made up front per radius. A frame holds one per distinct mask drawn, which is
    /// at most two per visible light — a moving emitter reads the tile it left and the one it is entering —
    /// so this covers a couple of dozen emitters all at one radius, well past what fits on screen at once.</summary>
    private const int PrewarmedPerRadius = 48;

    /// <summary>
    /// Draws the additive FX glow cores (spell balls, sparkles) at the post-composite "glow seam" so they
    /// read through night darkness. Called by <see cref="MirageGame"/> after the light multiply with the
    /// composite's map rect + scale; positions are world-RT screen space (glow CENTER). No-op when empty.
    /// </summary>
    public void DrawGlows(SpriteBatch sb, Texture2D haloTex, Rectangle mapRect, float scaleX, float scaleY)
    {
        if (_renderFrame.Glows.Count == 0) return;
        var prevScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.GraphicsDevice.ScissorRectangle = mapRect;
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, null,
            MirageGame.WorldCompositeRaster);
        foreach (var g in _renderFrame.Glows)
        {
            float r = g.Radius * scaleX;
            float cx = mapRect.X + g.ScreenX * scaleX;
            float cy = mapRect.Y + g.ScreenY * scaleY;
            var dest = new Rectangle((int)(cx - r), (int)(cy - r), (int)(r * 2f), (int)(r * 2f));
            sb.Draw(haloTex, dest, UnpackColor(g.Rgb));
        }
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = prevScissor;
    }

    // The box texture is a flat interior ringed by a skirt of MapAreaBleed px that feathers 1 -> 0. Drawn as
    // a nine-patch: the four corners keep the skirt's authored size, the four edges stretch along the one
    // axis they are constant on, and the flat interior stretches to the map. Stretching the whole texture
    // instead would scale the skirt with the map, so the feather would widen as maps grow.
    private static void DrawMapAreaBox(SpriteBatch sb, Texture2D boxTex, in MapLightCmd m, Color color)
    {
        int b = MirageGame.MapAreaBleed;
        int srcInnerW = boxTex.Width - b * 2, srcInnerH = boxTex.Height - b * 2;
        int x0 = (int)m.ScreenX - b, x1 = (int)m.ScreenX, x2 = (int)m.ScreenX + m.PxW;
        int y0 = (int)m.ScreenY - b, y1 = (int)m.ScreenY, y2 = (int)m.ScreenY + m.PxH;
        int[] dx = { x0, x1, x2 }, dw = { b, m.PxW, b };
        int[] dy = { y0, y1, y2 }, dh = { b, m.PxH, b };
        int[] sx = { 0, b, b + srcInnerW }, sw = { b, srcInnerW, b };
        int[] sy = { 0, b, b + srcInnerH }, sh = { b, srcInnerH, b };
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                sb.Draw(boxTex, new Rectangle(dx[c], dy[r], dw[c], dh[r]),
                        new Rectangle(sx[c], sy[r], sw[c], sh[r]), color);
            }
        }
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Sound;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Client.Shell;

/// <summary>The draw half: the world render target, the night/bridge split-target composite, the
/// heat-haze shader pass, and the letterbox that maps it all to the window.</summary>
public sealed partial class MirageGame : Game
{
    /// <summary>Renders the frame in passes: the scrolling world into its supersampled target (with the
    /// light and blood passes, split per layer when a bridge is in view), the composite of that target
    /// onto the letterboxed map area, then the HUD, panels and dialogs at reference scale.</summary>
    protected override void Draw(GameTime gameTime)
    {
        long drawStart = Stopwatch.GetTimestamp();
        var gs = _screens.Current as GameplayScreen;
        var lb = GetLetterbox();

        // 🔴 While a map is being fetched the world is half torn down: ClearMapState has dropped all eight
        // neighbors and the center cell still holds the map being LEFT, so drawing it shows the old map
        // ringed in black, then the tiles swap, then the creatures pop in — three visible steps for one
        // warp, and warping repeatedly stacks them.
        //
        // So the last COMPLETE frame is held instead, and the cut happens once, when the new map is ready.
        // Both world and light targets are PreserveContents, so holding is simply not redrawing them:
        // the composite in Pass 2 blits whatever they last held, at a fixed rect, with the camera already
        // baked in. The frame is frozen for the length of the fetch — milliseconds from cache — which reads
        // as an instant cut rather than a flicker.
        bool holdWorld = gs is not null && _state.GettingMap && _worldDrawn;

        // Pass 1a: render ONLY the scrolling world into its own supersampled target (gameplay only), so it
        // can be composited to the screen with a sub-pixel slide instead of being baked into the reference
        // target's whole-pixel grid (which judders under non-integer window upscaling).
        float rawDarkness = gs is not null ? _state.GetCurrentDarkness() : 0f;
        bool needsLightPass = rawDarkness > 0f;
        if (!needsLightPass && gs is not null)
        {
            var neighbors = _state.NeighborMaps;
            for (int nr = 0; nr < 3 && !needsLightPass; nr++)
            {
                for (int nc = 0; nc < 3 && !needsLightPass; nc++)
                    needsLightPass = _state.LightingOf(neighbors[nc, nr]) == MapLighting.AlwaysDark;
            }
        }
        // Set when the two-light-map occlusion split ran this frame (it bakes lighting per layer), so Pass 2
        // below skips its whole-view light multiply. Hoisted here to reach that second `gs is not null` block.
        bool splitPath = false;
        if (holdWorld)
        {
            // Pass 2 has to light the held frame exactly the way the frame it is holding was lit, so it
            // reuses that frame's answers rather than this frame's. Recomputing them against a torn-down
            // grid is how a held night frame would come back either doubly dark or not dark at all.
            splitPath = _heldSplitPath;
            needsLightPass = _heldNeedsLightPass;
        }
        else if (gs is not null)
        {
            EnsureWorldTarget(lb);
            var wt = Matrix.CreateScale(_worldSS);

            // Bind a world target BEFORE BuildWorldFrame: its blood accumulation saves/restores whatever target is
            // currently bound and NO-OPS if none is (it won't draw to the backbuffer), so one must be live for the
            // blood fields to build. The split/flat branches below re-bind their own targets afterward.
            GraphicsDevice.SetRenderTarget(_worldRT);

            // Build the frame + accumulate the blood fields, and learn whether a bridge/fringe surface is visible.
            bool hasFringe = gs.BuildWorldFrame(_sb!, _font!, wt, _bloodRT, _bloodRTFringe);

            bool haveLights = needsLightPass && _lightRT is not null && _mapLightTex is not null
                && _lightHaloOuterTex is not null && _lightHaloInnerTex is not null;

            // Two-light-map OCCLUSION split — only when it matters: night/dark AND a bridge is visible. Ground
            // content is lit by the ground map (ALL lights), fringe content by the fringe map (fringe lights only),
            // composited so the ground shows through grate/edge gaps lit by the GROUND light while a solid deck
            // occludes it. Every other frame (flat map or daylight) stays on the proven single-target path below.
            // (The black-map bug this once produced was _worldRT defaulting to DiscardContents while the label
            // overlay re-binds it — fixed by the PreserveContents on _worldRT in EnsureWorldTarget.)
            splitPath = haveLights && hasFringe
                && _worldRTGround is not null && _worldRTFringe is not null && _lightRTFringe is not null;

            if (splitPath)
            {
                var ambient = Color.Lerp(Color.White, NightAmbient, rawDarkness);

                // Ground WORLD content into the (opaque) ground target; fringe WORLD content into a TRANSPARENT
                // target so grate/edge gaps keep alpha and the ground shows through. Labels are NOT drawn with the
                // world here — they go ON TOP afterward (below) so the deck never occludes them.
                GraphicsDevice.SetRenderTarget(_worldRTGround);
                GraphicsDevice.Clear(Color.Black);
                gs.DrawWorldGround(_sb!, _font!, wt);
                GraphicsDevice.SetRenderTarget(_worldRTFringe);
                GraphicsDevice.Clear(Color.Transparent);
                gs.DrawWorldFringe(_sb!, _font!, wt, includeWeather: false);   // weather is a separate ALL-lights pass below (else a ground light couldn't brighten it)

                // Two light maps (occlusion model), each cleared to the shared per-map ambient. The GROUND map gets
                // ALL lights (no filter): a ground light lights the ground, AND a FRINGE light — a fire on the
                // bridge — spills down to light the ground beneath it. The FRINGE map gets fringe lights ONLY, so a
                // ground light is OCCLUDED by the fringe (it never lights the deck/décor above it).
                GraphicsDevice.SetRenderTarget(_lightRT);
                GraphicsDevice.Clear(ambient);
                _lightStart = Stopwatch.GetTimestamp();
                gs.DrawLightMap(_sb!, wt, _mapLightTex!, _lightHaloOuterTex!, _lightHaloInnerTex!, _totalTimeSeconds, null, _lightMaskEffect);
                _lightTicks += Stopwatch.GetTimestamp() - _lightStart;   // ALL lights → ground
                GraphicsDevice.SetRenderTarget(_lightRTFringe);
                GraphicsDevice.Clear(ambient);
                _lightStart = Stopwatch.GetTimestamp();
                gs.DrawLightMap(_sb!, wt, _mapLightTex!, _lightHaloOuterTex!, _lightHaloInnerTex!, _totalTimeSeconds, WorldLayer.Fringe, _lightMaskEffect);
                _lightTicks += Stopwatch.GetTimestamp() - _lightStart;   // fringe lights only

                // Pre-multiply each world target by its OWN light map IN PLACE (LightModulateBlend preserves the
                // target's alpha, so the transparent fringe gaps survive lighting).
                GraphicsDevice.SetRenderTarget(_worldRTGround);
                _sb!.Begin(SpriteSortMode.Deferred, LightModulateBlend, SamplerState.PointClamp);
                _sb.Draw(_lightRT!, Vector2.Zero, Color.White);
                _sb.End();
                GraphicsDevice.SetRenderTarget(_worldRTFringe);
                _sb.Begin(SpriteSortMode.Deferred, LightModulateBlend, SamplerState.PointClamp);
                _sb.Draw(_lightRTFringe!, Vector2.Zero, Color.White);
                _sb.End();

                // Composite the two lit targets into _worldRT: ground opaque, fringe alpha-over (transparent deck
                // lets the ground beneath show through). Downstream heat/glow/UI then treat _worldRT as usual.
                GraphicsDevice.SetRenderTarget(_worldRT);
                GraphicsDevice.Clear(Color.Black);
                _sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
                _sb.Draw(_worldRTGround!, Vector2.Zero, Color.White);
                _sb.End();
                _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                _sb.Draw(_worldRTFringe!, Vector2.Zero, Color.White);
                _sb.End();

                // ── Labels ON TOP, lit per the design rule ────────────────────────────────────────────────────
                // Names/bars float over EVERYTHING (never occluded by the deck). GROUND labels + the shared extras
                // (target arrow / chat bubbles / floats / debug) are lit by ALL lights (any layer); FRINGE labels
                // only by fringe lights (ground lights don't reach the deck). Reuse _worldRTFringe as a scratch
                // overlay target (its world content is already composited). Both light maps are read as they
                // stand: everything since they were built has bound them as a source texture and never as a
                // target, so _lightRT still holds the ALL-halos map and _lightRTFringe the fringe-only one.
                // Rebuilding either here would re-run the whole halo batch to land on the same pixels — a third
                // of the frame's light cost, and the split path's alone.

                // Draw labels into the scratch target, multiply by the given light map in place (the scratch stays
                // bound after the overlay batch), then alpha-composite the lit labels over _worldRT.
                void LitLabelPass(RenderTarget2D lightMap, WorldLayer nameLayer, bool extras)
                {
                    GraphicsDevice.SetRenderTarget(_worldRTFringe);
                    GraphicsDevice.Clear(Color.Transparent);
                    gs.DrawWorldOverlay(_sb!, _font!, wt, nameLayer, extras);
                    _sb!.Begin(SpriteSortMode.Deferred, LightModulateBlend, SamplerState.PointClamp);
                    _sb.Draw(lightMap, Vector2.Zero, Color.White);
                    _sb.End();
                    GraphicsDevice.SetRenderTarget(_worldRT);
                    _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                    _sb.Draw(_worldRTFringe!, Vector2.Zero, Color.White);
                    _sb.End();
                }
                // Weather (GLOBAL — snow/rain/wind falls over BOTH planes): draw into the scratch, multiply by the
                // ALL-lights map (_lightRT is the all-halos map here), then composite over _worldRT BELOW the labels.
                // If it rode the fringe target above it would see only the fringe light map, leaving snow over a
                // GROUND torch stuck at night ambient — the reported bug.
                GraphicsDevice.SetRenderTarget(_worldRTFringe);
                GraphicsDevice.Clear(Color.Transparent);
                gs.DrawWorldWeather(_sb!, wt);
                _sb!.Begin(SpriteSortMode.Deferred, LightModulateBlend, SamplerState.PointClamp);
                _sb.Draw(_lightRT!, Vector2.Zero, Color.White);
                _sb.End();
                GraphicsDevice.SetRenderTarget(_worldRT);
                _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                _sb.Draw(_worldRTFringe!, Vector2.Zero, Color.White);
                _sb.End();

                LitLabelPass(_lightRT!, WorldLayer.Ground, extras: true);        // ground labels + extras, lit by ALL lights
                LitLabelPass(_lightRTFringe!, WorldLayer.Fringe, extras: false); // fringe labels, lit by fringe lights only
            }
            else
            {
                // Single-target path (flat map or daylight): the whole world into _worldRT, lit afterward by the
                // single light multiply in Pass 2.
                GraphicsDevice.SetRenderTarget(_worldRT);
                GraphicsDevice.Clear(Color.Black);
                gs.DrawWorld(_sb!, _font!, wt);

                // Pass 1c: light map — clear to the ambient (dark navy at night), then add warm halos at every
                // entity. Composited by multiply in Pass 2. Skipped in full daylight (no RT switch / batch).
                if (haveLights)
                {
                    GraphicsDevice.SetRenderTarget(_lightRT);
                    GraphicsDevice.Clear(Color.Lerp(Color.White, NightAmbient, rawDarkness));
                    _lightStart = Stopwatch.GetTimestamp();
                    gs.DrawLightMap(_sb!, wt, _mapLightTex!, _lightHaloOuterTex!, _lightHaloInnerTex!, _totalTimeSeconds, null, _lightMaskEffect);
                    _lightTicks += Stopwatch.GetTimestamp() - _lightStart;
                }
            }

            // What Pass 2 needs to light this frame, kept so a later held frame can be lit the same way.
            _heldSplitPath = splitPath;
            _heldNeedsLightPass = needsLightPass;
            _worldDrawn = true;
        }

        // Pass 1b: draw the UI / everything else into the 800×600 reference render target.  In gameplay
        // the map area is left TRANSPARENT so the world composite shows through underneath it in Pass 2.
        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(Color.Transparent);
        _sb!.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        _screens.Draw(_sb, _font!);
        // When not in gameplay, draw the options + config panels here. In gameplay, GameplayScreen
        // owns the options draw (it's included in the Z-order draw loop). Draw bottom-first per
        // _configOnTop so the most-recently-opened/clicked panel renders on top.
        if (_screens.Current is not GameplayScreen)
        {
            if (_configOnTop)
            {
                _optionsPanel.Draw(_sb, _font!, _input, isActive: _optionsPanelFocused);
                _configPanel.Draw(_sb, _font!, _input, isActive: _configPanelFocused);
            }
            else
            {
                _configPanel.Draw(_sb, _font!, _input, isActive: _configPanelFocused);
                _optionsPanel.Draw(_sb, _font!, _input, isActive: _optionsPanelFocused);
            }
        }
        if (_dialog.IsVisible)
            _dialog.Draw(_sb, _font!, GraphicsDevice);
        if (_quitConfirm.IsVisible)
            _quitConfirm.Draw(_sb, _font!, _input, GraphicsDevice);
        if (_guildOffer.IsVisible)
            _guildOffer.Draw(_sb, _font!, _input, GraphicsDevice);
        if (_tradeDialog.IsVisible)
            _tradeDialog.Draw(_sb, _font!, _input, GraphicsDevice);
        // Sidebar link strip — pre-game layouts only. The in-game Options (O) / Help (H)
        // pair is drawn inside GameplayScreen.Draw BEFORE its panels so the panels can occlude
        // it; drawing it here would put the link on top of every floating panel.
        if (IsPreConnectScreen())
        {
            HudPanel.OptionsLinkPregame.Draw(_sb!, _font!, _input);
            HudPanel.ConfigureLink.Draw(_sb!, _font!, _input);
        }
        else if (_screens.Current is not GameplayScreen)
        {
            HudPanel.OptionsLink.Draw(_sb!, _font!, _input);
        }
        // Last, so it sits over every screen and every panel. It is the thing you open to read while
        // something else is wrong on screen, which only works if nothing can cover it.
        _consolePanel.Draw(_sb!, _font!, Environment.TickCount64, isActive: true);
        _sb.End();

        // Pass 2: composite the world UNDER the UI.  World first into its map area, then darkness + glow,
        // then the reference target (whose map area is transparent) blended over it.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        if (gs is not null)
        {
            CompositeWorld(lb, gs.CameraWorldY / Camera.ViewH);

            // Map rect + scale from reference space to the letterboxed backbuffer — shared by the light
            // multiply and the glow seam below.
            float scaleX = lb.Width / (float)RefW;
            float scaleY = lb.Height / (float)RefH;
            var mapRect = new Rectangle(lb.X, lb.Y, (int)(Camera.ViewW * scaleX), (int)(Camera.ViewH * scaleY));

            if (!splitPath && needsLightPass && _lightRT is not null && _mapLightTex is not null
                && _lightHaloOuterTex is not null && _lightHaloInnerTex is not null)
            {
                // Multiply the composited world by the light map. Modulates rather than covers, so all
                // world detail (sprites, names, bars, floats) survives while lit areas warm and unlit darken.
                // Skipped on the split path — lighting was already baked per layer into _worldRT.
                var sampler = _worldSS <= 1 ? SamplerState.PointClamp : SamplerState.LinearClamp;
                var prevScissor = GraphicsDevice.ScissorRectangle;
                GraphicsDevice.ScissorRectangle = mapRect;
                _sb.Begin(SpriteSortMode.Deferred, MultiplyBlend, sampler, null, WorldCompositeRaster);
                _sb.Draw(_lightRT!, new Vector2(lb.X, lb.Y), null, Color.White, 0f, Vector2.Zero,
                    new Vector2(scaleX / _worldSS, scaleY / _worldSS), SpriteEffects.None, 0f);
                _sb.End();
                GraphicsDevice.ScissorRectangle = prevScissor;
            }

            // Glow seam (c): additive bright FX cores over the multiplied composite. OUTSIDE the darkness
            // guard so magical FX punch through at night AND read in daylight. Scissored to the map viewport.
            if (_lightHaloInnerTex is not null)
                gs.DrawGlows(_sb, _lightHaloInnerTex, mapRect, scaleX, scaleY);
        }

        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _sb.Draw(_renderTarget!, lb, Color.White);
        if (gs is not null) UiHelper.DrawBorder(_sb, lb, UiHelper.DlgBorderColor, 1);
        _sb.End();

        // Final OS cursor commit — Request*Cursor calls fired during this frame's draw/update
        // are collapsed here into one SetCursor invocation so widgets don't fight one another.
        UiHelper.CommitFrameCursor();

        base.Draw(gameTime);
        RecordFrame(drawStart);
    }

    /// <summary>Closes off the frame's accounting. Present-to-present is the interval a player actually
    /// perceives, so the frame is measured Draw-to-Draw; the very first one has nothing to measure from and
    /// is skipped rather than recorded as however long startup took.</summary>
    private void RecordFrame(long drawStart)
    {
        long end = Stopwatch.GetTimestamp();
        double toMs = 1000.0 / Stopwatch.Frequency;
        // Collections that ran DURING this frame, so a spike that coincides with one has named itself.
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        // Thread-local, and the loop is one thread, so this is the frame's own churn rather than the
        // process's. What drives Gen0, and the number to watch when a collection count is climbing.
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_lastPresentStamp != 0)
        {
            _state.Frames.Record((end - _lastPresentStamp) * toMs, _updateTicks * toMs,
                                 (end - drawStart) * toMs, _updatesThisFrame, _runningSlowly,
                                 g0 - _lastGen0, g1 - _lastGen1, g2 - _lastGen2, allocated - _lastAllocated,
                                 _lightTicks * toMs);
        }

        _lastAllocated = allocated;
        _lightTicks = 0;
        _lastGen0 = g0;
        _lastGen1 = g1;
        _lastGen2 = g2;
        _lastPresentStamp = end;
        _updatesThisFrame = 0;
        _updateTicks = 0;
        _runningSlowly = false;
    }

    /// <summary>Sizes the world render target to the current window: _worldSS = ceil(window scale)
    /// (capped), so the target is always at least the on-screen map size and the composite is a sharp
    /// DOWNSCALE, never an upscale.  Uses the larger of the X/Y scales so a stretch-to-fill (non-4:3)
    /// window doesn't upscale on either axis.  Recreates the target only when the factor actually
    /// changes (a window resize).</summary>
    private void EnsureWorldTarget(Rectangle lb)
    {
        float scale = Math.Max(lb.Width / (float)RefW, lb.Height / (float)RefH);
        int ss = Math.Clamp((int)MathF.Ceiling(scale), 1, MaxWorldSS);
        if (ss == _worldSS && _worldRT is not null) return;
        _worldRT?.Dispose();
        _worldRTGround?.Dispose();
        _worldRTFringe?.Dispose();
        _lightRT?.Dispose();
        _lightRTFringe?.Dispose();
        _bloodRT?.Dispose();
        _bloodRTFringe?.Dispose();
        // PreserveContents is REQUIRED on _worldRT too: the split path composites the world into it, then RE-BINDS it
        // TWICE MORE to alpha-composite the lit label overlays on top (LitLabelPass). With the default DiscardContents
        // those re-binds wipe the just-composited world, leaving only the labels — the "black map, only my name
        // renders" bug. It's Cleared at the start of every frame regardless, so the flat/daylight path is unaffected.
        _worldRT = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        // PreserveContents is REQUIRED: the split path re-binds each of these to multiply it by its light map in
        // place, and the default DiscardContents would wipe the world content on that re-bind (→ black screen).
        _worldRTGround = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);   // two-light-map split: ground content
        _worldRTFringe = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);   // two-light-map split: fringe content (transparent gaps)
        // PreserveContents is REQUIRED on both light maps: the split path builds each one, then keeps reading it
        // as a source texture across several later target binds — the world multiply, then the weather and label
        // passes. Under DiscardContents those reads are only valid by grace of the backend.
        _lightRT = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _lightRTFringe = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);   // fringe-layer light map (split only)
        _bloodRT = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss);
        _bloodRTFringe = new RenderTarget2D(GraphicsDevice, Camera.ViewW * ss, Camera.ViewH * ss);   // fringe-layer blood field
        _worldSS = ss;
    }

    /// <summary>Downscales the supersampled world target onto the (black) map area of the letterboxed
    /// frame.  NO fractional slide: the camera — including its sub-pixel position — is baked into the
    /// world target at supersample granularity (the render positions are float), so the world scrolls
    /// smoothly and the camera-centered player lands on an exact pixel (no wobble).  Scale = screen scale
    /// / supersample factor so the target lands exactly on the map area; native (_worldSS==1)
    /// point-samples for crispness, upscaled (>=2) linear-downscales for smooth + no shimmer.
    /// Scissored against <1px float overhang.</summary>
    private void CompositeWorld(Rectangle lb, float heatScrollY)
    {
        float scaleX = lb.Width / (float)RefW;
        float scaleY = lb.Height / (float)RefH;
        int mapX = lb.X, mapY = lb.Y;                  // map viewport sits at reference (0,0)
        int mapW = (int)(Camera.ViewW * scaleX);
        int mapH = (int)(Camera.ViewH * scaleY);

        var pos = new Vector2(mapX, mapY);
        var scale = new Vector2(scaleX / _worldSS, scaleY / _worldSS);
        var sampler = _worldSS <= 1 ? SamplerState.PointClamp : SamplerState.LinearClamp;

        // Heat Wave distorts the world composite with a rising-ripple shader (daytime effect; combat glows
        // and the UI draw in later passes and stay crisp). No-op for every other weather state. Suppressed
        // on indoor maps, matching the particle weather (rain/snow/wind) that stops at the door.
        Effect? heat = null;
        if (_state.Weather == WeatherType.HeatWave && !_state.IndoorsOf(_state.Map) && _heatEffect is not null)
        {
            _heatEffect.Parameters["Time"]?.SetValue(_totalTimeSeconds);
            _heatEffect.Parameters["Intensity"]?.SetValue(HeatIntensity);
            _heatEffect.Parameters["ScrollY"]?.SetValue(heatScrollY);
            heat = _heatEffect;
        }

        var prevScissor = GraphicsDevice.ScissorRectangle;
        GraphicsDevice.ScissorRectangle = new Rectangle(mapX, mapY, mapW, mapH);
        _sb!.Begin(SpriteSortMode.Deferred, BlendState.Opaque, sampler, null, WorldCompositeRaster, heat);
        _sb.Draw(_worldRT!, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        _sb.End();
        GraphicsDevice.ScissorRectangle = prevScissor;
    }

    /// <summary>The 4:3 reference frame's on-screen rectangle, pillar/letterboxed inside the backbuffer.
    /// Returns the full backbuffer when the maintain-aspect option is off (stretch to fill).</summary>
    private Rectangle GetLetterbox()
    {
        int bbW = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int bbH = GraphicsDevice.PresentationParameters.BackBufferHeight;
        if (!_maintainAspectRatio) return new Rectangle(0, 0, bbW, bbH);
        float aspect = (float)RefW / RefH;  // 4:3
        int w, h, x, y;
        if ((float)bbW / bbH > aspect)
        {
            h = bbH;
            w = (int)(h * aspect);
            x = (bbW - w) / 2;
            y = 0;
        }
        else
        {
            w = bbW;
            h = (int)(w / aspect);
            x = 0;
            y = (bbH - h) / 2;
        }
        return new Rectangle(x, y, w, h);
    }
}

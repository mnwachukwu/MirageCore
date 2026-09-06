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

/// <summary>Building and drawing the scrolling world into the supersampled render target: ground,
/// fringe, weather and overlay passes, the entity groups and name/bar overlays, and the camera
/// projection from a map tile to a screen pixel that hover and floating text both read.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── World draw ────────────────────────────────────────────────────────────────────────────────────
    // Renders the scrolling world (tiles, entities, blood, particles, names, bars, floating text) into a
    // SUPERSAMPLED world target (WorldSS× the 512×384 viewport, via the transform) that MirageGame later
    // linear-downscales to the screen offset by the camera's sub-pixel fraction — smooth scrolling, no shimmer,
    // still crisp (commands are reference-pixel; the transform scales up, PointClamp keeps tiles/sprites sharp).
    // BuildWorldFrame runs once (before any target is bound); the ground / fringe / overlay passes each manage
    // their own batch so MirageGame can drive them into one target (flat) or split targets (night-on-a-bridge).

    // Two-layer world light split: BuildWorldFrame stashes the per-frame blood state for the DrawWorld* passes,
    // which MirageGame drives either into one target (flat / daylight) or into split ground/fringe targets
    // (night on a bridge). Set once per frame in BuildWorldFrame; read by DrawWorldGround / DrawWorldFringe.
    private bool _haveGroundBlood, _haveFringeBlood;
    private RenderTarget2D? _frameBloodRT, _frameBloodRTFringe;

    /// <summary>Build this frame's draw commands, append spell-FX lights, and accumulate the two blood fields —
    /// everything that must run ONCE per frame before the world passes. Blood accumulation saves/restores the
    /// currently-bound render target (and no-ops if none is bound), so MirageGame binds a world target before
    /// calling this. Returns whether any FRINGE content is visible (fringe tiles or a fringe-layer entity), which
    /// <see cref="MirageGame"/> uses to gate the two-light-map occlusion split (night-on-a-bridge) against the
    /// single-target path (flat / daylight). Stashes blood state for the DrawWorld* passes.</summary>
    public bool BuildWorldFrame(SpriteBatch sb, SpriteFont font, Matrix transform, RenderTarget2D? bloodRT, RenderTarget2D? bloodRTFringe)
    {
        var hovered = AlwaysShowBars ? default : ComputeHoveredEntity();
        SpriteFont nameFontForBuild = _gameFont ?? font;
        RenderCommandBuilder.Build(_ctx.State, _renderFrame, _camera, AlwaysShowBars, hovered,
            targetEntity: _tabTarget,
            showNpcNames: _showNpcNames,
            showOtherPlayerNames: _showOtherPlayerNames,
            showPlayerName: _showPlayerName,
            myIndex: _ctx.State.MyIndex,
            nameLineH: nameFontForBuild.LineSpacing,
            showCooldownBar: _showCooldownBar,
            showOtherCooldownBars: _showOtherCooldownBars);

        // Append spell-FX lights/glows to the freshly-built frame BEFORE the light pass consumes it.
        EmitParticleLights();

        // Blood metaball: accumulate each layer's pools into its own offscreen field with MAX blend (overlapping
        // blobs form a smooth UNION). Runs first because it swaps render targets. Stashed for the passes below.
        _frameBloodRT = bloodRT;
        _frameBloodRTFringe = bloodRTFringe;
        _haveGroundBlood = _showBlood && bloodRT is not null && _renderFrame.Blood.Count > 0
                           && AccumulateBloodField(sb, bloodRT!, transform, WorldLayer.Ground);
        _haveFringeBlood = _showBlood && bloodRTFringe is not null && _renderFrame.Blood.Count > 0
                           && AccumulateBloodField(sb, bloodRTFringe!, transform, WorldLayer.Fringe);

        // Any fringe content visible this frame? A fringe tile (deck/décor) or a fringe-layer entity/item/corpse.
        // Under the occlusion model the fringe plane is lit by fringe lights only, so any fringe content wants the
        // split (a ground light still reaches the ground beneath via the all-lights ground map).
        foreach (var layer in _renderFrame.Above)
            if (layer.Count > 0) return true;
        foreach (var c in _renderFrame.Npcs) if (c.Layer == WorldLayer.Fringe) return true;
        foreach (var c in _renderFrame.Players) if (c.Layer == WorldLayer.Fringe) return true;
        foreach (var c in _renderFrame.Items) if (c.Layer == WorldLayer.Fringe) return true;
        foreach (var c in _renderFrame.Corpses) if (c.Layer == WorldLayer.Fringe) return true;
        return false;
    }

    // Two-layer ("bridge") world: draw the entity group for ONE logical layer — corpses, corpse names, contest
    // points, items, NPCs, players carrying that layer. Delayed-death sprites (no layer tag) draw with the ground
    // group. Extracted from DrawWorld so the single-target path and the split ground/fringe targets share it.
    private void DrawEntityGroup(SpriteBatch sb, WorldLayer group, SpriteFont nameFont, float nameCellW, float nameLineH)
    {
        // Corpses: a tile-sized (32x32) red X where each dead player fell — a body on the ground,
        // drawn ABOVE blood but UNDER items (so dropped loot stays visible/lootable) and UNDER the living
        // entities. A dark outline underneath keeps the red X readable even over a same-red blood pool.
        foreach (var c in _renderFrame.Corpses)
        {
            if (c.Layer != group) continue;
            var tl = new Vector2(c.ScreenX, c.ScreenY);
            var tr = new Vector2(c.ScreenX + Constants.PicX, c.ScreenY);
            var bl = new Vector2(c.ScreenX, c.ScreenY + Constants.PicY);
            var br = new Vector2(c.ScreenX + Constants.PicX, c.ScreenY + Constants.PicY);
            UiHelper.DrawLine(sb, tl, br, Color.Black, 5f);
            UiHelper.DrawLine(sb, tr, bl, Color.Black, 5f);
            UiHelper.DrawLine(sb, tl, br, Color.Red, 3f);
            UiHelper.DrawLine(sb, tr, bl, Color.Red, 3f);
        }
        // Corpse names live in the world layer with the red X — below items/NPCs/players — so a live entity
        // walking over the tile draws on top of the name instead of the name floating above everyone.
        foreach (var cmd in _renderFrame.CorpseNames)
            if (cmd.Layer == group) DrawWorldName(sb, nameFont, cmd, nameCellW, nameLineH);

        // Territory-contest capture points: radius circle + triangular flag + name, in the world
        // layer (walk-over-able) so entities draw over them. Participant-only (frame.ContestPoints empty else).
        foreach (var cp in _renderFrame.ContestPoints)
            if (cp.Layer == group) DrawContestPoint(sb, nameFont, cp, nameCellW, nameLineH);

        if (_items is not null)
        {
            foreach (var cmd in _renderFrame.Items)
                if (cmd.Layer == group) DrawItem(sb, cmd);
        }

        if (_sprites is not null)
        {
            foreach (var cmd in _renderFrame.Npcs) if (cmd.Layer == group) DrawSprite(sb, cmd);
            foreach (var cmd in _renderFrame.Players) if (cmd.Layer == group) DrawSprite(sb, cmd);
            // Delayed-death: killed sprites held in place until a killing spell bolt lands (hit-timing
            // deferral). Untagged — drawn with the ground group.
            if (group == WorldLayer.Ground)
            {
                for (int i = 0; i < _delayedDeaths.Count; i++)
                {
                    var g = _delayedDeaths[i];
                    DrawSprite(sb, new SpriteDrawCmd(g.WorldX - _camera.CameraX, g.WorldY - _camera.CameraY, g.SpriteRow, 0, g.Dir, g.Size));
                }
            }
        }
    }

    /// <summary>Ground pass: the ground tile stack, ground blood, and the ground-layer entity group with its
    /// spell/combat particles. Drawn into whatever target is bound — the single world target (flat path) or the
    /// split ground target.</summary>
    public void DrawWorldGround(SpriteBatch sb, SpriteFont font, Matrix transform)
    {
        SpriteFont nameFont = _gameFont ?? font;
        float nameCellW = nameFont.MeasureString("A").X;
        float nameLineH = nameFont.LineSpacing;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);

        // Ground layer stack (below entities), layer-major so each layer batches together.
        foreach (var layer in _renderFrame.Below)
            foreach (var cmd in layer) DrawTile(sb, cmd);

        // Ground blood: composite the merged ground field (tinted) below the ground entities.
        if (_haveGroundBlood) CompositeBloodField(sb, _frameBloodRT!);

        // Ground-layer entities (under the bridge surface), then their spell/combat particles — so a ground
        // burst is occluded by the bridge deck above.
        DrawEntityGroup(sb, WorldLayer.Ground, nameFont, nameCellW, nameLineH);
        DrawLayerParticles(sb, WorldLayer.Ground);

        sb.End();
    }

    /// <summary>Fringe pass: the fringe tile stack (bridge surface), fringe blood, the fringe-layer entity group
    /// and its particles, the canopy stack (over everything), then GLOBAL weather. Drawn into the single world
    /// target (flat path) or the TRANSPARENT split fringe target, so a grate/edge gap keeps its alpha and the
    /// ground shows through beneath it.</summary>
    public void DrawWorldFringe(SpriteBatch sb, SpriteFont font, Matrix transform, bool includeWeather = true)
    {
        SpriteFont nameFont = _gameFont ?? font;
        float nameCellW = nameFont.MeasureString("A").X;
        float nameLineH = nameFont.LineSpacing;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);

        // Fringe tile stack — the bridge surface / over-player décor, between the ground- and fringe-layer entity
        // passes. Lit by the fringe light map (fringe lights only) so ground light is occluded by the fringe.
        foreach (var layer in _renderFrame.Above)
            foreach (var cmd in layer) DrawTile(sb, cmd);

        // Fringe (bridge-top) blood: composite ON the deck, below the fringe entities.
        if (_haveFringeBlood) CompositeBloodField(sb, _frameBloodRTFringe!);

        // Fringe-layer entities (on the bridge), then their spell/combat particles — so a bridge-top burst draws
        // over the deck.
        DrawEntityGroup(sb, WorldLayer.Fringe, nameFont, nameCellW, nameLineH);
        DrawLayerParticles(sb, WorldLayer.Fringe);

        // Canopy tile stack — décor over EVERYTHING (treetops/roofs), so a bridge walker passes under it.
        foreach (var layer in _renderFrame.Canopy)
            foreach (var cmd in layer) DrawTile(sb, cmd);

        // Weather particles: GLOBAL — world-anchored above everything, below names. Combat/spell particles were
        // already drawn in their layer's pass (so they occlude with the bridge). On the SPLIT (two-light) path the
        // caller draws weather in its OWN pass (DrawWorldWeather) lit by the ALL-lights map — if it rode this fringe
        // target it would only see the fringe light map, so a ground torch couldn't brighten the snow above it.
        if (includeWeather) DrawWeatherParticles(sb);

        sb.End();
    }

    /// <summary>Self-batched GLOBAL weather draw for the split path's lit-weather pass. Weather falls over BOTH
    /// planes, so the split path lights it by the ALL-lights map (not the fringe-only map). The flat path draws
    /// weather inline in <see cref="DrawWorldFringe"/> instead (single world target, lit by the whole-view multiply).</summary>
    public void DrawWorldWeather(SpriteBatch sb, Matrix transform)
    {
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        DrawWeatherParticles(sb);
        sb.End();
    }

    /// <summary>Overlay pass: the /debug cell outline, then the floating names / bars / target arrows / chat
    /// bubbles / floating combat text — always drawn OVER (never occluded by) the composited world.
    /// <paramref name="nameLayer"/> filters the layer-tagged names/bars to ONE logical layer (null = all), and
    /// <paramref name="includeExtras"/> gates the non-layer-tagged extras (debug box / target arrow / chat
    /// bubbles / floating text). The two-light-map split calls this TWICE into a scratch target that MirageGame
    /// then lights and composites on top: once for GROUND labels + the extras (lit by ALL lights) and once for
    /// FRINGE labels (lit by fringe lights only). The flat / daylight path calls it once with the defaults (all
    /// labels + extras).</summary>
    public void DrawWorldOverlay(SpriteBatch sb, SpriteFont font, Matrix transform, WorldLayer? nameLayer = null, bool includeExtras = true)
    {
        SpriteFont nameFont = _gameFont ?? font;
        float nameCellW = nameFont.MeasureString("A").X;
        float nameLineH = nameFont.LineSpacing;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);

        // /debug — Mapper+ overlay: white outline around the center cell (the map the local player currently
        // occupies in the 3×3 observable area). The camera follows the player so the box slides as they walk.
        if (_debugOverlay && includeExtras)
        {
            var (dbgX, dbgY) = _camera.WorldTileToScreen(
                _ctx.State.MapTilesX, _ctx.State.MapTilesY, 0f, 0f);
            int cellW = _ctx.State.MapTilesX * Constants.PicX;
            int cellH = _ctx.State.MapTilesY * Constants.PicY;
            UiHelper.DrawBorder(sb, dbgX, dbgY, cellW, cellH, Color.White);
        }

        foreach (var cmd in _renderFrame.Names)
        {
            if (nameLayer is not null && cmd.Layer != nameLayer) continue;   // split: this layer's labels only
            DrawWorldName(sb, nameFont, cmd, nameCellW, nameLineH);
        }

        const int baseBarW = 28;
        foreach (var cmd in _renderFrame.Bars)
        {
            if (nameLayer is not null && cmd.Layer != nameLayer) continue;   // split: this layer's bars only
            int barW = baseBarW * cmd.Size;   // 2x/3x wide for size-2/3 NPCs, centered over the footprint
            // Combat border is 2 px outside the bar; the regular outline is 1 px. Pad by the wider
            // of the two so an entity at the viewport edge keeps its border visible too.
            int barBorderPad = cmd.ShowCombatBorder ? 2 : 1;
            float bx = cmd.CenterX - barW / 2f;
            bx = Math.Clamp(bx, barBorderPad, Camera.ViewW - barW - barBorderPad);
            float by = cmd.TopY;
            // The cooldown bar (when present) is the group's bottom row, inside the one shared outline.
            int rows = (cmd.HpFrac >= 0 ? 1 : 0) + (cmd.MpFrac >= 0 ? 1 : 0) + (cmd.SpFrac >= 0 ? 1 : 0) + (cmd.CdFrac >= 0 ? 1 : 0);
            int actualH = rows * InWorldBarH;
            if (actualH > 0)
            {
                if (cmd.ShowCombatBorder)
                    UiHelper.DrawBorder(sb, bx - 2, by - 2, barW + 4, actualH + 4, UiHelper.WorldBarCombatColor, 2);
                else if (cmd.IsTarget)
                    UiHelper.DrawBorder(sb, bx - 1, by - 1, barW + 2, actualH + 2, cmd.OutOfRange ? Color.Gray : Color.Cyan);
                else
                    UiHelper.DrawBorder(sb, bx - 1, by - 1, barW + 2, actualH + 2, Color.White);
            }
            DrawBar(sb, bx, by, barW, InWorldBarH, cmd.HpFrac, UiHelper.VitalHpColor);
            if (cmd.HpFrac >= 0) by += InWorldBarH;
            if (cmd.MpFrac >= 0)
            {
                DrawBar(sb, bx, by, barW, InWorldBarH, cmd.MpFrac, UiHelper.VitalMpColor);
                by += InWorldBarH;
            }
            if (cmd.SpFrac >= 0)
            {
                DrawBar(sb, bx, by, barW, InWorldBarH, cmd.SpFrac, UiHelper.VitalSpColor);
                by += InWorldBarH;
            }
            if (cmd.CdFrac >= 0)
                DrawBar(sb, bx, by, barW, InWorldBarH, cmd.CdFrac, UiHelper.CooldownBarColor);
        }

        if (includeExtras && _renderFrame.TargetArrows.Count > 0)
        {
            float hover = (float)Math.Sin(Environment.TickCount64 / TargetArrowHoverPeriodMs) * TargetArrowHoverAmplitude;
            // Edge clamp on the centerX so the full arrow stays inside the viewport when the entity
            // is near a horizontal edge. Shadow extends 1 px right; account for it in the right clamp.
            float arrowHalfW = TargetArrowW / 2f;
            float arrowMinX = arrowHalfW;
            float arrowMaxX = Camera.ViewW - arrowHalfW - 1;
            foreach (var a in _renderFrame.TargetArrows)
            {
                // Gray out the arrow when the cast cannot land — either out of range or a wall /
                // closed door breaks the line of sight (RenderCommandBuilder mirrors the server gate).
                Color arrowColor = (a.OutOfRange || a.NoLineOfSight) ? Color.Gray : Color.Red;
                float arrowX = Math.Clamp(a.CenterX, arrowMinX, arrowMaxX);
                if (a.NameAlignBottom)
                {
                    // Labels above sprite: arrow above the name, pointing down toward entity.
                    float tipY = a.NameY - nameLineH - TargetArrowGap + hover;
                    DrawDownArrow(sb, arrowX + 1, tipY + 1, TargetArrowW, TargetArrowH, Color.Black);
                    DrawDownArrow(sb, arrowX, tipY, TargetArrowW, TargetArrowH, arrowColor);
                }
                else
                {
                    // Labels below sprite: arrow below the name, pointing up toward entity.
                    float tipY = a.NameY + nameLineH + TargetArrowGap + hover;
                    DrawUpArrow(sb, arrowX + 1, tipY + 1, TargetArrowW, TargetArrowH, Color.Black);
                    DrawUpArrow(sb, arrowX, tipY, TargetArrowW, TargetArrowH, arrowColor);
                }
            }
        }

        // Chat bubbles — drawn before floating combat text so fresh damage numbers always read clearly
        // on top, even if a bubble happens to overlap them. Each bubble draws shadow + rounded panel +
        // border + word-wrapped white text, all multiplied by Alpha so the whole thing fades as one unit.
        // Uses a dedicated Tahoma font (rendered via SpriteFont, not the fixed-cell name font) so
        // variable-width chat reads naturally instead of like a console.
        if (includeExtras && _renderFrame.ChatBubbles.Count > 0)
            DrawChatBubbles(sb, _bubbleFont ?? nameFont, _renderFrame.ChatBubbles);

        if (includeExtras)
        {
            foreach (var ft in _floatingTexts)
            {
                // (X,Y) is the WORLD pixel where it spawned — convert to screen each frame so it stays pinned
                // to that spot on the gameworld and floats from there, instead of riding the scrolling camera.
                // Floored render-camera (like the rest of the world pass); the fraction is applied at composite.
                float sx = ft.X - _camera.CameraX;
                float anchorY = ft.Y - _camera.CameraY;
                // Subject-visibility gate: a float is pinned to a world SPOT, not an entity, so once that spot
                // scrolls out of view we HIDE it rather than let the edge-clamp below strand it on the border.
                // Recover the source sprite's screen pos from the anchor (exact — every float comes through
                // SpawnFloatingTextAtEntity) and apply the same OnScreen test that culls names/bubbles, so a
                // float appears/vanishes in lockstep with whatever spawned it.
                float spriteX = sx - Constants.PicX / 2f;
                float spriteY = ft.FloatDown ? anchorY - Constants.PicY - FloatTextGapBelow : anchorY + FloatTextGapAbove;
                if (!(spriteX > -Constants.PicX && spriteX < Camera.ViewW
                      && spriteY > -Constants.PicY && spriteY < Camera.ViewH))
                {
                    continue;
                }

                float t = ft.Age / FloatingText.MaxAge;
                byte alpha = (byte)((1f - t) * 255);
                float baseY = anchorY + ft.StackOffset;
                float drawY = ft.FloatDown ? baseY + ft.Age * FloatingTextDriftSpeed : baseY - ft.Age * FloatingTextDriftSpeed;
                var ftColor = new Color(ft.Color.R, ft.Color.G, ft.Color.B, alpha);
                // Horizontal edge clamp (mirrors names/bubbles) — slide the whole string inside the world view.
                // Width is fixed-cell (DrawStringFixed advances one nameCellW per char), so clamp against that.
                float totalW = nameCellW * ft.Text.Length;
                float maxDrawX = Camera.ViewW - totalW;
                if (maxDrawX < 0) maxDrawX = 0;
                float drawX = Math.Clamp(sx - totalW / 2f, 0, maxDrawX);
                DrawStringFixed(sb, nameFont, ft.Text, new Vector2(drawX, drawY), ftColor, nameCellW);
            }
        }

        sb.End();
    }

    /// <summary>Flat / daylight path: draw the whole world into the currently-bound target in the original order
    /// (ground → fringe → overlay). Lighting is applied afterward by <see cref="MirageGame"/>'s single light
    /// multiply. The night-on-a-bridge occlusion split drives the three passes into separate targets instead.
    /// Call <see cref="BuildWorldFrame"/> once (before binding the target) first.</summary>
    public void DrawWorld(SpriteBatch sb, SpriteFont font, Matrix transform)
    {
        DrawWorldGround(sb, font, transform);
        DrawWorldFringe(sb, font, transform);
        DrawWorldOverlay(sb, font, transform);
    }

    /// <summary>The camera's world-pixel Y — the heat shader uses it to anchor its wave to the world so the
    /// shimmer doesn't appear to speed up while moving vertically.</summary>
    public float CameraWorldY => _camera.CameraY;

    /// <summary>Grid cell (col,row) currently holding <paramref name="mapNum"/>, or null if off-grid.</summary>
    private (int col, int row)? GridCellForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        var nums = _ctx.State.NeighborMapNums;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                if (nums[c, r] == mapNum) return (c, r);
        }

        return null;
    }

    /// <summary>
    /// Top-left screen pixel of an entity given its map + local tile + sub-tile offset, via the
    /// camera.  Returns false when that map isn't part of the current 3×3 grid.  Shared by hover
    /// hit-testing and floating combat text so both track the scrolling camera.
    /// </summary>
    public bool TryEntityScreen(int mapNum, int localX, int localY, float xOff, float yOff, out float sx, out float sy)
    {
        sx = sy = 0f;
        var cell = GridCellForMap(mapNum);
        if (cell is null) return false;
        (sx, sy) = _camera.WorldTileToScreen(
            cell.Value.col * _ctx.State.MapTilesX + localX,
            cell.Value.row * _ctx.State.MapTilesY + localY, xOff, yOff);
        return true;
    }
}

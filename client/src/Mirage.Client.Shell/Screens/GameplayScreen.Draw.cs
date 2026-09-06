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

/// <summary>The screen-space pass over the world: the frame's UI draw (background, sidebar, HUD, chat
/// and panels in z-order) and the small primitives (bars, arrows, fixed-cell text) it shares.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Rendering helpers ──────────────────────────────────────────────────────

    // Drawn at the FLOAT screen position (native source size); the DrawWorld supersample transform scales
    // it up, so sub-pixel positions rasterize at supersample granularity → smooth scroll, steady player.
    private void DrawTile(SpriteBatch sb, TileDrawCmd cmd)
    {
        if (cmd.Sheet < 0 || cmd.Sheet >= _tilesets.Length) return;
        var tex = _tilesets[cmd.Sheet];
        if (tex is null) return;
        var src = TileAtlas.GetSourceRect(cmd.Sheet, cmd.TileIndex);
        if (src == Rectangle.Empty) return;
        sb.Draw(tex, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    private void DrawSprite(SpriteBatch sb, SpriteDrawCmd cmd)
    {
        // Pick the size-matched atlas (32/64/96 cell). A larger NPC draws at its native cell size from
        // (ScreenX,ScreenY) - the top-left of its footprint - so no scaling or offset is needed. A missing
        // size sheet (art not added yet) draws nothing; the NPC's bars/name/collision still work.
        // Two independent choices: the size picks which folder's sheets to read, and the sheet number picks
        // one of them. A sheet the install has not got draws nothing, same as a missing size.
        var sizeSet = cmd.Size >= 3 ? _ctx.Sprites96 : cmd.Size == 2 ? _ctx.Sprites64 : _sprites;
        var tex = sizeSet.Sheet(cmd.Sheet);
        if (tex is null) return;
        int cell = cmd.Size * Constants.PicX;
        var src = SpriteAtlas.GetSourceRect(cmd.SpriteRow, cmd.Dir, cmd.AnimFrame, cell);
        sb.Draw(tex, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    private void DrawItem(SpriteBatch sb, ItemDrawCmd cmd)
    {
        var src = ItemAtlas.GetSourceRect(cmd.Pic);
        if (src == Rectangle.Empty) return;
        var tex = _items.Sheet(cmd.Sheet);
        if (tex is null) return;
        sb.Draw(tex, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    // Reused across all DrawStringFixed calls; avoids one string allocation per character.
    private static readonly StringBuilder _charBuf = new(4);

    // Each character advances by a fixed cell width so that space characters (whose advance
    // MonoGame may zero-out for bitmap fonts) still create a visible gap.
    // Draw one world-anchored name label (the floating overhead names AND the demoted corpse names share
    // this): fixed-cell text with a black drop shadow, a raw-RGB override winning over the palette index,
    // LineOffset stacking by real font height, and an edge clamp so a name near the viewport border slides
    // inward instead of getting scissor-clipped.
    // Capture-point flag geometry (px), drawn within one 32x32 tile.
    private const float ContestFlagPoleInset = 6f;     // pole x, left of the tile center
    private const float ContestFlagMargin = 3f;        // pole top/bottom inset from the tile edges
    private const float ContestFlagPennantW = 14f;     // pennant reach to the right of the pole
    private const float ContestFlagPennantApexY = 5f;  // pennant apex y, below the pole top
    private const float ContestFlagPennantBotY = 10f;  // pennant base y, below the pole top
    private const float ContestLabelGap = 2f;          // gap between the flag top and the name label
    private const float ContestZoneEdgeAlpha = 0.85f;  // the staircase boundary, the line that answers "in or out"
    private const float ContestEdgeMargin = 6f;        // how far an off-screen marker sits inside the viewport edge
    private const float ContestZoneEdgeThickness = 1.5f;

    // Draw one contest capture point in the world layer: the capture zone, a small triangular flag, and the
    // point's name above it, all in the per-viewer control color. Walk-over-able (drawn under the entities).
    private static void DrawContestPoint(SpriteBatch sb, SpriteFont nameFont, ContestPointCmd cp, float nameCellW, float nameLineH)
    {
        Color color = cp.Control switch
        {
            ContestControl.Own => UiHelper.ContestOwnColor,
            ContestControl.Enemy => UiHelper.ContestEnemyColor,
            _ => UiHelper.ContestNeutralColor,
        };

        // 🔴 The zone is drawn whenever it REACHES the view, including from a point standing outside it. A
        // point near the border holds ground you can be standing on, and the ring is the only thing saying
        // where that ground ends — cutting it because the flag's own tile left the viewport takes the mark
        // off ground that is still being scored.
        float reach = (Constants.TerritoryCapturePointRadius + 1) * Constants.PicX;
        if (cp.ScreenX > -reach && cp.ScreenX < Camera.ViewW + reach
            && cp.ScreenY > -reach && cp.ScreenY < Camera.ViewH + reach)
        {
            DrawContestZone(sb, cp, color);
        }

        // Past the viewport the point is a BEARING: the label alone, pinned to the edge it lies past, with
        // the other axis left where the point really is. Walk that way on that axis and the label slides to
        // the corner, then onto the screen with the flag under it. The FLAG is skipped because it is a
        // 32px mark on a tile nobody can see; the zone above is not, because it is not on that tile.
        if (cp.OffScreen)
        {
            float totalW = nameCellW * cp.Label.Length;
            var edgeCmd = new TextDrawCmd(
                Math.Clamp(cp.ScreenX + Constants.PicX / 2f, totalW / 2f + ContestEdgeMargin,
                           Camera.ViewW - totalW / 2f - ContestEdgeMargin),
                Math.Clamp(cp.ScreenY, ContestEdgeMargin + nameLineH, Camera.ViewH - ContestEdgeMargin),
                cp.Label, 0, AlignBottom: true,
                RgbOverride: (color.R << 16) | (color.G << 8) | color.B);
            DrawWorldName(sb, nameFont, edgeCmd, nameCellW, nameLineH);
            return;
        }

        // Flag: a dark pole + a colored triangular pennant near its top, centered on the tile.
        float poleX = cp.ScreenX + Constants.PicX / 2f - ContestFlagPoleInset;
        float poleTop = cp.ScreenY + ContestFlagMargin;
        float poleBot = cp.ScreenY + Constants.PicY - ContestFlagMargin;
        UiHelper.DrawLine(sb, new Vector2(poleX, poleTop), new Vector2(poleX, poleBot), Color.Black, 2f);
        var apexA = new Vector2(poleX, poleTop);
        var apexB = new Vector2(poleX + ContestFlagPennantW, poleTop + ContestFlagPennantApexY);
        var apexC = new Vector2(poleX, poleTop + ContestFlagPennantBotY);
        UiHelper.FillTriangle(sb, apexA, apexB, apexC, color);
        UiHelper.DrawLine(sb, apexA, apexB, Color.Black, 1f);
        UiHelper.DrawLine(sb, apexB, apexC, Color.Black, 1f);

        // Name above the flag, colored to match via RgbOverride; drawn in the world layer like a corpse name.
        int rgb = (color.R << 16) | (color.G << 8) | color.B;
        var labelCmd = new TextDrawCmd(cp.ScreenX + Constants.PicX / 2f, poleTop - ContestLabelGap, cp.Label,
            0, AlignBottom: true, RgbOverride: rgb);
        DrawWorldName(sb, nameFont, labelCmd, nameCellW, nameLineH);
    }

    /// <summary>The capture zone's boundary: the line between the tiles that score and the tiles that do not.
    ///
    /// <para>Only the edge is drawn. The interior needs no marking — the flag light already washes exactly
    /// this ground in a pale cyan/pink, and the flag and label name the point — so between them the zone is
    /// found at a glance and this line answers the only question that is ever close: which side of it a
    /// particular tile is on.</para>
    ///
    /// <para>🔴 The boundary is a STAIRCASE, never a circle. <see cref="TerritoryContestFormulas.WithinRadius"/>
    /// asks whether a tile's CENTER is within the radius, so scoring is decided a whole tile at a time and the
    /// edge of the scoring set runs along tile sides. Drawing it as a smooth curve puts the line through the
    /// middle of tiles, where it disagrees with the rule in both directions at once — a tile scoring with half
    /// of it outside the line, and a tile the line crosses scoring nothing. Drawn as the staircase, "inside
    /// the line" and "this tile scores" are one statement.</para>
    ///
    /// <para>The flag color, not the flag LIGHT's color: the light is a pale wash of a neighboring hue over
    /// the same ground, so the two stay separable by saturation.</para></summary>
    private static void DrawContestZone(SpriteBatch sb, ContestPointCmd cp, Color color)
    {
        var origin = new Vector2(cp.ScreenX, cp.ScreenY);
        foreach (var (a, b) in ContestZoneEdges(Constants.TerritoryCapturePointRadius))
            UiHelper.DrawLine(sb, origin + a, origin + b, color * ContestZoneEdgeAlpha, ContestZoneEdgeThickness);
    }

    // The boundary segments, in pixels relative to the point tile's own origin. Fixed for a given radius, so
    // it is built once rather than per point per frame.
    private static List<(Vector2 A, Vector2 B)> ContestZoneEdges(int radius)
    {
        if (_contestZoneRadius == radius) return _contestZoneEdges;

        _contestZoneEdges.Clear();
        bool Scores(int dx, int dy) => TerritoryContestFormulas.WithinRadius(dx, dy, 0, 0, radius);

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!Scores(dx, dy)) continue;

                // A side is on the boundary when the tile across it does not score. Corners need no special
                // case: each is where two of these segments meet.
                float x0 = dx * Constants.PicX, y0 = dy * Constants.PicY;
                float x1 = x0 + Constants.PicX, y1 = y0 + Constants.PicY;
                if (!Scores(dx - 1, dy)) _contestZoneEdges.Add((new Vector2(x0, y0), new Vector2(x0, y1)));
                if (!Scores(dx + 1, dy)) _contestZoneEdges.Add((new Vector2(x1, y0), new Vector2(x1, y1)));
                if (!Scores(dx, dy - 1)) _contestZoneEdges.Add((new Vector2(x0, y0), new Vector2(x1, y0)));
                if (!Scores(dx, dy + 1)) _contestZoneEdges.Add((new Vector2(x0, y1), new Vector2(x1, y1)));
            }
        }

        _contestZoneRadius = radius;
        return _contestZoneEdges;
    }

    private static int _contestZoneRadius = -1;
    private static readonly List<(Vector2 A, Vector2 B)> _contestZoneEdges = [];

    private static void DrawWorldName(SpriteBatch sb, SpriteFont nameFont, TextDrawCmd cmd, float nameCellW, float nameLineH)
    {
        // The overhead guild line carries the guild name in Text plus (optionally) a numeric rank + standing to
        // append: "<Guild> {Rank} ({Standing})". Localize the rank word + assemble here (the logic layer that
        // built the command has no string table).
        string text = cmd.Text;
        if (cmd.GuildRankWord > 0) text += " " + OverheadRankWord((GuildRank)cmd.GuildRankWord);
        if (cmd.GuildStanding > 0) text += " (" + cmd.GuildStanding + ")";
        Color nameColor = cmd.RgbOverride >= 0
            ? new Color(GameColor.RedOf(cmd.RgbOverride), GameColor.GreenOf(cmd.RgbOverride), GameColor.BlueOf(cmd.RgbOverride))
            : ChatPanel.GetColor(cmd.ColorIndex);
        float totalW = nameCellW * text.Length;
        // AlignBottom (labels ABOVE the sprite): the name's bottom sits at ScreenY and extra lines ($/?/.../guild)
        // stack UPWARD. Flipped BELOW the sprite (AlignBottom=false — NPC/player near the top of the view): the name
        // is TOP-aligned at ScreenY, so extra lines must stack DOWNWARD below it. A LineOffset that always
        // subtracts rides below-sprite markers back up into the sprite, overlapping the name.
        float drawY = cmd.AlignBottom
            ? cmd.ScreenY - nameLineH - cmd.LineOffset * nameLineH
            : cmd.ScreenY + cmd.LineOffset * nameLineH;
        float nameX = cmd.ScreenX - totalW / 2f;
        float maxNameX = Camera.ViewW - totalW;
        if (maxNameX < 0) maxNameX = 0;
        nameX = Math.Clamp(nameX, 0, maxNameX);
        var namePos = new Vector2(nameX, drawY);
        // Crossed-swords "at war" marker to the left of the guild name (plan: identify guilds you're at war with).
        if (cmd.AtWar) DrawCrossedSwords(sb, nameX, drawY, nameLineH, nameColor);
        DrawStringFixed(sb, nameFont, text, namePos + new Vector2(2, 2), Color.Black, nameCellW);
        DrawStringFixed(sb, nameFont, text, namePos + new Vector2(1, 1), Color.Black, nameCellW);
        DrawStringFixed(sb, nameFont, text, namePos, nameColor, nameCellW);
    }

    // The localized overhead rank word (Officer / Leader) for the overhead rank line.
    private static string OverheadRankWord(GuildRank rank) => ClientStrings.Get(rank switch
    {
        GuildRank.Leader => ClientStrings.SocialPanel_RankLeader,
        GuildRank.Officer => ClientStrings.SocialPanel_RankOfficer,
        _ => ClientStrings.SocialPanel_RankMember,
    });

    // An improvised crossed-swords glyph (two blades + a crossguard bar) drawn just left of the guild name,
    // in the guild's color, when the viewer is at war with that guild. A black shadow underlay keeps it
    // legible over any terrain, matching the name text's outline.
    private static void DrawCrossedSwords(SpriteBatch sb, float textLeftX, float topY, float lineH, Color color)
    {
        float sz = Math.Max(6f, lineH - 4f);
        float x = textLeftX - sz - 2f;   // sits just left of the text's left edge
        float y = topY + 2f;
        void Swords(Vector2 off, Color c)
        {
            UiHelper.DrawLine(sb, new Vector2(x, y + sz) + off, new Vector2(x + sz, y) + off, c, 2f);          // "/" blade
            UiHelper.DrawLine(sb, new Vector2(x, y) + off, new Vector2(x + sz, y + sz) + off, c, 2f);          // "\" blade
            UiHelper.DrawLine(sb, new Vector2(x + 1, y + sz - 3) + off, new Vector2(x + sz - 1, y + sz - 3) + off, c, 1f); // crossguard
        }
        Swords(new Vector2(1, 1), Color.Black);   // shadow
        Swords(Vector2.Zero, color);
    }

    private static void DrawStringFixed(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, Color color, float cellW)
    {
        var cur = pos;
        foreach (char c in text)
        {
            if (c != ' ')
            {
                _charBuf.Clear();
                _charBuf.Append(c);
                sb.DrawString(font, _charBuf, cur, color);
            }
            cur.X += cellW;
        }
    }

    // Downward-pointing filled triangle: widest row at top, 1px tip at tipY.
    // centerX/tipY are float (track the entity sub-pixel); w/h are the genuine pixel dimensions of the
    // triangle, and rowW is its per-row pixel width — only the half-width CENTER offset is float (/2f).
    private void DrawDownArrow(SpriteBatch sb, float centerX, float tipY, int w, int h, Color color)
    {
        for (int row = 0; row < h; row++)
        {
            int rowW = Math.Max(1, w - 2 * row);
            UiHelper.DrawFilledRect(sb, centerX - rowW / 2f, tipY - (h - 1) + row, rowW, 1, color);
        }
    }

    // Upward-pointing filled triangle: 1px tip at tipY (top), widest row at bottom.
    private void DrawUpArrow(SpriteBatch sb, float centerX, float tipY, int w, int h, Color color)
    {
        for (int row = 0; row < h; row++)
        {
            int rowW = Math.Max(1, w - 2 * (h - 1 - row));
            UiHelper.DrawFilledRect(sb, centerX - rowW / 2f, tipY + row, rowW, 1, color);
        }
    }

    private void DrawBar(SpriteBatch sb, float x, float y, float w, float h, float frac, Color fill)
    {
        if (frac < 0) return;
        UiHelper.DrawFilledRect(sb, x, y, w, h, UiHelper.BarBg);
        if (frac > 0)
            UiHelper.DrawFilledRect(sb, x, y, w * frac, h, fill);
    }

    /// <summary>
    /// Draws the UI (background, sidebar, HUD, chat, panels) into the main reference target, inside the
    /// batch MirageGame already opened.  The scrolling world is drawn separately by
    /// <see cref="DrawWorld"/> into its own target and composited over the (black) map area afterward.
    /// </summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long nowMs = Environment.TickCount64;
        // Black background for the UI region ONLY — leave the map viewport (0,0,ViewW,ViewH) TRANSPARENT
        // so the world composite shows through underneath it, with panels drawn on top (these two rects
        // are the whole reference frame minus the map area).
        UiHelper.DrawFilledRect(sb, new Rectangle(0, Camera.ViewH, UiHelper.RefW, UiHelper.RefH - Camera.ViewH), Color.Black);
        UiHelper.DrawFilledRect(sb, new Rectangle(Camera.ViewW, 0, UiHelper.RefW - Camera.ViewW, Camera.ViewH), Color.Black);

        // Sidebar background (right column, separated from the map viewport area).
        UiHelper.DrawFilledRect(sb, new Rectangle(Camera.ViewW, 0, UiHelper.RefW - Camera.ViewW, UiHelper.RefH), UiHelper.BarBg);

        // Find the topmost open panel under the mouse BEFORE any UI draws. Widgets below it
        // (HUD buttons, vital bars, sidebar links, chat hyperlinks, lower-z panels) must not
        // hover-highlight or request a cursor — the mouse is visually over the panel, not them.
        // The topmost-under-mouse panel resets hover around its own Draw so its widgets work.
        int topUnderMouse = -1;
        var mpos = _lastInput.MousePosition;
        for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
        {
            int idx = _zOrder[zi];
            if (PanelIsOpen(idx) && PanelContainsMouse(idx, mpos))
            {
                topUnderMouse = idx;
                break;
            }
        }
        bool mouseOverPanel = topUnderMouse >= 0;
        if (mouseOverPanel) _lastInput.ConsumeMouseHover();

        _hud.Draw(sb, font, _ctx.TitleFont ?? font, _ctx.State, _lastInput);
        _partyOverlay.Draw(sb, font, _ctx.State, _lastInput, _tabTarget, nowMs);
        _contestHud.Draw(sb, font, _ctx.State);
        _chat.Draw(sb, font, nowMs);

        // Sidebar [Options (O)] / [Help (H)] links — drawn BEFORE the panel z-order so any
        // open panel (or tooltip) overlapping the bottom-right link strip renders on top of them,
        // matching the user expectation that floating panels always win over background UI.
        // Mail is drawn first (leftmost) and tinted gold while there is unread mail, so the inbox
        // announces itself without a separate badge; Options/Help keep their default gray idle color.
        HudPanel.MailLink.IdleColor = _ctx.State.UnreadMailCount() > 0 ? Color.Gold : Color.Gray;
        HudPanel.MailLink.Draw(sb, font, _lastInput);
        HudPanel.OptionsLinkInGame.Draw(sb, font, _lastInput);
        HudPanel.HelpLink.Draw(sb, font, _lastInput);

        // The action bar sits directly above those links and belongs to the same background layer, so it
        // draws here — before the panel z-order — and an open window covers it like any other chrome.
        // Its hover tooltip is suppressed whenever a panel is under the mouse, for the same reason.
        HotkeyBarPanel.Draw(sb, font, _ctx.State, _items, hk => HotkeyCooldownFraction(hk, nowMs),
            _lastInput.ShowGamepadPrompts, _lastInput, canHover: topUnderMouse < 0);

        DrawFrameReadout(sb, font, nowMs);

        // The single tooltip is fed by panel Draws; only the topmost open panel under the mouse
        // may notify it this frame, so a hovered row in a panel hidden behind another panel
        // doesn't leak its tooltip through the occluding window above it.
        foreach (int idx in _zOrder)
        {
            if (idx == topUnderMouse) _lastInput.ResetMouseHover();
            DrawPanel(idx, sb, font, nowMs, idx == _activePanel, idx == topUnderMouse);
            if (idx == topUnderMouse) _lastInput.ConsumeMouseHover();
        }

        // Tooltip floats above every panel — panels call Tooltip.NotifyHover* during their draws
        // and this single global tick decides whether to render, where (pinned), and when to hide.
        Tooltip.TickAndDraw(sb, font, nowMs, _lastInput.MousePosition);

        // Chat tab options panel draws above the chat panel but below the context menu.
        _chatOptions.Draw(sb, font, _lastInput);

        // Context menu draws LAST so it overlays every panel, the tooltip, and the world.
        _contextMenu.Draw(sb, font);

        // Death overlay is the true top layer while the local player is dead (a full-screen modal).
        _death.Draw(sb, font, _lastInput, _ctx.State);
    }
}

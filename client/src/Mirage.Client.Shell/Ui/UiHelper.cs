using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Rendering;
using Mirage.Shared;
using Mirage.Shared.Records;
using XnaMouse = Microsoft.Xna.Framework.Input.Mouse;

namespace Mirage.Client.Shell.Ui;

public static class UiHelper
{
    private static Texture2D? _pixel;

    // ── Requirement label formatting ───────────────────────────────────────────
    /// <summary>Render an equip/learn requirement with the class head-start made visible: "27 (-3)"
    /// when the wearer's class shaves points off the raw requirement, or plain "27" when it doesn't.
    /// <paramref name="rawReq"/> is the item/spell's authored requirement (an item's Power, a spell's
    /// VitalAmount or IntReq); <paramref name="effectiveReq"/> is it after the head-start and floor —
    /// i.e. what the player actually needs.</summary>
    public static string FormatRequirement(int rawReq, int effectiveReq)
    {
        int reduction = rawReq - effectiveReq;
        return reduction > 0 ? $"{effectiveReq} (-{reduction})" : effectiveReq.ToString();
    }

    // ── OS mouse-cursor request bus ────────────────────────────────────────────
    // Every link/resize-handle widget calls Request*Cursor() during its draw/update if the mouse is
    // over it; the highest-priority request wins (Hand > column-resize > diagonal-resize > Arrow) and
    // CommitFrameCursor() applies it exactly once at the end of the frame. Centralizing the SetCursor
    // call keeps two widgets — say a TextArea URL hover and a panel resize handle in the same area —
    // from fighting over what the OS cursor should look like.
    //
    // The pending request is a plain CursorRequest ENUM, not a MonoGame MouseCursor, for two reasons:
    // (1) priority is the enum's own value order — no fragile ReferenceEquals on framework statics; and
    // (2) touching MouseCursor.* creates SDL system cursors, unavailable in a headless unit test — so
    // the whole arbitration stays testable and the MouseCursor mapping is deferred to CommitFrameCursor
    // (which only ever runs at real runtime, never in a test).

    /// <summary>The OS cursors the UI can request, in ASCENDING priority (a higher value wins when
    /// several widgets request in one frame). Arrow is the default / no-request state.</summary>
    public enum CursorRequest { Arrow = 0, ResizeNwse = 1, ResizeWe = 2, Hand = 3 }

    private static CursorRequest _requestedCursor = CursorRequest.Arrow;
    private static CursorRequest _committedCursor = CursorRequest.Arrow;

    /// <summary>Hand a URL to the OS-default browser (ShellExecute / open / xdg-open). Failures are
    /// swallowed: no browser configured is not something the game can do anything about, and a crash
    /// on a decorative link would be far worse than the link doing nothing.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // No browser configured / shell rejected.
        }
    }

    public static void RequestHandCursor() => RequestCursor(CursorRequest.Hand);
    public static void RequestResizeNwseCursor() => RequestCursor(CursorRequest.ResizeNwse);
    /// <summary>Horizontal resize cursor — requested while hovering/dragging a table column divider.</summary>
    public static void RequestResizeWeCursor() => RequestCursor(CursorRequest.ResizeWe);

    /// <summary>Record a cursor request for this frame; the highest-priority one wins at commit.</summary>
    public static void RequestCursor(CursorRequest c) { if (c > _requestedCursor) _requestedCursor = c; }

    /// <summary>The winning request accumulated so far this frame (before <see cref="CommitFrameCursor"/>
    /// applies + resets it). Lets the arbitration be observed/asserted without a graphics device.</summary>
    public static CursorRequest RequestedCursor => _requestedCursor;

    /// <summary>Clear this frame's pending request WITHOUT touching the OS cursor — the real per-frame
    /// reset lives in <see cref="CommitFrameCursor"/>; this is the seam that isolates one test case from
    /// the next over the static bus.</summary>
    public static void ResetFrameCursor() => _requestedCursor = CursorRequest.Arrow;

    public static void CommitFrameCursor()
    {
        if (_requestedCursor != _committedCursor)
        {
            XnaMouse.SetCursor(ToMouseCursor(_requestedCursor));
            _committedCursor = _requestedCursor;
        }
        _requestedCursor = CursorRequest.Arrow;
    }

    // Map a request to the MonoGame cursor. Isolated here + reached ONLY from CommitFrameCursor because
    // touching MouseCursor.* creates SDL system cursors (unavailable headless).
    private static MouseCursor ToMouseCursor(CursorRequest c) => c switch
    {
        CursorRequest.Hand => MouseCursor.Hand,
        CursorRequest.ResizeWe => MouseCursor.SizeWE,
        CursorRequest.ResizeNwse => MouseCursor.SizeNWSE,
        _ => MouseCursor.Arrow,
    };

    // Reference resolution — all screens and panels draw at this size inside the render target.
    public const int RefW = 800;
    public const int RefH = 600;

    // Pre-game dialog dimensions matching the original form template (546×304 px).
    public const int MenuDlgW = 546;
    public const int MenuDlgH = 304;
    public const int MenuDlgArtW = 201;   // left decorative art panel width

    /// <summary>Width of the WIDE pre-game dialog, used by the one screen whose content genuinely does
    /// not fit the template: character creation, which shows a class's stats, pools, regen, combat output
    /// AND the gear and spells it starts with, all at once.
    /// <para>Wider rather than taller on purpose. The art panel keeps its authored 201×304 rectangle
    /// either way, so only the content column grows; a taller dialog would stretch that art by a sixth
    /// and every other menu screen would have to answer for it.</para></summary>
    public const int MenuDlgWideW = 700;

    // Primary UI accent: blue-dominant (more blue than purple).
    public static readonly Color DlgArtColor = new(15, 10, 80);  // deep indigo panel
    public static readonly Color DlgBorderColor = new(60, 80, 200); // medium blue border
    public static readonly Color DlgLabelColor = new(120, 140, 255); // cornflower label

    // Shared bar/floating-text color for the EXP vital.
    public static readonly Color ExpBarColor = new(90, 0, 130);
    // Vital bar fill colors — HudPanel + StatsPanel + in-world overhead bars + party overlay.
    // Single bright palette across every bar so the right-sidebar HUD, stats screen, overhead
    // world bars, and the party overlay all read as one visual language.
    public static readonly Color VitalHpColor = new(220, 40, 40);
    public static readonly Color VitalMpColor = new(40, 100, 220);
    // SP is amber-yellow (was green): green is reserved for healing/gain everywhere (float numbers + spell FX),
    // so SP's identity color moves off green. Feeds every SP bar + potion label via this one constant.
    public static readonly Color VitalSpColor = new(225, 195, 45);
    // Post-cast cooldown timer bar — drawn below a caster's sprite. Light neutral gray: reads as a "cooldown"
    // status, clearly distinct from every vital color (esp. the amber SP bar it sits near).
    public static readonly Color CooldownBarColor = new(180, 184, 194);
    // Unfilled bar background — HudPanel + StatsPanel
    public static readonly Color BarBg = new(20, 20, 20);
    // Confirmation overlay — InventoryPanel + ShopPanel
    public static readonly Color ConfirmOverlayBg = new(20, 20, 40, 220);
    public static readonly Color ConfirmOverlayBorder = new(100, 100, 160);
    // PK player indicator — HudPanel + StatsPanel
    public static readonly Color PkNameColor = Color.Red;
    // Stat/training row strip background — StatsPanel + TrainingPanel
    public static readonly Color StatRowBg = new(8, 8, 20, 235);
    // Panel title bar — DraggablePanel + ChatPanel
    public static readonly Color PanelTitleBg = new(30, 30, 60);
    public static readonly Color PanelTitleActiveBg = new(55, 55, 110);
    // Active Button-tab tint — Market + Mail tab strips
    public static readonly Color ActiveTabColor = new(70, 90, 140);
    // Danger confirmation button — QuitConfirmDialog + CharSelectScreen
    public static readonly Color DangerButtonNormal = new(80, 30, 30);
    public static readonly Color DangerButtonHover = new(120, 50, 50);
    // Primary "enter game" button — MainMenuScreen (Login/Connect) + NewCharScreen (Create) + CharSelectScreen (Play)
    public static readonly Color PrimaryButtonNormal = new(30, 80, 30);
    public static readonly Color PrimaryButtonHover = new(50, 120, 50);
    // Accent "create / sign up" button — MainMenuScreen (New Account)
    public static readonly Color AccentButtonNormal = new(30, 60, 130);
    public static readonly Color AccentButtonHover = new(50, 90, 180);
    // Modal popup background — AlertDialog + DeleteConfirmScreen + QuitConfirmDialog
    public static readonly Color PopupBg = new(30, 30, 50);
    // Standard button colors — Button + DropDown
    public static readonly Color ButtonNormalBg = new(50, 50, 80);
    public static readonly Color ButtonHoverBg = new(80, 80, 120);
    public static readonly Color ButtonDisabledBg = new(30, 30, 30);
    // Toggle-button OFF state — a neutral mid-gray, deliberately lighter than ButtonDisabledBg so an OFF
    // toggle never reads as a disabled/unclickable button. The ON state reuses PrimaryButton* (green).
    public static readonly Color ToggleOffBg = new(70, 70, 70);
    public static readonly Color ToggleOffHover = new(100, 100, 100);
    // List/dropdown scrollbar — ListBox + DropDown
    public static readonly Color ListScrollTrackBg = new(30, 30, 50);
    public static readonly Color ListScrollThumbBg = new(80, 80, 120);
    public static readonly Color ListScrollThumbBorder = new(100, 100, 160);
    // Generic UI control border — DropDown + DeleteConfirmScreen
    public static readonly Color UiControlBorder = new(100, 120, 200);
    // Disabled control foreground — Slider + Checkbox
    public static readonly Color DisabledColor = new(80, 80, 80);
    // Text input field background — TextInputField + ChatPanel input box
    public static readonly Color TextInputBg = new(20, 20, 40);
    // Overhead/party-overlay combat outline — amber border when an entity is in combat.
    public static readonly Color WorldBarCombatColor = new(200, 180, 0);
    // Floating combat text — MirageGame
    public static readonly Color FloatHealColor = Color.Lime;
    public static readonly Color FloatDmgColor = Color.Red;
    // Block/dodge avoidance floating text - matches the BrightCyan chat color (GameColor 11 = Color.Cyan).
    public static readonly Color FloatBlockColor = Color.Cyan;
    // Mitigated-to-0 hit floating text - gray "0 HP/MP/SP".
    public static readonly Color FloatZeroColor = Color.Gray;

    // ── Item durability condition coloring ─────────────────────────────────────
    // Shared white/yellow/red coding for a "current/max" durability readout, used by the equipment
    // paper-doll, the item tooltip, and the shop repair panel so an item's wear reads identically
    // everywhere. Thresholds are percent of max: strictly above Good → white (healthy), strictly
    // above Warn → yellow (wearing down), at/below Warn → red (needs repair / broken). Kept separate
    // from CombatFormulas' degrade-chance bands on purpose — this is a display choice, not gameplay
    // tuning, so retuning one must not silently shift the other.
    public const int DurabilityGoodPct = 75;  // condition strictly above this: white
    public const int DurabilityWarnPct = 25;  // condition strictly above this (and <= Good): yellow; at/below: red
    public static readonly Color DurabilityGoodColor = Color.White;
    public static readonly Color DurabilityWarnColor = Color.Yellow;
    public static readonly Color DurabilityBadColor = Color.Red;

    /// <summary>Color for a "<paramref name="dur"/>/<paramref name="maxDur"/>" durability readout:
    /// white above <see cref="DurabilityGoodPct"/>% condition, yellow above <see cref="DurabilityWarnPct"/>%,
    /// red at/below it (0 durability / broken lands here). Returns white when <paramref name="maxDur"/>
    /// <= 0 (item carries no durability) so a caller drawing it unconditionally still reads as "fine".</summary>
    public static Color DurabilityColor(int dur, int maxDur)
    {
        if (maxDur <= 0) return DurabilityGoodColor;
        double pct = (double)dur * 100 / maxDur;
        if (pct > DurabilityGoodPct) return DurabilityGoodColor;
        if (pct > DurabilityWarnPct) return DurabilityWarnColor;
        return DurabilityBadColor;
    }

    // Drop-shadow treatment shared by every floating panel — DraggablePanel + HudPanel +
    // PartyOverlayPanel + chat bubbles. Single source of truth so the whole UI lifts together.
    public const int PanelShadowOffset = 2;
    public static readonly Color PanelShadowColor = new(0, 0, 0, 120);

    // ── Chat / text-area palette ─────────────────────────────────────────────
    // ChatPanel backgrounds: log area + input row.
    public static readonly Color ChatBg = new(10, 10, 20, 200);
    public static readonly Color ChatInputRowBg = new(15, 15, 30);
    // Selection highlight shades — ChatPanel input vs TextArea body vs TextInputField.
    // Distinct alpha/blue tones kept on purpose: chat input is brightest (over dark bg),
    // TextArea is softer (long bodies of text), text input field is a flat opaque tone.
    public static readonly Color ChatInputSelectionHighlight = new(60, 100, 200, 160);
    public static readonly Color TextAreaSelectionHighlight = new(60, 100, 200, 120);
    public static readonly Color TextInputSelectionHighlight = new(70, 130, 200);
    // TextArea scrollbar palette — slightly cooler than ListBox/DropDown's ListScroll* set
    // by design (text bodies sit against darker frames, the cooler bar reads less busy).
    public static readonly Color TextAreaSbTrackBg = new(28, 28, 48);
    public static readonly Color TextAreaSbTrackBorder = new(60, 60, 92);
    public static readonly Color TextAreaSbThumbBg = new(100, 100, 158);
    public static readonly Color TextAreaSbThumbBorder = new(150, 150, 210);
    // Hyperlink colors — used by TextArea when EnableHyperlinks is on. Light blue stands out
    // against the dark log background and reads as a link without colliding with the QBColor
    // chat palette; the hover tone brightens to confirm the hand cursor is over a live link.
    public static readonly Color HyperlinkColor = new(120, 180, 255);
    public static readonly Color HyperlinkHoverColor = new(180, 220, 255);
    // Map-name color shown when the player is standing on a Safe-moral map (HudPanel).
    public static readonly Color SafeMapNameColor = new(120, 190, 120);
    // Map-name color for an Arena-moral map (penalty-free open PvP) — HudPanel.
    public static readonly Color ArenaMapNameColor = Color.Yellow;
    // Time-of-Day / Weather status-line color (HudPanel).
    public static readonly Color WeatherStatusColor = new(0, 120, 255);

    /// <summary>Player-facing map name: the map's authored DisplayName, else its MapGroup's
    /// DisplayName, else the internal Name, else a generic "Map N". The first three steps are the shared
    /// <see cref="MapGroupResolve.DisplayName"/>, resolved client-side against the map's cached
    /// <paramref name="group"/> (pass <c>state.GroupOf(map)</c>); this only adds the localized "Map N" fallback.</summary>
    public static string ResolveMapDisplayName(MapRecord map, int mapNum, MapGroupRecord? group)
    {
        string resolved = MapGroupResolve.DisplayName(map, group);
        return resolved.Length > 0
            ? resolved
            : ClientStrings.Format(ClientStrings.HudPanel_MapNameFallbackFormat, ("Number", mapNum));
    }

    // Pre-game dialog rectangle centered in the 800×600 reference viewport: (127, 148, 546, 304).
    public static Rectangle MenuDialogRect =>
        new((RefW - MenuDlgW) / 2, (RefH - MenuDlgH) / 2, MenuDlgW, MenuDlgH);

    /// <summary>The wide variant, same height and same centering: (50, 148, 700, 304).</summary>
    public static Rectangle WideMenuDialogRect =>
        new((RefW - MenuDlgWideW) / 2, (RefH - MenuDlgH) / 2, MenuDlgWideW, MenuDlgH);

    public static void Init(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    // ── Scissor clipping (reference-space) ─────────────────────────────────────
    // The UI/panel pass draws into the 800x600 reference render target with a plain Begin(Deferred,
    // AlphaBlend), so a scissor rectangle here is in reference pixels — no transform math. To clip a widget
    // (e.g. a horizontally-scrolled Table) we must flush the batch, set the scissor rect, and re-begin with
    // a scissor-enabled rasterizer: SpriteBatch's Deferred mode applies the scissor as device state at flush,
    // so it can't be scoped per-draw without splitting the batch. EndClip restores the standard UI batch.
    // ASSUMES the caller is inside that UI batch (the only place clipped widgets draw). Not re-entrant.
    private static readonly RasterizerState ScissorRaster = new() { ScissorTestEnable = true };
    private static Rectangle _savedScissor;

    /// <summary>Begin clipping subsequent draws to <paramref name="clip"/> (reference pixels). Pair with
    /// <see cref="EndClip"/>. Splits the current UI batch; only valid inside the standard UI pass.</summary>
    public static void BeginClip(SpriteBatch sb, Rectangle clip)
    {
        sb.End();
        var gd = sb.GraphicsDevice;
        _savedScissor = gd.ScissorRectangle;
        gd.ScissorRectangle = Rectangle.Intersect(clip, gd.Viewport.Bounds);
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, ScissorRaster);
    }

    /// <summary>End the clip opened by <see cref="BeginClip"/>, restoring the standard UI batch + scissor.</summary>
    public static void EndClip(SpriteBatch sb)
    {
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = _savedScissor;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
    }

    // ── Truncation tooltips ────────────────────────────────────────────────────
    // The generic hook every label-drawing control uses: if a label's full text doesn't fit the width it's
    // drawn into AND the mouse is over it, register a full-text hover tooltip (rendered by Tooltip.TickAndDraw
    // after all panels). Each control passes a unique scope (from NextTooltipScope) + a per-label key; the two
    // are combined into the tooltip identity so labels in different controls never collide on a shared key.
    private static int _tooltipScopeSeq;

    /// <summary>Mint a process-unique tooltip scope for a control instance, e.g. NextTooltipScope("listbox").</summary>
    public static string NextTooltipScope(string prefix) => prefix + ":" + (++_tooltipScopeSeq);

    /// <summary>Register a full-text tooltip for a label that is truncated (its measured width exceeds
    /// <paramref name="textRect"/>.Width) and currently hovered. No-op otherwise. Call during Draw/Update
    /// where the font, the text's drawn rect, and the mouse are all known.</summary>
    public static void LabelTooltip(SpriteFont font, string text, Rectangle textRect, Point mouse, string scope, object key)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (font.MeasureString(text).X <= textRect.Width) return;   // fits → nothing to reveal
        if (!textRect.Contains(mouse)) return;
        Tooltip.NotifyHoverText(scope, (scope, key), text, mouse);
    }

    public static void DrawFilledRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel!, rect, color);

    /// <summary>Float-positioned filled rect (1×1 pixel scaled), so world-layer overlays drawn into the
    /// supersampled target keep sub-pixel positions instead of snapping to whole pixels.</summary>
    public static void DrawFilledRect(SpriteBatch sb, float x, float y, float w, float h, Color color)
        => sb.Draw(_pixel!, new Vector2(x, y), null, color, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);

    /// <summary>Draw a straight line from <paramref name="a"/> to <paramref name="b"/> of the given
    /// thickness — the 1x1 pixel texture stretched to the segment length and rotated. Used for the death
    /// corpse's red X.</summary>
    public static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness = 1f)
    {
        Vector2 delta = b - a;
        float len = delta.Length();
        if (len <= 0f) return;
        sb.Draw(_pixel!, a, null, color, MathF.Atan2(delta.Y, delta.X), new Vector2(0f, 0.5f),
            new Vector2(len, thickness), SpriteEffects.None, 0f);
    }

    // Per-viewer territory-contest colors, shared by the in-world flags/circles and the HUD:
    // the viewer's own guild = blue, an enemy guild = red, neutral/contested = gray.
    public static readonly Color ContestOwnColor = new(90, 140, 255);
    public static readonly Color ContestEnemyColor = new(235, 70, 70);
    public static readonly Color ContestNeutralColor = new(175, 175, 175);

    /// <summary>Draw a plain circle outline as a ring of straight segments centered at <paramref name="center"/>
    /// — the territory capture-radius marker, the client analogue of the editor's light-radius circle.</summary>
    public static void DrawCircleOutline(SpriteBatch sb, Vector2 center, float radius, Color color, float thickness = 1f, int segments = 48)
    {
        if (radius <= 0f || segments < 3) return;
        float step = MathHelper.TwoPi / segments;
        Vector2 prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 next = center + new Vector2(MathF.Cos(step * i) * radius, MathF.Sin(step * i) * radius);
            DrawLine(sb, prev, next, color, thickness);
            prev = next;
        }
    }

    /// <summary>Fill a solid triangle by horizontal scanlines (1px-tall filled rects). Used for the small
    /// contest capture-point flag pennant — cheap at the ~16px sizes it's drawn at.</summary>
    public static void FillTriangle(SpriteBatch sb, Vector2 p0, Vector2 p1, Vector2 p2, Color color)
    {
        float minY = MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y));
        float maxY = MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y));
        for (float y = MathF.Floor(minY); y < MathF.Ceiling(maxY); y++)
        {
            float cy = y + 0.5f;
            float xL = float.MaxValue, xR = float.MinValue;
            ScanEdge(p0, p1, cy, ref xL, ref xR);
            ScanEdge(p1, p2, cy, ref xL, ref xR);
            ScanEdge(p2, p0, cy, ref xL, ref xR);
            if (xR > xL) DrawFilledRect(sb, xL, y, xR - xL, 1f, color);
        }
    }

    // One triangle edge's intersection with a horizontal scanline at y, widening [xL,xR] if it crosses.
    private static void ScanEdge(Vector2 a, Vector2 b, float y, ref float xL, ref float xR)
    {
        if ((a.Y <= y) == (b.Y <= y)) return;   // both endpoints on the same side → no crossing
        float x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
        if (x < xL) xL = x;
        if (x > xR) xR = x;
    }

    public static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(_pixel!, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    /// <summary>Float-positioned border (four float-positioned edge rects).</summary>
    public static void DrawBorder(SpriteBatch sb, float x, float y, float w, float h, Color color, float thickness = 1f)
    {
        DrawFilledRect(sb, x, y, w, thickness, color);                 // top
        DrawFilledRect(sb, x, y + h - thickness, w, thickness, color); // bottom
        DrawFilledRect(sb, x, y, thickness, h, color);                 // left
        DrawFilledRect(sb, x + w - thickness, y, thickness, h, color); // right
    }

    /// <summary>Pixel-art rounded filled rect — decomposes into a center vertical strip plus two
    /// inset side strips. At small radii (2-4 px) the missing single-pixel corners read as clean
    /// rounding without needing corner sprites. Used by chat bubbles.</summary>
    public static void DrawRoundedFilledRect(SpriteBatch sb, Rectangle rect, int radius, Color color)
    {
        if (radius <= 0 || rect.Width < radius * 2 || rect.Height < radius * 2)
        {
            DrawFilledRect(sb, rect, color);
            return;
        }
        // Center strip (full height, slimmer than full width).
        sb.Draw(_pixel!, new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, rect.Height), color);
        // Left + right strips (full inset height).
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y + radius, radius, rect.Height - radius * 2), color);
        sb.Draw(_pixel!, new Rectangle(rect.Right - radius, rect.Y + radius, radius, rect.Height - radius * 2), color);
    }

    /// <summary>Pixel-art rounded border — four edges matching the inset rule from
    /// <see cref="DrawRoundedFilledRect"/>. Used by chat bubbles.</summary>
    public static void DrawRoundedBorder(SpriteBatch sb, Rectangle rect, int radius, Color color, int thickness = 1)
    {
        if (radius <= 0 || rect.Width < radius * 2 || rect.Height < radius * 2)
        {
            DrawBorder(sb, rect, color, thickness);
            return;
        }
        // Top + bottom edges — inset by radius on each side.
        sb.Draw(_pixel!, new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, thickness), color);
        sb.Draw(_pixel!, new Rectangle(rect.X + radius, rect.Bottom - thickness, rect.Width - radius * 2, thickness), color);
        // Left + right edges — inset by radius top/bottom.
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y + radius, thickness, rect.Height - radius * 2), color);
        sb.Draw(_pixel!, new Rectangle(rect.Right - thickness, rect.Y + radius, thickness, rect.Height - radius * 2), color);
    }

    public static Vector2 CenterText(SpriteFont font, string text, Rectangle rect)
    {
        var size = font.MeasureString(text);
        return new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
    }

    // ── Panel layout helpers ─────────────────────────────────────────────────
    // Shared dimensions for the "two equal-width buttons sitting at the bottom of a content
    // area" pattern used by InventoryPanel, ShopPanel, and their confirmation overlays.
    public const int PanelButtonHeight = 26;
    public const int PanelButtonRowBottomPad = 34;  // gap between content.Bottom and button top
    public const int PanelButtonEdgePad = 4;        // left/right inset and inter-button gap

    /// <summary>Compute one of two side-by-side button bounds at the bottom of a panel's
    /// content area. Half-width with <see cref="PanelButtonEdgePad"/> insets, anchored
    /// <see cref="PanelButtonRowBottomPad"/> above content.Bottom. <paramref name="column"/>
    /// is 0 (left) or 1 (right). Mirrors the layout InventoryPanel and ShopPanel duplicated
    /// inline 5+ times.</summary>
    public static Rectangle PanelBottomButton(Rectangle content, int column, int columns = 2)
    {
        int slotW = (content.Width - (columns + 1) * PanelButtonEdgePad) / columns;
        int x = content.X + PanelButtonEdgePad + column * (slotW + PanelButtonEdgePad);
        int y = content.Bottom - PanelButtonRowBottomPad;
        return new Rectangle(x, y, slotW, PanelButtonHeight);
    }

    /// <summary>
    /// Draws a horizontal vital bar: <paramref name="bgColor"/> background (defaults to
    /// <see cref="BarBg"/>), colored fill clamped to <paramref name="fillRatio"/>, a 1-pixel
    /// outline in <paramref name="outline"/>, and a centered text label.  Shared by HudPanel,
    /// StatsPanel, and the party overlay so the rendering stays in one place.  Callers decide
    /// the text (e.g. "HP 50/100" vs. "50%" on hover) since each panel has its own caching/hover
    /// rules; the party overlay also passes alpha-tinted colors through every channel for the
    /// proximity-dim effect.
    /// </summary>
    public static void DrawVitalBar(SpriteBatch sb, SpriteFont font, Rectangle bounds,
        float fillRatio, Color fill, Color outline, string text, Color textColor,
        int outlineThickness = 1, Color? bgColor = null)
    {
        DrawFilledRect(sb, bounds, bgColor ?? BarBg);
        int fillW = (int)(bounds.Width * Math.Clamp(fillRatio, 0f, 1f));
        if (fillW > 0)
            DrawFilledRect(sb, new Rectangle(bounds.X, bounds.Y, fillW, bounds.Height), fill);
        DrawBorder(sb, bounds, outline, outlineThickness);
        if (text.Length > 0)
        {
            string fitted = FitText(font, text, Math.Max(MinFitWidth, bounds.Width - 4));
            sb.DrawString(font, fitted, CenterText(font, fitted, bounds), textColor);
        }
    }

    /// <summary>Standard vital-bar readout "<paramref name="label"/> current/max" with grouped
    /// thousands (e.g. "HP 1,250/1,400"). Pure layout — the caller supplies an already-localized
    /// label and the format itself has no translatable text. Shared by HudPanel, StatsPanel, and
    /// the party overlay so the bar text reads identically everywhere.</summary>
    public static string VitalBarText(string label, long current, long max) => $"{label} {current:N0}/{max:N0}";

    private const float MinFitWidth = 10f;
    private const float CenteredLabelInset = 8f; // total horizontal margin for DrawLabelCentered (4 px each side)

    /// <summary>Draws <paramref name="text"/> left-aligned at <paramref name="pos"/>, truncating with "…" if wider than <paramref name="maxWidth"/>.</summary>
    public static void DrawLabel(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, Color color, float maxWidth)
        => sb.DrawString(font, FitText(font, text, Math.Max(MinFitWidth, maxWidth)), pos, color);

    /// <summary>Draws <paramref name="text"/> horizontally centered in the region [<paramref name="x"/>, <paramref name="x"/>+<paramref name="width"/>],
    /// truncating with "…" if needed. 4 px margin is kept on each side before truncation kicks in.</summary>
    public static void DrawLabelCentered(SpriteBatch sb, SpriteFont font, string text, float x, float y, float width, Color color)
    {
        string fitted = FitText(font, text, Math.Max(MinFitWidth, width - CenteredLabelInset));
        sb.DrawString(font, fitted, new Vector2(x + (width - font.MeasureString(fitted).X) / 2f, y), color);
    }

    /// <summary>
    /// Returns <paramref name="text"/> truncated with "..." if it exceeds <paramref name="maxWidth"/> pixels.
    /// Uses binary search so it's accurate with kerning.
    /// </summary>
    public static string FitText(SpriteFont font, string text, float maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth) return text;
        const string suffix = "...";
        float avail = maxWidth - font.MeasureString(suffix).X;
        if (avail <= 0) return "";
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureString(text[..mid]).X <= avail) lo = mid;
            else hi = mid - 1;
        }
        return text[..lo] + suffix;
    }

    /// <summary>Draws <paramref name="title"/> centered above the menu dialog in the title font.
    /// <paramref name="rect"/> overrides the dialog it centers on, for a screen using the wide variant.</summary>
    public static void DrawMenuTitle(SpriteBatch sb, SpriteFont titleFont, string title, Rectangle? rect = null)
    {
        var dlg = rect ?? MenuDialogRect;
        var size = titleFont.MeasureString(title);
        float x = dlg.X + dlg.Width / 2f - size.X / 2f;
        float y = dlg.Y - titleFont.LineSpacing - 4;
        sb.DrawString(titleFont, title, new Vector2(x, y), DlgLabelColor);
    }

    /// <summary>
    /// Draws the standard pre-game menu dialog: black window background, dark-maroon art panel on
    /// the left, black content area on the right, vertical divider, and a two-pixel border.
    /// <para><paramref name="rect"/> overrides the dialog rectangle (see <see cref="WideMenuDialogRect"/>).
    /// The art panel is a fixed <see cref="MenuDlgArtW"/> wide whatever the dialog is, so a wider dialog
    /// spends every extra pixel on content and the art is never stretched sideways.</para>
    /// </summary>
    public static void DrawMenuDialog(SpriteBatch sb, Rectangle viewport,
        out Rectangle dlg, out Rectangle content, Texture2D? artTexture = null, Rectangle? rect = null)
    {
        DrawFilledRect(sb, viewport, Color.Black);

        dlg = rect ?? MenuDialogRect;
        content = new Rectangle(dlg.X + MenuDlgArtW, dlg.Y, dlg.Width - MenuDlgArtW, dlg.Height);

        var artRect = new Rectangle(dlg.X, dlg.Y, MenuDlgArtW, dlg.Height);
        if (artTexture is not null)
            sb.Draw(artTexture, artRect, Color.White);
        else
            DrawFilledRect(sb, artRect, DlgArtColor);

        // Content area background.
        DrawFilledRect(sb, content, Color.Black);

        // Vertical divider between art panel and content area.
        DrawFilledRect(sb, new Rectangle(dlg.X + MenuDlgArtW - 1, dlg.Y, 2, dlg.Height), DlgBorderColor);

        // Outer dialog border.
        DrawBorder(sb, dlg, DlgBorderColor, 2);
    }

    /// <summary>
    /// Draws an error or status message centered horizontally below the menu dialog border.
    /// </summary>
    public static void DrawMenuAlert(SpriteBatch sb, SpriteFont font, string msg, Color color, Rectangle? rect = null)
    {
        var dlg = rect ?? MenuDialogRect;
        var size = font.MeasureString(msg);
        sb.DrawString(font, msg, new Vector2(dlg.X + (dlg.Width - size.X) / 2f, dlg.Bottom + 8f), color);
    }

    public static void DrawMenuSpritePreview(SpriteBatch sb, IReadOnlyList<Texture2D?> sprites, int spriteRow, int spriteSheet, int animFrame, Rectangle dest, Direction direction = Direction.Down)
    {
        if (spriteRow < 0) return;
        var sheet = sprites.Sheet(spriteSheet);
        if (sheet is null) return;
        var src = SpriteAtlas.GetSourceRect(spriteRow, direction, animFrame);
        sb.Draw(sheet, dest, src, Color.White);
    }

    // ── Word wrap ─────────────────────────────────────────────────────────────
    // Authored text carries its own line breaks — a quest description, a mail body, a conversation
    // node — so wrapping splits on newlines FIRST and wraps each paragraph on its own. Splitting on
    // spaces alone leaves the "\n" inside a word: DrawString still renders the break, but the caller's
    // y never advances for it, and whatever comes next is drawn over the overflow.

    /// <summary>Greedy word-wrap that honors authored newlines. A blank line survives as a blank
    /// entry, so a deliberate paragraph gap is preserved. A single word wider than
    /// <paramref name="maxWidth"/> is left whole for the caller to truncate.</summary>
    public static List<string> WrapLines(SpriteFont font, string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }
            string cur = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length == 0 || font.MeasureString(candidate).X <= maxWidth) cur = candidate;
                else
                {
                    lines.Add(cur);
                    cur = word;
                }
            }
            if (cur.Length > 0) lines.Add(cur);
        }
        return lines;
    }

    /// <summary>Draws <see cref="WrapLines"/>'s output and returns the y past the last line, so a
    /// caller can lay the next section directly beneath it.</summary>
    public static float DrawWrapped(SpriteBatch sb, SpriteFont font, string text, float x, float y,
                                   float maxWidth, Color color, float lineHeight)
    {
        foreach (string line in WrapLines(font, text, maxWidth))
        {
            if (line.Length > 0) sb.DrawString(font, line, new Vector2(x, y), color);
            y += lineHeight;
        }
        return y;
    }
}

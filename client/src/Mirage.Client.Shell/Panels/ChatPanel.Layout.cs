using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using TextCopy;

namespace Mirage.Client.Shell.Panels;

/// <summary>Geometry and the tab strip: the log/input/dropdown rectangles, drag-resize, and the tab
/// strip's layout, hit-testing and drawing.</summary>
public sealed partial class ChatPanel
{
    // ── Layout helpers ─────────────────────────────────────────────────────────

    // Tab strip / log / input all lay out within the panel's content area (below its 16px title bar).
    private Rectangle TabStripRect()
    {
        var c = _panel.ContentBounds;
        return new(c.X, c.Y, c.Width, TabStripH);
    }

    // Log area between the tab strip and the input row (scrollbar gutter included).
    private Rectangle LogAreaBounds()
    {
        var c = _panel.ContentBounds;
        return new(c.X, c.Y + TabStripH, c.Width, c.Height - TabStripH - InputH);
    }

    // The channel dropdown occupies a fixed slice at the left of the input row; the input box takes
    // the rest. All caret/selection/scroll math reads InputRect().X/Width, so it follows automatically.
    private Rectangle ChannelDropRect()
    {
        var c = _panel.ContentBounds;
        return new(c.X, c.Bottom - InputH, ChannelDropW, InputH);
    }

    private Rectangle InputRect()
    {
        var c = _panel.ContentBounds;
        int x = c.X + ChannelDropW + ChannelDropGap;
        return new(x, c.Bottom - InputH, c.Width - ChannelDropW - ChannelDropGap, InputH);
    }

    private static string FilterForFont(string s) => TextValidation.Filter(s);

    // ── Drag / resize ──────────────────────────────────────────────────────────

    // ── Tab strip layout ───────────────────────────────────────────────────────

    private const int TabHGap = 2;
    private const int TabPaddingX = 6;
    private const int TabCloseW = 12;
    private const int AddBtnW = 18;
    private const int MinTabW = 34;

    // Tab rects + the labels actually drawn (which may be pixel-truncated when the strip is full)
    // are computed against the font during Draw and cached so the next frame's Update (which has
    // no font) can hit-test them. One-frame stale after an add/remove, which is imperceptible.
    // Empty until the first Draw — Update no-ops against empty arrays.
    private Rectangle[] _tabRects = Array.Empty<Rectangle>();
    private Rectangle[] _closeRects = Array.Empty<Rectangle>();
    private string[] _tabLabels = Array.Empty<string>();
    private Rectangle _addRect = Rectangle.Empty;

    private const string Ellipsis = "..."; // literal ASCII, not U+2026, per ASCII-only memory

    /// <summary>Lays out each tab cell, the close button on each tab's right edge (hidden when
    /// only one tab remains), and the +-button immediately after the last tab (hidden at the cap).
    /// Tabs size to their text when they all fit; when they'd overflow the strip, every tab gets
    /// an equal share and its label is pixel-truncated with "..." so the + button is never pushed
    /// out of view. Requires the font, so it runs in Draw and caches the result for Update.</summary>
    private void LayoutTabStrip(SpriteFont font)
    {
        var strip = TabStripRect();
        int innerY = strip.Y + 3;
        int innerH = strip.Height - 5;
        int count = _tabs.Count;
        bool hasClose = count > 1;
        int closeReserve = hasClose ? TabCloseW + 2 : 0;
        bool showAdd = count < MaxTabs;
        int addReserve = showAdd ? AddBtnW + TabHGap : 0;

        _tabRects = new Rectangle[count];
        _closeRects = new Rectangle[count];
        _tabLabels = new string[count];

        // Natural widths (label already display-capped at 15 chars).
        var naturalLabel = new string[count];
        var naturalW = new int[count];
        long naturalTotal = TabHGap; // leading gap before the first tab
        for (int i = 0; i < count; i++)
        {
            naturalLabel[i] = TruncateForTab(_tabs[i].Config.Name);
            int textW = (int)Math.Ceiling(font.MeasureString(naturalLabel[i]).X);
            naturalW[i] = Math.Max(MinTabW, textW + TabPaddingX * 2 + closeReserve);
            naturalTotal += naturalW[i] + TabHGap;
        }

        // Width available for the row of tabs, leaving room for the + button (and a trailing gap).
        int availForTabs = strip.Width - addReserve - TabHGap;
        bool clamp = count > 0 && naturalTotal > availForTabs;
        // Equal share when clamping; integer floor keeps the running total within availForTabs so
        // the + button stays on-screen.
        int slotW = clamp ? Math.Max(MinTabW, (availForTabs - TabHGap * count) / count) : 0;

        int x = strip.X + TabHGap;
        for (int i = 0; i < count; i++)
        {
            int tabW = clamp ? slotW : naturalW[i];
            _tabLabels[i] = clamp
                ? FitLabelToWidth(font, _tabs[i].Config.Name, tabW - TabPaddingX * 2 - closeReserve)
                : naturalLabel[i];
            _tabRects[i] = new Rectangle(x, innerY, tabW, innerH);
            // X button on the right edge — visible only when more than one tab exists, since the
            // last tab can't be removed.
            _closeRects[i] = hasClose
                ? new Rectangle(_tabRects[i].Right - TabCloseW - 2, innerY + 3, TabCloseW, innerH - 6)
                : Rectangle.Empty;
            x += tabW + TabHGap;
        }

        // + button trails the last tab. The clamp math above leaves room for it; the Min is a
        // final guard so an extremely narrow strip can never push it off the right edge.
        _addRect = showAdd
            ? new Rectangle(Math.Min(x, strip.Right - AddBtnW - TabHGap), innerY, AddBtnW, innerH)
            : Rectangle.Empty;
    }

    // Display cap: names over 15 chars show the first 15 + "...".
    private static string TruncateForTab(string name)
    {
        if (name.Length <= TabDisplayCharLimit) return name;
        return name[..TabDisplayCharLimit] + Ellipsis;
    }

    // Returns the longest prefix of `name` (within the 15-char cap) that fits in maxTextW pixels,
    // appending a single "..." when anything was dropped. Used only in the clamped (overflow) path.
    private static string FitLabelToWidth(SpriteFont font, string name, int maxTextW)
    {
        if (maxTextW <= 0) return "";
        bool charCapped = name.Length > TabDisplayCharLimit;
        string capped = charCapped ? name[..TabDisplayCharLimit] : name;
        string full = charCapped ? capped + Ellipsis : capped;
        if (font.MeasureString(full).X <= maxTextW) return full;

        float ellW = font.MeasureString(Ellipsis).X;
        for (int len = capped.Length - 1; len > 0; len--)
        {
            if (font.MeasureString(capped[..len]).X + ellW <= maxTextW)
                return capped[..len] + Ellipsis;
        }

        return Ellipsis;
    }

    // ── Tab strip input ────────────────────────────────────────────────────────

    /// <summary>Hit-tests left/right clicks against the tab strip (using the rects cached by the
    /// last Draw). Returns true if any tab-strip element consumed the click — caller (`Update`)
    /// skips other handlers when true so a tab click doesn't fall through to the log's right-click
    /// name menu.</summary>
    private bool HandleTabStripInput(InputState input)
    {
        if (!TabStripRect().Contains(input.MousePosition)) return false;
        int n = Math.Min(_tabRects.Length, _tabs.Count);

        // Left click: priority order is X button → tab body → + button.
        if (input.IsMouseClicked())
        {
            for (int i = 0; i < n; i++)
            {
                if (!_closeRects[i].IsEmpty && input.IsClickIn(_closeRects[i]))
                {
                    RemoveTab(i);
                    input.ConsumeMouseClick();
                    return true;
                }
                if (input.IsClickIn(_tabRects[i]))
                {
                    _activeTab = i;
                    _tabs[i].NotifyPending = false;
                    input.ConsumeMouseClick();
                    return true;
                }
            }
            if (!_addRect.IsEmpty && input.IsClickIn(_addRect))
            {
                AddTab();
                _activeTab = _tabs.Count - 1;
                input.ConsumeMouseClick();
                return true;
            }
        }

        if (input.IsRightMouseClicked())
        {
            for (int i = 0; i < n; i++)
            {
                if (_tabRects[i].Contains(input.MousePosition))
                {
                    OnTabRightClicked?.Invoke(i, input.MousePosition);
                    input.ConsumeRightMouseClick();
                    return true;
                }
            }
        }

        return false;
    }

    // ── Tab strip drawing ──────────────────────────────────────────────────────

    private void DrawTabStrip(SpriteBatch sb, SpriteFont font, long nowMs)
    {
        var strip = TabStripRect();
        // The empty track behind the tabs — distinct dark fill so leftover space reads as
        // "room for more tabs", clearly separate from the chat log below.
        UiHelper.DrawFilledRect(sb, strip, TabStripBg);

        LayoutTabStrip(font);
        bool flashOn = (nowMs / 500) % 2 == 0;

        for (int i = 0; i < _tabRects.Length; i++)
        {
            bool active = i == _activeTab;
            Color bg = active
                ? TabStrip.ActiveBg
                : (_tabs[i].NotifyPending && flashOn ? TabStrip.HoverBg : TabStrip.InactiveBg);
            UiHelper.DrawFilledRect(sb, _tabRects[i], bg);
            UiHelper.DrawBorder(sb, _tabRects[i], TabBorder);

            sb.DrawString(font, _tabLabels[i],
                new Vector2(_tabRects[i].X + TabPaddingX, _tabRects[i].Y + 3),
                Color.White);

            if (!_closeRects[i].IsEmpty)
            {
                sb.DrawString(font, "X",
                    new Vector2(_closeRects[i].X + 2, _closeRects[i].Y - 1),
                    Color.White);
            }
        }

        if (!_addRect.IsEmpty)
        {
            UiHelper.DrawFilledRect(sb, _addRect, AddTabBg);
            UiHelper.DrawBorder(sb, _addRect, TabBorder);
            sb.DrawString(font, "+",
                new Vector2(_addRect.X + 5, _addRect.Y + 2),
                Color.White);
        }
    }
}

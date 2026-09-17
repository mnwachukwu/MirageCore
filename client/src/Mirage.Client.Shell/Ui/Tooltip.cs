using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Rendering;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Hover tooltip for items. Exactly one tooltip is rendered at a time; panels feed
/// it via <see cref="NotifyHoverItem"/> while their row is
/// hovered, and <c>GameplayScreen</c> calls <see cref="TickAndDraw"/> after every panel
/// has been drawn so the tooltip floats above the rest of the UI.
///
/// Timing rules:
///   • Appears immediately on the first frame a row is hovered (no delay).
///   • Position is pinned to the mouse coords at first-show + a small offset; it does not move
///     until the hovered identity changes.
///   • Moving onto a different row instantly swaps content and re-pins to the new mouse position
///     — no fade, no double-render.
///   • Mouse over the source row OR over the tooltip's own rect keeps it open. The instant both
///     leave (and no new row is hovered), the tooltip clears on the same frame — no linger.
///
/// Item tooltips show the item icon from items.bmp next to the header.
/// </summary>
public static class Tooltip
{
    private const int MouseOffsetX = 14;
    private const int MouseOffsetY = 18;

    private const int PadX = 8;
    private const int PadY = 6;
    private const int LineGap = 2;
    private const int HeaderGap = 4;
    private const int IconSize = 32;
    private const int IconRightGap = 8;

    private static readonly Color BgColor = new(12, 26, 22, 240);
    private static readonly Color BorderColor = new(70, 150, 138);
    private static readonly Color HeaderColor = Color.White;
    private static readonly Color LabelColor = new(142, 190, 178);
    private static readonly Color ValueColor = Color.White;
    private static readonly Color WarnColor = Color.OrangeRed;
    private static readonly Color GoodColor = Color.LightGreen;

    private readonly record struct Line(string Label, string Value, Color Color);

    private enum Kind { None, Item, Text }

    private static Kind _kind;
    private static string _scope = "";   // panel id that spawned the active tooltip
    private static object? _key;
    private static int _x, _y;
    private static Rectangle _bounds;
    private static bool _hoverPersists;

    // Cached content needed to redraw each frame. Captured by reference so live data (durability,
    // stat changes) reflects in the tooltip without callers having to refresh on every change.
    private static ItemRecord? _item;
    private static PlayerInvSlot? _slot;
    private static string? _text;   // Kind.Text: the full string a truncated label shows on hover
    private static PlayerRecord? _me;
    private static IReadOnlyList<Texture2D?> _itemsTex = [];
    private static ItemRecord?[] _itemDefs = Array.Empty<ItemRecord?>();   // item definitions (for the SubHp reagent name)
    private static WeatherType _weather;                                    // current weather (for the rain "(x2)" reagent hint)

    private static readonly List<Line> _lines = new();

    /// <summary>
    /// Called by a panel during its Update each frame the mouse is over a row that should show an item
    /// tooltip. <paramref name="key"/> identifies the row+item so the tooltip re-pins position when the
    /// user moves to a different slot or the slot's item changes. <paramref name="scope"/> tags this
    /// tooltip with the spawning panel id so <see cref="CloseScope"/> can dismiss it when that panel
    /// closes.
    ///
    /// <para>The last three are only needed for a SPELL SCROLL, whose tooltip continues into the spell
    /// it teaches — everything that decides whether a scroll is worth buying lives on the spell, not on
    /// the scroll. A caller with no spell table simply omits them and the scroll shows its item half
    /// alone, which is what it did everywhere before.</para>
    /// </summary>
    public static void NotifyHoverItem(string scope, object key, ItemRecord item, PlayerInvSlot? slot,
        PlayerRecord? me, IReadOnlyList<Texture2D?> itemsTex, Point mousePos,
 ItemRecord?[]? itemDefs = null, WeatherType weather = default)
    {
        if (_kind != Kind.Item || !Equals(_key, key))
        {
            _kind = Kind.Item;
            _scope = scope;
            _key = key;
            PinTo(mousePos);
        }
        _item = item;
        _slot = slot;
        _me = me;
        _itemsTex = itemsTex;
        // Assigned even when null: these are shared with the spell path, and leaving them behind would
        // price a scroll's reagent line off whatever spell was hovered last.
        _itemDefs = itemDefs ?? [];
        _weather = weather;
        _hoverPersists = true;
    }

    /// <summary>Show a plain-text tooltip — used by a truncated label to reveal its full text on hover.
    /// <paramref name="key"/> identifies the hovered label so the tooltip re-pins when the pointer moves to a
    /// different one.</summary>
    public static void NotifyHoverText(string scope, object key, string text, Point mousePos)
    {
        if (_kind != Kind.Text || !Equals(_key, key))
        {
            _kind = Kind.Text;
            _scope = scope;
            _key = key;
            PinTo(mousePos);
        }
        _text = text;
        _item = null;
        _slot = null;
        _itemsTex = [];
        _hoverPersists = true;
    }

    /// <summary>Dismiss the tooltip if it was spawned by <paramref name="scope"/>. Called by a
    /// panel when it closes so any tooltip it had open doesn't linger over the empty space the
    /// panel just vacated. No-op when a different panel owns the active tooltip.</summary>
    public static void CloseScope(string scope)
    {
        if (_kind != Kind.None && _scope == scope) Reset();
    }

    /// <summary>Per-frame tick + draw — called once after every panel finishes drawing so the
    /// tooltip floats above all of them. Mouse on the source row (via NotifyHover*) or on the
    /// tooltip's own rect keeps it open; the instant both are no longer hovered the tooltip
    /// clears on this same frame.</summary>
    public static void TickAndDraw(SpriteBatch sb, SpriteFont font, long nowMs, Point mousePos)
    {
        if (_kind == Kind.None) return;

        // Mouse over the tooltip's own rect counts as continuing to hover.
        if (_bounds.Contains(mousePos)) _hoverPersists = true;

        if (!_hoverPersists)
        {
            Reset();
            return;
        }

        Draw(sb, font);
        _hoverPersists = false;
    }

    public static void Reset()
    {
        _kind = Kind.None;
        _scope = "";
        _key = null;
        _hoverPersists = false;
        _bounds = Rectangle.Empty;
        _item = null;
        _slot = null;
        _me = null;
        _itemsTex = [];
        _text = null;
    }

    // The RAW cursor position. The offsets are applied at draw, where the tooltip's own size is known
    // and decides whether it opens below the cursor or above it.
    private static void PinTo(Point mousePos)
    {
        _x = mousePos.X;
        _y = mousePos.Y;
    }

    /// <summary>Where a card of <paramref name="w"/> x <paramref name="h"/> sits for a cursor at
    /// (<paramref name="mouseX"/>, <paramref name="mouseY"/>).
    ///
    /// <para>Below the cursor by default, ABOVE it when the whole card will not fit below. Clamping
    /// alone put a tooltip near the bottom edge on top of the row that spawned it — the action bar
    /// sits 24px off the floor, so every one of its four slots did that. The clamp still runs last,
    /// for a card taller than the viewport or a cursor at the right edge.</para></summary>
    internal static Point Place(int mouseX, int mouseY, int w, int h)
    {
        int below = mouseY + MouseOffsetY;
        int y = below + h <= UiHelper.RefH - 2 ? below : mouseY - MouseOffsetY - h;
        return new Point(
            Math.Clamp(mouseX + MouseOffsetX, 2, UiHelper.RefW - 2 - w),
            Math.Clamp(y, 2, UiHelper.RefH - 2 - h));
    }

    private static void Draw(SpriteBatch sb, SpriteFont font)
    {
        _lines.Clear();
        string header;
        bool hasIcon;
        short pic;
        short itemSheet;

        switch (_kind)
        {
            case Kind.Item when _item is not null:
                header = _item.Name?.TrimEnd() ?? "Unknown";
                BuildItemLines(_item, _slot, _me, _itemDefs, _weather);
                hasIcon = _item.Pic >= 0 && _itemsTex.Sheet(_item.ItemSheet) is not null;
                pic = _item.Pic;
                itemSheet = _item.ItemSheet;
                break;
            case Kind.Text when _text is not null:
                header = _text;   // a single-line tooltip: just the full (un-truncated) label text
                hasIcon = false;
                pic = 0;
                itemSheet = 0;
                break;
            default:
                return;
        }

        float lineH = font.LineSpacing;
        float headerW = font.MeasureString(header).X;
        float bodyW = 0f;
        for (int i = 0; i < _lines.Count; i++)
        {
            var ln = _lines[i];
            float lineW = font.MeasureString(ln.Value.Length == 0 ? ln.Label : ln.Label + ": " + ln.Value).X;
            if (lineW > bodyW) bodyW = lineW;
        }

        int headerRowH = hasIcon ? Math.Max((int)lineH, IconSize) : (int)lineH;
        int bodyRowsH = _lines.Count == 0 ? 0
            : HeaderGap + _lines.Count * (int)lineH + (_lines.Count - 1) * LineGap;

        float contentW = Math.Max(
            hasIcon ? IconSize + IconRightGap + headerW : headerW,
            bodyW);

        int w = (int)Math.Ceiling(contentW) + PadX * 2;
        int h = headerRowH + bodyRowsH + PadY * 2;

        var at = Place(_x, _y, w, h);
        _bounds = new Rectangle(at.X, at.Y, w, h);
        UiHelper.DrawFilledRect(sb, _bounds, BgColor);
        UiHelper.DrawBorder(sb, _bounds, BorderColor);

        int cx = _bounds.X + PadX;
        int cy = _bounds.Y + PadY;

        if (hasIcon)
        {
            sb.DrawItemIcon(_itemsTex, pic, itemSheet, new Rectangle(cx, cy, IconSize, IconSize), Color.White);
            float headerY = cy + (IconSize - lineH) / 2f;
            sb.DrawString(font, header, new Vector2(cx + IconSize + IconRightGap, headerY), HeaderColor);
            cy += IconSize;
        }
        else
        {
            sb.DrawString(font, header, new Vector2(cx, cy), HeaderColor);
            cy += (int)lineH;
        }

        if (_lines.Count > 0) cy += HeaderGap;

        for (int i = 0; i < _lines.Count; i++)
        {
            var ln = _lines[i];
            // No value means the line is a heading: label alone, no colon. Matches the width pass above.
            if (ln.Value.Length == 0)
            {
                sb.DrawString(font, ln.Label, new Vector2(cx, cy), LabelColor);
            }
            else
            {
                string labelText = ln.Label + ": ";
                sb.DrawString(font, labelText, new Vector2(cx, cy), LabelColor);
                float labelW = font.MeasureString(labelText).X;
                sb.DrawString(font, ln.Value, new Vector2(cx + labelW, cy), ln.Color);
            }
            cy += (int)lineH + LineGap;
        }
    }

    private static void BuildItemLines(ItemRecord item, PlayerInvSlot? slot, PlayerRecord? me,
        ItemRecord?[] itemDefs, WeatherType weather)
    {
        if (ItemRecord.IsEquipment(item.Type) && item.Durability > 0)
        {
            // A real inventory slot carries the item's actual wear; the cur/max readout is color-coded
            // by condition (white/yellow/red) exactly like the equipment panel and repair panel, so a
            // worn or broken piece reads the same everywhere. With no backing slot (e.g. a shop listing)
            // there's no wear to show, so display full (which colors white).
            int dur = slot?.Dur ?? item.Durability;
            _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Durability), $"{dur}/{item.Durability}", UiHelper.DurabilityColor(dur, item.Durability)));
        }

        if (item.Type == ItemType.Currency && slot is not null)
            _lines.Add(new Line(ClientStrings.Get(ClientStrings.Tooltip_Quantity), slot.Quantity.ToString("N0"), ValueColor));

        // ── The spell half of a scroll ───────────────────────────────────
        // A scroll is a delivery mechanism: what it teaches lives on the spell, not on the paper.
        // Appended below rather than replacing the item lines — a scroll is still a thing with a price
        // that occupies a bag slot, and the buy confirm shows both halves the same way.
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Shared window chrome for floating game panels: title bar, drag, resize, close button.
/// Panels own one instance and delegate window management to it.
/// </summary>
public sealed class DraggablePanel
{
    private Rectangle _bounds;
    // The rectangle the panel was declared with, kept so Reset Panels can put it back. Held
    // separately from _bounds because _bounds is overwritten by the persisted layout on load and by
    // every drag/resize afterwards.
    private readonly Rectangle _defaultBounds;

    private bool _dragging;
    private Point _dragOffset;
    private bool _resizing;
    private Point _resizeStartMouse;
    private Rectangle _resizeStartBounds;

    public const int TitleH = 16;
    private const int TitlePad = 4;
    private const int ResizeSize = 12;
    private int _minW;
    private int _minH;
    private readonly bool _showClose;
    private readonly bool _resizable;
    private readonly bool _movable;

    private static readonly Color PanelBg = new(10, 10, 20, 220);
    private static readonly Color CloseBg = new(120, 30, 30);
    private static readonly Color ResizeHandleColor = new(80, 80, 100);

    public Rectangle Bounds => _bounds;

    // Clamps up to the minima so a stale persisted size (saved before the panel's content grew, or
    // from an older min) can never load smaller than its content needs. The live drag/resize path
    // clamps separately in HandleDragResize.
    public void SetBounds(Rectangle bounds) =>
        _bounds = new Rectangle(bounds.X, bounds.Y, Math.Max(bounds.Width, _minW), Math.Max(bounds.Height, _minH));

    /// <summary>Drop the panel back to the position and size it was declared with, discarding whatever
    /// the player dragged or resized it to. Goes through SetBounds so a default that predates a later
    /// minimum still clamps up rather than loading too small.</summary>
    public void ResetBounds() => SetBounds(_defaultBounds);
    public void SetMinH(int minH) => _minH = Math.Max(minH, 40);
    // Area below the title bar where panel content is drawn.
    public Rectangle ContentBounds =>
        new(_bounds.X, _bounds.Y + TitleH, _bounds.Width, _bounds.Height - TitleH);

    // True for exactly one frame when the close button is clicked.
    public bool WasClosed { get; private set; }

    // True for exactly one frame when a drag or resize just completed (mouse released).
    public bool LayoutChanged { get; private set; }

    public DraggablePanel(Rectangle defaultBounds, int minH = 60, int minW = 160,
                          bool showClose = true, bool resizable = true, bool movable = true)
    {
        _bounds = defaultBounds;
        _defaultBounds = defaultBounds;
        _minH = minH;
        _minW = minW;
        _showClose = showClose;
        _resizable = resizable;
        _movable = movable;
    }

    public bool ContainsMouse(Point mousePos) => _bounds.Contains(mousePos);

    /// <summary>Whether hovering <paramref name="mouse"/> should show the diagonal NW–SE resize cursor:
    /// the panel is resizable AND either a resize drag is already in progress (so the cursor doesn't snap
    /// back to Arrow when the mouse is pulled off the handle mid-drag) or the mouse is over the bottom-right
    /// resize handle. Pure geometry (no <see cref="InputState"/>) so the cursor rule is unit-testable; the
    /// Update path forwards <c>input.MousePosition</c> here.</summary>
    public bool WantsResizeCursor(Point mouse) => _resizable && (_resizing || ResizeHandleRect.Contains(mouse));

    public void Update(InputState input)
    {
        WasClosed = false;
        LayoutChanged = false;
        HandleDragResize(input);

        // Show the NW–SE diagonal-arrow cursor whenever the user could grab the bottom-right
        // resize triangle (or while a resize drag is in progress). Routed through UiHelper's
        // per-frame cursor bus to coexist with link widgets that may also want a non-default
        // cursor in the same frame.
        if (WantsResizeCursor(input.MousePosition))
            UiHelper.RequestResizeNwseCursor();

        // Close button. Pointer capture already keeps a drag/resize release (or a child-widget drag like
        // a scrollbar/slider) from reaching here — the captured owner eats it — so no extra suppression
        // guard is needed; press-origin (IsClickIn) means only a genuine press+release on the X closes
        // the panel.
        if (_showClose && input.IsClickIn(CloseRect))
        {
            WasClosed = true;
            input.ConsumeMouseClick();
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, string title, bool isActive = false)
    {
        UiHelper.DrawFilledRect(sb, _bounds, PanelBg);
        UiHelper.DrawBorder(sb, _bounds, Color.DimGray);

        // Title bar — brighter when this panel has focus
        Color titleBg = isActive ? UiHelper.PanelTitleActiveBg : UiHelper.PanelTitleBg;
        UiHelper.DrawFilledRect(sb, TitleBarRect, titleBg);
        UiHelper.DrawLabel(sb, font, title, new Vector2(_bounds.X + TitlePad, _bounds.Y + 1), Color.Gold, _bounds.Width - (_showClose ? TitleH : 0) - TitlePad * 2);

        // Close button (top-right corner of title bar) — hidden for uncloseable panels (e.g. the death overlay).
        if (_showClose)
        {
            var close = CloseRect;
            UiHelper.DrawFilledRect(sb, close, CloseBg);
            sb.DrawString(font, "X", new Vector2(close.X + TitlePad, close.Y + 1), Color.White);
        }
    }

    /// <summary>Draws the resize handle on top of all panel content. Call this after drawing all children.</summary>
    public void DrawOverlay(SpriteBatch sb) { if (_resizable) DrawResizeHandle(sb); }

    private Rectangle TitleBarRect => new(_bounds.X, _bounds.Y, _bounds.Width, TitleH);
    private Rectangle CloseRect => new(_bounds.Right - TitleH, _bounds.Y, TitleH, TitleH);
    private Rectangle ResizeHandleRect => new(_bounds.Right - ResizeSize, _bounds.Bottom - ResizeSize, ResizeSize, ResizeSize);

    private void DrawResizeHandle(SpriteBatch sb)
    {
        var r = ResizeHandleRect;
        // Bottom-right corner triangle: each row is 1px wider, right-aligned.
        // Row 0 (top) = 1px at far right edge; row 11 (bottom) = 12px spanning full handle width.
        for (int row = 0; row < ResizeSize; row++)
            UiHelper.DrawFilledRect(sb, new Rectangle(r.Right - (row + 1), r.Y + row, row + 1, 1), ResizeHandleColor);
    }

    private void HandleDragResize(InputState input)
    {
        var mouse = input.MousePosition;

        if (!_dragging && !_resizing)
        {
            // Start a drag/resize on a press-edge over the handle. CaptureMouse claims the pointer so
            // the release that ends it can't activate a child control, and a lower-Z panel can't grab
            // the same press (IsPressIn is capture-gated once we own it).
            if (_resizable && input.IsPressIn(ResizeHandleRect))
            {
                _resizing = true;
                _resizeStartMouse = mouse;
                _resizeStartBounds = _bounds;
                input.CaptureMouse(this);
            }
            else if (_movable && input.IsPressIn(TitleBarRect) && !(_showClose && CloseRect.Contains(mouse)))
            {
                _dragging = true;
                _dragOffset = new Point(mouse.X - _bounds.X, mouse.Y - _bounds.Y);
                input.CaptureMouse(this);
            }
        }

        if ((_dragging || _resizing) && !input.IsMouseDown())
        {
            LayoutChanged = true;   // drag/resize just completed; capture auto-releases
            _dragging = false;
            _resizing = false;
            return;
        }

        if (_dragging)
        {
            int nx = Math.Clamp(mouse.X - _dragOffset.X, 0, UiHelper.RefW - _bounds.Width);
            int ny = Math.Clamp(mouse.Y - _dragOffset.Y, 0, UiHelper.RefH - _bounds.Height);
            _bounds = new Rectangle(nx, ny, _bounds.Width, _bounds.Height);
        }
        else if (_resizing)
        {
            int dx = mouse.X - _resizeStartMouse.X;
            int dy = mouse.Y - _resizeStartMouse.Y;
            int newW = Math.Clamp(_resizeStartBounds.Width + dx, _minW, UiHelper.RefW - _resizeStartBounds.X);
            int newH = Math.Clamp(_resizeStartBounds.Height + dy, _minH, UiHelper.RefH - _resizeStartBounds.Y);
            _bounds = new Rectangle(_resizeStartBounds.X, _resizeStartBounds.Y, newW, newH);
        }
    }
}

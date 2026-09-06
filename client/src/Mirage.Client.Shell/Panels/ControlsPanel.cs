using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using System.IO;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Floating, resizeable panel that shows the in-game control-scheme reference as a single
/// image per tab — Keyboard always, Xbox/PlayStation only when a gamepad is connected and
/// "Use Gamepad" is enabled. Opened from the [Controls] link inside the Help panel.
/// </summary>
public sealed class ControlsPanel : IGamePanel
{
    private readonly DraggablePanel _panel;
    private readonly Texture2D? _keyboardTex;
    private readonly Texture2D? _xboxTex;
    private readonly Texture2D? _psTex;

    private const int TabStripH = 26;
    private const int TabGap = 2;
    private const int MinTabW = 80;

    // The control-scheme images are authored at this fixed size (Art/generate_control_images.py in the .Tools repo).
    private const int RefImageW = 800;
    private const int RefImageH = 600;
    // A residual letterbox bar this small (px) is an integer-rounding sliver, not a real aspect
    // mismatch, so the picture is snapped to fill the body rather than show a hairline black edge.
    private const int AspectSnapPx = 2;

    private int _activeTab; // 0 = Keyboard, 1 = Gamepad
    private Point _lastMousePos;

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen) _activeTab = 0;
    }
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    public ControlsPanel(GraphicsDevice graphics)
    {
        _keyboardTex = TryLoad(graphics, AppPaths.Asset("assets", "graphics", "ControlsKeyboard.png"));
        _xboxTex = TryLoad(graphics, AppPaths.Asset("assets", "graphics", "ControlsXbox.png"));
        _psTex = TryLoad(graphics, AppPaths.Asset("assets", "graphics", "ControlsPlaystation.png"));

        // Default size: the content area below the title bar and tab strip is sized to the
        // images' 4:3 aspect, so the picture fills it with no letterbox bars. Width is chosen
        // large enough to read comfortably while keeping a margin inside the 800×600 viewport.
        const int defW = 600;
        int defBodyH = defW * RefImageH / RefImageW;             // 4:3 content height (450)
        // Title bar + tab strip + 4:3 body, then rounded to the nearest 5 (the <=2px aspect change from
        // rounding is within AspectSnapPx, so the image still snaps to fill with no visible letterbox).
        int defH = (DraggablePanel.TitleH + TabStripH + defBodyH + 2) / 5 * 5;
        int defX = (UiHelper.RefW - defW) / 2;
        int defY = (UiHelper.RefH - defH) / 2;
        _panel = new DraggablePanel(new Rectangle(defX, defY, defW, defH),
            minH: DraggablePanel.TitleH + TabStripH + 80, minW: 240);
    }

    public void Update(InputState input, bool isActive, bool gamepadVisible, bool isPlayStation)
    {
        if (!IsOpen) return;
        _lastMousePos = input.MousePosition;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        // If the gamepad goes away mid-session while the gamepad tab is active, snap back to keyboard
        // so the tab strip doesn't show an orphaned active slot.
        if (!gamepadVisible && _activeTab != 0) _activeTab = 0;

        if (input.IsMouseClicked())
        {
            var tabs = ComputeTabRects(gamepadVisible);
            for (int i = 0; i < tabs.Length; i++)
            {
                if (input.IsClickIn(tabs[i]))
                {
                    _activeTab = i;
                    input.ConsumeMouseClick();
                    break;
                }
            }
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, long nowMs, bool gamepadVisible, bool isPlayStation, bool isActive)
    {
        if (!IsOpen) return;
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.Common_ControlsHeader), isActive);

        var content = _panel.ContentBounds;

        var stripRect = new Rectangle(content.X, content.Y, content.Width, TabStripH);
        UiHelper.DrawFilledRect(sb, stripRect, UiHelper.PanelTitleBg);

        var tabs = ComputeTabRects(gamepadVisible);
        string[] labels = TabLabels(gamepadVisible, isPlayStation);
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == _activeTab;
            bool hovered = !active && tabs[i].Contains(_lastMousePos);
            TabStrip.DrawCenteredTab(sb, font, tabs[i], labels[i], active, hovered);
        }

        var body = new Rectangle(content.X, content.Y + TabStripH, content.Width, content.Height - TabStripH);
        UiHelper.DrawFilledRect(sb, body, Color.Black);

        Texture2D? tex = _activeTab == 1
            ? (isPlayStation ? _psTex : _xboxTex)
            : _keyboardTex;
        if (tex is not null && body.Width > 0 && body.Height > 0)
        {
            // Aspect-preserve letterbox inside body so the picture never stretches.
            float texAspect = (float)tex.Width / tex.Height;
            float bodyAspect = (float)body.Width / body.Height;
            int drawW, drawH;
            if (bodyAspect > texAspect)
            {
                drawH = body.Height;
                drawW = (int)(drawH * texAspect);
            }
            else
            {
                drawW = body.Width;
                drawH = (int)(drawW / texAspect);
            }
            // When the body is sized (near-)exactly to the image's aspect, the integer truncation
            // above can leave a 1-2px black sliver on one edge. Snap the picture to fill the body
            // so a 4:3 panel shows no hairline letterbox; bodies that are genuinely off-aspect (a
            // wider bar than this) still letterbox normally.
            if (body.Width - drawW <= AspectSnapPx) drawW = body.Width;
            if (body.Height - drawH <= AspectSnapPx) drawH = body.Height;
            int drawX = body.X + (body.Width - drawW) / 2;
            int drawY = body.Y + (body.Height - drawH) / 2;
            sb.Draw(tex, new Rectangle(drawX, drawY, drawW, drawH), Color.White);
        }

        _panel.DrawOverlay(sb);
    }

    private Rectangle[] ComputeTabRects(bool gamepadVisible)
    {
        var content = _panel.ContentBounds;
        int tabCount = gamepadVisible ? 2 : 1;
        int availW = content.Width - TabGap * (tabCount + 1);
        int tabW = System.Math.Max(MinTabW, availW / tabCount);
        int totalW = tabW * tabCount + TabGap * (tabCount - 1);
        int startX = content.X + (content.Width - totalW) / 2;
        int y = content.Y + 2;
        int h = TabStripH - 4;
        var rects = new Rectangle[tabCount];
        for (int i = 0; i < tabCount; i++)
            rects[i] = new Rectangle(startX + i * (tabW + TabGap), y, tabW, h);
        return rects;
    }

    private static string[] TabLabels(bool gamepadVisible, bool isPlayStation)
    {
        if (!gamepadVisible) return new[] { ClientStrings.Get(ClientStrings.ControlsPanel_KeyboardTab) };
        return new[] { ClientStrings.Get(ClientStrings.ControlsPanel_KeyboardTab), isPlayStation ? ClientStrings.Get(ClientStrings.ControlsPanel_PlayStationTab) : ClientStrings.Get(ClientStrings.ControlsPanel_XboxTab) };
    }

    private static Texture2D? TryLoad(GraphicsDevice graphics, string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(graphics, stream);
        }
        catch { return null; }
    }
}

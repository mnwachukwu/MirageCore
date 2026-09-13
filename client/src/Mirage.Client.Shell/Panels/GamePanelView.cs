using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// One screen a game declared, drawn by a client that has never heard of it.
///
/// <para><b>Nothing here knows what it is showing.</b> The body is whatever
/// <see cref="DisplayField"/> rows the game declared for this panel's surface, read live off the
/// player's own attributes; the buttons carry action ids straight back to the server. A value moving
/// redraws because the attribute moved, so there is no panel state to keep in step and nothing to
/// refresh.</para>
///
/// <para><b>One at a time.</b> A declared panel occupies a single slot, so opening a second closes the
/// first. That is a real limit and a deliberate one: a game that could open arbitrarily many windows on a
/// client it does not control is a game that can bury the player's own.</para>
/// </summary>
public sealed class GamePanelView : IGamePanel
{
    private const int DefaultW = 280, DefaultH = 220;
    private const int Pad = 8;
    private const int RowH = 16;
    private const int BtnH = 20;
    private const int BtnGap = 4;

    private readonly DraggablePanel _panel = new(new Rectangle(60, 60, DefaultW, DefaultH), minH: 120, minW: 200);
    private readonly List<Button> _buttons = new();

    private GamePanel? _declared;
    private InputState _input = new();

    public bool IsOpen { get; private set; }

    /// <summary>The declared panel on show, or blank for none. A key that opens a panel has to know
    /// whether it is looking at its own panel or somebody else's before it decides to close it.</summary>
    public string OpenId => IsOpen ? _declared?.Id ?? string.Empty : string.Empty;
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    /// <summary>Show the panel a game declared under <paramref name="panelId"/>. An id naming nothing
    /// opens nothing rather than an empty window — a game that removed a panel should not leave a blank
    /// frame behind on a client still offering the old action.</summary>
    public void Open(ClientState state, string panelId)
    {
        if (state.Panels.Find(panelId) is not { } panel) return;

        _declared = panel;
        int w = panel.Width > 0 ? panel.Width : DefaultW;
        int h = panel.Height > 0 ? panel.Height : DefaultH;
        _panel.SetBounds(new Rectangle(_panel.Bounds.X, _panel.Bounds.Y, w, h));
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Toggle() => IsOpen = !IsOpen;

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen || _declared is not { } panel) return;

        _input = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        LayoutButtons(_panel.ContentBounds, panel.Buttons.Count);
        for (int i = 0; i < panel.Buttons.Count; i++)
        {
            if (!_buttons[i].IsClicked(input)) continue;

            // The id goes back exactly as it arrived. Whether the verb applies, and what it does, are
            // the server's to say.
            sender.SendInvokeAction(panel.Buttons[i].ActionId, state.Map is null ? 0 : state.CenterMapNum,
                                    state.Me.X, state.Me.Y);
            return;   // one click per frame; the bounds are stale if the panel closed itself
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool active)
    {
        if (!IsOpen || _declared is not { } panel) return;

        string title = panel.TitleKey.Length > 0
            ? ClientStrings.GetOrFallback(panel.TitleKey, panel.TitleKey)
            : state.GameName;
        _panel.Draw(sb, font, title, active);

        var content = _panel.ContentBounds;
        int y = content.Y + Pad;
        int width = content.Width - Pad * 2;
        int floor = content.Bottom - Pad - panel.Buttons.Count * (BtnH + BtnGap);

        foreach (var row in state.DisplayFields.Project(panel.Surface, state.Me.Attributes).Rows)
        {
            if (y + RowH > floor) break;   // the buttons keep their place; the rows take what is left
            DrawRow(sb, font, row, content.X + Pad, y, width);
            y += RowH;
        }

        LayoutButtons(content, panel.Buttons.Count);
        for (int i = 0; i < panel.Buttons.Count; i++)
        {
            _buttons[i].Label = ClientStrings.GetOrFallback(panel.Buttons[i].LabelKey, panel.Buttons[i].LabelKey);
            _buttons[i].Draw(sb, font, _input);
        }
    }

    private static void DrawRow(SpriteBatch sb, SpriteFont font, DisplayRow row, int x, int y, int width)
    {
        var color = new Color(GameColor.RedOf(row.Color), GameColor.GreenOf(row.Color), GameColor.BlueOf(row.Color));
        string label = row.LabelKey is { Length: > 0 } key ? ClientStrings.GetOrFallback(key, key) : "";

        switch (row.Style)
        {
            case DisplayStyle.Heading:
                UiHelper.DrawLabel(sb, font, label, new Vector2(x, y), UiHelper.DlgLabelColor, width);
                break;

            case DisplayStyle.Meter:
                UiHelper.DrawMeter(sb, font, new Rectangle(x, y, width, RowH - 2), (float)row.Fill,
                    color, Color.Black,
                    label.Length > 0 ? UiHelper.MeterText(label, (long)row.Value, (long)row.Max) : row.Text,
                    Color.White);
                break;

            case DisplayStyle.Badge:
                UiHelper.DrawLabel(sb, font, row.Text, new Vector2(x, y), color, width);
                break;

            default:
                if (label.Length > 0)
                    UiHelper.DrawLabel(sb, font, label, new Vector2(x, y), UiHelper.DlgLabelColor, width / 2f);
                float valueW = font.MeasureString(row.Text).X;
                UiHelper.DrawLabel(sb, font, row.Text, new Vector2(x + width - valueW, y), color, width / 2f);
                break;
        }
    }

    private void LayoutButtons(Rectangle content, int count)
    {
        while (_buttons.Count < count) _buttons.Add(new Button());
        int top = content.Bottom - Pad - count * (BtnH + BtnGap);
        for (int i = 0; i < count; i++)
        {
            _buttons[i].Bounds = new Rectangle(content.X + Pad, top + i * (BtnH + BtnGap),
                                               content.Width - Pad * 2, BtnH);
        }
    }
}

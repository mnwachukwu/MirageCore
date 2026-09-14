using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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
/// <para><b>A panel that ASKS carries the one piece of state here.</b> What somebody has typed and not
/// yet sent is not derived from anything, so it is held until it is sent or the panel closes. Closing
/// clears it: a half-filled form reopening with yesterday's answers in it is worse than an empty one,
/// because it looks like it was saved.</para>
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
    private const int InputH = 20;

    private readonly DraggablePanel _panel = new(new Rectangle(60, 60, DefaultW, DefaultH), minH: 120, minW: 200);
    private readonly List<Button> _buttons = new();

    // One control per declared input, built when the panel opens and thrown away when it closes.
    private readonly List<TextInputField> _texts = new();
    private readonly List<Checkbox> _flags = new();
    private readonly List<DropDown> _choices = new();
    private readonly Button _send = new();
    private readonly List<Rectangle> _rects = new();

    private GamePanel? _declared;
    private InputState _input = new();
    private int _focused = -1;

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
        Build(panel);
        IsOpen = true;
    }

    /// <summary>A control per declared input, in the order the game declared them.</summary>
    private void Build(GamePanel panel)
    {
        _texts.Clear();
        _flags.Clear();
        _choices.Clear();
        _focused = -1;

        foreach (PanelInput input in panel.Inputs)
        {
            var text = new TextInputField { MaxLength = input.MaxLength > 0 ? input.MaxLength : 64 };
            var flag = new Checkbox();
            var choice = new DropDown();

            if (input.Kind is FieldKind.Choice)
            {
                choice.Items.AddRange(input.Choices);
                if (choice.Items.Count > 0) choice.SelectedIndex = 0;
            }

            _texts.Add(text);
            _flags.Add(flag);
            _choices.Add(choice);
        }
    }

    public void Close()
    {
        IsOpen = false;

        // What was typed and not sent goes with the window it was typed into.
        Emptied();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else IsOpen = true;
    }

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen || _declared is not { } panel) return;

        _input = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            Close();
            return;
        }

        var content = _panel.ContentBounds;
        LayoutRows(content, panel, state);

        if (panel.Inputs.Count > 0) Filling(input, panel);

        LayoutButtons(content, panel);

        if (Sends(panel) && _send.IsClicked(input))
        {
            sender.SendGameMessage(panel.Asks, Filled(panel));
            Emptied();
            return;
        }

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

    /// <summary>Whether this panel has a message to send, which is the button the inputs feed.</summary>
    private static bool Sends(GamePanel panel) => panel.Asks.Length > 0 && panel.Inputs.Count > 0;

    /// <summary>
    /// Typing, ticking and picking.
    ///
    /// <para>Each control consumes the click it handles, which is what keeps a click on an open
    /// drop-down's list from also pressing the button its popup is drawn over.</para>
    /// </summary>
    private void Filling(InputState input, GamePanel panel)
    {
        long nowMs = Environment.TickCount64;

        for (int i = 0; i < panel.Inputs.Count && i < _rects.Count; i++)
        {
            var control = Control(_rects[i]);

            switch (panel.Inputs[i].Kind)
            {
                case FieldKind.Choice:
                    _choices[i].Update(input, control);
                    break;

                case FieldKind.Flag:
                    _flags[i].Bounds = control;
                    _flags[i].Update(input);
                    break;

                default:
                    if (input.IsMouseJustPressed() && control.Contains(input.MousePosition))
                    {
                        _focused = i;
                        _texts[i].HandleMouseClick(input.MousePosition.X,
                            input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift));
                    }

                    break;
            }
        }

        if (_focused >= 0 && _focused < _texts.Count) _texts[_focused].Feed(input, nowMs);
    }

    /// <summary>
    /// What the player put in, in the shapes the model asked for.
    ///
    /// <para>A number that does not parse travels as zero rather than not travelling: the field is one
    /// the model declared, so leaving it out would reach <c>Has</c> as "they did not fill this in",
    /// which is not what typing nonsense into it means.</para>
    /// </summary>
    private Dictionary<string, object> Filled(GamePanel panel)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        for (int i = 0; i < panel.Inputs.Count; i++)
        {
            PanelInput input = panel.Inputs[i];

            values[input.Field] = input.Kind switch
            {
                FieldKind.Integer => Bounded(Whole(_texts[i].Text), input),
                FieldKind.Real => double.TryParse(_texts[i].Text, out double real) ? real : 0d,
                FieldKind.Flag => _flags[i].Checked,
                FieldKind.Choice => _choices[i].SelectedItem ?? string.Empty,
                _ => _texts[i].Text,
            };
        }

        return values;
    }

    private static long Whole(string text) => long.TryParse(text, out long whole) ? whole : 0L;

    private static long Bounded(long value, PanelInput input) =>
        input.Min == input.Max ? value : Math.Clamp(value, input.Min, input.Max);

    /// <summary>
    /// The form, empty.
    ///
    /// <para>A panel that keeps the last answer in it invites the same one twice. Every control goes
    /// back, not only the ones that look filled in — a drop-down left on the last pick while the box
    /// beside it cleared reads as one of them having failed to clear.</para>
    /// </summary>
    private void Emptied()
    {
        foreach (TextInputField text in _texts) text.Clear();
        foreach (Checkbox flag in _flags) flag.Checked = false;
        foreach (DropDown choice in _choices)
        {
            choice.SelectedIndex = choice.Items.Count > 0 ? 0 : -1;
        }

        _focused = -1;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool active)
    {
        if (!IsOpen || _declared is not { } panel) return;

        string title = panel.TitleKey.Length > 0
            ? ClientStrings.GetOrFallback(panel.TitleKey, panel.TitleKey)
            : state.GameName;
        _panel.Draw(sb, font, title, active, panel.Icon);

        var content = _panel.ContentBounds;
        int width = content.Width - Pad * 2;
        LayoutRows(content, panel, state);

        int at = content.Y + Pad;
        int floor = Floor(content, panel);

        foreach (var row in state.DisplayFields.Project(panel.Surface, state.Me.Attributes).Rows)
        {
            if (at + RowH > floor) break;   // the buttons keep their place; the rows take what is left
            DrawRow(sb, font, row, content.X + Pad, at, width);
            at += RowH;
        }

        long nowMs = Environment.TickCount64;
        for (int i = 0; i < panel.Inputs.Count && i < _rects.Count; i++)
        {
            DrawInput(sb, font, panel.Inputs[i], i, _rects[i], nowMs);
        }

        LayoutButtons(content, panel);

        for (int i = 0; i < panel.Buttons.Count; i++)
        {
            _buttons[i].Label = ClientStrings.GetOrFallback(panel.Buttons[i].LabelKey, panel.Buttons[i].LabelKey);
            _buttons[i].Draw(sb, font, _input);
        }

        if (Sends(panel))
        {
            _send.Label = ClientStrings.GetOrFallback(panel.SendLabelKey, panel.SendLabelKey);
            _send.Draw(sb, font, _input);
        }

        // Last, and after the buttons, because an open list is drawn over whatever it covers.
        // DrawPopup draws nothing for a closed one.
        for (int i = 0; i < panel.Inputs.Count && i < _rects.Count; i++)
        {
            if (panel.Inputs[i].Kind is FieldKind.Choice)
            {
                _choices[i].DrawPopup(sb, font, Control(_rects[i]), _input);
            }
        }
    }

    /// <summary>One input row: its caption on the left, its control on the right.</summary>
    private void DrawInput(
        SpriteBatch sb, SpriteFont font, PanelInput input, int at, Rectangle row, long nowMs)
    {
        string label = ClientStrings.GetOrFallback(input.LabelKey, input.LabelKey);
        UiHelper.DrawLabel(sb, font, label, new Vector2(row.X, row.Y + 2),
                           UiHelper.DlgLabelColor, row.Width / 2f);

        var control = Control(row);

        switch (input.Kind)
        {
            case FieldKind.Choice:
                _choices[at].DrawHeader(sb, font, control, _input);
                break;

            case FieldKind.Flag:
                _flags[at].Bounds = control;
                _flags[at].Draw(sb, font, _input);
                break;

            default:
                _texts[at].Draw(sb, font, control, _focused == at, nowMs);
                break;
        }
    }

    /// <summary>The right-hand half of a row, where the control sits.</summary>
    private static Rectangle Control(Rectangle row) =>
        new(row.X + row.Width / 2, row.Y, row.Width - row.Width / 2, row.Height);

    /// <summary>Where the buttons begin, which is what the rows above them may not cross.</summary>
    private static int Floor(Rectangle content, GamePanel panel)
    {
        int count = Sends(panel) ? panel.Buttons.Count + 1 : panel.Buttons.Count;
        return content.Bottom - Pad - count * (BtnH + BtnGap);
    }

    /// <summary>
    /// Places the input rows under the display rows.
    ///
    /// <para>Both <c>Update</c> and <c>Draw</c> ask for this rather than each working it out, because
    /// a control drawn in one place and hit-tested in another is a control that looks broken.</para>
    /// </summary>
    private void LayoutRows(Rectangle content, GamePanel panel, ClientState state)
    {
        _rects.Clear();

        int width = content.Width - Pad * 2;
        int floor = Floor(content, panel);
        int y = content.Y + Pad;

        foreach (var _ in state.DisplayFields.Project(panel.Surface, state.Me.Attributes).Rows)
        {
            if (y + RowH > floor) break;
            y += RowH;
        }

        foreach (var _ in panel.Inputs)
        {
            if (y + InputH > floor) break;
            _rects.Add(new Rectangle(content.X + Pad, y, width, InputH - 2));
            y += InputH;
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

    /// <summary>
    /// The buttons along the bottom, with the send button last where there is one.
    ///
    /// <para>⚠ The send button takes the last slot rather than a place of its own. Laid out separately
    /// it is laid out nowhere — a button with no bounds draws at the origin and is never clicked,
    /// which looks exactly like a panel that ignores its own form.</para>
    /// </summary>
    private void LayoutButtons(Rectangle content, GamePanel panel)
    {
        int count = Sends(panel) ? panel.Buttons.Count + 1 : panel.Buttons.Count;

        while (_buttons.Count < count) _buttons.Add(new Button());

        int top = content.Bottom - Pad - count * (BtnH + BtnGap);
        for (int i = 0; i < count; i++)
        {
            _buttons[i].Bounds = new Rectangle(content.X + Pad, top + i * (BtnH + BtnGap),
                                               content.Width - Pad * 2, BtnH);
        }

        if (Sends(panel)) _send.Bounds = _buttons[count - 1].Bounds;
    }
}

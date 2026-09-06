using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Panels;

/// <summary>Stat point allocation panel.
/// <para>Stage-and-confirm: [+] stages a pending buy locally and [-] takes one back (shown as "(+N)"
/// beside the stat, and the Points row as "spent/acquired"); nothing is sent until Confirm, which commits
/// the whole allocation atomically via one <see cref="ClientPacketSender.SendTrainStats"/>. Reset discards
/// every staged buy at once, as does closing the panel.</para>
/// <para>Only a STAGED point can be taken back. A stat the server has already been told about is its to
/// report, so [-] stops at the committed value rather than reaching below it.</para></summary>
public sealed class TrainingPanel : IGamePanel
{
    // Default bounds for a brand-new character with no saved layout.  Height fits four stat rows, the
    // Points row, and the Confirm/Reset row; DraggablePanel clamps any restored layout up to minH so an
    // older saved height can't clip the new bottom row.
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 210, 190), minH: 188);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    // Closing always discards staged buys, so reopening starts clean.
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (!IsOpen) ClearPending();
    }

    private readonly Button _addStrBtn = new() { Label = "+" };
    private readonly Button _addDefBtn = new() { Label = "+" };
    private readonly Button _addSpdBtn = new() { Label = "+" };
    private readonly Button _addIntBtn = new() { Label = "+" };
    private readonly Button _subStrBtn = new() { Label = "-" };
    private readonly Button _subDefBtn = new() { Label = "-" };
    private readonly Button _subSpdBtn = new() { Label = "-" };
    private readonly Button _subIntBtn = new() { Label = "-" };
    private readonly Button _confirmBtn = new();
    private readonly Button _resetBtn = new();
    private InputState _input = new();

    // BtnW is the width of the [-][+] PAIR, so the stat label keeps the room it has: the two step
    // buttons and the gap between them divide that span rather than widening it.
    private const int BtnW = 50;
    private const int StepBtnW = 23;
    private const int StepGap = 4;
    private const int BtnH = 22;
    private const int RowH = 28;
    private const int ActionBtnH = 24;   // Confirm / Reset row
    private const int ActionPad = 4;
    private const int ActionGap = 4;
    private static readonly Color PointsRowBg = new(20, 20, 8, 235);

    // Staged (pending) point buys, held locally until Confirm.  _awaitingConfirm gates input from the
    // Confirm click until the authoritative SendStats lands (detected by Points changing from the
    // _pointsAtConfirm snapshot), which is when the staged buys clear and the committed stats show.
    private int _pendingStr, _pendingDef, _pendingSpd, _pendingInt;
    private bool _awaitingConfirm;
    private int _pointsAtConfirm;
    private int PendingSpent => _pendingStr + _pendingDef + _pendingSpd + _pendingInt;

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            ClearPending();
            return;
        }

        var c = _panel.ContentBounds;
        SetButtonBounds(c);
        _confirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
        _resetBtn.Label = ClientStrings.Get(ClientStrings.TrainingPanel_ResetButton);

        int points = state.Me?.Points ?? 0;

        // A Confirm's authoritative SendStats has arrived once Points differs from the pre-send snapshot:
        // me.* already includes the gains, so drop the staged buys (the "(+N)" collapses seamlessly).
        if (_awaitingConfirm && points != _pointsAtConfirm) ClearPending();

        int available = points - PendingSpent;
        bool canAllocate = available > 0 && !_awaitingConfirm;
        _addStrBtn.Enabled = _addDefBtn.Enabled = _addSpdBtn.Enabled = _addIntBtn.Enabled = canAllocate;
        // A stat can give back only what THIS panel staged into it, so each [-] reads its own row.
        _subStrBtn.Enabled = _pendingStr > 0 && !_awaitingConfirm;
        _subDefBtn.Enabled = _pendingDef > 0 && !_awaitingConfirm;
        _subSpdBtn.Enabled = _pendingSpd > 0 && !_awaitingConfirm;
        _subIntBtn.Enabled = _pendingInt > 0 && !_awaitingConfirm;
        bool hasPending = PendingSpent > 0 && !_awaitingConfirm;
        _confirmBtn.Enabled = hasPending;
        _resetBtn.Enabled = hasPending;

        if (_addStrBtn.IsClicked(input)) _pendingStr++;
        if (_addDefBtn.IsClicked(input)) _pendingDef++;
        if (_addSpdBtn.IsClicked(input)) _pendingSpd++;
        if (_addIntBtn.IsClicked(input)) _pendingInt++;
        // The Enabled gates above are the floor: a disabled button never reports a click.
        if (_subStrBtn.IsClicked(input)) _pendingStr--;
        if (_subDefBtn.IsClicked(input)) _pendingDef--;
        if (_subSpdBtn.IsClicked(input)) _pendingSpd--;
        if (_subIntBtn.IsClicked(input)) _pendingInt--;

        if (_confirmBtn.IsClicked(input))
        {
            _pointsAtConfirm = points;
            _awaitingConfirm = true;
            sender.SendTrainStats(_pendingStr, _pendingDef, _pendingInt, _pendingSpd);
        }
        if (_resetBtn.IsClicked(input)) ClearPending();
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive = false)
    {
        if (!IsOpen) return;

        var me = state.Me;
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.TrainingPanel_Title), isActive);

        // Opaque strips behind each stat row for readability (drawn after background, before text).
        var c = _panel.ContentBounds;
        for (int row = 0; row < 4; row++)
            UiHelper.DrawFilledRect(sb, new Rectangle(c.X + 2, c.Y + 2 + RowH * row, c.Width - 4, RowH - 2), UiHelper.StatRowBg);

        SetButtonBounds(c);

        int str = me?.Str ?? 0, def = me?.Def ?? 0, spd = me?.Spd ?? 0, @int = me?.Int ?? 0, points = me?.Points ?? 0;

        float statW = c.Width - BtnW - 12;
        UiHelper.DrawLabel(sb, font, StatText(ClientStrings.TrainingPanel_StrFormat, str, _pendingStr), new Vector2(c.X + 4, c.Y + 4 + RowH * 0), Color.White, statW);
        UiHelper.DrawLabel(sb, font, StatText(ClientStrings.TrainingPanel_IntFormat, @int, _pendingInt), new Vector2(c.X + 4, c.Y + 4 + RowH * 1), Color.White, statW);
        UiHelper.DrawLabel(sb, font, StatText(ClientStrings.TrainingPanel_DefFormat, def, _pendingDef), new Vector2(c.X + 4, c.Y + 4 + RowH * 2), Color.White, statW);
        UiHelper.DrawLabel(sb, font, StatText(ClientStrings.TrainingPanel_SpdFormat, spd, _pendingSpd), new Vector2(c.X + 4, c.Y + 4 + RowH * 3), Color.White, statW);

        // Points row — always shown.  While buys are staged it reads "spent/acquired" (N/M); it collapses
        // back to a single number after Confirm or Reset.
        if (c.Y + 2 + RowH * 4 + (RowH - 2) <= c.Bottom)
        {
            UiHelper.DrawFilledRect(sb, new Rectangle(c.X + 2, c.Y + 2 + RowH * 4, c.Width - 4, RowH - 2), PointsRowBg);
            int spent = PendingSpent;
            string pointsVal = spent > 0 ? $"{spent}/{points}" : points.ToString();
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.TrainingPanel_PointsFormat, ("Value", pointsVal)), new Vector2(c.X + 4, c.Y + 4 + RowH * 4), Color.Yellow, c.Width - 8);
        }

        // Both step buttons are always drawn; Button.Enabled grays [+] when no points remain to allocate
        // and [-] when that row has nothing staged to give back.
        _addStrBtn.Draw(sb, font, _input);
        _addDefBtn.Draw(sb, font, _input);
        _addSpdBtn.Draw(sb, font, _input);
        _addIntBtn.Draw(sb, font, _input);
        _subStrBtn.Draw(sb, font, _input);
        _subDefBtn.Draw(sb, font, _input);
        _subSpdBtn.Draw(sb, font, _input);
        _subIntBtn.Draw(sb, font, _input);

        // Confirm / Reset row below the Points row.
        _confirmBtn.Draw(sb, font, _input);
        _resetBtn.Draw(sb, font, _input);

        _panel.DrawOverlay(sb);
    }

    // "STR:  12" normally; "STR:  12 (+3)" while 3 points are staged into it.
    private static string StatText(string format, int value, int pending)
        => ClientStrings.Format(format, ("Value", pending > 0 ? $"{value} (+{pending})" : value.ToString()));

    private void ClearPending()
    {
        _pendingStr = _pendingDef = _pendingSpd = _pendingInt = 0;
        _awaitingConfirm = false;
    }

    private void SetButtonBounds(Rectangle c)
    {
        // [-] then [+], with [+] holding the right edge it has always been aligned to.
        int bx = c.Right - BtnW - 4;
        int ax = bx + StepBtnW + StepGap;
        _subStrBtn.Bounds = new Rectangle(bx, c.Y + 2 + RowH * 0, StepBtnW, BtnH);
        _subIntBtn.Bounds = new Rectangle(bx, c.Y + 2 + RowH * 1, StepBtnW, BtnH);
        _subDefBtn.Bounds = new Rectangle(bx, c.Y + 2 + RowH * 2, StepBtnW, BtnH);
        _subSpdBtn.Bounds = new Rectangle(bx, c.Y + 2 + RowH * 3, StepBtnW, BtnH);
        _addStrBtn.Bounds = new Rectangle(ax, c.Y + 2 + RowH * 0, StepBtnW, BtnH);
        _addIntBtn.Bounds = new Rectangle(ax, c.Y + 2 + RowH * 1, StepBtnW, BtnH);
        _addDefBtn.Bounds = new Rectangle(ax, c.Y + 2 + RowH * 2, StepBtnW, BtnH);
        _addSpdBtn.Bounds = new Rectangle(ax, c.Y + 2 + RowH * 3, StepBtnW, BtnH);

        int by = c.Y + 2 + RowH * 5;
        int halfW = (c.Width - ActionPad * 2 - ActionGap) / 2;
        _confirmBtn.Bounds = new Rectangle(c.X + ActionPad, by, halfW, ActionBtnH);
        _resetBtn.Bounds = new Rectangle(c.X + ActionPad + halfW + ActionGap, by, halfW, ActionBtnH);
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;

namespace Mirage.Client.Shell.Panels;

/// <summary>Per-tab options modal opened by right-clicking a tab in the ChatPanel. Mirrors the
/// `OptionsPanel` chrome (DraggablePanel + two-column checkbox grid) so the two settings panels
/// feel like siblings. Lets the player rename the tab, toggle a General flash-on-new-message
/// option, and toggle channel filters. Every mutation persists immediately via
/// `ChatPanel.OnTabConfigChanged()`.
///
/// <para>🔴 <b>The rows come from what the server declared, not from this file.</b> Core's own five
/// head the list; under them are the channels this game invented, under their own heading. A client
/// compiled against no particular game draws whatever that game said it has.</para></summary>
public sealed class ChatOptionsPanel
{
    // Opening height/minH are recomputed in Open() from the visible row count (admin status and the
    // game's own channel count both change how many rows show). These are just construction defaults.
    // Centered on the 800x600 canvas (treated like the main Options panel), so it lands mid-screen in both states.
    private readonly DraggablePanel _panel =
        new(new Rectangle((UiHelper.RefW - 480) / 2, (UiHelper.RefH - 360) / 2, 480, 360), minH: 320);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    /// <summary>True while the rename field is focused — the host (GameplayScreen) reads this to
    /// route every keystroke here and block the chat input + world hotkeys/movement, so typing a
    /// tab name doesn't bleed through.</summary>
    public bool IsCapturingKeyboard => IsOpen && _nameFocused;

    // The user typed text exactly as-is; the on-tab display truncates to 15 chars + "..." (the
    // tab strip handles that). Storage cap is generous to avoid silently dropping pasted text.
    private readonly TextInputField _nameField = new() { MaxLength = 100 };
    private bool _nameFocused;
    private long _nowMs;  // last frame's clock for the caret blink — captured in Update

    /// <summary>One togglable channel: the id that travels and is saved, and the caption beside its box.
    /// A Core row draws from ClientStrings; a game's draws from whatever its declaration named, falling
    /// back to that name so an unlocalized key still reads as something.</summary>
    private readonly record struct Row(string Id, string LabelKey, bool IsCore)
    {
        public string Caption => IsCore
            ? ClientStrings.Get(LabelKey)
            : ClientStrings.GetOrFallback(LabelKey, LabelKey);
    }

    // Rebuilt in Open() from the declared set, so a channel added between sessions shows up without
    // anything here knowing its name. Core's five come first, then the game's.
    private readonly List<Row> _rows = [];
    private readonly List<Checkbox> _checks = [];
    private int _coreRows;

    // The General head is a plain label (its single Notify option is toggled directly), unlike
    // the two channel heads below which are clickable group toggles. Captured only for drawing.
    private Rectangle _generalHeaderRect;
    // Section heads are clickable tri-state toggles (all-on → all-off when anything is on,
    // off-state → all-on when everything is off). The rectangle is captured during Layout for
    // hit-testing.
    private Rectangle _coreHeaderRect, _gameHeaderRect;

    private readonly Checkbox _notifyChk = new();
    private readonly Button _closeBtn = new();

    private ChatPanel? _chatPanel;
    private int _tabIndex = -1;
    private AccountConfig.ChatTabConfig? _config;
    private bool _isAdmin;
    private bool _inGuild;
    private int _labelsGeneration = -1;

    public void Open(ChatPanel panel, int tabIndex, bool isAdmin, bool inGuild, ChatChannelSet channels)
    {
        _chatPanel = panel;
        _tabIndex = tabIndex;
        _config = panel.GetTabConfig(tabIndex);
        _isAdmin = isAdmin;
        _inGuild = inGuild;
        BuildRows(channels);
        _nameField.SetText(_config.Name);
        _nameFocused = false;
        SyncChecksFromConfig();

        // Size the panel to fit every row (the admin case shows one extra channel, and a game can
        // declare any number). minH stops the user shrinking it until the Close button or Notify
        // toggle clip; grow the current height if it's below the requirement.
        int reqH = RequiredContentHeight() + DraggablePanel.TitleH;
        _panel.SetMinH(reqH);
        var b = _panel.Bounds;
        if (b.Height < reqH) _panel.SetBounds(new Rectangle(b.X, b.Y, b.Width, reqH));

        IsOpen = true;
    }

    public void Close()
    {
        CommitNameIfNeeded();
        IsOpen = false;
    }

    private void BuildRows(ChatChannelSet channels)
    {
        _rows.Clear();
        foreach (string id in ChatChannels.Core)
        {
            string? key = ChatPanel.CoreChannelLabelKey(id);
            if (key is not null) _rows.Add(new Row(id, key, IsCore: true));
        }
        _coreRows = _rows.Count;
        foreach (var ch in channels.Channels)
            _rows.Add(new Row(ch.Id, ch.LabelKey, IsCore: false));

        while (_checks.Count < _rows.Count) _checks.Add(new Checkbox());
        // A caption is stale the moment the row list changes, and Draw only refreshes them when the
        // language does. Force it.
        _labelsGeneration = -1;
    }

    private void SyncChecksFromConfig()
    {
        if (_config is null) return;
        for (int i = 0; i < _rows.Count; i++)
            _checks[i].Checked = !_config.DisabledChannels.Contains(_rows[i].Id);
        _notifyChk.Checked = _config.Notify;
    }

    /// <summary>Whether a channel row is shown to this player: Admin only with admin access; Guild only
    /// while in a guild. A hidden row is completely absent from the panel (not grayed out), and its config
    /// state is left untouched. A game's own channels are always shown — Core has no idea who they are for,
    /// and a declaration that wanted an audience would have to say so on the sending side.</summary>
    private bool IsRowVisible(int i)
    {
        string id = _rows[i].Id;
        if (id == ChatChannels.Admin) return _isAdmin;
        if (id == ChatChannels.Guild) return _inGuild;
        return true;
    }

    /// <summary>Indexes of the channel rows visible to this player (see <see cref="IsRowVisible"/>).</summary>
    private IEnumerable<int> VisibleRowIndexes()
    {
        for (int i = 0; i < _rows.Count; i++)
            if (IsRowVisible(i)) yield return i;
    }

    public void Update(InputState input, long nowMs)
    {
        if (!IsOpen) return;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            Close();
            return;
        }

        LayoutControls();

        // Tab name field — click to focus, then Feed feeds keystrokes. Enter commits and blurs.
        var nameBounds = _nameField_Bounds;
        bool clickedField = input.IsMouseJustPressed() && nameBounds.Contains(input.MousePosition);
        if (clickedField)
        {
            _nameFocused = true;
            input.ConsumeMouseClick();
        }
        else if (input.IsMouseJustPressed() && !nameBounds.Contains(input.MousePosition))
        {
            // Click outside the field commits whatever was typed.
            CommitNameIfNeeded();
        }
        if (_nameFocused)
        {
            _nameField.Feed(input, nowMs);
            if (input.IsKeyPressed(Keys.Enter) || input.IsKeyPressed(Keys.Escape))
            {
                CommitNameIfNeeded();
            }
        }

        // Channel checkboxes. Each toggle persists immediately.
        bool anyChanged = false;
        foreach (int i in VisibleRowIndexes())
        {
            if (_checks[i].Update(input))
            {
                ApplyCheckChange(_rows[i].Id, _checks[i].Checked);
                anyChanged = true;
            }
        }

        // Section header tri-state group toggles. Click flips the whole group off if anything in
        // the group is on, otherwise turns it all on.
        if (input.IsMouseClicked())
        {
            if (input.IsClickIn(_coreHeaderRect))
            {
                ToggleGroup(CoreRange());
                anyChanged = true;
                input.ConsumeMouseClick();
            }
            else if (HasGameRows && input.IsClickIn(_gameHeaderRect))
            {
                ToggleGroup(GameRange());
                anyChanged = true;
                input.ConsumeMouseClick();
            }
        }

        if (_notifyChk.Update(input))
        {
            if (_config is not null) _config.Notify = _notifyChk.Checked;
            anyChanged = true;
        }

        if (_closeBtn.IsClicked(input))
        {
            Close();
            input.ConsumeMouseClick();
            return;
        }

        if (anyChanged) _chatPanel?.OnTabConfigChanged();
        _nowMs = nowMs;

        // Structural bleed-through guard: any mouse button landing on the panel is consumed here
        // (after the panel's own widgets have read it) so it can't reach the chat panel or world
        // behind. Mirrors the panel/input-layer fix used elsewhere.
        if (_panel.ContainsMouse(input.MousePosition))
        {
            input.ConsumeMouseClick();
            input.ConsumeMouseDown();
            input.ConsumeRightMouseClick();
        }
    }

    private void ApplyCheckChange(string id, bool isEnabled)
    {
        if (_config is null) return;
        if (isEnabled) _config.DisabledChannels.Remove(id);
        else if (!_config.DisabledChannels.Contains(id)) _config.DisabledChannels.Add(id);

        // Whatever they just decided is now their decision, so the next declaration sweep leaves it
        // alone. A row they never touched stays unknown and still follows the game's default.
        if (!_config.KnownChannels.Contains(id)) _config.KnownChannels.Add(id);
    }

    private bool HasGameRows => _rows.Count > _coreRows;
    private (int start, int endExclusive) CoreRange() => (0, _coreRows);
    private (int start, int endExclusive) GameRange() => (_coreRows, _rows.Count);

    // Rows a group occupies given its currently-visible channels, laid out two per row.
    private int GroupRows((int start, int endExclusive) range)
    {
        int n = 0;
        for (int i = range.start; i < range.endExclusive; i++)
            if (IsRowVisible(i)) n++;
        return (n + 1) / 2;
    }

    private void ToggleGroup((int start, int endExclusive) range)
    {
        // Decide direction: if anything in the group is currently enabled, turn it ALL off;
        // otherwise turn it ALL on. Skips channels hidden from this player (Admin for non-admins).
        bool anyEnabled = false;
        for (int i = range.start; i < range.endExclusive; i++)
        {
            if (!IsRowVisible(i)) continue;
            if (_checks[i].Checked)
            {
                anyEnabled = true;
                break;
            }
        }
        bool newState = !anyEnabled;
        for (int i = range.start; i < range.endExclusive; i++)
        {
            if (!IsRowVisible(i)) continue;
            _checks[i].Checked = newState;
            ApplyCheckChange(_rows[i].Id, newState);
        }
    }

    private void CommitNameIfNeeded()
    {
        if (!_nameFocused) return;
        _nameFocused = false;
        if (_config is null) return;
        string newName = _nameField.Text;
        if (newName != _config.Name)
        {
            _config.Name = newName;
            _chatPanel?.OnTabConfigChanged();
        }
    }

    // ── Layout ─────────────────────────────────────────────────────────────────

    // Shared by LayoutControls and RequiredContentHeight so the rendered layout and the enforced
    // minimum height can't drift apart.
    private const int Pad = 8;
    private const int RowH = 20;
    private const int ChkH = 14;
    private const int SectionGap = 6;
    private const int CloseBtnW = 100;
    private const int CloseBtnH = 20;

    private Rectangle _nameField_Bounds;

    /// <summary>Content height needed to show every row without clipping, given the current admin state
    /// (admin shows one extra channel → one extra row) and how many channels this game declared. Drives
    /// Open()'s minH.</summary>
    private int RequiredContentHeight()
    {
        int h = Pad;
        h += RowH;                                  // name label
        h += ChkH + 4 + SectionGap;                 // name field
        h += RowH + RowH + SectionGap;              // general header + notify row
        h += RowH + GroupRows(CoreRange()) * RowH + SectionGap;
        if (HasGameRows) h += RowH + GroupRows(GameRange()) * RowH + SectionGap;
        h += SectionGap + CloseBtnH + Pad;          // gap, close button, bottom pad
        return h;
    }

    private void LayoutControls()
    {
        var c = _panel.ContentBounds;
        int y = c.Y + Pad;

        // Tab name field
        sb_NameLabelY = y;
        y += RowH;
        _nameField_Bounds = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, ChkH + 4);
        y += ChkH + 4 + SectionGap;

        // General group (the Notify flash toggle) sits at the top, above the channel sections.
        // Its header is a plain label — the single option below is toggled directly.
        _generalHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
        y += RowH;
        _notifyChk.Bounds = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, ChkH);
        y += RowH + SectionGap;

        _coreHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
        y += RowH;
        y = LayoutGroupChecks(c, y, CoreRange(), RowH, ChkH, Pad);
        y += SectionGap;

        if (HasGameRows)
        {
            _gameHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
            y += RowH;
            y = LayoutGroupChecks(c, y, GameRange(), RowH, ChkH, Pad);
            y += SectionGap;
        }
        else
        {
            _gameHeaderRect = Rectangle.Empty;
        }

        // Close pinned to the bottom of the content area; Open()'s minH guarantees it stays clear
        // of the last section even at minimum size, and it tracks the bottom edge when enlarged.
        _closeBtn.Bounds = new Rectangle(c.X + (c.Width - CloseBtnW) / 2, c.Bottom - CloseBtnH - Pad, CloseBtnW, CloseBtnH);
    }

    private int LayoutGroupChecks(Rectangle c, int yStart, (int start, int endExclusive) range, int rowH, int chkH, int pad)
    {
        int half = c.Width / 2;
        int lx = c.X + pad;
        int rx = c.X + half + pad;
        int colW = half - pad * 2;

        int leftRow = 0, rightRow = 0;
        // Even-index visible row goes left, odd-index visible row goes right.
        bool leftNext = true;
        for (int i = range.start; i < range.endExclusive; i++)
        {
            if (!IsRowVisible(i)) continue;
            if (leftNext)
            {
                _checks[i].Bounds = new Rectangle(lx, yStart + leftRow * rowH, colW, chkH);
                leftRow++;
            }
            else
            {
                _checks[i].Bounds = new Rectangle(rx, yStart + rightRow * rowH, colW, chkH);
                rightRow++;
            }
            leftNext = !leftNext;
        }
        int rowsUsed = Math.Max(leftRow, rightRow);
        return yStart + rowsUsed * rowH;
    }

    // Captured at LayoutControls time for the name label drawing.
    private int sb_NameLabelY;

    // ── Drawing ────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            for (int i = 0; i < _rows.Count; i++)
                _checks[i].Label = _rows[i].Caption;
            _notifyChk.Label = ClientStrings.Get(ClientStrings.ChatOptionsPanel_Notify);
            _closeBtn.Label = ClientStrings.Get(ClientStrings.ChatOptionsPanel_Close);
        }

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ChatOptionsPanel_Title), isActive: true);
        LayoutControls();

        // Tab name label + field
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ChatOptionsPanel_TabName),
            new Vector2(_nameField_Bounds.X, sb_NameLabelY),
            UiHelper.DlgLabelColor);
        UiHelper.DrawFilledRect(sb, _nameField_Bounds, UiHelper.TextInputBg);
        UiHelper.DrawBorder(sb, _nameField_Bounds, _nameFocused ? Color.CornflowerBlue : Color.Gray);
        _nameField.Draw(sb, font, _nameField_Bounds, _nameFocused, _nowMs);

        // Section headers (drawn as text; the channel heads double as clickable group toggles, while
        // General is a plain label for the single Notify option below it).
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionGeneral, _generalHeaderRect);
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionChat, _coreHeaderRect);
        if (HasGameRows)
            DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionChannels, _gameHeaderRect);

        foreach (int i in VisibleRowIndexes())
            _checks[i].Draw(sb, font, input);

        _notifyChk.Draw(sb, font, input);
        _closeBtn.Draw(sb, font, input);
        _panel.DrawOverlay(sb);
    }

    private static void DrawSectionHeader(SpriteBatch sb, SpriteFont font, string key, Rectangle rect)
    {
        sb.DrawString(font, ClientStrings.Get(key),
            new Vector2(rect.X, rect.Y),
            Color.Gold);
    }
}

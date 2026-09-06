using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared.Protocol;

namespace Mirage.Client.Shell.Panels;

/// <summary>Per-tab options modal opened by right-clicking a tab in the ChatPanel. Mirrors the
/// `OptionsPanel` chrome (DraggablePanel + two-column checkbox grid) so the two settings panels
/// feel like siblings. Lets the player rename the tab, toggle a General flash-on-new-message
/// option, and toggle channel filters in three groups (Chat / System / Combat). Every mutation
/// persists immediately via `ChatPanel.OnTabConfigChanged()`.</summary>
public sealed class ChatOptionsPanel
{
    // Opening height/minH are recomputed in Open() from the visible row count (admin status
    // changes how many channels show). These are just sane construction defaults.
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

    // One Checkbox per visible channel. Built in display order so we can iterate ranges by
    // section. AdminChat is hidden when the player isn't an admin — the index is still allocated
    // (kept null) so the visible-rows count stays stable per session.
    private readonly Checkbox[] _channelChecks;
    private readonly ChatChannel[] _channelOrder;
    private readonly string[] _channelLabelKeys;

    // The General head is a plain label (its single Notify option is toggled directly), unlike
    // the three channel heads below which are clickable group toggles. Captured only for drawing.
    private Rectangle _generalHeaderRect;
    // Section heads are clickable tri-state toggles (all-on → all-off when anything is on,
    // off-state → all-on when everything is off). The rectangle is captured during Layout for
    // hit-testing.
    private Rectangle _chatHeaderRect, _systemHeaderRect, _combatHeaderRect;

    private readonly Checkbox _notifyChk = new();
    private readonly Button _closeBtn = new();

    private ChatPanel? _chatPanel;
    private int _tabIndex = -1;
    private AccountConfig.ChatTabConfig? _config;
    private bool _isAdmin;
    private bool _inGuild;
    private int _labelsGeneration = -1;

    public ChatOptionsPanel()
    {
        _channelOrder = new[]
        {
            // Chat group (the guild channels fold in here)
            ChatChannel.Say, ChatChannel.Yell, ChatChannel.Broadcast, ChatChannel.Tell,
            ChatChannel.AdminChat, ChatChannel.Guild, ChatChannel.GuildOfficer,
            // System group
            ChatChannel.Notice, ChatChannel.JoinLeaveNotice, ChatChannel.System,
            // Combat group (both war channels group here)
            ChatChannel.Combat, ChatChannel.Rewards, ChatChannel.War, ChatChannel.GuildWar,
        };
        _channelLabelKeys = new[]
        {
            ClientStrings.ChatOptionsPanel_Channel_Say,
            ClientStrings.ChatOptionsPanel_Channel_Yell,
            ClientStrings.ChatOptionsPanel_Channel_Broadcast,
            ClientStrings.ChatOptionsPanel_Channel_Tell,
            ClientStrings.ChatOptionsPanel_Channel_AdminChat,
            ClientStrings.ChatOptionsPanel_Channel_Guild,
            ClientStrings.ChatOptionsPanel_Channel_GuildOfficer,
            ClientStrings.ChatOptionsPanel_Channel_Notice,
            ClientStrings.ChatOptionsPanel_Channel_JoinLeaveNotice,
            ClientStrings.ChatOptionsPanel_Channel_System,
            ClientStrings.ChatOptionsPanel_Channel_Combat,
            ClientStrings.ChatOptionsPanel_Channel_Rewards,
            ClientStrings.ChatOptionsPanel_Channel_War,
            ClientStrings.ChatOptionsPanel_Channel_GuildWar,
        };
        _channelChecks = new Checkbox[_channelOrder.Length];
        for (int i = 0; i < _channelChecks.Length; i++)
            _channelChecks[i] = new Checkbox();
    }

    public void Open(ChatPanel panel, int tabIndex, bool isAdmin, bool inGuild)
    {
        _chatPanel = panel;
        _tabIndex = tabIndex;
        _config = panel.GetTabConfig(tabIndex);
        _isAdmin = isAdmin;
        _inGuild = inGuild;
        _nameField.SetText(_config.Name);
        _nameFocused = false;
        SyncChecksFromConfig();

        // Size the panel to fit every row (the admin case shows one extra channel). minH stops the
        // user shrinking it until the Close button or Notify toggle clip; grow the current height
        // if it's below the requirement.
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

    private void SyncChecksFromConfig()
    {
        if (_config is null) return;
        for (int i = 0; i < _channelOrder.Length; i++)
        {
            string name = _channelOrder[i].ToString();
            _channelChecks[i].Checked = !_config.DisabledChannels.Contains(name);
        }
        _notifyChk.Checked = _config.Notify;
    }

    /// <summary>Whether a channel row is shown to this player: AdminChat only with admin access; the guild
    /// channels (Guild / Guild Officer / Guild War) only while in a guild. A hidden row is completely absent
    /// from the panel (not grayed out), and its config state is left untouched.</summary>
    private bool IsChannelVisible(ChatChannel ch)
    {
        if (ch == ChatChannel.AdminChat) return _isAdmin;
        if (ch is ChatChannel.Guild or ChatChannel.GuildOfficer or ChatChannel.GuildWar) return _inGuild;
        return true;
    }

    /// <summary>Indexes of the channel rows visible to this player (see <see cref="IsChannelVisible"/>).</summary>
    private IEnumerable<int> VisibleChannelIndexes()
    {
        for (int i = 0; i < _channelOrder.Length; i++)
            if (IsChannelVisible(_channelOrder[i])) yield return i;
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
        foreach (int i in VisibleChannelIndexes())
        {
            if (_channelChecks[i].Update(input))
            {
                ApplyCheckChange(_channelOrder[i], _channelChecks[i].Checked);
                anyChanged = true;
            }
        }

        // Section header tri-state group toggles. Click flips the whole group off if anything in
        // the group is on, otherwise turns it all on.
        if (input.IsMouseClicked())
        {
            if (input.IsClickIn(_chatHeaderRect))
            {
                ToggleGroup(ChatGroupRange());
                anyChanged = true;
                input.ConsumeMouseClick();
            }
            else if (input.IsClickIn(_systemHeaderRect))
            {
                ToggleGroup(SystemGroupRange());
                anyChanged = true;
                input.ConsumeMouseClick();
            }
            else if (input.IsClickIn(_combatHeaderRect))
            {
                ToggleGroup(CombatGroupRange());
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

    private void ApplyCheckChange(ChatChannel ch, bool isEnabled)
    {
        if (_config is null) return;
        string name = ch.ToString();
        if (isEnabled) _config.DisabledChannels.Remove(name);
        else if (!_config.DisabledChannels.Contains(name)) _config.DisabledChannels.Add(name);
    }

    private (int start, int endExclusive) ChatGroupRange() => (0, 7);      // incl. Guild + GuildOfficer
    private (int start, int endExclusive) SystemGroupRange() => (7, 10);
    private (int start, int endExclusive) CombatGroupRange() => (10, 14);  // incl. War + GuildWar

    // Rows a group occupies given its currently-visible channels, laid out two per row.
    private int GroupRows((int start, int endExclusive) range)
    {
        int n = 0;
        for (int i = range.start; i < range.endExclusive; i++)
            if (IsChannelVisible(_channelOrder[i])) n++;
        return (n + 1) / 2;
    }

    private void ToggleGroup((int start, int endExclusive) range)
    {
        // Decide direction: if anything in the group is currently enabled, turn it ALL off;
        // otherwise turn it ALL on. Skips channels hidden from this player (AdminChat for non-admins).
        bool anyEnabled = false;
        for (int i = range.start; i < range.endExclusive; i++)
        {
            if (!IsChannelVisible(_channelOrder[i])) continue;
            if (_channelChecks[i].Checked)
            {
                anyEnabled = true;
                break;
            }
        }
        bool newState = !anyEnabled;
        for (int i = range.start; i < range.endExclusive; i++)
        {
            if (!IsChannelVisible(_channelOrder[i])) continue;
            _channelChecks[i].Checked = newState;
            ApplyCheckChange(_channelOrder[i], newState);
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

    /// <summary>Content height needed to show every row without clipping, given the current admin
    /// state (admin shows one extra channel → one extra Chat row). Drives Open()'s minH.</summary>
    private int RequiredContentHeight()
    {
        int h = Pad;
        h += RowH;                                  // name label
        h += ChkH + 4 + SectionGap;                 // name field
        h += RowH + RowH + SectionGap;              // general header + notify row
        h += RowH + GroupRows(ChatGroupRange()) * RowH + SectionGap;     // chat header + rows
        h += RowH + GroupRows(SystemGroupRange()) * RowH + SectionGap;   // system header + rows
        h += RowH + GroupRows(CombatGroupRange()) * RowH + SectionGap;   // combat header + rows
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

        _chatHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
        y += RowH;
        y = LayoutGroupChecks(c, y, ChatGroupRange(), RowH, ChkH, Pad);
        y += SectionGap;

        _systemHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
        y += RowH;
        y = LayoutGroupChecks(c, y, SystemGroupRange(), RowH, ChkH, Pad);
        y += SectionGap;

        _combatHeaderRect = new Rectangle(c.X + Pad, y, c.Width - Pad * 2, RowH - 4);
        y += RowH;
        y = LayoutGroupChecks(c, y, CombatGroupRange(), RowH, ChkH, Pad);
        y += SectionGap;

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
            if (!IsChannelVisible(_channelOrder[i])) continue;
            if (leftNext)
            {
                _channelChecks[i].Bounds = new Rectangle(lx, yStart + leftRow * rowH, colW, chkH);
                leftRow++;
            }
            else
            {
                _channelChecks[i].Bounds = new Rectangle(rx, yStart + rightRow * rowH, colW, chkH);
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
            for (int i = 0; i < _channelChecks.Length; i++)
                _channelChecks[i].Label = ClientStrings.Get(_channelLabelKeys[i]);
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

        // Section headers (drawn as text; the three channel heads double as clickable group
        // toggles, while General is a plain label for the single Notify option below it).
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionGeneral, _generalHeaderRect);
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionChat, _chatHeaderRect);
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionSystem, _systemHeaderRect);
        DrawSectionHeader(sb, font, ClientStrings.ChatOptionsPanel_SectionCombat, _combatHeaderRect);

        foreach (int i in VisibleChannelIndexes())
            _channelChecks[i].Draw(sb, font, input);

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

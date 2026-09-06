using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Globalization;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Mail (M) panel — the client half of the mail system. INBOX view lists the account's messages
/// (sender + timestamp); selecting one opens it in a reading pane (marks it read) with its attachments,
/// and Delete/Claim act on it. COMPOSE view sends player-to-player mail addressed by account name, with
/// text + attachments staged from the inventory (currency prompts for an amount). The mailbox lives on
/// <see cref="ClientState.Mail"/>, replaced wholesale by every <c>MailboxPacket</c>.
/// </summary>
public sealed class MailPanel : IGamePanel
{
    // Wider default so the inbox list (left) and the open message (right) sit side by side comfortably — sized to a
    // layout a player dialed in (the side-by-side panes want the width). A per-character saved bound still overrides.
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 700, 385), minH: 240, minW: 440);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    // While composing, the panel owns keyboard input (text fields) — GameplayScreen suppresses world
    // hotkeys via AnyPanelCapturingInput. Movement is blocked whenever the panel is merely OPEN (via
    // GameplayScreen's movementBlocked) — mail is a focused station like the bank/shop.
    public bool IsCapturingInput => _composing;

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _showOutbox = false;
            _lastMailVersion = -1;
        }  // open on the inbox; force a rebuild next Draw
        else
        {
            EndCompose();
            Tooltip.CloseScope(TooltipScope);
        }
    }

    // ── Inbox ───────────────────────────────────────────────────────────────────
    private readonly Table<MailMessage> _table = new();   // sortable/resizable From/Date/Subject inbox
    private readonly Button _deleteBtn = new();
    private readonly Button _claimBtn = new();     // collect a message's attachments
    private readonly Button _payCodBtn = new();    // pay a CoD to unlock its attachments (replaces Claim for a CoD)
    private readonly Button _composeBtn = new();   // open the compose view
    private readonly Button _replyBtn = new();     // reply to the open message (prefills To with the sender)
    private readonly Button _inboxTab = new();     // Inbox / Outbox (sent) view switch
    private readonly Button _outboxTab = new();
    private bool _showOutbox;                      // false = inbox, true = sent (outbox) view
    private bool _lastShowOutbox;
    private long _serverNowUtc;                    // server clock from the last mailbox push (drives in-transit)
    private int _labelsGeneration = -1;
    private int _lastMailVersion = -1;
    private int _lastSocialVersion = -1;           // re-filter the inbox when the ignore list changes
    private long _lastSyncNowUtc = -1;             // re-filter the inbox when the server clock advances (mail matures)
    // Id of the message currently shown in the reading pane; drives one mark-read per selection change.
    private int _shownId;
    private int _actionBtnY;   // y of the Claim/Reply/Delete row (set by Layout, used by LayoutActionButtons)
    private InputState _input = new();

    // ── Compose ─────────────────────────────────────────────────────────────────
    private bool _composing;
    private int _focusField = -1;   // 0 = To, 1 = Subject, 2 = Body, -1 = none
    private readonly TextInputField _toField = new() { MaxLength = int.MaxValue };   // no limit — a comma-separated list
    private readonly TextInputField _subjectField = new() { MaxLength = Constants.MailSubjectMaxLength };
    private readonly TextArea _bodyField = new() { ReadOnly = false, MaxLength = Constants.MailBodyMaxLength };
    private readonly TextInputField _codField = new() { MaxLength = 10 };   // CoD price (gold); blank/0 = ordinary mail
    private readonly ListBox _invList = new();          // inventory attach candidates
    private readonly List<int> _invSlots = new();       // row → inventory slot
    private readonly ListBox _stagedList = new();       // staged attachments
    private readonly List<(int Slot, int Quantity)> _staged = new();   // Quantity 0 = whole non-currency slot
    private readonly Button _attachBtn = new();
    private readonly Button _unstageBtn = new();
    private readonly Button _sendBtn = new();
    private readonly Button _cancelBtn = new();
    private readonly NumberPromptDialog _amountPrompt = new();
    private readonly ConfirmDialog _noSubjectConfirm = new();   // "send with no subject?" warn
    private Rectangle _toRect, _subjRect, _bodyRect, _invRect, _stagedRect, _codRect;
    private int _attachHeaderY;
    private int _costLabelY;   // y of the "Cost to Send" line, just above the Send/Cancel row

    private const int ButtonH = 26;
    private const int DeleteBtnW = 90;
    private const int ClaimBtnW = 70;
    private const int ComposeBtnW = 90;
    private const int ReplyBtnW = 70;
    private const int PayCodBtnW = 120;   // wider — the label carries the price ("Pay CoD (1,000)")
    private const string TooltipScope = "Mail";
    private const int MailColDate = 1;
    private const int TabW = 58;
    private static readonly Color InTransitRowColor = Color.Gray;   // grayed "in transit" mail row

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    public MailPanel()
    {
        _table.AllowReorder = true;   // opt in to drag-to-reorder columns (still sortable + resizable)
        // Column 0 is the "other party": the sender in the inbox, the recipient in the outbox (sent) view.
        _table.Column(() => ClientStrings.Get(_showOutbox ? ClientStrings.MailPanel_ColRecipient : ClientStrings.MailPanel_ColSender),
                          m => _showOutbox ? m.Recipient : m.Sender,
                          m => _showOutbox ? m.Recipient : (m.IsRead ? "  " : "* ") + m.Sender, width: 80, minWidth: 44)
              .Column(() => ClientStrings.Get(ClientStrings.MailPanel_ColDate), m => m.TimeUtc,
                          m => FormatTime(m.TimeUtc), width: 112, minWidth: 70)
              .Column(() => ClientStrings.Get(ClientStrings.MailPanel_ColSubject), m => m.Subject, width: 108, minWidth: 50)
              .WithRowKey(m => m.Id)
              .WithRowColor(m => IsInTransit(m) ? InTransitRowColor : Color.White);
        _table.SortBy(MailColDate, ascending: false);   // newest first by default
        ColumnTables = new Dictionary<string, IColumnLayoutTable> { ["mail.messages"] = _table };
    }

    /// <summary>This panel's persisted tables, keyed by table id (the host saves/restores column layout generically).</summary>
    public IReadOnlyDictionary<string, IColumnLayoutTable> ColumnTables { get; }

    /// <summary>True for the frame after the user resized/reordered/sorted a mail column, so the host persists it.</summary>
    public bool ColumnsChanged { get; private set; }

    // ── Update ───────────────────────────────────────────────────────────────────

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        ColumnsChanged = false;
        if (!IsOpen) return;
        _input = input;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            EndCompose();
            Tooltip.CloseScope(TooltipScope);
            return;
        }

        if (_composing)
        {
            UpdateCompose(input, state, sender, isActive);
            return;
        }

        var c = _panel.ContentBounds;
        Layout(c, out var listRect, out var readRect);

        if (_composeBtn.IsClicked(input))
        {
            StartCompose();
            return;
        }

        // Inbox / Outbox view switch (clicking the already-active tab is a no-op).
        if (!_showOutbox && _outboxTab.IsClicked(input)) SetView(outbox: true);
        else if (_showOutbox && _inboxTab.IsClicked(input)) SetView(outbox: false);

        SyncItems(state);
        _table.Update(input, listRect, keyboardActive: isActive);
        ColumnsChanged |= _table.LayoutChanged;   // persisted by the host when set

        var selMsg = _table.SelectedItem;
        int selId = selMsg?.Id ?? 0;
        bool selDelivered = selMsg is not null && !IsInTransit(selMsg);
        bool inboxCod = !_showOutbox && selMsg is { CodPrice: > 0 };   // an UNPAID CoD in the inbox: locked

        // Reply shows with any selection. The left action is Pay CoD for a delivered unpaid CoD, else Claim for a
        // delivered non-CoD message carrying an unclaimed stack. Both inbox-only. Position before the click checks.
        bool showReply = !_showOutbox && selMsg is not null;
        bool showPayCod = inboxCod && selDelivered;
        bool showClaim = !_showOutbox && !inboxCod && selDelivered && selMsg!.Attachments.Any(a => !a.Claimed && a.ItemNum > 0);
        LayoutActionButtons(readRect, showClaim, showPayCod, showReply);

        // Delete works in BOTH views once the message is delivered — but never an UNPAID inbox CoD (it must be paid
        // or returned first). Outbox delete removes only the sender's own copy (the recipient's is independent).
        bool canDelete = selDelivered && !inboxCod;
        _deleteBtn.Enabled = canDelete;
        if (canDelete && _deleteBtn.IsClicked(input))
        {
            sender.SendMailDelete(selId, _showOutbox);
            _table.ClearSelection();
            _shownId = 0;
            return;
        }

        // The outbox is otherwise a read-only "sent" view; only the inbox marks read + claims/pays + replies.
        if (_showOutbox)
        {
            _shownId = selId;
            return;
        }

        // Opening a message marks it read - once per selection change.
        if (selId != _shownId)
        {
            _shownId = selId;
            if (selMsg is { IsRead: false }) sender.SendMailMarkRead(selId);
        }

        // Pay CoD: unlock a delivered CoD's items (server charges gold, releases the items, mails the sender the net).
        // Gated on the receiver affording the price; the server still backstops gold + inventory room.
        if (showPayCod)
        {
            _payCodBtn.Label = ClientStrings.Format(ClientStrings.MailPanel_PayCod, ("Price", selMsg!.CodPrice));
            _payCodBtn.Enabled = state.PlayerGold() >= selMsg.CodPrice;
            if (_payCodBtn.Enabled && _payCodBtn.IsClicked(input)) sender.SendMailPayCod(selId);
        }

        // Claim attachments: a DELIVERED non-CoD message with an unclaimed stack. The server credits it + re-syncs.
        _claimBtn.Enabled = showClaim;
        if (showClaim && _claimBtn.IsClicked(input)) sender.SendMailClaim(selId);

        _replyBtn.Enabled = selMsg is not null;
        if (selMsg is not null && _replyBtn.IsClicked(input))
        {
            StartCompose(selMsg.Sender.Trim(), BuildReplySubject(selMsg.Subject));
            return;
        }
    }

    private void UpdateCompose(InputState input, ClientState state, ClientPacketSender sender, bool isActive)
    {
        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        LayoutCompose(c);

        // The currency amount prompt and the no-subject confirm are each modal while open.
        if (_amountPrompt.IsOpen)
        {
            _amountPrompt.Update(input, c, nowMs);
            return;
        }
        if (_noSubjectConfirm.IsOpen)
        {
            _noSubjectConfirm.Update(input);
            return;
        }

        // The body is an editable TextArea — it self-focuses on click and runs every frame.
        _bodyField.SetBounds(_bodyRect);
        _bodyField.Update(input, keyboardActive: isActive);

        // Click a To/Subject field to focus it (dropping body focus); clicking a list drops field focus.
        if (input.IsClickIn(_toRect))
        {
            _focusField = 0;
            _bodyField.Defocus();
            _toField.HandleMouseClick(input.MousePosition.X, false);
        }
        else if (input.IsClickIn(_subjRect))
        {
            _focusField = 1;
            _bodyField.Defocus();
            _subjectField.HandleMouseClick(input.MousePosition.X, false);
        }
        else if (input.IsClickIn(_codRect))
        {
            _focusField = 3;
            _bodyField.Defocus();
            _codField.HandleMouseClick(input.MousePosition.X, false);
        }
        else if (_bodyField.IsFocused)
        {
            _focusField = 2;
        }
        else if (input.IsClickIn(_invRect) || input.IsClickIn(_stagedRect))
        {
            _focusField = -1;
        }

        // Tab cycles To → Subject → Body → CoD price.
        if (input.IsKeyPressed(Keys.Tab))
        {
            _focusField = _focusField < 0 ? 0 : (_focusField + 1) % 4;
            if (_focusField == 2) _bodyField.Focus();
            else _bodyField.Defocus();
        }

        if (_focusField == 0) _toField.Feed(input, nowMs);
        else if (_focusField == 1) _subjectField.Feed(input, nowMs);
        else if (_focusField == 3) _codField.Feed(input, nowMs);

        RebuildInvList(state);
        _invList.Update(input, _invRect, keyboardActive: false);
        _stagedList.Update(input, _stagedRect, keyboardActive: false);

        _attachBtn.Enabled = _staged.Count < Constants.MaxMailAttachments
            && _invList.SelectedIndex >= 0 && _invList.SelectedIndex < _invSlots.Count;
        if (_attachBtn.Enabled && _attachBtn.IsClicked(input)) StageSelected(state);

        _unstageBtn.Enabled = _stagedList.SelectedIndex >= 0 && _stagedList.SelectedIndex < _staged.Count;
        if (_unstageBtn.Enabled && _unstageBtn.IsClicked(input))
        {
            _staged.RemoveAt(_stagedList.SelectedIndex);
            _stagedList.SelectedIndex = -1;
        }

        // Catch a bad recipient list (blank/too many) + the invalid multi+attachments combo client-side (the
        // server still backstops): Send disables and the reason shows in place of the cost line.
        _sendBtn.Enabled = SendBlockReason(state) == "" && RecipientCount() > 0;

        if (_sendBtn.IsClicked(input))
        {
            // A blank subject warns first (the server substitutes "(No Subject)"); confirming proceeds.
            if (string.IsNullOrWhiteSpace(_subjectField.Text))
                _noSubjectConfirm.Open(ClientStrings.Get(ClientStrings.MailPanel_NoSubjectWarn), () => DoSend(sender));
            else
                DoSend(sender);
            return;
        }
        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            EndCompose();
        }
    }

    private void DoSend(ClientPacketSender sender)
    {
        var attach = _staged.Select(s => new MailSendAttach { InvSlot = s.Slot, Quantity = s.Quantity }).ToList();
        sender.SendMailSend(_toField.Text.Trim(), _subjectField.Text, _bodyField.Text, attach, ParseCod());
        EndCompose();
    }

    // Stage the selected inventory candidate. Currency opens the amount prompt; anything else stages whole.
    private void StageSelected(ClientState state)
    {
        int slot = _invSlots[_invList.SelectedIndex];
        if (_staged.Any(s => s.Slot == slot)) return;
        var invSlot = state.Me?.Inv?[slot];
        if (invSlot is null || invSlot.Num <= 0) return;
        var item = state.Items[invSlot.Num];
        if (item?.Type == ItemType.Currency)
        {
            _amountPrompt.Open(ClientStrings.Get(ClientStrings.MailPanel_Attach), item.Name?.TrimEnd() ?? "", invSlot.Quantity,
                amt => { if (_staged.Count < Constants.MaxMailAttachments) _staged.Add((slot, amt)); });
        }
        else
        {
            _staged.Add((slot, 0));
        }
    }

    // ── Draw ─────────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex, bool isActive = false)
    {
        if (!IsOpen) return;

        SyncItems(state);
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }

        string title = _composing
            ? ClientStrings.Get(ClientStrings.MailPanel_Compose)
            : state.UnreadMailCount() is int unread && unread > 0
                ? ClientStrings.Format(ClientStrings.MailPanel_TitleUnreadFormat, ("Count", unread))
                : ClientStrings.Get(ClientStrings.MailPanel_Title);
        _panel.Draw(sb, font, title, isActive);

        if (_composing)
        {
            DrawCompose(sb, font, state, itemsTex);
            _panel.DrawOverlay(sb);
            return;
        }

        var c = _panel.ContentBounds;
        Layout(c, out var listRect, out var readRect);

        _composeBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _inboxTab.Draw(sb, font, _input, normalColor: _showOutbox ? (Color?)null : UiHelper.ActiveTabColor);
        _outboxTab.Draw(sb, font, _input, normalColor: _showOutbox ? UiHelper.ActiveTabColor : (Color?)null);

        var rows = _showOutbox ? state.Outbox : state.Mail;
        if (rows.Count == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(_showOutbox ? ClientStrings.MailPanel_EmptySent : ClientStrings.MailPanel_Empty),
                new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _table.Draw(sb, font, listRect);
        }

        DrawReadingPane(sb, font, state, readRect, itemsTex);

        // Action buttons (order Pay-CoD/Claim, Reply, Delete — centered): Delete always (disabled in transit / for
        // an unpaid CoD); Reply with any selection; the left slot is Pay CoD for a delivered unpaid CoD, else Claim.
        var openMsg = _table.SelectedItem;
        bool openDelivered = openMsg is not null && !IsInTransit(openMsg);
        bool openInboxCod = !_showOutbox && openMsg is { CodPrice: > 0 };
        bool showReply = !_showOutbox && openMsg is not null;
        bool showPayCod = openInboxCod && openDelivered;
        bool showClaim = !_showOutbox && !openInboxCod && openDelivered && openMsg!.Attachments.Any(a => !a.Claimed && a.ItemNum > 0);
        LayoutActionButtons(readRect, showClaim, showPayCod, showReply);
        _deleteBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        if (showReply) _replyBtn.Draw(sb, font, _input);
        if (showClaim) _claimBtn.Draw(sb, font, _input);
        if (showPayCod)
        {
            _payCodBtn.Label = ClientStrings.Format(ClientStrings.MailPanel_PayCod, ("Price", openMsg!.CodPrice));
            _payCodBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        }
        _panel.DrawOverlay(sb);
    }

    private void DrawCompose(SpriteBatch sb, SpriteFont font, ClientState state, IReadOnlyList<Texture2D?> itemsTex)
    {
        long nowMs = Environment.TickCount64;
        var c = _panel.ContentBounds;
        LayoutCompose(c);
        RebuildInvList(state);
        RebuildStagedList(state);

        DrawFieldLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_To), _toRect);
        // Multi-recipient hint, right-aligned on the To label line.
        string toHint = ClientStrings.Get(ClientStrings.MailPanel_MultiHint);
        float toHintW = font.MeasureString(toHint).X;
        UiHelper.DrawLabel(sb, font, toHint, new Vector2(_toRect.Right - toHintW, _toRect.Y - 14), Color.Gray, toHintW + 2);
        _toField.Draw(sb, font, _toRect, _focusField == 0, nowMs);
        DrawFieldLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_Subject), _subjRect);
        _subjectField.Draw(sb, font, _subjRect, _focusField == 1, nowMs);
        DrawFieldLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_Body), _bodyRect);
        // Character counter (compose only), right-aligned on the body label line — colored by the cost tier
        // (gray = normal, gold = x2, red = x10) so the escalating long-body cost is visible as you type.
        int bodyMult = BodyCostMultiplier();
        Color counterColor = bodyMult >= Constants.MailVeryLongBodyCostMultiplier ? Color.OrangeRed
            : bodyMult >= Constants.MailLongBodyCostMultiplier ? Color.Gold : Color.Gray;
        string counter = ClientStrings.Format(ClientStrings.MailPanel_CharCount,
            ("Length", _bodyField.Text.Length), ("Max", Constants.MailBodyMaxLength));
        float counterW = font.MeasureString(counter).X;
        UiHelper.DrawLabel(sb, font, counter, new Vector2(_bodyRect.Right - counterW, _bodyRect.Y - 14), counterColor, counterW + 2);
        UiHelper.DrawFilledRect(sb, _bodyRect, UiHelper.TextInputBg);
        UiHelper.DrawBorder(sb, _bodyRect, _bodyField.IsFocused ? Color.CornflowerBlue : Color.Gray);
        _bodyField.SetBounds(_bodyRect);
        _bodyField.Draw(sb, font, nowMs);

        string hdr = ClientStrings.Format(ClientStrings.MailPanel_AttachHeader,
            ("Count", _staged.Count), ("Max", Constants.MaxMailAttachments));
        UiHelper.DrawLabel(sb, font, hdr, new Vector2(c.X + 4, _attachHeaderY), Color.Yellow, c.Width - 8);
        _invList.Draw(sb, font, _invRect);
        _stagedList.Draw(sb, font, _stagedRect);

        // Item tooltip for the hovered inventory / staged row (suppressed while the amount prompt is up).
        if (itemsTex is not null && !_amountPrompt.IsOpen)
        {
            if (_invList.HoveredIndex >= 0 && _invList.HoveredIndex < _invSlots.Count)
                ShowSlotTooltip(state, itemsTex, _invSlots[_invList.HoveredIndex], (TooltipScope, "inv", _invSlots[_invList.HoveredIndex]));
            else if (_stagedList.HoveredIndex >= 0 && _stagedList.HoveredIndex < _staged.Count)
                ShowSlotTooltip(state, itemsTex, _staged[_stagedList.HoveredIndex].Slot, (TooltipScope, "staged", _stagedList.HoveredIndex));
        }

        _attachBtn.Draw(sb, font, _input);
        _unstageBtn.Draw(sb, font, _input);

        // CoD price field (blank/0 = ordinary mail): a labeled numeric input; when set, a live "you receive after
        // tax" preview to its right mirrors the server's per-item tax.
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_CodPriceLabel),
            new Vector2(_codRect.X - 76, _codRect.Y + 3), Color.LightGray, 74);
        _codField.Draw(sb, font, _codRect, _focusField == 3, nowMs);
        int codPrice = ParseCod();
        if (codPrice > 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.MailPanel_CodNet, ("Net", CodNet(codPrice, StagedItemCount(state)))),
                new Vector2(_codRect.Right + 8, _codRect.Y + 3), Color.LightGreen, Math.Max(0, c.Right - _codRect.Right - 12));
        }

        // Live "Cost to Send" preview — or a red warning (Send disabled) when the compose can't be sent yet
        // (bad recipient list / multi + attachments), so the user can correct it first.
        string sendBlock = SendBlockReason(state);
        if (sendBlock.Length > 0)
        {
            UiHelper.DrawLabel(sb, font, sendBlock, new Vector2(c.X + 4, _costLabelY), Color.OrangeRed, c.Width - 8);
        }
        else
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.MailPanel_CostToSend, ("Price", ComputeSendCost(state))),
                new Vector2(c.X + 4, _costLabelY), Color.Gold, c.Width - 8);
        }

        _sendBtn.Draw(sb, font, _input);
        _cancelBtn.Draw(sb, font, _input);

        // Amount prompt overlays the compose content; the no-subject confirm overlays everything.
        _amountPrompt.Draw(sb, font, c, nowMs);
        _noSubjectConfirm.Draw(sb, font, c);
    }

    // Non-blank recipient tokens in the (comma-separated) To field.
    private int RecipientCount() => _toField.Text.Split(',').Count(t => t.Trim().Length > 0);

    // Live send-cost preview mirroring the server rule: single = base + per-attachment + a percent of what
    // the parcel is WORTH; a comma-separated multi-recipient list (attachments disallowed) = base per
    // recipient; escalated for a long body.
    private long ComputeSendCost(ClientState state)
    {
        int recipients = RecipientCount();
        long cost = recipients > 1
            ? EconomyFormulas.MailSendCost(0) * recipients
            : EconomyFormulas.MailSendCost(_staged.Count, StagedValue(state));
        return cost * BodyCostMultiplier();
    }

    // Gold value of everything staged, by the same rule the server charges on — EconomyFormulas owns the
    // gold-is-a-currency-attachment special case so the two cannot drift. A staged entry carries a
    // Quantity of 0 to mean "the whole non-currency slot", which is the stack size.
    private long StagedValue(ClientState state)
    {
        long total = 0;
        foreach (var (slot, quantity) in _staged)
        {
            if (slot < 1 || slot > Constants.MaxInv) continue;
            var inv = state.Me.Inv[slot];
            if (inv is null || inv.Num <= 0 || inv.Num > state.Limits.Items) continue;
            int qty = quantity > 0 ? quantity : inv.Quantity;
            total += EconomyFormulas.MailAttachmentValue(inv.Num, qty, state.Items[inv.Num].Price);
        }
        return total;
    }

    // Tiered long-body cost multiplier (mirrors the server): x2 over the first threshold, x10 over the second.
    // Also colors the char counter so the escalating cost is visible as you type.
    private int BodyCostMultiplier() =>
        _bodyField.Text.Length > Constants.MailVeryLongBodyThreshold ? Constants.MailVeryLongBodyCostMultiplier
        : _bodyField.Text.Length > Constants.MailLongBodyThreshold ? Constants.MailLongBodyCostMultiplier : 1;

    // A localized reason the current compose can't be sent (empty string = OK to send). Drives the Send-button
    // disable + the warning line. An entirely empty To returns "" (Send is simply disabled — no scary warning
    // on a fresh compose); a blank token in a list, too many recipients, or multi + attachments each warn.
    private string SendBlockReason(ClientState state)
    {
        int nonBlank = 0;
        bool anyBlank = false, anySpace = false;
        foreach (var raw in _toField.Text.Split(','))
        {
            string t = raw.Trim();   // leading/trailing spaces don't count; an INNER space means an invalid name
            if (t.Length == 0)
            {
                anyBlank = true;
            }
            else
            {
                nonBlank++;
                if (t.Any(char.IsWhiteSpace)) anySpace = true;
            }
        }

        if (nonBlank == 0) return "";
        if (anyBlank) return ClientStrings.Get(ClientStrings.MailPanel_BlankRecipientWarn);
        if (anySpace) return ClientStrings.Get(ClientStrings.MailPanel_InvalidRecipientWarn);
        if (nonBlank > Constants.MaxMailRecipients)
            return ClientStrings.Format(ClientStrings.MailPanel_TooManyRecipientsWarn, ("Max", Constants.MaxMailRecipients));
        if (nonBlank > 1 && _staged.Count > 0) return ClientStrings.Get(ClientStrings.MailPanel_MultiNoAttachWarn);

        // CoD: single-recipient, and must carry at least one ITEM (non-gold) attachment (the server backstops both).
        if (ParseCod() > 0)
        {
            if (nonBlank > 1) return ClientStrings.Get(ClientStrings.MailPanel_CodSingleOnlyWarn);
            if (StagedItemCount(state) == 0) return ClientStrings.Get(ClientStrings.MailPanel_CodNeedsItemWarn);
        }

        // Affordability: the postage AND any staged gold both draw from the SAME gold pile, so the true outlay
        // is their sum — block Send when the player can't cover it (the server still backstops postage). This
        // also stops staging more gold than remains after postage, which the server would silently under-escrow.
        long need = ComputeSendCost(state) + StagedGold(state);
        if (need > state.PlayerGold())
            return ClientStrings.Format(ClientStrings.MailPanel_CannotAffordWarn, ("Cost", need));
        return "";
    }

    // Gold staged as an attachment (currency amounts drawn from the gold pile). Added to the postage for the
    // affordability check so the two outlays against the same pile are counted together.
    private long StagedGold(ClientState state)
    {
        long sum = 0;
        foreach (var (slot, amount) in _staged)
        {
            var inv = state.Me?.Inv?[slot];
            if (inv is not null && inv.Num == Constants.GoldItemIndex) sum += amount;
        }
        return sum;
    }

    // CoD price parsed from the field (blank/invalid = 0 = ordinary mail), capped at the market ceiling.
    private int ParseCod() => int.TryParse(_codField.Text, out int p) && p > 0 ? Math.Min(p, Constants.MarketMaxPrice) : 0;

    // Staged ITEM attachments (non-gold): a CoD requires at least one, and they drive the per-item tax.
    private int StagedItemCount(ClientState state)
    {
        int n = 0;
        foreach (var (slot, _) in _staged)
        {
            var inv = state.Me?.Inv?[slot];
            if (inv is not null && inv.Num > 0 && inv.Num != Constants.GoldItemIndex) n++;
        }
        return n;
    }

    // Gold the sender nets from a paid CoD after the per-item tax (mirrors MailSystem.CodNet server-side).
    private static int CodNet(int price, int itemCount) =>
        price - (int)((long)price * Constants.MarketSaleTaxPercent * itemCount / 100);

    private static void DrawFieldLabel(SpriteBatch sb, SpriteFont font, string label, Rectangle fieldRect)
        => UiHelper.DrawLabel(sb, font, label, new Vector2(fieldRect.X, fieldRect.Y - 14), Color.LightGray, fieldRect.Width);

    // Feed the shared item Tooltip for a hovered inventory slot (compose lists).
    private void ShowSlotTooltip(ClientState state, IReadOnlyList<Texture2D?> itemsTex, int invSlot, object key)
    {
        var slot = state.Me?.Inv?[invSlot];
        if (slot is null || slot.Num <= 0 || slot.Num >= state.Items.Length) return;
        var def = state.Items[slot.Num];
        if (def is not null)
            Tooltip.NotifyHoverItem(TooltipScope, key, def, slot, state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
    }

    private void DrawReadingPane(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle r, IReadOnlyList<Texture2D?> itemsTex)
    {
        UiHelper.DrawFilledRect(sb, r, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, r, UiHelper.ConfirmOverlayBorder);

        var msg = _table.SelectedItem;
        if (msg is null)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_NoSelection),
                new Vector2(r.X + 6, r.Y + 6), Color.Gray, r.Width - 12);
            return;
        }

        float maxW = r.Width - 12;
        int lineH = font.LineSpacing;
        var attach = msg.Attachments.Where(a => a.ItemNum > 0).ToList();

        // Attachments sit BELOW the message, pinned to the pane bottom (so a long body can't push them out of
        // reach): a divider, an "Attachments" label, then one line per stack.
        int countdownH = msg.DeleteAt > 0 ? lineH : 0;   // one line reserved at the very bottom for the countdown
        int attachRows = attach.Count > 0 ? attach.Count + 1 : 0;
        int attachTop = r.Bottom - 4 - countdownH - attachRows * lineH;

        float y = r.Y + 6;
        UiHelper.DrawLabel(sb, font, msg.Subject, new Vector2(r.X + 6, y), Color.White, maxW);
        y += lineH;
        string meta = _showOutbox
            ? ClientStrings.Format(ClientStrings.MailPanel_MetaFormatSent, ("Recipient", msg.Recipient), ("Time", FormatTime(msg.TimeUtc)))
            : ClientStrings.Format(ClientStrings.MailPanel_MetaFormat, ("Sender", msg.Sender), ("Time", FormatTime(msg.TimeUtc)));
        UiHelper.DrawLabel(sb, font, meta, new Vector2(r.X + 6, y), Color.Gray, maxW);
        y += lineH;
        // A per-message status line under the header: in-transit ETA, an inbox CoD's lock/afford status, or an
        // outbox CoD's price + expected net.
        if (IsInTransit(msg))
        {
            // In transit — outbox only now (the inbox hides undelivered mail): estimated time until delivery,
            // rounded UP to the nearest 2 minutes (2, 4, 6, ...).
            int mins = Math.Max(2, (int)Math.Ceiling((msg.DeliverAt - _serverNowUtc) / 120.0) * 2);
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.MailPanel_EstDelivery, ("Minutes", mins)),
                new Vector2(r.X + 6, y), Color.Orange, maxW);
            y += lineH;
        }
        else if (!_showOutbox && msg.CodPrice > 0)
        {
            // A delivered but unpaid CoD in the inbox: locked; show the lock hint, or a red note if unaffordable.
            bool afford = state.PlayerGold() >= msg.CodPrice;
            string codLine = afford
                ? ClientStrings.Get(ClientStrings.MailPanel_CodLocked)
                : ClientStrings.Format(ClientStrings.MailPanel_CodCannotAfford, ("Price", msg.CodPrice));
            UiHelper.DrawLabel(sb, font, codLine, new Vector2(r.X + 6, y), afford ? Color.Orange : Color.OrangeRed, maxW);
            y += lineH;
        }
        else if (_showOutbox && msg.CodPrice > 0)
        {
            // The sender's CoD receipt: the price charged and the net they'll receive after the per-item tax.
            int net = CodNet(msg.CodPrice, msg.Attachments.Count(a => a.ItemNum > 0 && a.ItemNum != Constants.GoldItemIndex));
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.MailPanel_CodOutbox, ("Price", msg.CodPrice), ("Net", net)),
                new Vector2(r.X + 6, y), Color.Gold, maxW);
            y += lineH;
        }
        y += 3;

        // Divider between the header and the message body.
        UiHelper.DrawFilledRect(sb, new Rectangle(r.X + 6, (int)y, (int)maxW, 1), UiHelper.ConfirmOverlayBorder);
        y += 5;

        int bodyBottom = attach.Count > 0 ? attachTop - 6 : r.Bottom - 4 - countdownH;
        foreach (string line in WrapLines(font, msg.Body, maxW))
        {
            if (y + lineH > bodyBottom) break;
            UiHelper.DrawLabel(sb, font, line, new Vector2(r.X + 6, y), Color.LightGray, maxW);
            y += lineH;
        }

        // Deletion countdown pinned to the very bottom of the pane (absolute stamp + additive relative readout).
        // Only the recipient's inbox CoD actually RETURNS (the outbox receipt just expires on the normal clock).
        string del = FormatDeletionLine(msg.DeleteAt, _serverNowUtc, !_showOutbox && msg.CodPrice > 0);
        if (del.Length > 0)
            UiHelper.DrawLabel(sb, font, del, new Vector2(r.X + 6, r.Bottom - 4 - lineH), Color.Gray, maxW);

        if (attach.Count == 0) return;

        UiHelper.DrawFilledRect(sb, new Rectangle(r.X + 6, attachTop - 4, (int)maxW, 1), UiHelper.ConfirmOverlayBorder);
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.MailPanel_AttachmentsHeader),
            new Vector2(r.X + 6, attachTop), Color.Gray, maxW);
        float ay = attachTop + lineH;
        for (int ai = 0; ai < attach.Count; ai++)
        {
            var a = attach[ai];
            var def = a.ItemNum > 0 && a.ItemNum < state.Items.Length ? state.Items[a.ItemNum] : null;
            string name = def?.Name?.TrimEnd() ?? "";
            string label = a.Quantity > 1 ? $"{name} ({a.Quantity:N0})" : name;
            UiHelper.DrawLabel(sb, font, label, new Vector2(r.X + 6, ay), a.Claimed || IsInTransit(msg) || msg.CodPrice > 0 ? Color.Gray : Color.Gold, maxW);
            var rowRect = new Rectangle(r.X + 6, (int)ay, (int)maxW, lineH);
            if (def is not null && itemsTex is not null && rowRect.Contains(_input.MousePosition))
            {
                Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, "read", msg.Id, ai), def,
                    new PlayerInvSlot { Num = a.ItemNum, Quantity = a.Quantity, Dur = a.Dur },
                    state.Me, state.Classes, itemsTex, _input.MousePosition,
            state.SpellDefs, state.Items, state.Weather);
            }

            ay += lineH;
        }
    }

    private void RefreshLabels()
    {
        _deleteBtn.Label = ClientStrings.Get(ClientStrings.Common_Delete);
        _claimBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Claim);
        _composeBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Compose);
        _replyBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Reply);
        _inboxTab.Label = ClientStrings.Get(ClientStrings.MailPanel_TabInbox);
        _outboxTab.Label = ClientStrings.Get(ClientStrings.MailPanel_TabOutbox);
        _attachBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Attach);
        _unstageBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Unstage);
        _sendBtn.Label = ClientStrings.Get(ClientStrings.MailPanel_Send);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }

    // Feed the table the (wholesale-replaced) mailbox only when it changes; the Table follows the selected
    // row by Id across the swap and re-sorts. Column layout + sort persist via the Mail* properties.
    private void SyncItems(ClientState state)
    {
        _serverNowUtc = state.MailNowUtc;   // always current, so in-transit tracking follows the latest push
        // Re-filter on a clock advance too: a matured message must leave "in transit" and enter the inbox even
        // if the mail list content is otherwise unchanged.
        if (_lastMailVersion == state.MailVersion && _lastShowOutbox == _showOutbox
            && _lastSocialVersion == state.SocialVersion && _lastSyncNowUtc == _serverNowUtc)
        {
            return;
        }

        _lastMailVersion = state.MailVersion;
        _lastShowOutbox = _showOutbox;
        _lastSocialVersion = state.SocialVersion;
        _lastSyncNowUtc = _serverNowUtc;
        // Inbox hides ignored senders AND in-transit mail (not delivered yet — it shows only in the outbox).
        // The outbox (your sent mail) keeps in-transit rows so you can watch them travel.
        _table.Items = _showOutbox
            ? state.Outbox
            : state.Mail.Where(m => !state.IsSenderIgnored(m.Sender) && !IsInTransit(m)).ToList();
    }

    // A message is "in transit" until its server-side DeliverAt is reached (frozen server clock from the last
    // mailbox push). Legacy mail with DeliverAt 0 reads as already delivered.
    private bool IsInTransit(MailMessage m) => m.DeliverAt > _serverNowUtc;

    // Switch between the inbox and the sent (outbox) view; clears the reading-pane selection.
    private void SetView(bool outbox)
    {
        _showOutbox = outbox;
        _table.ClearSelection();
        _shownId = 0;
        Tooltip.CloseScope(TooltipScope);
    }

    // Rebuild the inventory attach candidates each frame, skipping empty, equipped, already-staged, and
    // non-mailable slots. Selection is preserved by slot so it survives the per-frame rebuild.
    private void RebuildInvList(ClientState state) =>
        InventoryListBuilder.Rebuild(state, _invList, _invSlots,
            (i, item) => (item?.NonMailable == true) || _staged.Any(s => s.Slot == i));   // valor etc. + already-staged

    private void RebuildStagedList(ClientState state)
    {
        _stagedList.Items.Clear();
        foreach (var (slot, amount) in _staged)
        {
            var invSlot = state.Me?.Inv?[slot];
            var item = invSlot is not null ? state.Items[invSlot.Num] : null;
            string name = item?.Name?.TrimEnd() ?? "?";
            _stagedList.Items.Add(amount > 0 ? $"{name} ({amount:N0})" : name);
        }
    }

    // A reply reuses the subject with a single "RE: " prefix; if it already carries one, keep it verbatim.
    private static string BuildReplySubject(string subject)
    {
        string subj = subject.Trim();
        string prefix = ClientStrings.Get(ClientStrings.MailPanel_ReplyPrefix);
        return subj.StartsWith(prefix.TrimEnd(), StringComparison.OrdinalIgnoreCase) ? subj : prefix + subj;
    }

    private void StartCompose(string? prefillTo = null, string? prefillSubject = null)
    {
        _composing = true;
        _focusField = 0;
        _toField.Clear();
        if (!string.IsNullOrEmpty(prefillTo)) _toField.SetText(prefillTo);
        _subjectField.Clear();
        if (!string.IsNullOrEmpty(prefillSubject)) _subjectField.SetText(prefillSubject);
        _bodyField.ClearText();
        _codField.Clear();
        _staged.Clear();
        _invList.SelectedIndex = -1;
        _stagedList.SelectedIndex = -1;
        _amountPrompt.Close();
    }

    private void EndCompose()
    {
        _composing = false;
        _focusField = -1;
        _staged.Clear();
        _amountPrompt.Close();
        _noSubjectConfirm.Close();
    }

    private void Layout(Rectangle c, out Rectangle listRect, out Rectangle readRect)
    {
        _composeBtn.Bounds = new Rectangle(c.X + 4, c.Y + 4, ComposeBtnW, ButtonH);
        _inboxTab.Bounds = new Rectangle(_composeBtn.Bounds.Right + 8, c.Y + 4, TabW, ButtonH);
        _outboxTab.Bounds = new Rectangle(_inboxTab.Bounds.Right + 2, c.Y + 4, TabW, ButtonH);
        int contentTop = _composeBtn.Bounds.Bottom + 4;
        _actionBtnY = c.Bottom - ButtonH - 4;
        int paneBottom = _actionBtnY - 4;
        int usableH = Math.Max(0, paneBottom - contentTop);

        // Side-by-side: the message LIST on the left (~46% width), the OPEN message on the right.
        int listW = (c.Width - 12) * 46 / 100;
        listRect = new Rectangle(c.X + 4, contentTop, listW, usableH);
        int readX = listRect.Right + 4;
        readRect = new Rectangle(readX, contentTop, Math.Max(0, c.Right - 4 - readX), usableH);
        // The action buttons are positioned by LayoutActionButtons once their visibility is known.
    }

    // Position the visible action buttons as a CENTERED group under the reading pane, ordered Claim, Reply,
    // Delete (Delete — the most frequent — rightmost). Claim/Reply take no space when hidden; Delete is always
    // present. Called from Update and Draw with the same visibility so their bounds agree.
    private void LayoutActionButtons(Rectangle readRect, bool showClaim, bool showPayCod, bool showReply)
    {
        // The leftmost slot is Pay CoD (an unpaid CoD) OR Claim (a normal unclaimed message) — mutually exclusive.
        int primaryW = showPayCod ? PayCodBtnW : ClaimBtnW;
        bool showPrimary = showClaim || showPayCod;
        int groupW = DeleteBtnW + (showReply ? ReplyBtnW + 4 : 0) + (showPrimary ? primaryW + 4 : 0);
        int x = readRect.X + Math.Max(0, (readRect.Width - groupW) / 2);
        if (showPayCod)
        {
            _payCodBtn.Bounds = new Rectangle(x, _actionBtnY, PayCodBtnW, ButtonH);
            x += PayCodBtnW + 4;
        }
        else if (showClaim)
        {
            _claimBtn.Bounds = new Rectangle(x, _actionBtnY, ClaimBtnW, ButtonH);
            x += ClaimBtnW + 4;
        }
        if (showReply)
        {
            _replyBtn.Bounds = new Rectangle(x, _actionBtnY, ReplyBtnW, ButtonH);
            x += ReplyBtnW + 4;
        }
        _deleteBtn.Bounds = new Rectangle(x, _actionBtnY, DeleteBtnW, ButtonH);
    }

    private void LayoutCompose(Rectangle c)
    {
        const int fieldH = 20, labelH = 14, gap = 4, bodyH = 58;
        int x = c.X + 4, w = c.Width - 8;
        int y = c.Y + 2 + labelH;
        _toRect = new Rectangle(x, y, w, fieldH);
        y += fieldH + gap + labelH;
        _subjRect = new Rectangle(x, y, w, fieldH);
        y += fieldH + gap + labelH;
        _bodyRect = new Rectangle(x, y, w, bodyH);
        y += bodyH + gap;
        _attachHeaderY = y;
        y += labelH + 2;

        _sendBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
        _costLabelY = _sendBtn.Bounds.Y - labelH - 2;   // the "Cost to Send" line sits just above Send/Cancel
        // CoD price field row, just above the cost line (label drawn to its left, net preview to its right).
        _codRect = new Rectangle(x + 80, _costLabelY - fieldH - 4, 90, fieldH);
        int attachY = _codRect.Y - ButtonH - 6;
        int halfW = (w - 4) / 2;
        _attachBtn.Bounds = new Rectangle(x, attachY, halfW, ButtonH);
        _unstageBtn.Bounds = new Rectangle(x + halfW + 4, attachY, w - halfW - 4, ButtonH);

        int listsTop = y;
        int listH = Math.Max(0, attachY - 4 - listsTop);
        _invRect = new Rectangle(x, listsTop, halfW, listH);
        _stagedRect = new Rectangle(x + halfW + 4, listsTop, w - halfW - 4, listH);
    }

    private static string FormatTime(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    // The reading-pane "Deletes {Time} (…)" line: an absolute stamp plus an additive relative readout. Empty
    // for a message that never expires (DeleteAt 0).
    private static string FormatDeletionLine(long deleteAt, long nowUtc, bool isCod)
        => deleteAt <= 0 ? "" : ClientStrings.Format(isCod ? ClientStrings.MailPanel_ReturnsLine : ClientStrings.MailPanel_DeletesLine,
            ("Time", FormatTime(deleteAt)), ("Rel", FormatCountdown(deleteAt - nowUtc)));

    // Additive days/hours/minutes, ROUNDED UP to the minute (0-59s reads "1 minute"): a zero days/hours part is
    // dropped, minutes is always shown, and the parts join "A, B, and C" / "A and B" / "A". Pluralization is
    // localized (1 day vs 2 days). Used by the deletion countdown (and, later, the CoD return countdown).
    private static string FormatCountdown(long remainingSeconds)
    {
        long totalMinutes = (Math.Max(0, remainingSeconds) + 59) / 60;   // round up
        long days = totalMinutes / (24 * 60);
        long hours = totalMinutes % (24 * 60) / 60;
        long minutes = totalMinutes % 60;

        var parts = new List<string>(3);
        if (days > 0)
            parts.Add(ClientStrings.Format(days == 1 ? ClientStrings.MailPanel_CountdownDay : ClientStrings.MailPanel_CountdownDays, ("N", days)));
        if (hours > 0)
            parts.Add(ClientStrings.Format(hours == 1 ? ClientStrings.MailPanel_CountdownHour : ClientStrings.MailPanel_CountdownHours, ("N", hours)));
        parts.Add(ClientStrings.Format(minutes == 1 ? ClientStrings.MailPanel_CountdownMinute : ClientStrings.MailPanel_CountdownMinutes, ("N", minutes)));

        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return parts[0] + " and " + parts[1];
        return parts[0] + ", " + parts[1] + ", and " + parts[2];
    }

    private static List<string> WrapLines(SpriteFont font, string text, float maxWidth) =>
        UiHelper.WrapLines(font, text, maxWidth);
}

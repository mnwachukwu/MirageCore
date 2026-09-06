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

/// <summary>The input line: typing, history, selection and word motion, channel routing, and the
/// slash-command handling that turns a typed line into a packet or a local action.</summary>
public sealed partial class ChatPanel
{
    public void Update(InputState input, ClientState state, ClientPacketSender sender, long nowMs, bool keyboardActive = true, bool suppressKeyboard = false)
    {
        _lastInput = input;
        _panel.Update(input);   // locked chrome (no-op today, but keeps the standard update path)

        // Tab strip is hit-tested before the log so right-click on a tab opens the options menu
        // instead of falling through to the log's player-name right-click handler. Left-click on
        // a tab also consumes here so it never reaches the world below the panel.
        HandleTabStripInput(input);

        // Channel dropdown (left of the input row). Updated before the log + input box so its clicks
        // (header, and the upward popup that overlaps the log) are consumed before those handlers run.
        RebuildChannelDropdown(state);
        _channelDropDown.Update(input, ChannelDropRect());
        int dropIdx = _channelDropDown.SelectedIndex;
        if (dropIdx >= 0 && dropIdx < _channelDropChannels.Count && _channelDropChannels[dropIdx] != _activeChannel)
        {
            _activeChannel = _channelDropChannels[dropIdx];
            OnActiveChannelChanged?.Invoke();
        }

        // Log area handles its own scroll, scrollbar drag and text selection. Its copy/cut
        // only fires when the chat owns keyboard focus (no floating panel is active).
        _log.SetBounds(LogAreaBounds());
        _log.Update(input, keyboardActive && !suppressKeyboard);

        // A modal text field elsewhere (the ChatOptionsPanel rename box) owns the keyboard this
        // frame. Drop chat-input focus so we neither type into it nor blink a caret, and skip all
        // key handling below. Mouse-driven tab/scroll interaction above still runs.
        if (suppressKeyboard)
        {
            if (_focused)
            {
                _focused = false;
                _anchorIndex = -1;
            }
            return;
        }

        // Mouse press outside input box → defocus immediately
        if (input.IsMouseJustPressed() && _focused && !InputRect().Contains(input.MousePosition))
        {
            _focused = false;
            _anchorIndex = -1;
        }

        // Enter
        if (input.IsKeyPressed(Keys.Enter))
        {
            if (!_focused)
            {
                _focused = true;
                _caretIndex = _inputText.Length;
                _anchorIndex = -1;
                _log.Defocus();
            }
            else if (_inputText.Length > 0)
            {
                AddToHistory(_inputText);
                bool keepInput = DispatchInput(_inputText, state, sender);
                if (!keepInput) ClearInput();
            }
            else
            {
                _focused = false;
            }
        }

        // Right-click on a name span in the chat log → bubble up to GameplayScreen so it can
        // open the player context menu. Don't fire on own name. Click is consumed so the
        // ContextMenu's outside-click logic next frame doesn't immediately re-dismiss.
        if (input.IsRightMouseClicked() && _log.Bounds.Contains(input.MousePosition))
        {
            string? nm = _log.NameAt(input.MousePosition);
            if (!string.IsNullOrEmpty(nm) && nm != state.Me.Name.Trim())
            {
                OnPlayerRightClicked?.Invoke(nm, input.MousePosition);
                input.ConsumeRightMouseClick();
            }
        }

        // Escape
        if (input.IsKeyPressed(Keys.Escape))
        {
            if (_focused)
            {
                _focused = false;
                _anchorIndex = -1;
            }
            _log.Defocus();
        }

        // Mouse press on input box → focus and start selection drag
        if (input.IsMouseJustPressed() && InputRect().Contains(input.MousePosition))
        {
            _focused = true;
            _inputDragging = true;
            _log.Defocus();
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            if (!shift)
            {
                _inputDragAnchorX = input.MousePosition.X;
                _inputDragAnchorPos = -1;
                _anchorIndex = -1;
            }
            else if (_anchorIndex < 0)
            {
                _anchorIndex = _caretIndex;
            }

            _pendingClickX = input.MousePosition.X;
        }
        // Continue input drag — update caret while mouse is held
        if (_inputDragging)
        {
            if (input.IsMouseDown())
                _pendingClickX = input.MousePosition.X;
            else
                _inputDragging = false;
        }

        bool ctrl = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl);

        if (_focused)
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);

            // Character input (backspace comes through TextInput as '\b')
            foreach (char c in input.TextInput)
            {
                if (c == '\b')
                {
                    if (_anchorIndex >= 0)
                    {
                        DeleteSelection();
                    }
                    else if (_caretIndex > 0)
                    {
                        _inputText = _inputText.Remove(_caretIndex - 1, 1);
                        _caretIndex--;
                    }
                    _historyPos = -1;
                }
                else if (!char.IsControl(c) && _inputText.Length < 200)
                {
                    if (_anchorIndex >= 0) DeleteSelection();
                    _inputText = _inputText.Insert(_caretIndex, c.ToString());
                    _caretIndex++;
                    _historyPos = -1;
                    // `/r ` auto-rewrite: typing space after a bare "/r" replaces the buffer
                    // with "/w <lastPartner> " so the user can immediately type the reply body.
                    // Same UX as pressing enter on /r, just one character earlier in the flow.
                    // Silent no-op if no partner is known.
                    if (c == ' ' && _inputText == ReplyTriggerWithSpace && !string.IsNullOrEmpty(_lastWhisperPartner))
                    {
                        _inputText = $"/w {_lastWhisperPartner} ";
                        _caretIndex = _inputText.Length;
                    }
                }
            }

            // Ctrl+Left — jump word left
            if (ctrl && input.IsKeyPressedOrRepeating(Keys.Left, nowMs))
            {
                if (_anchorIndex >= 0 && !shift)
                {
                    _caretIndex = Math.Min(_caretIndex, _anchorIndex);
                    _anchorIndex = -1;
                }
                else
                {
                    if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                    _caretIndex = PrevWordBoundary(_inputText, _caretIndex);
                    if (!shift) _anchorIndex = -1;
                }
            }

            // Ctrl+Right — jump word right
            if (ctrl && input.IsKeyPressedOrRepeating(Keys.Right, nowMs))
            {
                if (_anchorIndex >= 0 && !shift)
                {
                    _caretIndex = Math.Max(_caretIndex, _anchorIndex);
                    _anchorIndex = -1;
                }
                else
                {
                    if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                    _caretIndex = NextWordBoundary(_inputText, _caretIndex);
                    if (!shift) _anchorIndex = -1;
                }
            }

            // Left arrow
            if (!ctrl && input.IsKeyPressedOrRepeating(Keys.Left, nowMs))
            {
                if (_anchorIndex >= 0 && !shift)
                {
                    _caretIndex = Math.Min(_caretIndex, _anchorIndex);
                    _anchorIndex = -1;
                }
                else
                {
                    if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                    if (_caretIndex > 0) _caretIndex--;
                    if (!shift) _anchorIndex = -1;
                }
            }

            // Right arrow
            if (!ctrl && input.IsKeyPressedOrRepeating(Keys.Right, nowMs))
            {
                if (_anchorIndex >= 0 && !shift)
                {
                    _caretIndex = Math.Max(_caretIndex, _anchorIndex);
                    _anchorIndex = -1;
                }
                else
                {
                    if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                    if (_caretIndex < _inputText.Length) _caretIndex++;
                    if (!shift) _anchorIndex = -1;
                }
            }

            // Up arrow — browse to older history entry (only when input is empty or already browsing)
            if (input.IsKeyPressed(Keys.Up) && (_inputText.Length == 0 || _historyPos >= 0))
            {
                if (_historyPos < 0 && _history.Count > 0)
                    _historyPos = _history.Count - 1;
                else if (_historyPos > 0)
                    _historyPos--;

                if (_historyPos >= 0)
                    LoadHistory(_historyPos);
            }

            // Down arrow — browse to more recent history entry; past end returns to blank
            if (input.IsKeyPressed(Keys.Down) && _historyPos >= 0)
            {
                _historyPos++;
                if (_historyPos >= _history.Count)
                {
                    _historyPos = -1;
                    ClearInput();
                }
                else
                {
                    LoadHistory(_historyPos);
                }
            }

            // Ctrl+A — select all
            if (ctrl && input.IsKeyPressed(Keys.A))
            {
                _anchorIndex = 0;
                _caretIndex = _inputText.Length;
            }

            // Ctrl+V — paste (replaces selection)
            if (ctrl && input.IsKeyPressed(Keys.V))
            {
                string? clip = ClipboardService.GetText();
                if (clip is not null)
                {
                    if (_anchorIndex >= 0) DeleteSelection();
                    string clean = FilterForFont(clip.Replace("\r", "").Replace("\n", ""));
                    int room = 200 - _inputText.Length;
                    if (room > 0)
                    {
                        string ins = clean[..Math.Min(clean.Length, room)];
                        _inputText = _inputText.Insert(_caretIndex, ins);
                        _caretIndex += ins.Length;
                    }
                    _historyPos = -1;
                }
            }

            // Ctrl+X — cut selection
            if (ctrl && input.IsKeyPressed(Keys.X) && _anchorIndex >= 0 && _anchorIndex != _caretIndex)
            {
                int s = Math.Min(_caretIndex, _anchorIndex);
                int e = Math.Max(_caretIndex, _anchorIndex);
                ClipboardService.SetText(_inputText[s..e]);
                DeleteSelection();
                _historyPos = -1;
            }

            // Ctrl+C — copy selection (or full input if no selection) when focused
            if (ctrl && input.IsKeyPressed(Keys.C))
            {
                if (_anchorIndex >= 0 && _anchorIndex != _caretIndex)
                {
                    int s = Math.Min(_caretIndex, _anchorIndex);
                    int e = Math.Max(_caretIndex, _anchorIndex);
                    ClipboardService.SetText(_inputText[s..e]);
                }
                else if (_inputText.Length > 0)
                {
                    ClipboardService.SetText(_inputText);
                }
            }
        }

        // Ctrl+L — clear log (always)
        if (ctrl && input.IsKeyPressed(Keys.L))
            _log.Clear();
    }

    private void AddToHistory(string text)
    {
        if (_history.Count == 0 || _history[^1] != text)
        {
            _history.Add(text);
            if (_history.Count > 100) _history.RemoveAt(0);
        }
    }

    private void LoadHistory(int pos)
    {
        _inputText = _history[pos];
        _caretIndex = _inputText.Length;
        _viewOffset = 0;
        _anchorIndex = -1;
    }

    private void ClearInput()
    {
        _inputText = "";
        _caretIndex = 0;
        _viewOffset = 0;
        _anchorIndex = -1;
        _historyPos = -1;
    }

    private void DeleteSelection()
    {
        if (_anchorIndex < 0) return;
        int start = Math.Min(_caretIndex, _anchorIndex);
        int end = Math.Max(_caretIndex, _anchorIndex);
        _inputText = _inputText.Remove(start, end - start);
        _caretIndex = start;
        _anchorIndex = -1;
    }

    private static int PrevWordBoundary(string text, int pos)
    {
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
        return pos;
    }

    private static int NextWordBoundary(string text, int pos)
    {
        while (pos < text.Length && !char.IsWhiteSpace(text[pos])) pos++;
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        return pos;
    }

    /// <summary>Returns true when the input buffer was rewritten and must be preserved
    /// (used by `/r`). False means the caller should clear the input as usual.</summary>
    private bool DispatchInput(string text, ClientState state, ClientPacketSender sender)
    {
        text = text.Trim();
        if (text.StartsWith('/'))
            return HandleCommand(text[1..], state, sender);

        if (!string.IsNullOrWhiteSpace(text))
            RouteToActiveChannel(text, state, sender);
        return false;
    }

    /// <summary>Sends plain (non-slash) input to whichever channel the dropdown (left of the input
    /// box) has selected. Admin/Guild/Officer fall back to Say when the player no longer qualifies
    /// (access lost, left the guild, demoted) so text is never silently dropped. Every channel is
    /// reached by a slash command or the dropdown; bare symbol prefixes (' - ! @ " =) are not input
    /// syntax.</summary>
    private void RouteToActiveChannel(string text, ClientState state, ClientPacketSender sender)
        => ExecuteSpeech(SpeechChannelRouter.ForActiveChannel(_activeChannel, text,
            state.Me.Access, state.Me.GuildId, state.GuildInfo?.MyRank ?? GuildRank.None), sender);

    /// <summary>Executes a resolved <see cref="SpeechIntent"/> — sends the packet, or shows the error
    /// line. Shared by plain-text routing and the speech slash commands so both behave identically.</summary>
    private void ExecuteSpeech(SpeechIntent intent, ClientPacketSender sender)
    {
        switch (intent.Kind)
        {
            case SpeechKind.Say:
                sender.SendSayMsg(intent.Body);
                break;
            case SpeechKind.Yell:
                sender.SendYell(intent.Body);
                break;
            case SpeechKind.Broadcast:
                sender.SendBroadcastMsg(intent.Body);
                break;
            case SpeechKind.Emote:
                sender.SendEmoteMsg(intent.Body);
                break;
            case SpeechKind.Tell:
                sender.SendPlayerMsg(intent.Target, intent.Body);
                _lastWhisperPartner = intent.Target;
                break;
            case SpeechKind.Notice:
                sender.SendNoticeMsg(intent.Body);
                break;
            case SpeechKind.AdminChat:
                sender.SendAdminMsg(intent.Body);
                break;
            case SpeechKind.Guild:
                sender.SendGuildChat(intent.Body, officer: false);
                break;
            case SpeechKind.Officer:
                sender.SendGuildChat(intent.Body, officer: true);
                break;
            case SpeechKind.TellUsage:
                AddLine(ClientStrings.Get(ClientStrings.ChatPanel_UsageTell), GameColor.Warning);
                break;
            case SpeechKind.NotInGuild:
                AddLine(ClientStrings.Get(ClientStrings.ChatPanel_NotInGuild), GameColor.Warning);
                break;
            case SpeechKind.NotOfficer:
                AddLine(ClientStrings.Get(ClientStrings.ChatPanel_NotOfficer), GameColor.Warning);
                break;
        }
    }

    /// <summary>Parks plain-text input on <paramref name="ch"/> (from a bare `/channel` command) and persists
    /// the choice. Switches silently — the dropdown header follows _activeChannel on the next rebuild, so the
    /// combo box updating to match is the only feedback.</summary>
    private void SwitchActiveChannel(ActiveSpeechChannel ch)
    {
        if (ch == _activeChannel) return;
        _activeChannel = ch;
        OnActiveChannelChanged?.Invoke();
    }

    /// <summary>Returns true when the input buffer was rewritten and must be preserved
    /// (used by `/r`). False means the caller should clear the input as usual.</summary>
    private bool HandleCommand(string cmd, ClientState state, ClientPacketSender sender)
    {
        var parts = cmd.Split(' ', 2);
        string word = parts[0].ToLowerInvariant();
        string cmdArg = parts.Length > 1 ? parts[1] : "";
        var rank = state.GuildInfo?.MyRank ?? GuildRank.None;
        // A bare channel command (no message) parks the input on that dropdown channel instead of sending:
        // `/g` switches to Guild chat, `/y` to Yell, and so on. Only the six dropdown channels, and only when
        // the player qualifies (same gating as the dropdown); every command alias resolves through the router.
        // The dropdown header follows _activeChannel on the next rebuild, so the combo box updates to match.
        if (string.IsNullOrWhiteSpace(cmdArg) &&
            SpeechChannelRouter.ChannelSwitchForCommand(word, state.Me.Access, state.Me.GuildId, rank) is { } switchTo)
        {
            SwitchActiveChannel(switchTo);
            return false;
        }
        // Speech commands (say/yell/broadcast/emote/tell/notice/admin/guild/officer + aliases) resolve
        // through the shared pure router so plain-text and slash routing stay identical and testable.
        if (SpeechChannelRouter.ForCommand(word, cmdArg,
                state.Me.Access, state.Me.GuildId, rank) is { } speech)
        {
            ExecuteSpeech(speech, sender);
            return false;
        }
        switch (word)
        {
            case "help":
                OnToggleHelp?.Invoke();
                break;
            case "fps":
                state.ShowFps = !state.ShowFps;
                AddLine(ClientStrings.Get(state.ShowFps ? ClientStrings.ChatPanel_FpsOn : ClientStrings.ChatPanel_FpsOff),
                        GameColor.Pink);
                break;
            case "stats":
                OnToggleStats?.Invoke();
                break;
            case "who":
                sender.SendWhoIsOnline();
                break;
            case "info":
                sender.SendPlayerInfoRequest(parts.Length > 1 ? parts[1] : state.Me.Name.Trim());
                break;
            case "played":
                sender.SendPlayedRequest();
                break;
            case "home":
                // Refused here as well as on the server, so the answer is instant and the packet is not
                // sent at all mid-fight. The server refuses it too — this half is courtesy, that half is
                // the rule.
                if (state.AmIInCombat())
                    AddLine(ClientStrings.Get(ClientStrings.ChatPanel_HomeInCombat), GameColor.BrightRed);
                else
                    sender.SendHomeRequest();
                break;
            case "homecd":
                // Always allowed, combat included: asking the time costs nothing and spends nothing.
                sender.SendHomeCooldownRequest();
                break;
            case "inv":
                OnToggleInventory?.Invoke();
                break;
            case "train":
                OnToggleTraining?.Invoke();
                break;
            case "join":
                // /join <name> sends a party invite (and the server treats it as an acceptance
                // when <name> has already invited me); /join with no args accepts a pending invite.
                if (parts.Length > 1) sender.SendPartyRequest(parts[1]);
                else sender.SendJoinParty(0);
                break;
            case "leave":
                sender.SendLeaveParty();
                break;
            case "trade":
                // /trade <name> sends a direct-trade invite — the mouse equivalent is the "Trade" item on a
                // player's right-click menu. The server validates the target is online and within range (r=5).
                if (parts.Length > 1) sender.SendTradeInvite(parts[1]);
                break;
            case "r":
                // Reply shortcut. With a partner, prefill the input as `/w <name> ` and keep focus —
                // the user types the actual message and presses enter again, and we return true so
                // the caller skips ClearInput. With no partner: fall through to the normal break
                // path so the caller's ClearInput runs (clears the literal `/r` text) — no message
                // is sent, no error is shown, the buffer just empties.
                if (!string.IsNullOrEmpty(_lastWhisperPartner))
                {
                    _inputText = $"/w {_lastWhisperPartner} ";
                    _caretIndex = _inputText.Length;
                    _anchorIndex = -1;
                    _viewOffset = 0;
                    _historyPos = -1;
                    _focused = true;
                    return true;
                }
                break;
            case "loc":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                sender.SendRequestLocation();
                break;
            case "debug":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                OnToggleDebug?.Invoke();
                break;
            case "kick":
                if (state.Me.Access < AdminLevel.Monitor) goto default;
                if (parts.Length > 1)
                {
                    var kickParts = parts[1].Split(' ', 2);
                    int minutes = 0;
                    if (kickParts.Length == 2 && int.TryParse(kickParts[1], out int km) && km > 0) minutes = km;
                    sender.SendKick(kickParts[0], minutes);
                }
                break;
            case "ban":
                if (state.Me.Access <= AdminLevel.Player) goto default;
                if (parts.Length > 1) sender.SendBan(parts[1]);
                break;
            // A rung above /ban, and against an ONLINE character only: it bans the machine as well as the
            // account, and the machine key exists only on a live session.
            case "hwban":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                if (parts.Length > 1) sender.SendHwBan(parts[1]);
                break;
            case "mute":
                if (state.Me.Access < AdminLevel.Monitor) goto default;
                if (parts.Length > 1)
                {
                    var muteParts = parts[1].Split(' ', 2);
                    int minutes = 0;
                    if (muteParts.Length == 2 && int.TryParse(muteParts[1], out int m) && m > 0) minutes = m;
                    sender.SendMute(muteParts[0], minutes);
                }
                break;
            case "refreshbanlist":
                if (state.Me.Access < AdminLevel.Monitor) goto default;
                sender.SendRefreshBanList();
                break;
            // Lifting a punishment is CREATOR only — a rung above issuing one. These take an account
            // rather than a character: whoever is being un-punished is not on screen to be pointed at.
            case "unban":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                if (parts.Length > 1) sender.SendUnban(parts[1]);
                break;
            case "hwunban":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                if (parts.Length > 1) sender.SendHwUnban(parts[1]);
                break;
            case "unkick":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                if (parts.Length > 1) sender.SendUnkick(parts[1]);
                break;
            case "unmute":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                if (parts.Length > 1) sender.SendUnmute(parts[1]);
                break;
            // Opens the panel, which asks for the report itself. The three commands above stay for
            // lifting somebody by name without opening anything.
            case "moderation":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                OnToggleModeration?.Invoke();
                break;
            case "godmode":
                if (state.Me.Access < AdminLevel.Developer) goto default;
                sender.SendGodMode();
                break;
            case "warpmeto":
                if (state.Me.Access < AdminLevel.Developer) goto default;
                if (parts.Length > 1) sender.SendWarpMeTo(parts[1]);
                break;
            case "warptome":
                if (state.Me.Access < AdminLevel.Developer) goto default;
                if (parts.Length > 1) sender.SendWarpToMe(parts[1]);
                break;
            case "warpto":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                if (parts.Length > 1 && short.TryParse(parts[1], out short map) && map > 0 && map <= state.Limits.Maps)
                    sender.SendWarpTo(map);
                else
                    AddLine(ClientStrings.Get(ClientStrings.ChatPanel_InvalidMapNumber), GameColor.Warning);
                break;
            case "setaccess":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                var t = parts.Length > 1 ? parts[1].Split(' ', 2) : [];
                if (t.Length == 2 && byte.TryParse(t[0], out byte lvl) && Enum.IsDefined(typeof(AdminLevel), lvl))
                    sender.SendSetAccess(t[1], (AdminLevel)lvl);
                break;
            case "startwar":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                sender.SendTerritoryWarDebug(TerritoryWarDebugAction.Start);
                break;
            case "advancewar":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                sender.SendTerritoryWarDebug(TerritoryWarDebugAction.Advance);
                break;
            case "endwar":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                sender.SendTerritoryWarDebug(TerritoryWarDebugAction.End);
                break;
            case "guildreset":
                if (state.Me.Access < AdminLevel.Creator) goto default;
                {
                    var scope = SettlementScope.Day;
                    string arg = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "day";
                    if (arg is "" or "day")
                    {
                        scope = SettlementScope.Day;
                    }
                    else if (arg == "week")
                    {
                        scope = SettlementScope.Week;
                    }
                    else if (arg == "season")
                    {
                        scope = SettlementScope.Season;
                    }
                    else
                    {
                        AddLine(ClientStrings.Get(ClientStrings.ChatPanel_UsageGuildReset), GameColor.Warning);
                        break;
                    }
                    sender.SendGuildReset(scope);
                }
                break;
            case "setsprite":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                if (parts.Length > 1 && short.TryParse(parts[1], out short spr)) sender.SendSetSprite(spr);
                break;
            case "mapreport":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                sender.SendMapReport();
                break;
            case "respawn":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                sender.SendMapRespawn();
                break;
            case "motd":
                if (state.Me.Access < AdminLevel.Mapper) goto default;
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) sender.SendSetMotd(parts[1]);
                break;
            case "tod":
                if (state.Me.Access < AdminLevel.Developer) goto default;
                if (parts.Length > 1 && Enum.TryParse<TimePhase>(parts[1], ignoreCase: true, out var todPhase))
                    sender.SendSetTimeOfDay(todPhase);
                break;
            case "weather":
                if (state.Me.Access < AdminLevel.Developer) goto default;
                if (parts.Length > 1 && Enum.TryParse<WeatherType>(parts[1], ignoreCase: true, out var weather))
                    sender.SendSetWeather(weather);
                break;
            case "roll":
                byte max = 100;
                if (parts.Length > 1 && (!byte.TryParse(parts[1], out max) || max < 2))
                {
                    AddLine(ClientStrings.Get(ClientStrings.ChatPanel_UsageRoll), GameColor.Warning);
                    break;
                }
                sender.SendRoll(max);
                break;
            default:
                AddLine(ClientStrings.Format(ClientStrings.ChatPanel_UnknownCommand, ("Command", parts[0])), GameColor.BrightRed);
                break;
        }
        return false;
    }

}

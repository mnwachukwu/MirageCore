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

public sealed partial class ChatPanel
{
    public static Color GetColor(int index) => TextArea.GetColor(index);

    // Standard window chrome (title bar + border). The chat is a fixed bottom dock, so the panel is fully
    // LOCKED — no move, resize, or close — via DraggablePanel's movable/resizable/showClose options. Built
    // with the dock bounds in the constructor.
    private readonly DraggablePanel _panel;

    // Tabbed chat: each tab owns its own TextArea (line buffer, scroll position, selection),
    // so switching tabs preserves where you were. Channel filter is per tab; a packet is appended
    // to every tab whose filter accepts its `ChatChannel`. Always-channel messages (welcome batch)
    // bypass all filters.
    private sealed class ChatTab
    {
        public AccountConfig.ChatTabConfig Config = new();
        public readonly TextArea Log = new() { EnableHyperlinks = true, ReadOnly = true };
        public bool NotifyPending;
    }

    private readonly List<ChatTab> _tabs = new();
    private int _activeTab;
    private AccountConfig? _config;
    private string _accountName = "";
    // Chat-log display prefs (see OptionsPanel). Mirrored onto every tab's TextArea so a tab added at
    // runtime inherits the current settings. Off by default until GameplayScreen applies the
    // per-character prefs on login.
    private bool _showTimestamps;
    private bool _use24HourClock;
    private bool _showChannelLabels;

    // Active tab's TextArea — kept as a property so the rest of the file (focus, selection, copy,
    // scrollbar, right-click name resolution) reads/writes the visible tab without per-call branching.
    private TextArea _log => _tabs[_activeTab].Log;

    private const int InputH = 20;
    // Channel selector dropdown to the left of the input box.
    private const int ChannelDropW = 88;
    private const int ChannelDropGap = 2;
    private const int TabStripH = 22;
    private const int MaxTabs = 6;
    private const int TabDisplayCharLimit = 15;
    private const int MinW = 220;
    private const int MinH = 100;

    // Tab-strip palette — matches ControlsPanel's tab feel. HoverTabBg doubles as the notify
    // flash color for an inactive tab with a pending message. TabStripBg is the empty "track"
    // behind the tabs — deliberately distinct from the chat log below so the unused space reads
    // as room for more tabs.
    private static readonly Color TabStripBg = new(34, 34, 58);
    private static readonly Color AddTabBg = new(40, 60, 40);
    private static readonly Color TabBorder = new(110, 110, 140);

    // Right-click handler for tab options — wired by GameplayScreen.
    public Action<int, Point>? OnTabRightClicked { get; set; }

    private bool _focused;
    private string _inputText = "";
    private int _caretIndex;
    private int _viewOffset;
    private int _anchorIndex = -1;  // -1 = no selection; otherwise selection anchor position
    private int _pendingClickX = -1; // resolved in Draw where font metrics are available
    private bool _inputDragging;
    private int _inputDragAnchorX = -1; // pixel X of drag start; resolved once in Draw
    private int _inputDragAnchorPos = -1; // resolved char index of drag anchor

    private readonly List<string> _history = new();
    private int _historyPos = -1; // -1 = not browsing history

    public Action? OnToggleInventory { get; set; }
    public Action? OnToggleTraining { get; set; }
    public Action? OnToggleStats { get; set; }
    public Action? OnToggleHelp { get; set; }
    public Action? OnToggleDebug { get; set; }
    public Action? OnToggleModeration { get; set; }
    /// <summary>Fired when the user right-clicks a player name span in the chat log.
    /// GameplayScreen wires this to open the right-click context menu.</summary>
    public Action<string, Point>? OnPlayerRightClicked { get; set; }

    // Tracks the most recent whisper partner in either direction (last tell sent OR last tell
    // received). `/r` then prefills the input with `/w <name> ` so the user can reply without
    // retyping the target.
    private string? _lastWhisperPartner;
    // Buffer state that triggers the inline `/r ` → `/w <name> ` rewrite as soon as the user
    // types the space. Defined as a const so the trigger and its description stay in sync.
    private const string ReplyTriggerWithSpace = "/r ";

    // The channel plain (non-slash) input is sent to, driven by the channel dropdown to the left of
    // the input box. Defaults to Say; the dropdown + saved per-character pref set it. Slash commands
    // (/yell, /guild, ...) always override this for the one message. The enum is public in
    // SpeechChannelRouter.cs so the pure router shares it.
    private ActiveSpeechChannel _activeChannel = ActiveSpeechChannel.Say;
    // Channel selector dropdown docked to the left of the input box. Opens UPWARD (bottom-docked).
    // Rebuilt each frame from access/guild/rank; `_channelDropChannels` maps its row index → channel.
    private readonly DropDown _channelDropDown = new() { OpenUp = true };
    private readonly List<ActiveSpeechChannel> _channelDropChannels = new();
    private InputState? _lastInput;   // cached in Update so Draw can render the dropdown (needs input for hover)
    /// <summary>Fired when the user picks a different channel from the dropdown; GameplayScreen
    /// persists the choice per-character.</summary>
    public Action? OnActiveChannelChanged { get; set; }

    public bool IsFocused => _focused;
    public bool IsLogFocused => _log.IsFocused;

    public bool ContainsMouse(Point mousePos) => _panel.ContainsMouse(mousePos);

    public ChatPanel(int x, int y, int width, int height)
    {
        // Locked chrome: fixed bottom dock — not movable, resizable, or closeable.
        _panel = new DraggablePanel(new Rectangle(x, y, width, height),
            minH: MinH, minW: MinW, showClose: false, resizable: false, movable: false);
        // Seed the install-default tabs so the panel is usable before AccountConfig loads (welcome,
        // /fps, command help all need a non-empty `_tabs`). LoadTabs() replaces these on login if
        // the player has a persisted tab list.
        _tabs.AddRange(MakeInstallDefaultTabs());
    }

    // A user-added tab (the "+" button) — "Tab {N}" with everything enabled.
    private static ChatTab MakeDefaultTab(int slotNum) =>
        new()
        {
            Config = new AccountConfig.ChatTabConfig
            {
                Name = ClientStrings.Format(ClientStrings.ChatPanel_DefaultTabName, ("N", slotNum)),
            },
        };

    /// <summary>The out-of-the-box tab layout for a fresh account: a "General" tab carrying everything except
    /// the raw Combat feed (Rewards + both war channels stay here) with notify on, and a "Combat" tab showing
    /// the combat feed — Combat, Rewards, and both war channels. Disabled lists are built from the enum names
    /// so they stay in sync with the channel set.</summary>
    private static List<ChatTab> MakeInstallDefaultTabs()
    {
        var general = new ChatTab
        {
            Config = new AccountConfig.ChatTabConfig
            {
                Name = ClientStrings.Get(ClientStrings.ChatPanel_DefaultTab_General),
                Notify = true,
                // Everything except the raw Combat feed. War (public) + Guild War (private) both surface here.
                DisabledChannels = new List<string> { nameof(ChatChannel.Combat) },
            },
        };
        var combat = new ChatTab
        {
            Config = new AccountConfig.ChatTabConfig
            {
                Name = ClientStrings.Get(ClientStrings.ChatPanel_DefaultTab_Combat),
                Notify = false,
                // The combat feed: Combat + Rewards + both war channels (public War + private Guild War);
                // everything else (chat, system, guild chat) is hidden here.
                DisabledChannels = new List<string>
                {
                    nameof(ChatChannel.Say), nameof(ChatChannel.Yell), nameof(ChatChannel.Broadcast),
                    nameof(ChatChannel.Tell), nameof(ChatChannel.AdminChat), nameof(ChatChannel.Notice),
                    nameof(ChatChannel.JoinLeaveNotice), nameof(ChatChannel.System),
                    nameof(ChatChannel.Guild), nameof(ChatChannel.GuildOfficer),
                },
            },
        };
        return new List<ChatTab> { general, combat };
    }

    /// <summary>Replaces the in-memory tab list with whatever's persisted in AccountConfig
    /// (or keeps the install-default tabs if the player has none saved yet). Called by
    /// GameplayScreen once the account name + config are known, after the panel is constructed.</summary>
    public void LoadTabs(AccountConfig config, string accountName)
    {
        _config = config;
        _accountName = accountName;
        if (config.ChatTabs.Count == 0)
        {
            // Fresh account or migration from a pre-tabs config — persist the install defaults so
            // the file gains a `chatTabs` key. The in-memory default tabs (from the ctor) stay.
            SaveTabs();
            return;
        }
        _tabs.Clear();
        foreach (var tc in config.ChatTabs)
            _tabs.Add(new ChatTab { Config = tc });
        // Safety net: a corrupted config that deserialized to a zero-length list still leaves a
        // usable tab. Should never trigger in normal flows.
        if (_tabs.Count == 0)
            _tabs.AddRange(MakeInstallDefaultTabs());
        _activeTab = 0;
    }

    private void SaveTabs()
    {
        if (_config is null || _accountName.Length == 0) return;
        _config.ChatTabs = _tabs.Select(t => t.Config).ToList();
        _config.Save(_accountName);
    }

    /// <summary>Sets the chat-log display prefs and pushes them onto every tab's log. GameplayScreen
    /// calls this on login with the saved per-character prefs (after LoadTabs) and again whenever any
    /// of the Options checkboxes toggle.</summary>
    public void SetChatDisplayOptions(bool showTimestamps, bool use24HourClock, bool showChannelLabels)
    {
        _showTimestamps = showTimestamps;
        _use24HourClock = use24HourClock;
        _showChannelLabels = showChannelLabels;
        foreach (var tab in _tabs)
        {
            tab.Log.ShowTimestamps = _showTimestamps;
            tab.Log.Use24HourClock = _use24HourClock;
            tab.Log.ShowChannelLabels = _showChannelLabels;
        }
    }

    // Client-local diagnostic lines (FPS, command help, errors) — there is no `ChatChannel` for
    // these, so route to every tab. They are user-triggered and never spam, so they shouldn't be
    // filterable anyway.
    public void AddLine(string text, int colorIndex = 0)
    {
        foreach (var tab in _tabs) tab.Log.AddLine(text, colorIndex);
    }

    /// <summary>Focuses the chat input and prefills it with `/w <name> ` so the user can immediately
    /// type a whisper. Mirrors the `/r` reply UX. Used by the right-click "Whisper" menu item.</summary>
    public void StartWhisper(string targetName)
    {
        _inputText = $"/w {targetName} ";
        _caretIndex = _inputText.Length;
        _anchorIndex = -1;
        _viewOffset = 0;
        _historyPos = -1;
        _focused = true;
        _log.Defocus();
    }

    /// <summary>Restores the saved active speech channel (per-character pref). Unknown/invalid names
    /// fall back to Say; if the player doesn't currently qualify (admin/guild/officer) the next
    /// dropdown rebuild snaps it back to Say.</summary>
    public void SetActiveChannel(string name)
        => _activeChannel = Enum.TryParse<ActiveSpeechChannel>(name, out var ch) ? ch : ActiveSpeechChannel.Say;

    /// <summary>The active speech channel's enum name, for per-character persistence.</summary>
    public string GetActiveChannel() => _activeChannel.ToString();

    /// <summary>Repopulates the channel dropdown from the player's current access/guild/rank (rebuilt
    /// each frame so options appear/disappear live). Snaps the active channel back to Say if the
    /// player no longer qualifies, and keeps the dropdown's selection synced to it.</summary>
    private void RebuildChannelDropdown(ClientState state)
    {
        _channelDropDown.Items.Clear();
        _channelDropChannels.Clear();
        void Add(ActiveSpeechChannel ch, string key)
        {
            _channelDropDown.Items.Add(ClientStrings.Get(key));
            _channelDropChannels.Add(ch);
        }
        Add(ActiveSpeechChannel.Say, ClientStrings.ChatOptionsPanel_Channel_Say);
        Add(ActiveSpeechChannel.Yell, ClientStrings.ChatOptionsPanel_Channel_Yell);
        Add(ActiveSpeechChannel.Broadcast, ClientStrings.ChatOptionsPanel_Channel_Broadcast);
        if (state.Me.Access > AdminLevel.Player)
            Add(ActiveSpeechChannel.Admin, ClientStrings.ChatOptionsPanel_Channel_AdminChat);
        if (state.Me.GuildId > 0)
            Add(ActiveSpeechChannel.Guild, ClientStrings.ChatOptionsPanel_Channel_Guild);
        if (state.GuildInfo?.MyRank >= GuildRank.Officer)
            Add(ActiveSpeechChannel.Officer, ClientStrings.ChatOptionsPanel_Channel_GuildOfficer);

        int idx = _channelDropChannels.IndexOf(_activeChannel);
        if (idx < 0)
        {
            _activeChannel = ActiveSpeechChannel.Say;
            idx = 0;
        }
        _channelDropDown.SelectedIndex = idx;
    }

    /// <summary>Packet-aware overload — extracts the speaker name span (if any) so the log
    /// can color the name via PlayerNameColor.For and serve right-click hit-testing. Also
    /// updates the `/r` reply partner when this line is an inbound tell.
    ///
    /// Routes the line to every tab whose filter accepts the packet's `ChatChannel`. `Always`
    /// channel bypasses the filter entirely (welcome batch). Inactive tabs with `Notify` enabled
    /// pulse until the user clicks them.</summary>
    public void AddLine(ChatMsgPacket pkt)
    {
        List<TextArea.NameSpan>? names = null;
        if (!string.IsNullOrEmpty(pkt.SpeakerName) && pkt.SpeakerAccess is not null)
        {
            int idx = pkt.Msg.IndexOf(pkt.SpeakerName, StringComparison.Ordinal);
            if (idx >= 0)
            {
                names = new List<TextArea.NameSpan>
                {
                    new(idx, pkt.SpeakerName.Length, pkt.SpeakerName,
                        pkt.SpeakerAccess.Value, pkt.SpeakerShowAsPk ?? false),
                };
            }
            // Refresh `/r` partner on tell-colored messages (covers both inbound tells from a
            // peer and the loopback echo of an outbound tell — both carry the OTHER player's
            // name as SpeakerName, which is exactly what we want for the reply target).
            if (pkt.Color == GameColor.Tell)
                _lastWhisperPartner = pkt.SpeakerName;
        }

        ChatChannel ch = pkt.Channel;
        string chName = ch.ToString();
        // Resolved once per packet (frozen at arrival), so revealing labels later stamps past lines
        // and the channel name is the same across every tab this line lands in.
        string? channelLabel = ChannelLabel(ch);
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            // Always channel never filters; otherwise drop if the tab disables this channel.
            if (ch != ChatChannel.Always && tab.Config.DisabledChannels.Contains(chName))
                continue;
            tab.Log.AddLine(pkt.Msg, pkt.Color, names, colors: null, channelLabel: channelLabel);
            if (i != _activeTab && tab.Config.Notify)
                tab.NotifyPending = true;
        }
    }

    /// <summary>Localized display name for a channel's inline "[label]" prefix (shown when "Show
    /// Channel Labels" is on), reusing the ChatOptionsPanel channel strings. Returns null for
    /// `Always` — the un-filterable welcome/MOTD bucket has no meaningful channel to surface — so
    /// those lines (and client-local diagnostics, which carry no channel at all) show no label.</summary>
    private static string? ChannelLabel(ChatChannel ch) => ch switch
    {
        ChatChannel.Say => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Say),
        ChatChannel.Yell => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Yell),
        ChatChannel.Broadcast => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Broadcast),
        ChatChannel.Tell => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Tell),
        ChatChannel.AdminChat => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_AdminChat),
        ChatChannel.Notice => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Notice),
        ChatChannel.JoinLeaveNotice => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_JoinLeaveNotice),
        ChatChannel.System => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_System),
        ChatChannel.Combat => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Combat),
        ChatChannel.Rewards => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Rewards),
        ChatChannel.War => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_War),
        ChatChannel.GuildWar => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_GuildWar),
        ChatChannel.Guild => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_Guild),
        ChatChannel.GuildOfficer => ClientStrings.Get(ClientStrings.ChatOptionsPanel_Channel_GuildOfficer),
        _ => null,
    };
}

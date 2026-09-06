using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Moderation panel — every ban and every running kick or mute, with the lift beside it. Creator only.
///
/// <para>A Creator in game gets the same job the operator gets in the server window, because they are
/// doing the same job: they need to SEE who is punished before deciding, and a chat command cannot show
/// a list you can act on.</para>
///
/// <para>Rows come from <see cref="ClientState"/>, replaced wholesale on each push and rebuilt only when
/// <see cref="ClientState.ModerationVersion"/> changes. The server pushes again after every lift, so the
/// row a Creator just acted on disappears rather than sitting there beside a button that would now do
/// nothing.</para>
///
/// <para>Lifting is deliberately NOT confirmed. It is the safe direction — a mis-click frees somebody
/// rather than punishing them — and the server window does not confirm it either.</para>
/// </summary>
public sealed class ModerationPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(80, 60, 420, 300), minW: 320, minH: 220);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    private enum Tab { Bans, Penalties, Machines }
    private static readonly Tab[] AllTabs = Enum.GetValues<Tab>();
    private Tab _activeTab;

    private const int TabStripH = 24;
    private const int ButtonH = 24;
    private const int Pad = 4;
    private const int FooterH = ButtonH + Pad * 2;

    private readonly ListBox _list = new();
    private readonly Button _refreshBtn = new();
    private readonly Button _liftBtn = new();
    private InputState _input = new();

    // The rows currently rendered, rebuilt from state only when its version moves.
    private readonly List<Row> _rows = [];
    private int _builtVersion = -1;
    private Tab? _builtTab;

    private readonly record struct Row(string Login, string Detail, RowKind Kind);

    /// <summary>Which lift a row's button sends. Carried on the row rather than inferred from the active
    /// tab, so the three lists cannot drift apart from the three commands.</summary>
    private enum RowKind { Ban, Penalty, Machine }

    /// <summary>Opens the panel and asks the server for a fresh report. Both, always: an empty panel that
    /// waits to be told to refresh is a panel that looks broken the first time it is opened.</summary>
    public void Open(ClientPacketSender sender)
    {
        IsOpen = true;
        sender.SendRequestModeration();
    }

    public void Toggle(ClientPacketSender sender)
    {
        if (IsOpen) IsOpen = false;
        else Open(sender);
    }

    /// <summary>Closes without touching the server — nothing here is a session it has to know about.</summary>
    public void Close() => IsOpen = false;

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;

        // Access is per-account and can be taken away mid-session; the panel goes with it rather than
        // sitting there with stale rows and buttons the server would now refuse.
        if ((state.Me?.Access ?? AdminLevel.Player) < AdminLevel.Creator)
        {
            IsOpen = false;
            return;
        }

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        var c = _panel.ContentBounds;
        Rebuild(state);
        Layout(c);

        _refreshBtn.Label = ClientStrings.Get(ClientStrings.ModerationPanel_Refresh);
        _liftBtn.Label = ClientStrings.Get(ClientStrings.ModerationPanel_Lift);
        _liftBtn.Enabled = _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Count;

        foreach (var tab in AllTabs)
        {
            if (tab == _activeTab || !input.IsClickIn(TabRect(c, tab))) continue;
            _activeTab = tab;
            _list.SelectedIndex = -1;   // a row index means nothing once the list underneath changes
        }

        _list.Update(input, ListBounds(c));

        if (_refreshBtn.IsClicked(input)) sender.SendRequestModeration();

        if (_liftBtn.IsClicked(input) && _liftBtn.Enabled)
        {
            var row = _rows[_list.SelectedIndex];
            // One command per kind, matching the console — nothing here guesses what a row is.
            if (row.Kind == RowKind.Ban) sender.SendUnban(row.Login);
            else if (row.Kind == RowKind.Machine) sender.SendHwUnban(row.Login);
            else if (row.Detail.StartsWith(KickPrefix, StringComparison.Ordinal)) sender.SendUnkick(row.Login);
            else sender.SendUnmute(row.Login);
            // The row goes when the server's re-push lands, not now: until it does, nothing here knows
            // whether the lift was accepted.
            _list.SelectedIndex = -1;
        }
    }

    // The Kind string the server sends for a kick. Compared rather than parsed into an enum because it
    // arrives as text on the wire and only ever picks between two commands.
    private const string KickPrefix = "Kick";

    private void Rebuild(ClientState state)
    {
        if (state.ModerationVersion == _builtVersion && _activeTab == _builtTab) return;
        _builtVersion = state.ModerationVersion;
        _builtTab = _activeTab;

        _rows.Clear();
        if (_activeTab == Tab.Bans)
        {
            foreach (var b in state.Bans)
                _rows.Add(new Row(b.Login, b.Reason, RowKind.Ban));
        }
        else if (_activeTab == Tab.Machines)
        {
            foreach (var h in state.HardwareBans)
                _rows.Add(new Row(h.Login, h.Reason, RowKind.Machine));
        }
        else
        {
            foreach (var p in state.Penalties)
            {
                string detail = ClientStrings.Format(ClientStrings.ModerationPanel_PenaltyDetail,
                    ("Kind", p.Kind), ("Minutes", MinutesLeft(p.ExpiresUtc)));
                if (p.IsOnline && p.CharName.Length > 0)
                    detail += ClientStrings.Format(ClientStrings.ModerationPanel_PlayingAs, ("Name", p.CharName));
                _rows.Add(new Row(p.Login, detail, RowKind.Penalty));
            }
        }

        _list.Items.Clear();
        foreach (var r in _rows) _list.Items.Add(r.Login);
        if (_list.SelectedIndex >= _rows.Count) _list.SelectedIndex = -1;
    }

    // Rounded UP, matching the server: something still running must never read as already over.
    private static int MinutesLeft(long expiresUtc)
    {
        long left = expiresUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return (int)Math.Max(1, (left + 59) / 60);
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive = false)
    {
        if (!IsOpen) return;

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ModerationPanel_Title), isActive);
        var c = _panel.ContentBounds;
        Layout(c);

        // ── Tabs ──
        foreach (var tab in AllTabs)
        {
            var r = TabRect(c, tab);
            TabStrip.DrawCenteredTab(sb, font, r, TabLabel(tab), tab == _activeTab, r.Contains(_input.MousePosition));
        }

        var list = ListBounds(c);
        _list.RowRenderer = (batch, f, row, rect) =>
        {
            if (row < 0 || row >= _rows.Count) return;
            var e = _rows[row];
            // The login is the identity and the only thing a lift acts on, so it leads and stays legible;
            // the detail is why, and gives way first when the panel is narrow.
            float loginW = Math.Min(140, rect.Width * 0.45f);
            UiHelper.DrawLabel(batch, f, e.Login, new Vector2(rect.X + 4, rect.Y + 2), Color.White, loginW);
            UiHelper.DrawLabel(batch, f, e.Detail, new Vector2(rect.X + 8 + loginW, rect.Y + 2),
                new Color(180, 180, 200), rect.Width - loginW - 14);
        };
        _list.Draw(sb, font, list);

        // ── Empty states ──
        // "Nothing has been gathered" and "nothing is in force" are different facts and a count of zero
        // cannot tell them apart, so the panel says which.
        if (_rows.Count == 0)
        {
            string msg = !state.HasModeration
                ? ClientStrings.Get(ClientStrings.ModerationPanel_NotLoaded)
                : ClientStrings.Get(_activeTab switch
                {
                    Tab.Bans => ClientStrings.ModerationPanel_NoBans,
                    Tab.Penalties => ClientStrings.ModerationPanel_NoPenalties,
                    Tab.Machines => ClientStrings.ModerationPanel_NoMachines,
                    _ => throw new ArgumentOutOfRangeException(nameof(state)),
                });
            UiHelper.DrawLabel(sb, font, msg, new Vector2(list.X + 6, list.Y + 6),
                new Color(160, 160, 180), list.Width - 12);
        }

        // The mode replaces the swept-accounts count on the machine tab. It is the one fact a Creator
        // cannot infer from the rows: under Signal these people are being watched, not kept out, and a
        // list that looks identical either way would let somebody believe the wrong one.
        if (_activeTab == Tab.Machines && state.HardwareBanMode.Length > 0)
        {
            string mode = ClientStrings.Format(ClientStrings.ModerationPanel_MachineMode,
                ("Mode", ClientStrings.Get(state.HardwareBanMode == "Block"
                    ? ClientStrings.ModerationPanel_MachineModeBlock
                    : ClientStrings.ModerationPanel_MachineModeSignal)));
            UiHelper.DrawLabel(sb, font, mode,
                new Vector2(c.X + Pad * 2 + BtnW * 2, c.Bottom - FooterH + Pad + 5),
                new Color(150, 150, 170), c.Right - Pad - BtnW - (c.X + Pad * 2 + BtnW * 2) - 6);
        }

        _refreshBtn.Draw(sb, font, _input);
        _liftBtn.Draw(sb, font, _input);

        if (state.HasModeration && _activeTab != Tab.Machines)
        {
            string swept = ClientStrings.Format(ClientStrings.ModerationPanel_Scanned, ("Count", state.ModerationScanned));
            var footer = new Vector2(c.X + Pad * 2 + BtnW * 2, c.Bottom - FooterH + Pad + 5);
            UiHelper.DrawLabel(sb, font, swept, footer, new Color(150, 150, 170),
                c.Right - Pad - BtnW - footer.X - 6);
        }

        _panel.DrawOverlay(sb);
    }

    private const int BtnW = 78;

    private static string TabLabel(Tab tab) => ClientStrings.Get(tab switch
    {
        Tab.Bans => ClientStrings.ModerationPanel_TabBans,
        Tab.Penalties => ClientStrings.ModerationPanel_TabPenalties,
        Tab.Machines => ClientStrings.ModerationPanel_TabMachines,
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    });

    private static Rectangle TabRect(Rectangle c, Tab tab)
    {
        int w = (c.Width - Pad) / AllTabs.Length;
        return new Rectangle(c.X + (int)tab * w, c.Y, w - 2, TabStripH);
    }

    private static Rectangle ListBounds(Rectangle c) =>
        new(c.X, c.Y + TabStripH + Pad, c.Width, Math.Max(ListBox.RowPixels, c.Height - TabStripH - Pad - FooterH));

    private void Layout(Rectangle c)
    {
        int y = c.Bottom - FooterH + Pad;
        _refreshBtn.Bounds = new Rectangle(c.X + Pad, y, BtnW, ButtonH);
        _liftBtn.Bounds = new Rectangle(c.Right - Pad - BtnW, y, BtnW, ButtonH);
    }
}

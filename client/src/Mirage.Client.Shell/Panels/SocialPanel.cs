using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Social (G) panel — the account's people, in three tabs: Friends, Ignore, and Guild. All three are
/// per-ACCOUNT lists rendered from <see cref="ClientState"/> (pushed wholesale by the server), so a row's
/// identity is a login and its character columns are only meaningful while that account is online.
/// The Guild tab is itself organized into second-level sub-tabs — Main / Roster / Vault / Quests / Wars —
/// so each management surface gets its own uncluttered page (a create/browse on-ramp shows instead when
/// guildless). The Roster page is a full <see cref="Table{T}"/> (sortable/resizable/reorderable columns).
/// Rebuilds its lists only when <see cref="ClientState.SocialVersion"/> changes. Tab strip mirrors ControlsPanel's.
/// </summary>
public sealed partial class SocialPanel : IGamePanel
{
    private readonly DraggablePanel _panel;

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    // A text prompt (create name / MOTD), the label editor, or the color picker owns the keyboard/mouse
    // while up, so the GameplayScreen world-input gate must treat this panel as modal — same contract as
    // other panels.
    public bool IsCapturingInput => _prompt.IsCapturingInput || _labelEditing || _colorPicker.IsOpen || _confirm.IsCapturingInput || _reviewingApps || _reviewingWarReqs;

    // Tab strip — same metrics/colors as ControlsPanel so the two panels read as one UI.
    private const int TabStripH = 26;
    private const int TabGap = 2;
    private const int MinTabW = 80;
    private const int MinSubTabW = 48;   // the guild has 6 sub-tabs, so they pack tighter than the top strip

    private const int TabFriends = 0;
    private const int TabIgnore = 1;
    private const int TabGuild = 2;
    private const int TabCount = 3;
    private int _activeTab;

    // Guild sub-tabs (second level, shown only in-guild).
    private enum GuildSub { Main, Roster, Vault, Quests, Wars, Territories, Standings }
    private static readonly GuildSub[] AllGuildSubs = Enum.GetValues<GuildSub>();
    private GuildSub _guildSubTab;

    private const int ButtonH = 24;
    private const int HistoryBtnW = 70;   // standings "History"/"Current" toggle
    private const int SeasonNavW = 24;    // standings prev/next season arrows
    private const int Pad = 4;
    private const int RowH = 18;
    private const int HeaderH = RowH * 3; // Main page: name / labels / MOTD, so the level bar start is stable

    // The nine guild labels, in enum order — the editor grid and the roster display both iterate this.
    private static readonly GuildLabel[] AllLabels =
    {
        GuildLabel.Pvp, GuildLabel.Pve, GuildLabel.Leveling, GuildLabel.CasualSocial, GuildLabel.Hardcore,
        GuildLabel.OrganizedWars, GuildLabel.ItemFarming, GuildLabel.NewbieFocused, GuildLabel.VeteranFocused,
    };

    private readonly ListBox _list = new();               // Friends / Ignore rows
    private readonly Button _removeBtn = new();
    private readonly Button _addBtn = new();               // add a friend / ignored account BY NAME (opens the name prompt)
    // Guild member-action row (Roster page).
    private readonly Button _kickBtn = new();
    private readonly Button _promoteBtn = new();
    private readonly Button _demoteBtn = new();
    private readonly Button _transferBtn = new();
    // Guild-wide rows (Main page).
    private readonly Button _leaveBtn = new();
    private readonly Button _disbandBtn = new();
    private readonly Button _motdBtn = new();
    private readonly Button _labelsBtn = new();
    private readonly Button _colorBtn = new();
    private readonly Button _openBtn = new();       // leader: toggle open-for-membership
    private readonly Button _rankBtn = new();       // leader: toggle overhead rank word
    private readonly Button _appsBtn = new();   // officer: open the applications review
    // Guildless create view + open-guild browser.
    private readonly Button _createBtn = new();
    private readonly Button _applyBtn = new();
    private readonly ListBox _browseList = new();
    private readonly List<int> _browseIndices = new(); // browse row → guild index
    // Label editor.
    private readonly Button _labelSaveBtn = new();
    private readonly Button _labelCancelBtn = new();
    private readonly Button[] _labelBtns = InitLabelButtons();
    // Applications review overlay (officer+).
    private readonly ListBox _appList = new();
    private readonly List<string> _appLogins = new(); // app row → applicant login
    private readonly Button _approveBtn = new();
    private readonly Button _rejectBtn = new();
    private readonly Button _appsBackBtn = new();
    private bool _reviewingApps;
    // Vault page.
    private readonly Button _donateBtn = new();
    private readonly Button _donateValorBtn = new();
    private readonly Button _payTaxBtn = new();
    // Vault log view toggle: Donations (incoming member gifts) vs Spending (outgoing war-repair payouts).
    private readonly Button _vaultDonationsBtn = new();
    private readonly Button _vaultSpendingBtn = new();
    private bool _vaultShowSpending;
    // Quests page.
    private readonly Button _questAcquireBtn = new();
    private readonly Button _questAbandonBtn = new();
    // Territories page: the Challenge/Withdraw action on the selected territory.
    private readonly Button _challengeBtn = new();
    // Wars page: a selectable war list + the selected war's status area + action buttons.
    private readonly ListBox _warList = new();
    private readonly List<int> _warOpp = new();        // war row → opponent guild index
    private readonly Button _warDeclareBtn = new();
    private readonly Button _warRetractBtn = new();
    private readonly Button _warPeaceBtn = new();      // toggles Sue-for-Peace / Withdraw
    private readonly Button _warAcceptBtn = new();
    private readonly Button _warRejectBtn = new();
    // Wager row (leader-only, mutual war within the first hour).
    private readonly Button _warWagerBtn = new();       // toggles Propose-Wager / Withdraw
    private readonly Button _warWagerAcceptBtn = new();
    private readonly Button _warWagerRejectBtn = new();
    private readonly Button _warReqsBtn = new();        // leader: open the officer request queue
    // War-requests review overlay (leader) — the officer queue of declare/retract/peace asks.
    private readonly ListBox _warReqList = new();
    private readonly List<(GuildWarRequestKind Kind, int Target)> _warReqKeys = new(); // req row → (kind, target)
    private readonly Button _warReqAcceptBtn = new();
    private readonly Button _warReqDenyBtn = new();
    private readonly Button _warReqBackBtn = new();
    private bool _reviewingWarReqs;
    // Set when the panel opens already on the Guild tab (restored from config), so we fire the same
    // roster/browser refresh that switching TO the Guild tab does (the live online column has no push).
    private bool _pendingGuildRefresh;

    private readonly TextPromptDialog _prompt = new();
    private readonly ColorPickerDialog _colorPicker = new();
    private readonly ConfirmDialog _confirm = new();   // quest acquire/abandon confirmations
    private bool _labelEditing;
    private readonly List<GuildLabel> _pendingLabels = new();

    private readonly List<string> _rowLogins = new();   // Friends/Ignore row index → account login, parallel to _list.Items

    // Roster page: a data-bound Table fed the member roster directly — it declares its own columns from
    // SocialEntry fields and follows the selected member by login across server pushes.
    private readonly Table<SocialEntry> _rosterTable;
    private const int RosterColAccount = 1;   // Account = the WithRowKey column + default sort (matches the .Column() order)

    // Territories page: a read-only, data-bound Table of every territory (alphabetical) — owner, weeks held,
    // and previous-week income. No row actions, so no WithRowKey.
    private readonly Table<TerritoryView> _territoryTable;

    // Standings page: a read-only, data-bound Table of the seasonal leaderboard (every guild).
    private readonly Table<LeaderboardEntry> _standingsTable;
    // Historical-season browser: a past season's archived standings + a toggle + prev/next season paging.
    private readonly Table<SeasonStanding> _archiveTable;
    private bool _viewingHistory;
    private readonly Button _historyBtn = new();       // toggle current ↔ past seasons
    private readonly Button _prevSeasonBtn = new();     // "<" older season
    private readonly Button _nextSeasonBtn = new();     // ">" newer season

    private int _labelsGeneration = -1;
    private int _lastSocialVersion = -1;
    private int _builtTab = -1;
    private InputState _input = new();
    private ClientState? _state;   // captured each frame so the roster's Class column can read state.Classes
    private Point _lastMousePos;

    private static Button[] InitLabelButtons()
    {
        var arr = new Button[AllLabels.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = new Button();
        return arr;
    }

    public SocialPanel()
    {
        // Min height fits the Guild tab's tallest sub-page (Wars: list + a 6-line status block + three button
        // rows — peace / wager / declare) under both tab strips. The default (440x366) is a playtested size
        // that shows every tab — including the widest tables (roster/standings/territory) — without truncation.
        _panel = new DraggablePanel(new Rectangle(20, 20, 440, 365),
            minH: DraggablePanel.TitleH + TabStripH * 2 + RowH * 8 + (ButtonH + 2) * 3 + Pad * 2, minW: 300);
        // Roster columns, declared from SocialEntry fields. Headers are Func<string> so they re-localize
        // automatically; the sort key doubles as the display text unless a separate formatter is given.
        // Account/Rank are per-account (always shown); the character columns (Character/Class/Level) show a
        // hyphen while the member is offline — only the account row holds data then. Default widths fit the
        // default panel; all are user-resizable/reorderable (and the table h-scrolls when they overflow).
        _rosterTable = new Table<SocialEntry>()
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColRank), e => (int)e.Rank, e => RankName(e.Rank), 46, 40)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColAccount), e => e.Login, width: 86, minWidth: 60)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColCharacter), e => e.Online ? e.CharName : "-", width: 86, minWidth: 60)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColClass), e => e.Online ? ClassName(e.CharClass) : "-", width: 66, minWidth: 50)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColLevel),
                    e => e.Online ? e.CharLevel : -1,                        // offline sorts below any real level
                    e => e.Online ? e.CharLevel.ToString() : "-", 42, 30)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColLastSeen),
                    e => e.Online ? long.MaxValue : e.LastSeenUtc,           // online sorts as most-recent
                    e => ClientStrings.Get(e.Online ? ClientStrings.SocialPanel_Online : ClientStrings.SocialPanel_Offline), 66, 50)
            .WithRowKey(e => e.Login);   // selection follows a member across a roster push
        _rosterTable.SortBy(RosterColAccount);   // default-sort by the row-key column so its sort arrow shows immediately
        _rosterTable.AllowReorder = true;   // the roster is the one table that opts in to drag-to-reorder columns

        _territoryTable = new Table<TerritoryView>()
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColTerritory), t => t.Name, width: 104, minWidth: 70)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColOwner),
                    t => t.Owner,   // "" (unclaimed) sorts first
                    t => string.IsNullOrEmpty(t.Owner) ? ClientStrings.Get(ClientStrings.SocialPanel_Unclaimed) : t.Owner, 96, 60)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColWeeksHeld), t => t.WeeksHeld, width: 56, minWidth: 40)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColPrevIncome), t => t.PreviousWeekIncome, width: 72, minWidth: 50)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColIncomeWeek),
                    t => t.IncomeThisWeek,
                    t => t.OwnedByUs ? t.IncomeThisWeek.ToString() : "-", 76, 52)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColPending),
                    t => t.PendingTerritoryIncome,
                    t => t.OwnedByUs ? t.PendingTerritoryIncome.ToString() : "-", 70, 50)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColContesting), t => t.Contesting, width: 100, minWidth: 60)
            .WithRowKey(t => t.Index);   // selection follows a territory across pushes (for the Challenge button)
        _territoryTable.SortBy(0);   // default alphabetical by territory name (matches the server order)

        _standingsTable = new Table<LeaderboardEntry>()
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColPlacing),
                    e => (long)e.Rank, e => e.Rank > 0 ? e.Rank.ToString() : "-", 44, 32)   // seasonal standing (0 = unranked)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColGuild), e => e.Guild, width: 118, minWidth: 80)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColScore), e => e.Score, width: 80, minWidth: 55)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColKD),
                    e => (long)(e.Kills - e.Deaths),   // sort by net territory-war K/D
                    e => $"{e.Kills}/{e.Deaths}", 66, 50)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColSize), e => e.Size, width: 56, minWidth: 40);
        _standingsTable.SortBy(2, ascending: false);   // default: highest season score first (the server order); Rank col shifted Score to index 2

        _archiveTable = new Table<SeasonStanding>()
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColPlacing),
                    s => (long)s.Placing, s => s.Placing > 0 ? s.Placing.ToString() : "-", 44, 32)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColGuild), s => s.Guild, width: 120, minWidth: 80)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColScore), s => s.Score, width: 76, minWidth: 55)
            .Column(() => ClientStrings.Get(ClientStrings.SocialPanel_ColKD),
                    s => (long)(s.Kills - s.Deaths), s => $"{s.Kills}/{s.Deaths}", 66, 50);
        _archiveTable.SortBy(2, ascending: false);   // highest score first (non-scorers, placing 0, sink to the bottom)
        ColumnTables = new Dictionary<string, IColumnLayoutTable>
        {
            ["social.roster"] = _rosterTable,
            ["social.territory"] = _territoryTable,
            ["social.standings"] = _standingsTable,
            ["social.archive"] = _archiveTable,
        };
    }

    /// <summary>The active tab (0 = Friends, 1 = Ignore, 2 = Guild) — persisted so the panel reopens where the
    /// player left off.</summary>
    public int ActiveTab => _activeTab;

    /// <summary>True for the frame after the player switches tabs, so the host can persist the choice
    /// (mirrors <see cref="LayoutChanged"/>).</summary>
    public bool TabChanged { get; private set; }

    /// <summary>This panel's persisted tables, keyed by table id (the host saves/restores column layout generically).
    /// Only the roster opts in to reorder; territory/standings/archive are fixed but still persist widths + sort.</summary>
    public IReadOnlyDictionary<string, IColumnLayoutTable> ColumnTables { get; }

    /// <summary>True for the frame after the user resized/reordered/sorted any guild-table column, so the host persists it.</summary>
    public bool ColumnsChanged { get; private set; }

    /// <summary>Restore the persisted active tab (clamped) — called once on world entry.</summary>
    public void SetActiveTab(int tab)
    {
        _activeTab = Math.Clamp(tab, 0, TabCount - 1);
        Invalidate();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        // Reopen on the last-used tab (restored from config on world entry, kept across close/open here).
        if (IsOpen)
        {
            Invalidate();
            _pendingGuildRefresh = _activeTab == TabGuild;
        }
        else
        {
            _prompt.Close();
            _labelEditing = false;
            _colorPicker.Close();
            _confirm.Close();
            _reviewingApps = false;
            _reviewingWarReqs = false;
        }
    }

    /// <summary>Force a rebuild on the next Draw (tab switch / reopen / fresh server push).</summary>
    private void Invalidate()
    {
        _lastSocialVersion = -1;
        _builtTab = -1;
    }

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);
}

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Root view-model for the editor shell: owns every per-record child editor, the section nav list,
/// and the online/offline lifecycle (connect, disconnect, reconnect, and the unsaved-changes prompts
/// that guard each).
/// <para>The editor runs offline against files on disk or online against a live server. Switching
/// between the two re-points every child editor in one sweep (<c>RefreshEditors</c>), and any
/// transition that could discard unsaved work routes through the push-changes dialog first.</para>
/// <para>Dialogs are opened through the <c>Show…Async</c> delegates the View assigns, so this
/// view-model never references a View type.</para>
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly EditorDataService _data;
    private readonly EditorConnection _conn;
    private readonly EditorBitmapCache _bitmaps;
    /// <summary>Who holds what, as the server last said. Shared by every child editor.</summary>
    public EditorLockState Locks { get; } = new();
    // Parallel to AllSectionNames: the online/offline reload pair for each child editor, so a mode
    // switch can drive them all without naming each one again.
    private readonly (Action loadOnline, Action loadOffline)[] _editorLoaders;

    // Child editor VMs
    public MapEditorViewModel MapEditor { get; }
    public MapGroupEditorViewModel MapGroupEditor { get; }
    public ItemEditorViewModel ItemEditor { get; }
    public NpcEditorViewModel NpcEditor { get; }
    public ShopEditorViewModel ShopEditor { get; }
    public ConversationEditorViewModel ConversationEditor { get; }
    /// <summary>Creator-only, and the only section that is online-only — accounts are the server's.</summary>
    public AccountEditorViewModel AccountEditor { get; }

    // One section per family a MODULE declared, made the first time that section is opened rather than at
    // startup: which families exist is not known until a world is opened or a server is connected to.
    private readonly Dictionary<string, SchemaRecordEditorViewModel> _moduleEditors = new(StringComparer.Ordinal);

    private IEnumerable<SchemaRecordEditorViewModel> ModuleEditors => _moduleEditors.Values;

    /// <summary>The section for a family a module declared, made if this is the first time it is opened.
    /// Null for a Core family, which has its own screen, and for an id no family claims.</summary>
    private SchemaRecordEditorViewModel? ModuleEditorFor(string? section)
    {
        if (section is null || CoreRecordFamilies.Find(section) is not null) return null;
        if (_moduleEditors.TryGetValue(section, out var existing)) return existing;
        if (WorldFamilies.Find(section) is not { } family) return null;

        var editor = new SchemaRecordEditorViewModel(_data, _conn, family) { Locks = Locks };
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty" && _sectionMap.TryGetValue(section, out var row))
                row.HasDirty = editor.HasAnyDirty;
        };

        if (IsOnline)
        {
            editor.LoadOnline();
            _ = editor.EagerLoadAllAsync(CancellationToken.None);
        }
        else
        {
            editor.LoadOffline();
        }
        editor.RefreshLockState();

        _moduleEditors[section] = editor;
        return editor;
    }

    /// <summary>The child editor view-model the content pane is bound to; null for an unknown section.</summary>
    [ObservableProperty] private object? _currentEditor;
    [ObservableProperty] private SectionViewModel? _selectedSection;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatus))]
    [NotifyPropertyChangedFor(nameof(AutoSaveMenuItemLabel))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyWorld))]
    [NotifyPropertyChangedFor(nameof(HasWorld))]
    [NotifyPropertyChangedFor(nameof(WorldLabel))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromDiskCommand))]
    private bool _isOnline;

    /// <summary>The one word inside the toolbar badge. Derived rather than assigned at each transition:
    /// it is a function of <see cref="IsOnline"/> and of the current language, and a stored copy went
    /// stale on a language switch because nothing re-resolved it.</summary>
    public string ConnectionStatus => EditorStrings.Get(IsOnline
        ? EditorStrings.MainWindow_StatusOnline
        : EditorStrings.MainWindow_StatusOffline);

    /// <summary>Which server, shown beside the badge and never inside it; empty when offline.</summary>
    [ObservableProperty] private string _connectionEndpoint = "";

    // ── Section rail ──────────────────────────────────────────────────────────
    // Expanded is nine labelled rows; collapsed is nine icons. The width is stated here rather than in
    // the view so both states come from one place — 64 is the icon, the unsaved-work dot, and the row
    // padding, and nothing more.
    private const double RailExpandedWidth = 188;
    private const double RailCollapsedWidth = 64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailWidth))]
    [NotifyPropertyChangedFor(nameof(RailToggleGlyph))]
    [NotifyPropertyChangedFor(nameof(RailToggleTooltip))]
    private bool _isRailCollapsed = AppSettings.Current.RailCollapsed;

    public double RailWidth => IsRailCollapsed ? RailCollapsedWidth : RailExpandedWidth;

    /// <summary>Points the way the rail will move, not the way it is now.</summary>
    public string RailToggleGlyph => IsRailCollapsed ? "»" : "«";

    public string RailToggleTooltip => EditorStrings.Get(IsRailCollapsed
        ? EditorStrings.MainWindow_RailExpand
        : EditorStrings.MainWindow_RailCollapse);

    [RelayCommand]
    private void ToggleRail()
    {
        IsRailCollapsed = !IsRailCollapsed;
        foreach (var s in _sectionMap.Values) s.IsLabelVisible = !IsRailCollapsed;
        // Written through NOW rather than deferred to the close handler like the panel widths. Those are
        // the tail end of a drag and would write on every pixel; this is one deliberate click, and losing
        // it to a session that ended badly is the kind of small betrayal nobody reports.
        AppSettings.Current.RailCollapsed = IsRailCollapsed;
        AppSettings.Current.Save();
    }
    /// <summary>Progress line shown while the post-connect eager load runs; empty when idle.</summary>
    [ObservableProperty] private string _loadingStatus = "";

    /// <summary>True while first-run startup is still reading data off disk. Drives the blocking overlay
    /// in MainWindow.
    ///
    /// <para>It starts TRUE rather than being set when loading begins. App.axaml.cs launches
    /// <see cref="InitializeAsync"/> fire-and-forget (<c>_ = vm.InitializeAsync()</c>), so the window is
    /// on screen and taking clicks before the first line of it runs — and a click that lands in that gap
    /// is silently swallowed, which reads as "the app ignored me" rather than "it was not ready". Any
    /// default of false leaves exactly that window open.</para></summary>
    [ObservableProperty] private bool _isLoading = true;
    private CancellationTokenSource? _eagerLoadCts;

    /// <summary>Accounts are authored here but are not world content: they belong to the installation
    /// rather than to the world, and never travel with one. So the rail lists them, and
    /// <see cref="CoreRecordFamilies.World"/> does not.</summary>
    public const string AccountsSection = "Accounts";

    // Stable section ids — used for switching and lookup. Display labels are localized separately
    // via SectionLabelKey, so these strings never reach the UI.
    //
    // Read from WorldFamilies rather than from a compile-time table: the families are the SERVER's, so
    // connecting to a game that declares its own puts them in the rail without this build knowing them.
    private string[] AllSectionNames => SectionNamesFor(WorldFamilies.All);

    /// <summary>The rail's ids for a given set of families: every family in its declared order, then
    /// Accounts.
    ///
    /// <para>A function of the families rather than of the process, so what the rail lists for a game
    /// that declares its own can be asked without a running server.</para></summary>
    internal static string[] SectionNamesFor(IReadOnlyList<RecordFamily> families)
        => [.. families.Select(f => f.Id), AccountsSection];
    private readonly Dictionary<string, SectionViewModel> _sectionMap;
    /// <summary>The nav sections currently visible, narrowed by the connected account's access level.</summary>
    public ObservableCollection<SectionViewModel> Sections { get; }

    // Delegates assigned by the View to open dialogs without a VM→View reference
    public Func<ConnectDialogViewModel, Task>? ShowConnectDialogAsync { get; set; }
    public Func<PushChangesDialogViewModel, Task>? ShowPushChangesDialogAsync { get; set; }
    public Func<DisconnectDialogViewModel, Task>? ShowDisconnectDialogAsync { get; set; }
    public Func<string, Task>? ShowAlertAsync { get; set; }

    public MainWindowViewModel(EditorDataService data, EditorConnection conn, EditorBitmapCache bitmaps)
    {
        _data = data;
        _conn = conn;
        _bitmaps = bitmaps;

        MapEditor = new MapEditorViewModel(data, conn);
        MapGroupEditor = new MapGroupEditorViewModel(data, conn);

        // The map editor shows what a blank field would inherit, and the group rows are the only place
        // a full group record exists once the session is online.
        MapEditor.ResolveMapGroup = id =>
            MapGroupEditor.MapGroups.FirstOrDefault(g => g.Index == id)?.ToRecord();
        ItemEditor = new ItemEditorViewModel(data, conn);
        NpcEditor = new NpcEditorViewModel(data, conn);
        ShopEditor = new ShopEditorViewModel(data, conn);
        ConversationEditor = new ConversationEditorViewModel(data, conn);
        AccountEditor = new AccountEditorViewModel(data, conn);

        // Every editor's "what refers to this?" panel, wired once all of them exist — the scans read across
        // collections, so none of them can be hooked up before the last editor is constructed.
        WireReferenceScans();

        _editorLoaders =
        [
            (MapEditor.LoadOnline,   MapEditor.LoadOffline),
            (MapGroupEditor.LoadOnline, MapGroupEditor.LoadOffline),
            (ItemEditor.LoadOnline,  ItemEditor.LoadOffline),
            (NpcEditor.LoadOnline,   NpcEditor.LoadOffline),
            (ShopEditor.LoadOnline,  ShopEditor.LoadOffline),
            (ConversationEditor.LoadOnline, ConversationEditor.LoadOffline),
            (AccountEditor.LoadOnline, AccountEditor.LoadOffline),
        ];
        // Hand each section its label KEY, not the resolved text — the nav list outlives a language
        // switch, so a resolved string would freeze it in the startup language.
        _sectionMap = [];
        Sections = new ObservableCollection<SectionViewModel>(AllSectionNames.Select(SectionFor));
        WorldFamilies.Changed += OnWorldFamiliesChanged;
        // The rail reopens in the shape it was left in, so a restored collapse has to reach the rows.
        if (IsRailCollapsed) foreach (var s in _sectionMap.Values) s.IsLabelVisible = false;
        EditorStrings.LanguageChanged += OnLanguageChanged;
        // Mirror each editor's aggregate dirty flag onto its nav section, which shows the unsaved marker.
        MapEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapEditorViewModel.HasAnyDirtyMap))
                _sectionMap["Maps"].HasDirty = MapEditor.HasAnyDirtyMap;
        };
        MapGroupEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty") _sectionMap["MapGroups"].HasDirty = MapGroupEditor.HasAnyDirty;
        };
        ItemEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty") _sectionMap["Items"].HasDirty = ItemEditor.HasAnyDirty;
        };
        NpcEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty") _sectionMap["NPCs"].HasDirty = NpcEditor.HasAnyDirty;
        };
        ShopEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty") _sectionMap["Shops"].HasDirty = ShopEditor.HasAnyDirty;
        };
        ConversationEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HasAnyDirty") _sectionMap["Conversations"].HasDirty = ConversationEditor.HasAnyDirty;
        };
        _conn.OnDisconnected += OnConnectionLost;
        _conn.OnLivePacket += OnLivePacket;

        // Every record editor draws from the one table, and re-reads its rows whenever it moves.
        foreach (var ed in RecordEditors) ed.Locks = Locks;
        MapEditor.Locks = Locks;
        Locks.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            foreach (var ed in RecordEditors) ed.RefreshLockState();
            MapEditor.RefreshLockState();
        });
    }

    // A server-pushed live broadcast arrived (not a pending request's response). Fires on the connection's
    // receive-loop thread, so marshal to the UI thread before touching view-model state. Today only an NPC-size
    // change matters: the map editor re-reads the size cache, redraws, and re-prompts any loaded map that pins
    // the resized NPC.
    private void OnLivePacket(IPacket packet)
    {
        if (packet is EditorLocksPacket locks) { Locks.Apply(locks); return; }

        // A record somebody else saved. Applying it is what keeps an online session from ever holding the
        // older copy, which is why there is no staleness check anywhere: there is no staleness.
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            switch (packet)
            {
                case UpdateItemPacket p: ItemEditor.ApplyLiveRecord(p.ItemNum, p); break;
                case UpdateNpcPacket p:
                    NpcEditor.ApplyLiveRecord(p.NpcNum, p);
                    MapEditor.OnNpcLiveUpdated(p.NpcNum, p.Size);
                    break;
                case UpdateShopPacket p: ShopEditor.ApplyLiveRecord(p.ShopNum, p); break;
                case UpdateConversationPacket p: ConversationEditor.ApplyLiveRecord(p.ConvNum, p); break;
                case UpdateMapGroupPacket p: MapGroupEditor.ApplyLiveRecord(p.GroupNum, p); break;
                case SendMapPacket p: MapEditor.OnMapLivePushed(p); break;
            }
        });
    }

    /// <summary>The eight editors that keep a list of records the lock table can name. Maps are held apart:
    /// the map editor is not one of these and carries its own lock plumbing.</summary>
    private IEnumerable<dynamic> RecordEditors =>
        [ItemEditor, NpcEditor, ShopEditor, ConversationEditor, MapGroupEditor, .. ModuleEditors];

    /// <summary>First-run startup: read the offline data set, seed and load the editable asset folder,
    /// then open the map editor. Always starts offline — connecting is an explicit user action.</summary>
    public async Task InitializeAsync()
    {
        // finally, not a trailing assignment: if any step throws, the overlay must still come down or
        // the editor is bricked behind a panel with no way to reach the error.
        try
        {
            // Graphics are the editor's own and always load; a world is not, and is opened rather than
            // assumed. Assets first, so the window is drawable whether or not anything is opened into it.
            LoadingStatus = EditorStrings.Get(EditorStrings.MainWindow_LoadingAssets);
            await Task.Run(EditorPaths.SeedAssets);
            _bitmaps.Load(EditorPaths.Assets);
            ApplyBitmaps();

            // No world by default. Opening one is a decision, and starting on whatever was open last would
            // attach the editor to a world somebody had finished with.
            var settings = AppSettings.Current;
            if (settings.ReopenLastWorld && !string.IsNullOrWhiteSpace(settings.LastWorldPath))
                await OpenWorldAsync(settings.LastWorldPath!, remember: false);
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    // Pushes the currently-loaded bitmaps into the editors that render with them.
    private void ApplyBitmaps()
    {
        MapEditor.TilesetNames = _bitmaps.TilesetNames;
        MapEditor.Tilesets = _bitmaps.Tilesets;
        ItemEditor.SetItemSheets(_bitmaps.Items, _bitmaps.ItemNames);
        NpcEditor.SetSpriteSheets(_bitmaps.Sprites, _bitmaps.Sprites64, _bitmaps.Sprites96, _bitmaps.SpriteNames);
    }

    /// <summary>Re-scans every asset folder so sheets added, renamed or removed on disk appear without a
    /// restart. Available while connected: the graphics are this machine's, and a server has no say in
    /// them — only the world is the server's to hand out.</summary>
    [RelayCommand]
    private void ReloadAssets()
    {
        ReloadAssetsFromDisk();
        MapEditor.StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_AssetsReloaded);
    }

    /// <summary>Re-reads every sheet from disk and pushes it into the editors that draw with it.
    ///
    /// <para>Without the status line, because the asset manager reports its own outcomes: it says what it
    /// just did to which file, which is more use than "Assets reloaded" appearing behind an open dialog.</para></summary>
    internal void ReloadAssetsFromDisk()
    {
        _bitmaps.Reload(EditorPaths.Assets);
        ApplyBitmaps();
    }

    partial void OnSelectedSectionChanged(SectionViewModel? value) => SwitchToSection(value?.Name);

    /// <summary>Follow a link to a map: show the Maps section with that map open. Selecting it goes through the
    /// map editor's ordinary selection path, so it joins the back/forward trail — Back returns to the map that
    /// was open before, Forward comes here again. A number naming no row changes nothing at all, rather than
    /// switching the user to a section showing the wrong map.</summary>
    private void OpenMap(int mapNum)
    {
        if (!MapEditor.SelectByIndex(mapNum)) return;
        SelectedSection = _sectionMap["Maps"];
    }

    private void SwitchToSection(string? section)
    {
        // Every reference panel reads records owned by OTHER editors, so arriving at a section is the moment
        // to re-read it: what points at the selected record may have changed while you were elsewhere.
        RefreshReferences();

        CurrentEditor = section switch
        {
            "Maps" => MapEditor,
            "MapGroups" => MapGroupEditor,
            "Items" => ItemEditor,
            "NPCs" => NpcEditor,
            "Shops" => ShopEditor,
            "Conversations" => ConversationEditor,
            "Accounts" => AccountEditor,
            // A family a MODULE declared: the same list-and-detail screen, built from the fields the
            // family itself carries rather than from a view this build shipped.
            _ => ModuleEditorFor(section),
        };
    }

    // The nav labels are the one piece of shell chrome the window's own ApplyStrings cannot reach:
    // they live on the section rows, not on named controls.
    private void OnLanguageChanged()
    {
        foreach (var s in Sections) s.NotifyDisplayNameChanged();
        NotifyAutoSaveMenuChanged();
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(RailToggleTooltip));
        // Both name an unnamed world, which is a localized word. The rows are rebuilt from the setting
        // each time this is raised, so re-raising it is what re-words them.
        OnPropertyChanged(nameof(WorldLabel));
        OnPropertyChanged(nameof(RecentWorlds));
    }

    /// <summary>The localized nav label for a section id. Only the displayed label is localized; every
    /// lookup and comparison elsewhere keeps using the id.
    ///
    /// <para>A world family carries its own key on its row, so a family a game registers is labeled
    /// from that game's catalog without the editor knowing the key.</para></summary>
    internal static string SectionLabelKey(string id)
    {
        if (id == AccountsSection) return EditorStrings.MainWindow_Section_Accounts;
        return WorldFamilies.Find(id)?.LabelKey ?? "";
    }

    /// <summary>What a section is CALLED, resolved. A family that declares no caption key, or one whose
    /// key this build's language files have never heard of, is called by its ID — a name an author
    /// recognises from their own game, and the only one available.</summary>
    internal static string SectionLabel(string id) => EditorStrings.GameLabel(SectionLabelKey(id), id);

    /// <summary>The rail row for a section, made once and kept.
    ///
    /// <para>A module's family names a label key this build has never heard of, so the row falls back to
    /// the family id. <c>EditorStrings.Get</c> would throw on it, which is right for the editor's own
    /// text and wrong for a key that arrived over the wire.</para></summary>
    private SectionViewModel SectionFor(string id)
    {
        if (_sectionMap.TryGetValue(id, out var existing)) return existing;

        var row = new SectionViewModel(id, SectionLabelKey(id), fallbackLabel: id)
        {
            IsLabelVisible = !IsRailCollapsed,
        };
        _sectionMap[id] = row;
        return row;
    }

    // A schema arriving replaces the rail's contents; the rows for families that survive are the same
    // objects, so a section that was selected stays selected and its dirty marker is not reset.
    private void OnWorldFamiliesChanged()
    {
        var wanted = AllSectionNames;
        Sections.Clear();
        foreach (string id in wanted) Sections.Add(SectionFor(id));

        // A world opened after this one may declare different families, and a section left behind would
        // hold records nothing in the rail points at any more.
        foreach (string id in _moduleEditors.Keys.Where(id => WorldFamilies.Find(id) is null).ToList())
            _moduleEditors.Remove(id);

        if (SelectedSection is null || !Sections.Contains(SelectedSection)) SelectedSection = Sections[0];
    }

    // ── Online connect / disconnect ───────────────────────────────────────────

    // Going online replaces every editor's data, so unsaved offline work is offered to the server first.
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (ShowConnectDialogAsync is null) return;

        var dirty = GetAllDirty().ToList();
        if (dirty.Count > 0 && ShowPushChangesDialogAsync is not null)
        {
            bool proceed = false;
            var dirtyDlgVm = new PushChangesDialogViewModel(dirty, _conn, _data, PushChangesReason.Connecting);
            dirtyDlgVm.ProceedConfirmed += () => proceed = true;
            dirtyDlgVm.Canceled += () => { };
            await ShowPushChangesDialogAsync(dirtyDlgVm);
            if (!proceed) return;
        }

        var dlgVm = new ConnectDialogViewModel(_conn);
        dlgVm.ConnectSuccess += OnConnectSuccess;
        await ShowConnectDialogAsync(dlgVm);
    }

    private void OnConnectSuccess(EditorDataPacket pkt, AdminLevel access)
    {
        _data.LoadOnline(pkt, _conn.Hello?.Records);
        Locks.MyLogin = _conn.Login;
        Locks.MySession = _conn.SessionId;
        RefreshEditors(online: true);
        IsOnline = true;
        ConnectionEndpoint = _conn.Endpoint;
        ApplySectionRestrictions(access);
        SelectedSection = Sections[0];
        _ = StartEagerLoadAsync();
    }

    // Used after a reconnect: pushes dirty offline changes to the server before
    // reloading editor state so unsaved work isn't silently wiped.
    private async Task OnReconnectSuccessAsync(EditorDataPacket pkt, AdminLevel access)
    {
        var dirty = GetAllDirty().ToList();
        _data.LoadOnline(pkt, _conn.Hello?.Records);
        IsOnline = true;
        ConnectionEndpoint = _conn.Endpoint;
        ApplySectionRestrictions(access);
        if (dirty.Count > 0 && ShowPushChangesDialogAsync is not null)
        {
            var pushVm = new PushChangesDialogViewModel(dirty, _conn, _data, PushChangesReason.Reconnecting);
            await ShowPushChangesDialogAsync(pushVm);
        }
        RefreshEditors(online: true);
        SelectedSection = Sections[0];
        _ = StartEagerLoadAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var dirty = GetAllDirty().ToList();
        if (dirty.Count > 0 && ShowPushChangesDialogAsync is not null)
        {
            bool disconnectConfirmed = false;
            var dlgVm = new PushChangesDialogViewModel(dirty, _conn, _data, PushChangesReason.Disconnecting);
            dlgVm.DisconnectConfirmed += () => disconnectConfirmed = true;
            await ShowPushChangesDialogAsync(dlgVm);
            if (!disconnectConfirmed) return;
        }
        await DoDisconnect();
    }

    // Pull the whole data set down in the background right after connecting, so browsing a section
    // later is instant. Cancellable: a disconnect mid-load abandons it rather than racing the teardown.
    private async Task StartEagerLoadAsync()
    {
        _eagerLoadCts?.Cancel();
        _eagerLoadCts = new CancellationTokenSource();
        var ct = _eagerLoadCts.Token;
        try
        {
            // Maps first — per-map with progress (too large for a single payload)
            LoadingStatus = EditorStrings.Format(EditorStrings.MainWindow_LoadingSection,
                ("Section", EditorStrings.Get(EditorStrings.MainWindow_Section_Maps)));
            await MapEditor.EagerLoadAllAsync(
                (done, total) => LoadingStatus = EditorStrings.Format(EditorStrings.MainWindow_LoadingSectionProgress,
                    ("Section", EditorStrings.Get(EditorStrings.MainWindow_Section_Maps)), ("Done", done), ("Total", total)), ct);

            // Remaining types as single bulk packets, current section first
            (string Label, Func<CancellationToken, Task> Loader)[] steps =
            [
                ("MapGroups", c => MapGroupEditor.EagerLoadAllAsync(c)),
                ("Items",   c => ItemEditor.EagerLoadAllAsync(c)),
                ("NPCs",    c => NpcEditor.EagerLoadAllAsync(c)),
                ("Shops",   c => ShopEditor.EagerLoadAllAsync(c)),
                ("Conversations", c => ConversationEditor.EagerLoadAllAsync(c)),
                // Every family a module declared, opened or not — see FetchModuleFamilyAsync.
                .. WorldFamilies.All
                    .Where(f => CoreRecordFamilies.Find(f.Id) is null)
                    .Select(f => (f.Id, (Func<CancellationToken, Task>)(c => FetchModuleFamilyAsync(f, c)))),
            ];
            var currentSection = SelectedSection?.Name ?? "";
            foreach (var (label, loader) in steps.OrderBy(s => s.Label == currentSection ? 0 : 1))
            {
                if (ct.IsCancellationRequested) break;
                LoadingStatus = EditorStrings.Format(EditorStrings.MainWindow_LoadingSection,
                    ("Section", SectionLabel(label)));
                await loader(ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            LoadingStatus = "";
            // Online, every row starts as a name with an empty record, so no reference exists until the loads
            // above have run. Anything already showing a reference panel is reading zeroes until this fires.
            // Also runs on cancel, so a half-finished load still shows what it did get.
            RefreshReferences();
        }
    }

    /// <summary>Pulls one of a game's families down after connecting.
    ///
    /// <para>Fetched whether or not its section has been opened: a <c>RecordRef</c> field on one family's
    /// form lists another family's records BY NAME, and the form has no way to reach a section that does
    /// not exist yet. The records land on the data service either way; an open section fills its rows from
    /// the same fetch.</para></summary>
    private async Task FetchModuleFamilyAsync(RecordFamily family, CancellationToken ct)
    {
        if (_moduleEditors.TryGetValue(family.Id, out var editor))
        {
            await editor.EagerLoadAllAsync(ct);
            return;
        }

        var bulk = await _conn.RequestAllRecordsAsync(family.Id, ct);
        if (bulk is not null)
            _data.AdoptOnlineModuleRecords(family, bulk.Records.Select(r => (r.Num, r.Fields)));
    }

    // The single teardown path — every disconnect route ends here so nothing is left half-online.
    private async Task DoDisconnect()
    {
        // BEFORE the socket is touched. Tearing it down can raise the connection-lost event, and the first
        // thing that handler reads is this flag — leaving it true until afterwards let an ordinary
        // Disconnect open the lost-connection dialog on its own way out.
        // Every lock this session knew about belongs to a conversation that is over.
        Locks.Clear();
        IsOnline = false;
        _eagerLoadCts?.Cancel();
        _eagerLoadCts = null;
        LoadingStatus = "";
        await _conn.DisconnectAsync();
        _data.ClearOnline();
        // The session's world went with the connection, and so does everything else: disconnecting closes
        // the online world the way Close World closes an offline one, and leaves no world open. Falling back
        // to a folder opened before connecting put a different world on screen under the same window with
        // nothing announcing the swap, which is how somebody edits the wrong one.
        EditorPaths.OpenWorld("");
        _data.ClearOffline();
        RefreshEditors(online: false);
        RestoreAllSections();
        SelectedSection = null;
        CurrentEditor = null;
        ConnectionEndpoint = "";
        NotifyWorldChanged();
    }

    // Three tiers now: a Creator gets everything, Developer gets the content sections, and a Mapper gets
    // the map and map-group editors only. Offline editing is unrestricted — this narrowing applies to a
    // live server session, and the SERVER re-checks every one of these; see EditorPacketHandler's
    // RequireAccess. Hiding a section here is a courtesy, not the gate.
    private void ApplySectionRestrictions(AdminLevel access)
    {
        var visibleNames = access switch
        {
            >= AdminLevel.Creator => AllSectionNames,
            >= AdminLevel.Developer => [.. AllSectionNames.Where(n => n != AccountsSection)],
            _ => ["Maps", "MapGroups"],
        };
        Sections.Clear();
        foreach (var name in visibleNames) Sections.Add(SectionFor(name));
        if (SelectedSection is null || !Sections.Contains(SelectedSection))
            SelectedSection = Sections[0];
    }

    private void RestoreAllSections()
    {
        Sections.Clear();
        foreach (var name in AllSectionNames) Sections.Add(SectionFor(name));
    }

    private void RefreshEditors(bool online)
    {
        foreach (var (lo, lof) in _editorLoaders)
        {
            if (online) lo();
            else lof();
        }

        foreach (var ed in ModuleEditors)
        {
            if (online) ed.LoadOnline();
            else ed.LoadOffline();
        }

        // Every loader throws its rows away and builds new ones, and the padlock lives on the row. The table
        // itself has not moved, so re-reading it is what puts the indicators back — otherwise a session that
        // connects while somebody else is mid-edit shows nothing held until the next time the table changes.
        foreach (var ed in RecordEditors) ed.RefreshLockState();
        MapEditor.RefreshLockState();
    }

    // Every dirty row across every child editor, as one flat sequence for the push-changes dialog.
    private IEnumerable<object> GetAllDirty()
    {
        foreach (var editor in ModuleEditors)
            foreach (var vm in editor.GetDirty()) yield return vm;
        foreach (var vm in ItemEditor.GetDirty()) yield return vm;
        foreach (var vm in NpcEditor.GetDirty()) yield return vm;
        foreach (var vm in ShopEditor.GetDirty()) yield return vm;
        foreach (var vm in MapEditor.GetDirty()) yield return vm;
        foreach (var vm in MapGroupEditor.GetDirty()) yield return vm;
        foreach (var vm in ConversationEditor.GetDirty()) yield return vm;
    }

    /// <summary>Whether any child editor holds unsaved edits.</summary>
    public bool HasAnyDirty => GetAllDirty().Any();

    /// <summary>Window-close guard: prompts for unsaved work and reports whether the close may proceed.
    /// Returns true when there is nothing to save or the author confirmed.</summary>
    public async Task<bool> HandleDirtyForCloseAsync()
    {
        var dirty = GetAllDirty().ToList();
        if (dirty.Count == 0) return true;
        if (ShowPushChangesDialogAsync is null) return true;
        bool proceed = false;
        var dlgVm = new PushChangesDialogViewModel(dirty, _conn, _data, PushChangesReason.Closing);
        dlgVm.ProceedConfirmed += () => proceed = true;
        dlgVm.Canceled += () => { };
        await ShowPushChangesDialogAsync(dlgVm);
        return proceed;
    }

    /// <summary>Tear the session down without prompting — for shutdown paths that already handled
    /// unsaved work. No-op when already offline.</summary>
    public async Task ForceDisconnectAsync()
    {
        if (!IsOnline) return;
        await DoDisconnect();
    }

    // The server dropped us. Raised on the connection's receive-loop thread, so everything below runs
    // marshaled to the UI thread. With unsaved work the author gets the reconnect dialog (which can push
    // it to the recovered session); otherwise the session just closes with a notice.
    /// <summary>True while a lost connection is being handled. One loss can raise the event more than once,
    /// and each arrival would otherwise open its own modal over the main window — leaving a second dialog
    /// nothing can reach to close, which reads as the whole editor having frozen.</summary>
    private bool _handlingConnectionLoss;

    private void OnConnectionLost()
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (!IsOnline || _handlingConnectionLoss) return;
            _handlingConnectionLoss = true;
            try { await HandleConnectionLossAsync(); }
            finally { _handlingConnectionLoss = false; }
        });
    }

    private async Task HandleConnectionLossAsync()
    {
        var dirty = GetAllDirty().ToList();
        if (dirty.Count == 0)
        {
            await DoDisconnect();
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Get(EditorStrings.MainWindow_DisconnectedAlert));
            return;
        }
        if (ShowDisconnectDialogAsync is null)
        {
            await DoDisconnect();
            return;
        }
        var dlgVm = new DisconnectDialogViewModel(_conn);
        bool reconnected = false;
        EditorDataPacket? reconnectData = null;
        AdminLevel reconnectAccess = default;
        dlgVm.ReconnectSuccess += (pkt, lvl) => { reconnected = true; reconnectData = pkt; reconnectAccess = lvl; };
        await ShowDisconnectDialogAsync(dlgVm);
        if (reconnected && reconnectData is not null)
            await OnReconnectSuccessAsync(reconnectData, reconnectAccess);
        else
            // Reaching here without a reconnect means the dialog was left by the offline route — either the
            // button or the window's own close, which resolves to the same decision. Dropping offline keeps
            // the unsaved work in memory; the alternative is a window with no way out.
            await DoDisconnect();
    }
}

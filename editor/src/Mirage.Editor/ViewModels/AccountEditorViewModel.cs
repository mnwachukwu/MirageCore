using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The account browser — CREATOR only, and the only editor section that edits a person rather than a
/// piece of content.
///
/// <para>Unlike every other section, this one is ONLINE ONLY and never caches: accounts live on the
/// server, they change while nobody is looking, and a stale page would show an operator a level or a
/// location that has since moved. Every page and every record is fetched on demand, and a save waits for
/// the server to read the record back so the form shows what actually landed — the server clamps a level
/// and refuses an unknown map, and typing something it rejects should not leave the screen claiming
/// otherwise.</para>
///
/// <para> <b>No password, ever.</b> The wire has no field for one, so there is nothing here to show or
/// to send back.  <b>No moderation.</b> Kicks, mutes and bans are an operator's job, done from the
/// server window. Guild membership is shown but not editable — the guild's roster cache is kept in step
/// by GuildSystem, and writing the account's copy directly would desync it.</para>
/// </summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly EditorDataService _data;
    private readonly EditorConnection _conn;
    private CancellationTokenSource? _inFlight;

    public AccountEditorViewModel(EditorDataService data, EditorConnection conn)
    {
        _data = data;
        _conn = conn;
        BuildAccessFilters();
        _data.EntriesInvalidated += () => { foreach (var c in Chars) c.NotifyItemEntriesChanged(); };
        // PageText and GuildText resolve their wording on read, so a language switch has to re-raise them
        // or the pager and the guild line stay in whatever language the section was first opened in.
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // The filter options hold RESOLVED captions, so they are rebuilt rather than re-raised; the
        // current selection is carried across by level.
        BuildAccessFilters();
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(GuildText));
        OnPropertyChanged(nameof(WornLabel));
        OnPropertyChanged(nameof(VaultHeader));
        OnPropertyChanged(nameof(VaultEmpty));
        // Row captions resolve on read too, and the rows are not LocalizedUserControls — nothing else
        // would re-raise them.
        foreach (var c in Chars) c.NotifyLanguageChanged();
        // A status line is a sentence about something that already happened; re-resolving it would be
        // asserting it happened again, so it is cleared instead.
        StatusMessage = "";
    }

    public ObservableCollection<EditorAccountRow> Accounts { get; } = [];
    public ObservableCollection<AccountCharRowViewModel> Chars { get; } = [];

    /// <summary>The account vault. Account-shared rather than per character, so it sits with the access and
    /// guild lines rather than on a character card — every character is looking at this one.</summary>
    public ObservableCollection<EditorInvSlot> Bank { get; } = [];

    public bool HasNoBank => Bank.Count == 0;

    public NamedEntry[] ItemEntries => _data.LiveItemEntries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGiveToBank))]
    private NamedEntry? _bankItem;

    [ObservableProperty] private int _bankQuantity = 1;

    public bool CanGiveToBank => BankItem is { Id: > 0 };

    /// <summary>Every level a Creator can hand out, for the access picker.</summary>
    public static IReadOnlyList<AdminLevel> AccessLevels { get; } = Enum.GetValues<AdminLevel>();

    /// <summary>The access FILTER's options. Each carries its own caption so the "any level" entry needs
    /// neither a sentinel enum member nor a null the template has to special-case.</summary>
    public ObservableCollection<AccessFilterOption> AccessFilters { get; } = [];

    /// <summary>Narrows the list to one access level; the first option is every level. Costs the server a
    /// full scan, so it is a picker rather than something that fires per keystroke.</summary>
    [ObservableProperty] private AccessFilterOption? _selectedAccessFilter;

    private AdminLevel? AccessFilter => SelectedAccessFilter?.Level;

    private void BuildAccessFilters()
    {
        var keep = SelectedAccessFilter?.Level;
        AccessFilters.Clear();
        AccessFilters.Add(new AccessFilterOption(null, EditorStrings.Get(EditorStrings.AccountEditor_AnyAccess)));
        foreach (var level in Enum.GetValues<AdminLevel>())
            AccessFilters.Add(new AccessFilterOption(level, level.ToString()));
        SelectedAccessFilter = AccessFilters.FirstOrDefault(o => o.Level == keep) ?? AccessFilters[0];
    }

    [ObservableProperty] private EditorAccountRow? _selectedAccount;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _page;
    [ObservableProperty] private int _total;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";

    // The loaded record's own fields, held apart from the row so an edit in progress is not clobbered by
    // a list refresh.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanEditAccess))]
    [NotifyPropertyChangedFor(nameof(IsSelf))]
    private string _login = "";
    [ObservableProperty] private AdminLevel _access;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private int _guild;
    [ObservableProperty] private GuildRank _guildRank;

    /// <summary>Whether an account is open in the form. Keyed on the loaded login rather than the list
    /// selection, which comes and goes with every refresh of the page behind it.</summary>
    public bool HasSelection => Login.Length > 0;
    public bool IsOffline => !_conn.IsConnected;

    /// <summary>True while the loaded account is the one this editor session signed in as. Its access
    /// picker is disabled: the server refuses a self-change, and demoting yourself would take away the
    /// section that could put it back.</summary>
    public bool IsSelf => Login.Length > 0 && string.Equals(Login, _conn.Login, StringComparison.OrdinalIgnoreCase);
    public bool CanEditAccess => HasSelection && !IsSelf;

    public int PageCount => Total <= 0 ? 1 : (Total + PageSize - 1) / PageSize;
    public string PageText => EditorStrings.Format(EditorStrings.AccountEditor_PageOf,
        ("Page", Page + 1), ("Count", PageCount), ("Total", Total));

    public bool CanPrev => Page > 0;
    public bool CanNext => Page + 1 < PageCount;

    /// <summary>Read-only, and said so: changing a guild has to go through the guild system so the
    /// roster cache stays in step.</summary>
    public string GuildText => Guild <= 0
        ? EditorStrings.Get(EditorStrings.AccountEditor_NoGuild)
        : EditorStrings.Format(EditorStrings.AccountEditor_GuildFormat, ("Guild", Guild), ("Rank", GuildRank));

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>Offline this section has nothing to show — accounts are the server's, not the world
    /// folder's. The view says so rather than presenting an empty list that looks like "no accounts".</summary>
    public void LoadOffline()
    {
        Accounts.Clear();
        ClearChars();
        Bank.Clear();
        SelectedAccount = null;
        // Closing the form is explicit. A list refresh drops the selection without closing anything, so
        // this is the only place an open account goes away on its own.
        Login = "";
        Notify();
    }

    private void ClearChars()
    {
        Chars.Clear();
    }

    public void LoadOnline() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_conn.IsConnected) { LoadOffline(); return; }

        // One request at a time: a fast typist can outrun the server, and an older page landing after a
        // newer one would show the wrong rows for the search box's contents.
        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        IsBusy = true;
        try
        {
            var reply = await _conn.RequestAccountsAsync(SearchText.Trim(), AccessFilter, Page, PageSize, ct);
            if (reply is null || ct.IsCancellationRequested) return;

            Accounts.Clear();
            foreach (var a in reply.Accounts) Accounts.Add(a);
            // Clearing the list drops the ListBox's selection, so the open account is put back on its new
            // row. Re-selecting the same login re-reads nothing — the form is already showing it.
            SelectedAccount = Accounts.FirstOrDefault(a => string.Equals(a.Login, Login, StringComparison.OrdinalIgnoreCase));
            Total = reply.Total;
            Page = reply.Page;
            StatusMessage = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        Page = 0;                 // a new search starts at the beginning; page 4 of the old one means nothing
        _ = RefreshAsync();
    }

    partial void OnSelectedAccessFilterChanged(AccessFilterOption? value)
    {
        Page = 0;
        _ = RefreshAsync();
    }

    partial void OnSelectedAccountChanged(EditorAccountRow? value)
    {
        // The form belongs to the LOGIN it loaded, not to a row in the list. A refresh clears and refills
        // Accounts, which drops the ListBox's selection — so treating that as "nothing is open" would empty
        // a form full of unsaved changes every time the search box was typed into.
        if (value is null || string.Equals(value.Login, Login, StringComparison.OrdinalIgnoreCase)) return;
        _ = LoadAccountAsync(value.Login);
    }

    /// <summary>Read an account back from the server.
    ///
    /// <para><paramref name="keepEdits"/> is what a targeted operation passes. Those land immediately and
    /// have to be re-read — the bag on screen must be the bag that exists — but the form may also be holding
    /// typed changes that Save has not sent yet, and replacing it wholesale throws them away. See
    /// <see cref="AdoptServerOwned"/>.</para></summary>
    private async Task LoadAccountAsync(string login, bool keepEdits = false)
    {
        if (!_conn.IsConnected) return;
        try
        {
            var record = await _conn.RequestAccountAsync(login);
            if (record is null) return;
            if (keepEdits) AdoptServerOwned(record);
            else Apply(record);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    internal void Apply(EditorAccountPacket record)
    {
        Login = record.Login;
        Access = record.Access;
        IsOnline = record.IsOnline;
        Guild = record.Guild;
        GuildRank = record.GuildRank;

        Bank.Clear();
        foreach (var b in record.Bank) Bank.Add(b);
        OnPropertyChanged(nameof(HasNoBank));

        ClearChars();
        foreach (var c in record.Chars)
        {
            var row = new AccountCharRowViewModel(c, () => _data.LiveItemEntries);
            Chars.Add(row);
        }
        OnPropertyChanged(nameof(GuildText));
        OnPropertyChanged(nameof(IsSelf));
        OnPropertyChanged(nameof(CanEditAccess));
    }

    /// <summary>Take back only what the SERVER owns — the vault, the online flag, the guild line, and each
    /// character's name, bag, book and log. Access and every character's typed level, EXP, position and
    /// stats are left where the operator put them, because those are what Save carries and nothing else has
    /// touched them.
    ///
    /// <para>A different set of characters means the form is describing an account that has changed under
    /// it, and none of its assumptions hold — that takes the whole record.</para></summary>
    internal void AdoptServerOwned(EditorAccountPacket record)
    {
        if (record.Chars.Count != Chars.Count || record.Chars.Any(c => Chars.All(r => r.Slot != c.Slot)))
        {
            Apply(record);
            return;
        }

        IsOnline = record.IsOnline;
        Guild = record.Guild;
        GuildRank = record.GuildRank;

        Bank.Clear();
        foreach (var b in record.Bank) Bank.Add(b);
        OnPropertyChanged(nameof(HasNoBank));

        foreach (var c in record.Chars)
            Chars.First(r => r.Slot == c.Slot).AdoptServerState(c);

        OnPropertyChanged(nameof(GuildText));
    }

    public bool CanSave => HasSelection;

    /// <summary>Caption for the worn marker on a bag row. On the editor rather than the row because a bag
    /// slot is a wire record with no captions of its own.</summary>
    public string WornLabel => EditorStrings.Get(EditorStrings.AccountEditor_Worn);
    public string VaultHeader => EditorStrings.Get(EditorStrings.AccountEditor_VaultHeader);
    public string VaultEmpty => EditorStrings.Get(EditorStrings.AccountEditor_VaultEmpty);

    // ── Saving ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_conn.IsConnected || Login.Length == 0) return;

        IsBusy = true;
        try
        {
            var reply = await _conn.SaveAccountAsync(new EditorSaveAccountPacket
            {
                Login = Login,
                Access = Access,
                Chars = [.. Chars.Select(c => c.ToRow())],
            });

            // The reply is the server's own re-read. Applying it is what makes a clamped level or a
            // refused map visible instead of leaving the form asserting something that did not happen.
            if (reply is not null) Apply(reply);
            StatusMessage = EditorStrings.Get(EditorStrings.AccountEditor_Saved);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (Login.Length > 0) await LoadAccountAsync(Login);
    }

    // ── Renaming ──────────────────────────────────────────────────────────────

    /// <summary>Rename one character. Its own round trip rather than part of the Save: the server can refuse
    /// it — the name is taken, the character is logged in — and it says why, in its own words. On success the
    /// account and the browser list are both re-read, since the list rows name the characters too.</summary>
    [RelayCommand]
    private async Task RenameCharAsync(AccountCharRowViewModel? row)
    {
        if (row is null || !_conn.IsConnected || Login.Length == 0 || !row.CanRename) return;

        IsBusy = true;
        try
        {
            var notice = await _conn.RenameCharAsync(Login, row.Slot, row.RenameTo.Trim());
            if (notice is null) return;
            StatusMessage = notice.Message;
            if (!notice.Ok) return;

            await LoadAccountAsync(Login, keepEdits: true);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── The bag ───────────────────────────────────────────────────────────────
    // Each is one round trip naming one slot, so an edit cannot carry a stale copy of everything the
    // character has picked up since the form was filled.

    [RelayCommand]
    private Task GiveItemAsync(AccountCharRowViewModel? row) =>
        row is { CanGiveItem: true, GiveItem: { } pick }
            ? RunCharOpAsync(() => _conn.GiveItemAsync(Login, row.Slot, pick.Id, row.GiveQuantity))
            : Task.CompletedTask;

    [RelayCommand]
    private Task GiveToBankAsync() =>
        BankItem is { Id: > 0 } pick
            ? RunCharOpAsync(() => _conn.BankGiveAsync(Login, pick.Id, BankQuantity))
            : Task.CompletedTask;

    /// <summary>Quantity 0 = the whole slot, as everywhere else.</summary>
    [RelayCommand]
    private Task TakeFromBankAsync(EditorInvSlot? slot) =>
        slot is null ? Task.CompletedTask
            : RunCharOpAsync(() => _conn.BankTakeAsync(Login, slot.Slot, 0));

    [RelayCommand]
    private Task TakeItemAsync(EditorInvSlot? slot)
    {
        var row = slot is null ? null : Chars.FirstOrDefault(c => c.Inv.Contains(slot));
        // Quantity 0 = the whole slot. A partial take is what the quantity is for, and nothing here offers
        // one yet: emptying a slot is the operation an operator actually reaches for.
        return row is null ? Task.CompletedTask
            : RunCharOpAsync(() => _conn.TakeItemAsync(Login, row.Slot, slot!.Slot, 0));
    }

    /// <summary>One character operation: send it, show what the server said, and re-read the account when it
    /// worked so the form shows the bag that actually exists.</summary>
    private async Task RunCharOpAsync(Func<Task<EditorNoticePacket?>> op)
    {
        if (!_conn.IsConnected || Login.Length == 0) return;

        IsBusy = true;
        try
        {
            var notice = await op();
            if (notice is null) return;
            StatusMessage = notice.Message;
            if (notice.Ok) await LoadAccountAsync(Login, keepEdits: true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Paging ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanPrev) return;
        Page--;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanNext) return;
        Page++;
        await RefreshAsync();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(IsOffline));
    }
}

/// <summary>One entry in the access filter: a level, or null for every level, with the caption it shows.
/// A record rather than a bare <see cref="AdminLevel"/> so "any" needs no sentinel member on an enum that
/// also assigns real access.</summary>
public sealed record AccessFilterOption(AdminLevel? Level, string Label);

/// <summary>One editable character row. The name is shown but not editable — a rename has to go through
/// the character-name registry, which is a different job from fixing a position.</summary>
public sealed partial class AccountCharRowViewModel : ObservableObject
{
    private readonly int _slot;
    private string _name;

    public AccountCharRowViewModel(EditorCharRow row, Func<NamedEntry[]> itemEntriesProvider)
    {
        _itemEntriesProvider = itemEntriesProvider;
        foreach (var s in row.Inv) Inv.Add(s);
        _slot = row.Slot;
        _name = row.Name;
        _map = row.Map;
        _x = row.X;
        _y = row.Y;
        _renameTo = row.Name;
    }

    public int Slot => _slot;
    public string Name => _name;

    /// <summary>What the rename box holds. Separate from <see cref="Name"/>, which stays what the server
    /// last said: a rename is its own operation, not a field the account Save carries, so the two only agree
    /// again once the server has accepted it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    private string _renameTo = "";

    public bool CanRename => RenameTo.Trim().Length > 0 && RenameTo.Trim() != _name;

    [ObservableProperty] private int _map;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;

    // ── The bag ───────────────────────────────────────────────────────────────

    private readonly Func<NamedEntry[]> _itemEntriesProvider;

    /// <summary>The character's occupied bag slots, as the server last described them. Read-only: adding and
    /// removing are their own round trips, so nothing here is carried by the account Save.</summary>
    public ObservableCollection<EditorInvSlot> Inv { get; } = [];

    public bool HasNoInv => Inv.Count == 0;

    public NamedEntry[] ItemEntries => _itemEntriesProvider();



    /// <summary>The character's quest log, as the server last described it.</summary>



    /// <summary>Every state a quest can be put into, including NotStarted — which takes it out of the log.</summary>

    /// <summary>The item to hand over. Null until one is picked, which is what keeps Give grayed out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGiveItem))]
    private NamedEntry? _giveItem;

    /// <summary>How many, for a stack. One for anything else, which the server enforces anyway.</summary>
    [ObservableProperty] private int _giveQuantity = 1;

    public bool CanGiveItem => GiveItem is { Id: > 0 };

    // Captions resolved per row, since a DataTemplate has no x:Name for the code-behind to reach.
    public string RenameLabel => EditorStrings.Get(EditorStrings.AccountEditor_Rename);
    public string RenamePlaceholder => EditorStrings.Get(EditorStrings.AccountEditor_RenamePlaceholder);
    public string BagHeader => EditorStrings.Get(EditorStrings.AccountEditor_BagHeader);
    public string BagEmpty => EditorStrings.Get(EditorStrings.AccountEditor_BagEmpty);
    public string GiveLabel => EditorStrings.Get(EditorStrings.AccountEditor_Give);
    public string TakeLabel => EditorStrings.Get(EditorStrings.AccountEditor_Take);
    public string ItemPlaceholder => EditorStrings.Get(EditorStrings.AccountEditor_ItemPlaceholder);
    public string LogHeader => EditorStrings.Get(EditorStrings.AccountEditor_LogHeader);
    public string LogEmpty => EditorStrings.Get(EditorStrings.AccountEditor_LogEmpty);
    public string IneligibleLabel => EditorStrings.Get(EditorStrings.AccountEditor_Ineligible);

    internal void NotifyItemEntriesChanged()
    {
        OnPropertyChanged(nameof(ItemEntries));
    }

    internal void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(RenameLabel));
        OnPropertyChanged(nameof(RenamePlaceholder));
        OnPropertyChanged(nameof(BagHeader));
        OnPropertyChanged(nameof(BagEmpty));
        OnPropertyChanged(nameof(GiveLabel));
        OnPropertyChanged(nameof(TakeLabel));
        OnPropertyChanged(nameof(ItemPlaceholder));
        OnPropertyChanged(nameof(LogHeader));
        OnPropertyChanged(nameof(LogEmpty));
        OnPropertyChanged(nameof(IneligibleLabel));
    }

    /// <summary>Take back the parts of this character the SERVER owns — its name and the three collections
    /// a targeted operation changes — and leave every typed field the account Save carries exactly as it is.
    ///
    /// <para>The bag, the book and the log are read-only here and only ever change through their own round
    /// trips, so the server's copy is always the right one. Every typed field is the operator's until Save
    /// sends it, and re-reading over one throws away work they have not finished.</para></summary>
    internal void AdoptServerState(EditorCharRow row)
    {
        if (!string.Equals(_name, row.Name, StringComparison.Ordinal))
        {
            _name = row.Name;
            RenameTo = row.Name;            // the box tracks the accepted name, so Rename grays out again
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(CanRename));
        }

        Refill(Inv, row.Inv);
        OnPropertyChanged(nameof(HasNoInv));
    }

    private static void Refill<T>(ObservableCollection<T> target, List<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }

    public EditorCharRow ToRow() => new()
    {
        Slot = _slot,
        Name = _name,
        Map = Map,
        X = X,
        Y = Y,
    };
}

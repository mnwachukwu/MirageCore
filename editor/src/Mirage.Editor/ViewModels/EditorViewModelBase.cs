using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Shared behavior for every record-list editor (items, NPCs, spells, shops, classes, quests, …):
/// text filtering, per-row dirty tracking, and the Save / Save All / Discard / Discard All commands.
/// <para>A subclass supplies only the type-specific pieces — how to index a row, how to talk to the
/// server, and how to read and write the offline record — and inherits the rest. Every save routes
/// through one path that picks the online or offline branch from <see cref="EditorDataService.IsOnline"/>,
/// so the two modes can't drift.</para>
/// <para>Constructors must call <see cref="HookItems"/> after <c>base()</c>, once
/// <see cref="Items"/> exists, or dirty state and the filter will not track row changes.</para>
/// </summary>
/// <typeparam name="TRow">The per-record row view-model type; must raise <c>PropertyChanged</c>
/// for <c>"IsDirty"</c> so the aggregate dirty flags stay live.</typeparam>
public abstract partial class EditorViewModelBase<TRow> : ObservableObject, IAutoSaveTarget
    where TRow : class, INotifyPropertyChanged
{
    protected readonly EditorDataService _data;
    protected readonly EditorConnection _conn;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;
    /// <summary>Case-insensitive substring filter over <see cref="GetFilterText"/>; empty shows everything.</summary>
    [ObservableProperty] private string _filterText = "";
    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }

    // Rows currently subscribed to PropertyChanged, tracked so they can be detached again — a
    // CollectionChanged Reset reports no OldItems, so without this list those handlers would leak.
    private readonly List<TRow> _subscribedRows = [];

    protected EditorViewModelBase(EditorDataService data, EditorConnection conn)
    {
        _data = data;
        _conn = conn;
        // One subscription here covers every record-list editor and its rows. Each row's
        // DisplayName is a COMPUTED property that falls back to a localized "(empty)" placeholder,
        // so re-raising the list is enough to make the bindings re-read them — no per-row plumbing.
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Re-read everything on this view-model that resolves a localized string: the row
    /// labels (via the list) and the "N of M results" caption. Status messages are left alone —
    /// they are a record of something that already happened, in the language it happened in.</summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        // A lock held by another window of your own account is worded, not just named.
        RefreshLockState();
    }

    /// <summary>The row the editor pane is bound to, or null when nothing is selected.</summary>
    public abstract TRow? Selected { get; }

    // ── What points at the selected record ───────────────────────────────────
    // Every reference in this data model runs one way: the child names the parent, and no record carries a
    // list of its dependents. So the answer is a scan of the OTHER collections, which only
    // MainWindowViewModel can reach — it owns every editor. It supplies the scan here.

    /// <summary>Supplies the records that point at the given record number, grouped by relationship. Assigned
    /// by <see cref="MainWindowViewModel"/>; null in a test or before wiring, which reads as "no references".</summary>
    public Func<int, IReadOnlyList<ReferenceGroupViewModel>>? ResolveInboundRefs { get; set; }

    /// <summary>What refers to the selected record. Recomputed on demand rather than cached: it is a scan over
    /// collections this editor does not own, and a cache would need invalidating on every edit anywhere.</summary>
    public IReadOnlyList<ReferenceGroupViewModel> InboundRefs =>
        Selected is { } row && ResolveInboundRefs is { } resolve ? resolve(GetIndex(row)) : [];

    /// <summary>Whether anything refers to the selected record, so the panel can say "nothing" rather than
    /// showing an empty heading.</summary>
    public bool HasInboundRefs => InboundRefs.Count > 0;

    /// <summary>Re-read <see cref="InboundRefs"/>. The referring records live in other editors, so this one
    /// cannot see them change: the eager load that fills them finishes long after the list is built, and an
    /// edit elsewhere can add or remove a reference entirely behind this editor's back.</summary>
    public void NotifyInboundRefsChanged()
    {
        OnPropertyChanged(nameof(InboundRefs));
        OnPropertyChanged(nameof(HasInboundRefs));
    }
    /// <summary>Assign the selection. The backing property is named per editor (SelectedItem, SelectedNpc),
    /// so the base cannot set it directly.</summary>
    protected abstract void SetSelected(TRow? row);

    /// <summary>Select the row with this record number, as an ordinary selection so it runs the editor's
    /// normal lazy-fetch and dirty-tracking path. False when no row has that number, letting a caller leave
    /// the current section alone rather than switching to a pane showing the wrong record.</summary>
    public bool TrySelect(int index)
    {
        var row = Items.FirstOrDefault(r => GetIndex(r) == index);
        if (row is null) return false;
        SetSelected(row);
        return true;
    }

    /// <summary>Singular display name of the record type ("Item", "NPC"), used in status messages.</summary>
    /// <summary>Stable id for this section, matching the server's lock table and the nav rail. NOT
    /// <see cref="TypeName"/>, which is localized and would key the table differently per language.</summary>
    protected abstract string SectionId { get; }

    protected abstract string TypeName { get; }
    /// <summary>Plural display name; defaults to <see cref="TypeName"/> + "s". Override for irregular plurals.</summary>
    protected virtual string TypeNamePlural => TypeName + "s";
    /// <summary>The record's 1-based slot number, for status messages and save packets.</summary>
    protected abstract int GetIndex(TRow vm);
    /// <summary>Ask the server for this record's current definition; null if the request yielded nothing.</summary>
    protected abstract Task<IPacket?> RequestFromServerAsync(TRow vm);
    /// <summary>Copy a server response returned by <see cref="RequestFromServerAsync"/> into the row.</summary>
    protected abstract void ApplyServerResponse(TRow vm, IPacket pkt);
    /// <summary>Build the save packet sent when the editor is connected to a server.</summary>
    protected abstract IPacket BuildSavePacket(TRow vm);
    /// <summary>Persist the row straight to disk, for offline editing.</summary>
    protected abstract Task SaveOfflineAsync(TRow vm);
    /// <summary>Re-read the row from the on-disk record, discarding unsaved edits (offline discard).</summary>
    protected abstract void LoadFromOfflineRecord(TRow vm);
    /// <summary>Whether the row holds unsaved edits.</summary>
    protected abstract bool GetIsDirty(TRow vm);
    /// <summary>Mark the row clean after a successful save or discard.</summary>
    protected abstract void ClearDirtyState(TRow vm);
    /// <summary>Every row of this record type, in slot order.</summary>
    public abstract ObservableCollection<TRow> Items { get; }
    /// <summary>The text <see cref="FilterText"/> is matched against for one row.</summary>
    protected abstract string GetFilterText(TRow row);
    /// <summary>Whether a row survives the current filter. Override to match on more than the row's text.</summary>
    protected virtual bool MatchesFilter(TRow row) =>
        string.IsNullOrEmpty(FilterText) || GetFilterText(row).Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    /// <summary>The rows the list should display, after <see cref="MatchesFilter"/>.</summary>
    public IEnumerable<TRow> FilteredItems => Items.Where(MatchesFilter);
    /// <summary>Whether a filter is narrowing the list (drives the clear-filter affordance).</summary>
    public virtual bool IsFilterActive => !string.IsNullOrEmpty(FilterText);
    /// <summary>"showing N of M" caption beneath the list.</summary>
    public string FilterStatus => EditorStrings.Format(EditorStrings.Status_FilterCount,
        ("Filtered", FilteredItems.Count()), ("Total", Items.Count));
    [RelayCommand] private void ClearFilter() => FilterText = "";

    /// <summary>Every row holding unsaved edits — used by the quit-time push/save prompt.</summary>
    public IEnumerable<TRow> GetDirty() => Items.Where(GetIsDirty);

    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>The record's name, as authored. Empty for an unused slot.</summary>
    protected abstract string GetName(TRow row);

    /// <summary>Whether the slot holds no record, so a copy may land in it — the same rule the list
    /// already uses to label a slot "(empty)".</summary>
    protected virtual bool IsEmptyRow(TRow row) => string.IsNullOrWhiteSpace(GetName(row));
    /// <summary>Whether the row's full definition has been fetched. Always true offline.</summary>
    protected abstract bool GetIsLoaded(TRow row);
    /// <summary>Write a copy of <paramref name="source"/> into <paramref name="target"/>, applying this
    /// record type's copy rules — the renamed name, and any reference the type cannot share.</summary>
    protected abstract void CopyInto(TRow source, TRow target);

    /// <summary>The lowest-numbered unused slot, or null when every slot holds a record.</summary>
    private TRow? FirstEmptyRow() => Items.FirstOrDefault(IsEmptyRow);

    /// <summary>Whether Copy can run: a real record is open, and there is somewhere to put the copy.
    /// An empty slot is not copyable — duplicating one would just consume another slot to hold a second
    /// nothing, named " (Copy)".</summary>
    public bool CanCopy => Selected is { } row && !IsEmptyRow(row) && FirstEmptyRow() is not null;

    /// <summary>Why Copy is unavailable, for the tooltip on the disabled button. Empty when it is
    /// available, so the button falls back to describing what it does.</summary>
    public string CopyBlockedReason
    {
        get
        {
            if (Selected is not { } row) return EditorStrings.Get(EditorStrings.Common_CopyNeedsSelection);
            if (IsEmptyRow(row)) return EditorStrings.Get(EditorStrings.Common_CopyNeedsRecord);
            if (FirstEmptyRow() is null)
                return EditorStrings.Format(EditorStrings.EntityEditor_NoEmptySlot,
                    ("EntityTypePlural", TypeNamePlural));
            return "";
        }
    }

    /// <summary>What the Copy button says on hover: the reason it cannot run, or what it does.</summary>
    public string CopyTooltip =>
        CopyBlockedReason is { Length: > 0 } why ? why : EditorStrings.Get(EditorStrings.Common_CopyTooltip);

    /// <summary>Duplicate the open record into the first unused slot and select it, ready to edit.
    /// <para>The copy is DIRTY and lives only in memory until a save persists it — the same as any other
    /// edit, so an unwanted copy is a Discard away rather than a file to delete.</para></summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Selected is null) return;
        var source = Selected;

        // Online a row can be a name-only placeholder until it is fetched; copying one would duplicate a
        // blank and quietly lose everything the slot actually holds.
        if (_data.IsOnline && !GetIsLoaded(source)) await LoadEntityAsync(source);

        var target = FirstEmptyRow();
        if (target is null)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_NoEmptySlot,
                ("EntityTypePlural", TypeNamePlural));
            return;
        }

        CopyInto(source, target);
        SetSelected(target);
        NotifyDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_Copied,
            ("EntityType", TypeName), ("From", GetIndex(source)), ("To", GetIndex(target)));
    }

    /// <summary>Write every dirty row straight to disk, bypassing the online path entirely.
    /// Used when saving a whole offline session at once.</summary>
    public async Task SaveAllOfflineAsync()
    {
        foreach (var vm in Items.Where(GetIsDirty).ToList())
        {
            await SaveOfflineAsync(vm);
            ClearDirtyState(vm);
        }
        NotifyDirtyState();
    }

    // ── Auto-save ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public int DirtyCount => Items.Count(GetIsDirty);

    /// <inheritdoc />
    public string OpenRecordName => Selected is { } row ? GetName(row) : "";

    /// <inheritdoc />
    public async Task<int> AutoSaveAsync(AutoSaveReach reach)
    {
        if (reach == AutoSaveReach.OpenRecord)
        {
            if (Selected is not { } row || !GetIsDirty(row)) return 0;
            await SaveOfflineAsync(row);
            ClearDirtyState(row);
            NotifyDirtyState();
            AfterSave(row);
            return 1;
        }

        var dirty = Items.Where(GetIsDirty).ToList();
        if (dirty.Count == 0) return 0;
        foreach (var row in dirty)
        {
            await SaveOfflineAsync(row);
            ClearDirtyState(row);
            AfterSave(row);
        }
        NotifyDirtyState();
        return dirty.Count;
    }

    // ── Dirty-state computed properties ───────────────────────────────────────
    /// <summary>Whether the selected row has unsaved edits (enables Save / Discard).</summary>
    public bool IsSelectedDirty => Selected is not null && GetIsDirty(Selected);
    /// <summary>Whether any row has unsaved edits (enables Save All / Discard All).</summary>
    public bool HasAnyDirty => Items.Any(GetIsDirty);

    /// <summary>Re-raise the aggregate dirty flags. Call after anything that can change a row's
    /// dirty state, since neither flag is a stored value.</summary>
    protected void NotifyDirtyState()
    {
        OnPropertyChanged(nameof(IsSelectedDirty));
        OnPropertyChanged(nameof(HasAnyDirty));
        // Copy depends on the selection and on a free slot still existing, and both move here.
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CopyTooltip));
        // Selection moves through here too, and whether the panel is live depends on which row it is.
        OnPropertyChanged(nameof(IsSelectedLocked));
    }

    /// <summary>Subscribe to <see cref="Items"/> so dirty state and the filtered view follow row
    /// additions, removals, and edits. Call from each concrete constructor after <c>base()</c>, once
    /// <see cref="Items"/> is ready — the base constructor can't do it, since the collection is a
    /// subclass member that does not exist yet at that point.</summary>
    protected void HookItems()
    {
        Items.CollectionChanged += (_, e) =>
        {
            // A Reset carries no OldItems, so detach via the tracked list or the handlers leak.
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _subscribedRows)
                    row.PropertyChanged -= OnRowPropertyChanged;
                _subscribedRows.Clear();
                NotifyDirtyState();
                OnPropertyChanged(nameof(FilteredItems));
                OnPropertyChanged(nameof(FilterStatus));
                return;
            }
            if (e.NewItems is not null)
            {
                foreach (TRow row in e.NewItems.Cast<TRow>())
                {
                    row.PropertyChanged += OnRowPropertyChanged;
                    _subscribedRows.Add(row);
                }
            }

            if (e.OldItems is not null)
            {
                foreach (TRow row in e.OldItems.Cast<TRow>())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    _subscribedRows.Remove(row);
                }
            }

            NotifyDirtyState();
            OnPropertyChanged(nameof(FilteredItems));
            OnPropertyChanged(nameof(FilterStatus));
        };
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "IsDirty") return;
        NotifyDirtyState();
        // A record is claimed when it goes dirty and given back when it comes clean, which is what makes the
        // table name only people with changes in hand. Offline there is nobody to tell.
        if (sender is TRow row && Locks is not null && _conn.IsConnected)
        {
            int num = GetIndex(row);
            _ = GetIsDirty(row)
                ? _conn.SendLockAsync(SectionId, num)
                : _conn.SendUnlockAsync(SectionId, num);
        }
    }

    /// <summary>The shared lock table, assigned once by the shell. Null offline.</summary>
    public EditorLockState? Locks { get; set; }

    /// <summary>Takes a record the server pushed after somebody else saved it, so this session's copy can
    /// never be the older one. Safe to apply blind: only the holder of a record can save it, and a row this
    /// session has dirtied is one it holds — so a push and local changes cannot both exist.</summary>
    public void ApplyLiveRecord(int num, IPacket pkt)
    {
        var row = Items.FirstOrDefault(r => GetIndex(r) == num);
        if (row is null || GetIsDirty(row)) return;
        ApplyServerResponse(row, pkt);
        ClearDirtyState(row);
    }

    /// <summary>Whether the record on screen is held by somebody else. Bound to the detail panel, so the
    /// fields go dead rather than letting somebody type into a save that will be refused.</summary>
    public bool IsSelectedLocked =>
        Selected is ILockableRow { LockedByOther: true };

    /// <summary>Re-reads every row's lock indicator from the table. Called when the server sends a new one.</summary>
    public void RefreshLockState()
    {
        if (Locks is null) return;
        foreach (var row in Items)
        {
            if (row is not ILockableRow lockable) continue;
            int num = GetIndex(row);
            string? holder = Locks.HolderOf(SectionId, num);
            lockable.LockHolder = holder is null ? ""
                : Locks.IsHeldByMyAccountElsewhere(SectionId, num)
                    ? EditorStrings.Format(EditorStrings.Common_LockHeldByYourOtherSession, ("Holder", holder))
                    : holder;
            lockable.LockedByOther = Locks.IsHeldByOther(SectionId, num);
        }
        OnPropertyChanged(nameof(IsSelectedLocked));
    }

    // ── Load / Save / Discard ─────────────────────────────────────────────────

    /// <summary>Fetch one record from the server into <paramref name="vm"/>, driving
    /// <see cref="IsLoading"/> and <see cref="StatusMessage"/>. Failures are reported in the status
    /// line rather than thrown, so a dropped connection can't tear down the editor.</summary>
    protected async Task LoadEntityAsync(TRow vm)
    {
        IsLoading = true;
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadingEntity,
            ("EntityType", TypeName), ("Index", GetIndex(vm)));
        try
        {
            var pkt = await RequestFromServerAsync(vm);
            if (pkt is not null)
            {
                ApplyServerResponse(vm, pkt);
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedEntity,
                    ("EntityType", TypeName), ("Index", GetIndex(vm)));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadFailed,
                ("EntityType", TypeName), ("Index", GetIndex(vm)), ("Error", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Hook for work a subclass needs after a row saves successfully (refreshing a
    /// dependent list, re-sorting). Does nothing by default.</summary>
    protected virtual void AfterSave(TRow vm) { }

    // The single save path: online sends a packet, offline writes the file. Every command below
    // funnels through here so the two modes can't diverge.
    private async Task SaveOneAsync(TRow vm)
    {
        if (_data.IsOnline)
            await _conn.SendSaveAsync(BuildSavePacket(vm));
        else
            await SaveOfflineAsync(vm);
        ClearDirtyState(vm);
        NotifyDirtyState();
        AfterSave(vm);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected is null || !GetIsDirty(Selected)) return;
        var vm = Selected;
        try
        {
            await SaveOneAsync(vm);
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_Saved,
                ("EntityType", TypeName), ("Index", GetIndex(vm)));
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_SaveFailed,
                ("Error", ex.Message));
        }
    }

    // Stops at the first failure rather than pressing on, so a server-side rejection can't leave
    // half the batch saved without the author noticing.
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        int saved = 0;
        foreach (var vm in Items.Where(GetIsDirty))
        {
            try
            {
                await SaveOneAsync(vm);
                saved++;
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_SaveFailed,
                    ("Error", $"{TypeName} {GetIndex(vm)}: {ex.Message}"));
                return;
            }
        }
        StatusMessage = saved > 0
            ? EditorStrings.Format(EditorStrings.EntityEditor_SaveAllSaved,
                ("Count", saved), ("EntityTypePlural", TypeNamePlural))
            : EditorStrings.Format(EditorStrings.EntityEditor_NoDirty,
                ("EntityTypePlural", TypeNamePlural));
    }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        if (Selected is null || !GetIsDirty(Selected)) return;
        var vm = Selected;
        if (_data.IsOnline)
            await LoadEntityAsync(vm);
        else
            LoadFromOfflineRecord(vm);
        ClearDirtyState(vm);
        NotifyDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_Discarded,
            ("EntityType", TypeName), ("Index", GetIndex(vm)));
    }

    [RelayCommand]
    private async Task DiscardAllAsync()
    {
        foreach (var vm in Items.Where(GetIsDirty).ToList())
        {
            try
            {
                if (_data.IsOnline)
                    await LoadEntityAsync(vm);
                else
                    LoadFromOfflineRecord(vm);
                ClearDirtyState(vm);
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_DiscardFailed,
                    ("EntityType", TypeName), ("Index", GetIndex(vm)), ("Error", ex.Message));
                return;
            }
        }
        NotifyDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_AllDiscarded,
            ("EntityTypePlural", TypeNamePlural));
    }
}

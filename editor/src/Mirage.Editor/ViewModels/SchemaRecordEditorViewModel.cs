using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The section for one family a module declared — the same list-and-detail screen every other record type
/// has, built from what the server said the family looks like rather than from a compiled view.
///
/// <para><b>One instance per family.</b> A game declaring three families gets three sections, all from
/// this class, because everything that differs between them is on the <see cref="RecordFamily"/>: the
/// section id, the caption, how many slots, and what rows the form shows.</para>
///
/// <para><b>It inherits the whole editing contract.</b> Filtering, per-row dirty tracking, Save, Save All,
/// Discard, Discard All, Copy, the lock indicator and auto-save all come from
/// <see cref="EditorViewModelBase{TRow}"/>, so a game's records behave exactly like Core's and there is no
/// second set of rules to keep in step.</para>
/// </summary>
public sealed partial class SchemaRecordEditorViewModel : EditorViewModelBase<SchemaRecordRowViewModel>
{
    private readonly RecordFamily _family;

    [ObservableProperty] private SchemaRecordRowViewModel? _selectedRecord;
    public override SchemaRecordRowViewModel? Selected => SelectedRecord;
    protected override void SetSelected(SchemaRecordRowViewModel? row) => SelectedRecord = row;

    public ObservableCollection<SchemaRecordRowViewModel> Records { get; } = [];
    public override ObservableCollection<SchemaRecordRowViewModel> Items => Records;

    public SchemaRecordEditorViewModel(EditorDataService data, EditorConnection conn, RecordFamily family)
        : base(data, conn)
    {
        ArgumentNullException.ThrowIfNull(family);
        _family = family;
        HookItems();
    }

    /// <summary>The family this section authors. Bound by the view for its heading.</summary>
    public RecordFamily Family => _family;

    /// <summary>The section heading: the family's own caption, falling back to its id for a label key this
    /// build's language files have never heard of.</summary>
    public string FamilyLabel => EditorStrings.GetOrFallback(_family.LabelKey, _family.Id);

    protected override string SectionId => _family.Id;

    /// <summary>Singular caption for status messages. A family that declares none is called by its id,
    /// which is at least a name an author recognises from their own game.</summary>
    protected override string TypeName => EditorStrings.GetOrFallback(_family.SingularLabelKey, _family.Id);

    protected override string TypeNamePlural => EditorStrings.GetOrFallback(_family.LabelKey, _family.Id);

    protected override int GetIndex(SchemaRecordRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(SchemaRecordRowViewModel vm) => vm.IsDirty;
    protected override void ClearDirtyState(SchemaRecordRowViewModel vm) => vm.ClearDirty();
    protected override string GetFilterText(SchemaRecordRowViewModel row) => row.DisplayName;
    protected override string GetName(SchemaRecordRowViewModel row) => row.Name;
    protected override bool GetIsLoaded(SchemaRecordRowViewModel row) => row.IsLoaded;

    /// <summary>An unused slot is one holding nothing at all.
    ///
    /// <para>Not "has no name", which is how every compiled section decides: a family may nominate no name
    /// field, and then every slot would look empty and Copy would overwrite slot 1 each time.</para></summary>
    protected override bool IsEmptyRow(SchemaRecordRowViewModel row) => row.Record.IsEmpty;

    protected override void CopyInto(SchemaRecordRowViewModel source, SchemaRecordRowViewModel target)
        => target.CopyFrom(source);

    // ── Filling the list ──────────────────────────────────────────────────────

    /// <summary>Rebuild from the records on disk, fully populated — offline editing has no server to
    /// lazy-load from.</summary>
    public void LoadOffline()
    {
        var records = _data.OfflineModuleRecords(_family);
        Rebuild(records.Length - 1, num => records[num], isLoaded: true);
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Records.Count), ("EntityType", TypeNamePlural));
    }

    /// <summary>Rebuild as blank placeholders. A module's family has no name index in the login handshake,
    /// so the list starts as slot numbers and each row fills in when it is selected, or sooner through
    /// <see cref="EagerLoadAllAsync"/>.</summary>
    public void LoadOnline()
    {
        Rebuild(_data.Limits.For(_family), _ => new AttributeBag(), isLoaded: false);
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Records.Count), ("EntityType", TypeNamePlural));
    }

    private void Rebuild(int limit, Func<int, AttributeBag> recordAt, bool isLoaded)
    {
        SelectedRecord = null;
        Records.Clear();
        for (int i = 1; i <= limit; i++)
            Records.Add(new SchemaRecordRowViewModel(i, _family, recordAt(i), WorldFamilies.Schema,
                                                     References, isLoaded));
    }

    /// <summary>What a <see cref="FieldKind.RecordRef"/> row offers: the named records of the family it
    /// points at, so an author picks a name rather than typing a slot number.</summary>
    private IReadOnlyList<NamedEntry> References(string familyId) => _data.EntriesFor(familyId);

    /// <summary>Pre-fill every row from one bulk response, so browsing the list after connecting is
    /// instant instead of fetching per selection. No-op offline; canceled on disconnect.</summary>
    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;

        var bulk = await _conn.RequestAllRecordsAsync(_family.Id, ct);
        if (bulk is null) return;

        foreach (var entry in bulk.Records)
        {
            var row = Items.FirstOrDefault(r => r.Index == entry.Num);
            row?.LoadFromRecord(entry.Fields);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedRecordChanged(SchemaRecordRowViewModel? value)
    {
        NotifyInboundRefsChanged();
        NotifyDirtyState();
        if (value is not null && !value.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(value);
    }

    // ── Talking to the server, and to disk ────────────────────────────────────

    protected override async Task<IPacket?> RequestFromServerAsync(SchemaRecordRowViewModel vm)
        => await _conn.RequestRecordAsync(_family.Id, vm.Index);

    protected override void ApplyServerResponse(SchemaRecordRowViewModel vm, IPacket pkt)
        => vm.LoadFromRecord(((UpdateRecordPacket)pkt).Fields);

    protected override IPacket BuildSavePacket(SchemaRecordRowViewModel vm) => vm.BuildSavePacket();

    protected override Task SaveOfflineAsync(SchemaRecordRowViewModel vm)
        => _data.SaveOfflineModuleRecordAsync(_family, vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(SchemaRecordRowViewModel vm)
    {
        var records = _data.OfflineModuleRecords(_family);
        if (vm.Index < records.Length) vm.LoadFromRecord(records[vm.Index]);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>The conversation editor — clones QuestEditorViewModel over the EditorViewModelBase online/offline flow.
/// A conversation row needs the live NPC picker list (its SpeakerNpc) from EditorDataService; the node/choice
/// pickers are self-sourced from the conversation's own nodes.</summary>
public sealed partial class ConversationEditorViewModel : EditorViewModelBase<ConversationRowViewModel>
{
    [ObservableProperty] private ConversationRowViewModel? _selectedConversation;
    public override ConversationRowViewModel? Selected => SelectedConversation;
    protected override void SetSelected(ConversationRowViewModel? row) => SelectedConversation = row;
    public ObservableCollection<ConversationRowViewModel> Conversations { get; } = [];
    public override ObservableCollection<ConversationRowViewModel> Items => Conversations;
    protected override string GetFilterText(ConversationRowViewModel row) => row.DisplayName;

    public ConversationEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () => { foreach (var c in Conversations) c.NotifyEntriesChanged(); };
    }

    // ── Which way the nodes are shown ─────────────────────────────────────────

    /// <summary>The node region draws the branching graph rather than the stack of cards. The way a
    /// conversation is normally read, so it leads; the text form is the alternative. A per-user preference,
    /// carried across sessions by the view; both views edit the same rows, so switching loses nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextView))]
    private bool _isGraphView = true;

    /// <summary>The other half of the pair, for the radio buttons. Assigning false does nothing — the button
    /// being cleared is not the one making the choice.</summary>
    public bool IsTextView
    {
        get => !IsGraphView;
        set { if (value) IsGraphView = false; }
    }

    /// <summary>Opens one node for editing. Set by the window that owns the dialog, so this stays free of any
    /// reference to a view.</summary>
    public Func<ConversationRowViewModel, ConversationNodeRowViewModel, Task>? ShowNodeDialogAsync { get; set; }

    /// <summary>Invoked with the node whose box was clicked on the graph.</summary>
    [RelayCommand]
    private async Task EditNode(ConversationNodeRowViewModel? node)
    {
        if (node is null || SelectedConversation is null || ShowNodeDialogAsync is null) return;
        await ShowNodeDialogAsync(SelectedConversation, node);
    }

    protected override string SectionId => "Conversations";
    protected override string TypeName => EditorStrings.Get(EditorStrings.ConversationEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.ConversationEditor_TypeNamePlural);
    protected override int GetIndex(ConversationRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(ConversationRowViewModel vm) => vm.IsDirty;
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(ConversationRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(ConversationRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(ConversationRowViewModel source, ConversationRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        // The resolver picks the FIRST non-empty conversation whose SpeakerNpc matches, so a verbatim copy
        // would either be dead or hijack the NPC depending on slot order. The copy arrives unattached.
        rec.SpeakerNpc = 0;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(ConversationRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllConversationsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Conversations)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.ConvNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedConversationChanged(ConversationRowViewModel? value)
    {
        NotifyInboundRefsChanged();
        NotifyDirtyState();
        if (value is not null && !value.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(value);
    }

    public void LoadOffline()
    {
        SelectedConversation = null;
        Conversations.Clear();
        for (int i = 1; i < _data.OfflineConversations.Length; i++)
            Conversations.Add(NewRow(i, _data.OfflineConversations[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Conversations.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineConversations is null) return;
        SelectedConversation = null;
        Conversations.Clear();
        foreach (var entry in _data.OnlineConversations)
            Conversations.Add(NewRow(entry.Num, new ConversationRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Conversations.Count), ("EntityType", TypeNamePlural));
    }

    // Bind the SpeakerNpc picker from the live editor NPC cache; node/choice pickers are self-sourced.
    private ConversationRowViewModel NewRow(int index, ConversationRecord r, bool isLoaded = true) =>
        new(index, r, () => _data.LiveNpcEntries, isLoaded);

    protected override async Task<IPacket?> RequestFromServerAsync(ConversationRowViewModel vm)
        => await _conn.RequestConversationAsync(vm.Index);

    protected override void ApplyServerResponse(ConversationRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateConversationPacket)pkt);

    protected override IPacket BuildSavePacket(ConversationRowViewModel vm) => vm.BuildSavePacket();

    protected override void AfterSave(ConversationRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineConversationName(vm.Index, vm.Name);
    }

    protected override Task SaveOfflineAsync(ConversationRowViewModel vm)
        => _data.SaveOfflineConversationAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(ConversationRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineConversations[vm.Index]);
}

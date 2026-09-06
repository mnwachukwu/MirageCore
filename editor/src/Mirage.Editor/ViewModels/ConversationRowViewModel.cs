using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
namespace Mirage.Editor.ViewModels;

/// <summary>The conversation-editor row — clones QuestRowViewModel, extended ONE level: it owns a dynamic list of
/// dialogue NODES, and each node owns its own dynamic list of CHOICES. A choice's "next node" picker is
/// self-referential (the conversation's own nodes). Stable node ids are assigned on add and never reused, so a
/// choice keeps pointing at the right node across edits. Dirty aggregates the node (and choice) rows.</summary>
public sealed partial class ConversationRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    private const int MaxNodes = Constants.MaxConversationNodes;

    public int Index { get; }
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _speakerNpc;
    [ObservableProperty] private int _rootNodeId;

    public ObservableCollection<ConversationNodeRowViewModel> Nodes { get; } = [];
    public bool HasNoNodes => Nodes.Count == 0;

    /// <summary>Raised whenever the shape of the conversation changes — a node added, removed or retitled, a
    /// choice repointed, a different root picked. The graph derives every position from it, so the canvas
    /// redraws off this rather than off any one property.</summary>
    public event Action? GraphChanged;

    private void RaiseGraphChanged() => GraphChanged?.Invoke();

    /// <summary>The nodes as the layout sees them: an id and where each choice goes. A hand-off reports the
    /// way it leaves rather than a next node — the runtime ignores that field once an action is set and
    /// hands the player to the shop or the quest list, so a link drawn from it would picture a branch that
    /// never happens.</summary>
    public IReadOnlyList<ConversationGraphNode> GraphNodes() =>
    [
        .. Nodes.Select(n => new ConversationGraphNode(n.NodeId, [.. n.Choices.Select(BranchFor)])),
    ];

    private static ConversationGraphBranch BranchFor(ConversationChoiceRowViewModel choice) => choice.Action switch
    {
        ConversationAction.OpenShop => new ConversationGraphBranch(0, ConversationEndKind.OpensShop),
        ConversationAction.OpenQuests => new ConversationGraphBranch(0, ConversationEndKind.OpensQuests),
        // A target naming no node is a goodbye, which the layout decides — it holds the id index.
        _ => new ConversationGraphBranch(choice.NextNodeId, ConversationEndKind.None),
    };

    // The next stable node id to hand out (monotonic; never reuses a deleted id).
    private int _nextNodeId = 1;

    private bool _dirty;
    private bool _loading;

    public bool IsDirty => _dirty || Nodes.Any(n => n.IsDirty);

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    private readonly Func<NamedEntry[]> _npcEntriesProvider;

    // ── Pickers ──────────────────────────────────────────────────────────────
    public NamedEntry[] NpcEntries => _npcEntriesProvider();

    public NamedEntry? SelectedSpeaker
    {
        get => EntryFor(NpcEntries, SpeakerNpc);
        set
        {
            var id = value?.Id ?? 0;
            if (SpeakerNpc == id) return;
            SpeakerNpc = id;
        }
    }

    // Root-node picker: the conversation's nodes + a "(first node)" entry (id 0 = runtime uses the first node).
    public NamedEntry[] RootNodeEntries => WithPseudo(EditorStrings.Get(EditorStrings.ConversationEditor_RootFirst));
    // Shown while RootNodeId is 0 — the box is empty in that state (see SelectedRootNode's null sentinel).
    public string RootNodePlaceholder => EditorStrings.Get(EditorStrings.ConversationEditor_RootFirst);
    public NamedEntry? SelectedRootNode
    {
        // id 0 ("(first node)") is the NULL sentinel, not a selectable value: return null so the picker's text
        // box clears (an empty box lets the type-ahead list every node instead of filtering it down to none).
        get => RootNodeId <= 0 ? null : RootNodeEntries.FirstOrDefault(e => e.Id == RootNodeId);
        set
        {
            var id = value?.Id ?? 0;
            if (RootNodeId == id) return;
            RootNodeId = id;
        }
    }

    public ConversationRowViewModel(int index, ConversationRecord r,
        Func<NamedEntry[]> npcEntriesProvider, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _npcEntriesProvider = npcEntriesProvider;
        _name = r.Name;
        _speakerNpc = r.SpeakerNpc;
        _rootNodeId = r.RootNodeId;

        Nodes.CollectionChanged += OnNodesChanged;
        _loading = true;
        BuildNodes(r.Nodes);
        _loading = false;
    }

    // The conversation's nodes as a choice's NextNode options: an "(End)" pseudo at id 0, then each node.
    public NamedEntry[] NodeChoiceEntries() => WithPseudo(EditorStrings.Get(EditorStrings.ConversationEditor_ChoiceEnd));

    private NamedEntry[] WithPseudo(string zeroLabel)
    {
        var list = new List<NamedEntry>(Nodes.Count + 1) { new(0, zeroLabel) };
        foreach (var n in Nodes) list.Add(new NamedEntry(n.NodeId, n.Header));
        return list.ToArray();
    }

    private void BuildNodes(IReadOnlyList<ConversationNode> nodes)
    {
        Nodes.Clear();
        _nextNodeId = 1;
        foreach (var n in nodes)
        {
            int id = n.Id > 0 ? n.Id : _nextNodeId;
            if (id >= _nextNodeId) _nextNodeId = id + 1;
            Nodes.Add(new ConversationNodeRowViewModel(id, n, NodeChoiceEntries));
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ObservableObject r in e.OldItems) r.PropertyChanged -= OnNodePropertyChanged;
        if (e.NewItems is not null)
            foreach (ObservableObject r in e.NewItems) r.PropertyChanged += OnNodePropertyChanged;
        OnPropertyChanged(nameof(HasNoNodes));
        AddNodeCommand.NotifyCanExecuteChanged();
        RefreshNodeEntries();   // a node added/removed → every choice's NextNode picker changes
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanAddNode))]
    private void AddNode() =>
        Nodes.Add(new ConversationNodeRowViewModel(_nextNodeId++, new ConversationNode(), NodeChoiceEntries));
    private bool CanAddNode() => Nodes.Count < MaxNodes;
    [RelayCommand]
    private void RemoveNode(ConversationNodeRowViewModel row) => Nodes.Remove(row);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        if (e.PropertyName == nameof(ConversationNodeRowViewModel.Header))
            RefreshNodeEntries();   // a node's text changed → its picker label changed
        // A choice's label or target reaches here as the node's own dirty raise, which is the only signal
        // that a branch was repointed.
        else if (e.PropertyName == nameof(ConversationNodeRowViewModel.IsDirty)) RaiseGraphChanged();
        OnPropertyChanged(nameof(IsDirty));
    }

    // The node picker options changed (a node added/removed/renamed) — refresh the root picker + every choice's.
    private void RefreshNodeEntries()
    {
        OnPropertyChanged(nameof(RootNodeEntries));
        OnPropertyChanged(nameof(SelectedRootNode));
        foreach (var n in Nodes) n.NotifyEntriesChanged();
        RaiseGraphChanged();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        MarkDirty();
    }
    partial void OnSpeakerNpcChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedSpeaker));
        MarkDirty();
    }
    partial void OnRootNodeIdChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedRootNode));
        RaiseGraphChanged();   // a different opening node re-roots the whole picture
        MarkDirty();
    }

    public void ClearDirty()
    {
        _dirty = false;
        foreach (var n in Nodes) n.ClearDirty();
        OnPropertyChanged(nameof(IsDirty));
    }

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(NpcEntries));
        OnPropertyChanged(nameof(SelectedSpeaker));
        // The node pickers are self-sourced (not from the editor caches), so they don't refresh here.
    }

    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(ConversationRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(ConversationRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            SpeakerNpc = r.SpeakerNpc;
            RootNodeId = r.RootNodeId;
            BuildNodes(r.Nodes);
        }
        finally { _loading = false; }
        _dirty = false;
        RefreshNodeEntries();
        OnPropertyChanged(nameof(HasNoNodes));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ApplyPacket(UpdateConversationPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            SpeakerNpc = pkt.SpeakerNpc;
            RootNodeId = pkt.RootNodeId;
            BuildNodes(pkt.Nodes);
        }
        finally { _loading = false; }

        IsLoaded = true;
        RefreshNodeEntries();
        OnPropertyChanged(nameof(HasNoNodes));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public ConversationRecord ToRecord()
    {
        var r = new ConversationRecord { Name = Name, SpeakerNpc = SpeakerNpc, RootNodeId = RootNodeId };
        foreach (var n in Nodes) if (!n.IsEmpty) r.Nodes.Add(n.ToRecord());
        return r;
    }

    public EditorSaveConversationPacket BuildSavePacket() => new()
    {
        ConvNum = Index,
        Name = Name,
        SpeakerNpc = SpeakerNpc,
        RootNodeId = RootNodeId,
        Nodes = Nodes.Where(n => !n.IsEmpty).Select(n => n.ToRecord()).ToList(),
    };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

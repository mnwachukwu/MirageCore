using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One authored quest objective: a Kind, an NPC target, and a count. Mirrors
/// ShopBarterRowViewModel — an id↔NamedEntry picker facade + dirty tracking. An empty row (Kind None / Count 0) is
/// dropped on save.</summary>
public sealed partial class QuestObjectiveRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _npcEntriesProvider;

    public int SlotIndex { get; }

    public string TargetPlaceholder => EditorStrings.Get(EditorStrings.QuestEditor_TargetPlaceholder);

    // Only Kill has runtime objective logic wired; Fetch/Gather/Explore are placeholder kinds with no server hooks
    // yet, so hide them from the editor dropdown until they're implemented. None stays (the empty-row state).
    public IEnumerable<ObjectiveKind> Kinds { get; } = new[] { ObjectiveKind.None, ObjectiveKind.Kill };

    [ObservableProperty] private ObjectiveKind _kind;
    [ObservableProperty] private int _target;
    [ObservableProperty] private int _count;

    public bool IsDirty { get; private set; }

    public NamedEntry[] TargetEntries => _npcEntriesProvider();

    public NamedEntry? SelectedTarget
    {
        get => EntryFor(TargetEntries, Target);
        set
        {
            var id = value?.Id ?? 0;
            if (Target == id) return;
            Target = id;   // OnTargetChanged marks dirty + re-notifies
        }
    }

    public QuestObjectiveRowViewModel(int slotIndex, Objective o, Func<NamedEntry[]> npcEntriesProvider)
    {
        SlotIndex = slotIndex;
        _npcEntriesProvider = npcEntriesProvider;
        _kind = o.Kind;
        _target = o.Target;
        _count = o.Count;
    }

    partial void OnKindChanged(ObjectiveKind value) => IsDirty = true;
    partial void OnTargetChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedTarget));
    }
    partial void OnCountChanged(int value) => IsDirty = true;

    /// <summary>An unused objective row — no Kind or no count. Dropped when the quest is saved.</summary>
    public bool IsEmpty => Kind == ObjectiveKind.None || Count <= 0;

    public void ClearDirty() => IsDirty = false;

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(TargetEntries));
        OnPropertyChanged(nameof(SelectedTarget));
    }

    public Objective ToRecord() => new() { Kind = Kind, Target = Target, Count = Count };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

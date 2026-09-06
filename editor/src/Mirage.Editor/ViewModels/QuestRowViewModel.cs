using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The quest-editor row — clones ShopRowViewModel. Owns fixed-slot Objective and
/// Reward child lists (empties dropped on save), the requirement/reward scalars, and id↔NamedEntry picker
/// facades for the giver / turn-in NPC and the PrereqQuest. The class gate is a multi-select instead, so
/// it has no picker facade here — the editor view-model drives it. Dirty aggregates the child rows.</summary>
public sealed partial class QuestRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    // Editor-side safety ceiling for every quest child table (objectives + both reward lists). 255 matches
    // the shared MaxQuestObjectives; the reward lists have no separate shared cap (the server accepts
    // unlimited), so they reuse the same value.
    private const int MaxRows = Constants.MaxQuestObjectives;

    public int Index { get; }
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private int _reqLevel;
    [ObservableProperty] private int _reqStr;
    [ObservableProperty] private int _reqDef;
    [ObservableProperty] private int _reqSpd;
    [ObservableProperty] private int _reqInt;
    /// <summary>Classes allowed to accept it; null or empty = every class.</summary>
    [ObservableProperty] private List<short>? _allowedClasses;
    [ObservableProperty] private int _prereqQuest;
    [ObservableProperty] private long _rewardExp;
    [ObservableProperty] private long _repeatRewardExp;
    [ObservableProperty] private int _giverNpc;
    [ObservableProperty] private int _turnInNpc;
    [ObservableProperty] private bool _repeatable;
    [ObservableProperty] private QuestCadence _cadence;

    public IEnumerable<QuestCadence> Cadences { get; } = Enum.GetValues<QuestCadence>();

    public ObservableCollection<QuestObjectiveRowViewModel> Objectives { get; } = [];
    public ObservableCollection<QuestRewardRowViewModel> RewardItems { get; } = [];
    public ObservableCollection<QuestRewardRowViewModel> RepeatRewardItems { get; } = [];

    // Empty-state flags — drive the "no rows yet" hint under each table.
    public bool HasNoObjectives => Objectives.Count == 0;
    public bool HasNoRewards => RewardItems.Count == 0;
    public bool HasNoRepeatRewards => RepeatRewardItems.Count == 0;

    public bool IsDirty => _dirty
        || Objectives.Any(o => o.IsDirty)
        || RewardItems.Any(r => r.IsDirty)
        || RepeatRewardItems.Any(r => r.IsDirty);
    private bool _dirty;
    private bool _loading;

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    private readonly Func<NamedEntry[]> _npcEntriesProvider;
    private readonly Func<NamedEntry[]> _itemEntriesProvider;
    private readonly Func<NamedEntry[]> _classEntriesProvider;
    private readonly Func<NamedEntry[]> _questEntriesProvider;
    private readonly Func<int, bool> _isCurrency;

    // Picker sources (giver + turn-in share the NPC list; objective targets have their own on the row VM).
    public NamedEntry[] NpcEntries => _npcEntriesProvider();
    public NamedEntry[] ClassEntries => _classEntriesProvider();
    public NamedEntry[] QuestEntries => _questEntriesProvider();

    public NamedEntry? SelectedGiver
    {
        get => EntryFor(NpcEntries, GiverNpc);
        set
        {
            var id = value?.Id ?? 0;
            if (GiverNpc == id) return;
            GiverNpc = id;
        }
    }
    public NamedEntry? SelectedTurnIn
    {
        get => EntryFor(NpcEntries, TurnInNpc);
        set
        {
            var id = value?.Id ?? 0;
            if (TurnInNpc == id) return;
            TurnInNpc = id;
        }
    }
    public NamedEntry? SelectedPrereq
    {
        get => EntryFor(QuestEntries, PrereqQuest);
        set
        {
            var id = value?.Id ?? 0;
            if (PrereqQuest == id) return;
            PrereqQuest = id;
        }
    }

    public QuestRowViewModel(int index, QuestRecord r,
        Func<NamedEntry[]> npcEntriesProvider, Func<NamedEntry[]> itemEntriesProvider,
        Func<NamedEntry[]> classEntriesProvider, Func<NamedEntry[]> questEntriesProvider,
        Func<int, bool> isCurrency, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _npcEntriesProvider = npcEntriesProvider;
        _itemEntriesProvider = itemEntriesProvider;
        _classEntriesProvider = classEntriesProvider;
        _questEntriesProvider = questEntriesProvider;
        _isCurrency = isCurrency;

        _name = r.Name;
        _description = r.Description;
        _reqLevel = r.ReqLevel;
        _reqStr = r.ReqStr;
        _reqDef = r.ReqDef;
        _reqSpd = r.ReqSpd;
        _reqInt = r.ReqInt;
        _allowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
        _prereqQuest = r.PrereqQuest;
        _rewardExp = r.RewardExp;
        _repeatRewardExp = r.RepeatRewardExp;
        _giverNpc = r.GiverNpc;
        _turnInNpc = r.TurnInNpc;
        _repeatable = r.Repeatable;
        _cadence = r.Cadence;

        Objectives.CollectionChanged += OnRowsCollectionChanged;
        RewardItems.CollectionChanged += OnRowsCollectionChanged;
        RepeatRewardItems.CollectionChanged += OnRowsCollectionChanged;
        _loading = true;
        BuildCollections(r.Objectives, r.RewardItems, r.RepeatRewardItems);
        _loading = false;
    }

    // Rebuild the child tables from a record — one row per real entry, no fixed-slot padding, so an empty
    // quest shows blank tables. Persisted records already contain only non-empty entries (empties are
    // dropped on save), so this loads exactly the authored rows.
    private void BuildCollections(IReadOnlyList<Objective> objectives, IReadOnlyList<QuestReward> rewards, IReadOnlyList<QuestReward> repeatRewards)
    {
        Objectives.Clear();
        for (int i = 0; i < objectives.Count; i++)
            Objectives.Add(new QuestObjectiveRowViewModel(i + 1, objectives[i], _npcEntriesProvider));
        RewardItems.Clear();
        for (int i = 0; i < rewards.Count; i++)
            RewardItems.Add(new QuestRewardRowViewModel(i + 1, rewards[i], _itemEntriesProvider, _isCurrency));
        RepeatRewardItems.Clear();
        for (int i = 0; i < repeatRewards.Count; i++)
            RepeatRewardItems.Add(new QuestRewardRowViewModel(i + 1, repeatRewards[i], _itemEntriesProvider, _isCurrency));
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ObservableObject r in e.OldItems) r.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (ObservableObject r in e.NewItems) r.PropertyChanged += OnRowPropertyChanged;

        // Refresh empty-state hints + the Add buttons' CanExecute (cheap; notify all three).
        OnPropertyChanged(nameof(HasNoObjectives));
        OnPropertyChanged(nameof(HasNoRewards));
        OnPropertyChanged(nameof(HasNoRepeatRewards));
        AddObjectiveCommand.NotifyCanExecuteChanged();
        AddRewardCommand.NotifyCanExecuteChanged();
        AddRepeatRewardCommand.NotifyCanExecuteChanged();
        MarkDirty();   // no-op while _loading; otherwise adding/removing a row dirties the quest
    }

    [RelayCommand(CanExecute = nameof(CanAddObjective))]
    private void AddObjective() =>
        Objectives.Add(new QuestObjectiveRowViewModel(Objectives.Count + 1, new Objective(), _npcEntriesProvider));
    private bool CanAddObjective() => Objectives.Count < MaxRows;
    [RelayCommand]
    private void RemoveObjective(QuestObjectiveRowViewModel row) => Objectives.Remove(row);

    [RelayCommand(CanExecute = nameof(CanAddReward))]
    private void AddReward() =>
        RewardItems.Add(new QuestRewardRowViewModel(RewardItems.Count + 1, new QuestReward(), _itemEntriesProvider, _isCurrency));
    private bool CanAddReward() => RewardItems.Count < MaxRows;
    [RelayCommand]
    private void RemoveReward(QuestRewardRowViewModel row) => RewardItems.Remove(row);

    [RelayCommand(CanExecute = nameof(CanAddRepeatReward))]
    private void AddRepeatReward() =>
        RepeatRewardItems.Add(new QuestRewardRowViewModel(RepeatRewardItems.Count + 1, new QuestReward(), _itemEntriesProvider, _isCurrency));
    private bool CanAddRepeatReward() => RepeatRewardItems.Count < MaxRows;
    [RelayCommand]
    private void RemoveRepeatReward(QuestRewardRowViewModel row) => RepeatRewardItems.Remove(row);

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        OnPropertyChanged(nameof(IsDirty));
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
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnReqLevelChanged(int value) => MarkDirty();
    partial void OnReqStrChanged(int value) => MarkDirty();
    partial void OnReqDefChanged(int value) => MarkDirty();
    partial void OnReqSpdChanged(int value) => MarkDirty();
    partial void OnReqIntChanged(int value) => MarkDirty();
    partial void OnAllowedClassesChanged(List<short>? value) => MarkDirty();
    partial void OnPrereqQuestChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedPrereq));
        MarkDirty();
    }
    partial void OnRewardExpChanged(long value) => MarkDirty();
    partial void OnRepeatRewardExpChanged(long value) => MarkDirty();
    partial void OnGiverNpcChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedGiver));
        MarkDirty();
    }
    partial void OnTurnInNpcChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedTurnIn));
        MarkDirty();
    }
    partial void OnRepeatableChanged(bool value) => MarkDirty();
    partial void OnCadenceChanged(QuestCadence value) => MarkDirty();

    public void ClearDirty()
    {
        _dirty = false;
        foreach (var o in Objectives) o.ClearDirty();
        foreach (var r in RewardItems) r.ClearDirty();
        foreach (var r in RepeatRewardItems) r.ClearDirty();
        OnPropertyChanged(nameof(IsDirty));
    }

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(NpcEntries));
        OnPropertyChanged(nameof(ClassEntries));
        OnPropertyChanged(nameof(QuestEntries));
        OnPropertyChanged(nameof(SelectedGiver));
        OnPropertyChanged(nameof(SelectedTurnIn));
        OnPropertyChanged(nameof(SelectedPrereq));
        foreach (var o in Objectives) o.NotifyEntriesChanged();
        foreach (var r in RewardItems) r.NotifyEntriesChanged();
        foreach (var r in RepeatRewardItems) r.NotifyEntriesChanged();
    }

    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(QuestRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(QuestRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Description = r.Description;
            ReqLevel = r.ReqLevel;
            ReqStr = r.ReqStr;
            ReqDef = r.ReqDef;
            ReqSpd = r.ReqSpd;
            ReqInt = r.ReqInt;
            AllowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
            PrereqQuest = r.PrereqQuest;
            RewardExp = r.RewardExp;
            RepeatRewardExp = r.RepeatRewardExp;
            GiverNpc = r.GiverNpc;
            TurnInNpc = r.TurnInNpc;
            Repeatable = r.Repeatable;
            Cadence = r.Cadence;
            BuildCollections(r.Objectives, r.RewardItems, r.RepeatRewardItems);
        }
        finally { _loading = false; }
        _dirty = false;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ApplyPacket(UpdateQuestPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Description = pkt.Description;
            ReqLevel = pkt.ReqLevel;
            ReqStr = pkt.ReqStr;
            ReqDef = pkt.ReqDef;
            ReqSpd = pkt.ReqSpd;
            ReqInt = pkt.ReqInt;
            AllowedClasses = pkt.AllowedClasses is null ? null : new List<short>(pkt.AllowedClasses);
            PrereqQuest = pkt.PrereqQuest;
            RewardExp = pkt.RewardExp;
            RepeatRewardExp = pkt.RepeatRewardExp;
            GiverNpc = pkt.GiverNpc;
            TurnInNpc = pkt.TurnInNpc;
            Repeatable = pkt.Repeatable;
            Cadence = pkt.Cadence;
            BuildCollections(pkt.Objectives, pkt.RewardItems, pkt.RepeatRewardItems);
        }
        finally { _loading = false; }

        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public QuestRecord ToRecord()
    {
        var r = new QuestRecord
        {
            Name = Name,
            Description = Description,
            ReqLevel = ReqLevel, ReqStr = ReqStr, ReqDef = ReqDef, ReqSpd = ReqSpd, ReqInt = ReqInt,
            AllowedClasses = ClassGate.Normalize(AllowedClasses), PrereqQuest = PrereqQuest,
            RewardExp = RewardExp, RepeatRewardExp = RepeatRewardExp,
            GiverNpc = GiverNpc, TurnInNpc = TurnInNpc,
            Repeatable = Repeatable, Cadence = Cadence,
        };
        foreach (var o in Objectives) if (!o.IsEmpty) r.Objectives.Add(o.ToRecord());
        foreach (var rw in RewardItems) if (!rw.IsEmpty) r.RewardItems.Add(rw.ToRecord());
        foreach (var rw in RepeatRewardItems) if (!rw.IsEmpty) r.RepeatRewardItems.Add(rw.ToRecord());
        return r;
    }

    public EditorSaveQuestPacket BuildSavePacket() => new()
    {
        QuestNum = Index,
        Name = Name,
        Description = Description,
        Objectives = Objectives.Where(o => !o.IsEmpty).Select(o => o.ToRecord()).ToList(),
        ReqLevel = ReqLevel, ReqStr = ReqStr, ReqDef = ReqDef, ReqSpd = ReqSpd, ReqInt = ReqInt,
        AllowedClasses = ClassGate.Normalize(AllowedClasses), PrereqQuest = PrereqQuest,
        RewardExp = RewardExp,
        RewardItems = RewardItems.Where(r => !r.IsEmpty).Select(r => r.ToRecord()).ToList(),
        RepeatRewardExp = RepeatRewardExp,
        RepeatRewardItems = RepeatRewardItems.Where(r => !r.IsEmpty).Select(r => r.ToRecord()).ToList(),
        GiverNpc = GiverNpc, TurnInNpc = TurnInNpc,
        Repeatable = Repeatable, Cadence = Cadence,
    };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

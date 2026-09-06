using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One MapGroup slot in the MapGroup editor. Holds the authored fields; Moral + the two
/// environment bools are tri-state (null = "(Inherit)"/don't-provide) so a group can decline to supply a value.
/// A group is authored end to end: who holds the territory its maps make up is the server's and is not here.</summary>
public sealed partial class MapGroupRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    public int Index { get; }
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    private string _displayName = "";   // record DisplayName; managed via DisplayNameText (the generated
                                        // property would collide with the list-label DisplayName below)
    [ObservableProperty] private int _music;
    [ObservableProperty] private MapMoral? _moral;
    // Map-enter/leave greeting fallback: a member map inherits any greeting field it leaves
    // blank from the group.
    [ObservableProperty] private string _greetingSpeaker = "";
    [ObservableProperty] private string _joinSay = "";
    [ObservableProperty] private string _leaveSay = "";
    [ObservableProperty] private bool? _indoors;
    [ObservableProperty] private bool? _alwaysLit;
    [ObservableProperty] private bool? _alwaysDark;
    [ObservableProperty] private int _bootMap;
    [ObservableProperty] private int _bootX;
    [ObservableProperty] private int _bootY;
    [ObservableProperty] private bool _territory;

    public bool IsDirty => _dirty;
    private bool _dirty;
    private bool _loading;

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    public IReadOnlyList<MoralChoice> MoralOptions { get; private set; } = MoralChoices.Build();
    public MoralChoice? SelectedMoral
    {
        get => MoralOptions.FirstOrDefault(c => c.Value == Moral) ?? MoralOptions[0];
        set { if (value is not null) Moral = value.Value; }
    }

    private readonly Func<NamedEntry[]> _mapEntries;

    public MapGroupRowViewModel(int index, MapGroupRecord r, Func<NamedEntry[]> mapEntries, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _mapEntries = mapEntries;
        CopyFrom(r);
    }

    private void CopyFrom(MapGroupRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            DisplayNameText = r.DisplayName;
            Music = r.Music;
            Moral = r.Moral;
            GreetingSpeaker = r.GreetingSpeaker;
            JoinSay = r.JoinSay;
            LeaveSay = r.LeaveSay;
            Indoors = r.Indoors;
            AlwaysLit = r.AlwaysLit;
            AlwaysDark = r.AlwaysDark;
            BootMap = r.BootMap;
            BootX = r.BootX;
            BootY = r.BootY;
            Territory = r.Territory;
        }
        finally { _loading = false; }
    }

    // The record's player-facing DisplayName. Named to avoid colliding with the list-label DisplayName above.
    public string DisplayNameText
    {
        get => _displayName;
        set => DisplayName2Set(value);
    }
    private void DisplayName2Set(string value)
    {
        if (_displayName != value)
        {
            _displayName = value;
            OnPropertyChanged(nameof(DisplayNameText));
            MarkDirty();
        }
    }

    // ── Entity pickers (type-ahead) ───────────────────────────────────────────
    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) => id > 0 && id < entries.Length ? entries[id] : null;

    public NamedEntry? SelectedBootMap
    {
        get => EntryFor(_mapEntries(), BootMap);
        set
        {
            var id = value?.Id ?? 0;
            if (BootMap != id) BootMap = id;
        }
    }

    // ── Dirty tracking (single flag; every field edit marks the row) ───────────
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
    partial void OnMusicChanged(int value) => MarkDirty();
    partial void OnMoralChanged(MapMoral? value)
    {
        OnPropertyChanged(nameof(SelectedMoral));
        MarkDirty();
    }
    partial void OnGreetingSpeakerChanged(string value) => MarkDirty();
    partial void OnJoinSayChanged(string value) => MarkDirty();
    partial void OnLeaveSayChanged(string value) => MarkDirty();
    partial void OnIndoorsChanged(bool? value) => MarkDirty();
    partial void OnAlwaysLitChanged(bool? value)
    {
        if (value == true && AlwaysDark is not null) AlwaysDark = null;
        MarkDirty();
    }
    partial void OnAlwaysDarkChanged(bool? value)
    {
        if (value == true && AlwaysLit is not null) AlwaysLit = null;
        MarkDirty();
    }
    partial void OnBootMapChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedBootMap));
        MarkDirty();
    }
    partial void OnBootXChanged(int value) => MarkDirty();
    partial void OnBootYChanged(int value) => MarkDirty();
    partial void OnTerritoryChanged(bool value) => MarkDirty();

    public void ClearDirty()
    {
        _dirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    // Re-raise the type-ahead selections + Moral list when the shared entry lists / language change.
    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(SelectedBootMap));
    }

    public void RefreshMoralOptions()
    {
        MoralOptions = MoralChoices.Build();
        OnPropertyChanged(nameof(MoralOptions));
        OnPropertyChanged(nameof(SelectedMoral));
    }

    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(MapGroupRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(MapGroupRecord r)
    {
        CopyFrom(r);
        _dirty = false;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ApplyPacket(UpdateMapGroupPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            DisplayNameText = pkt.DisplayName;
            Music = pkt.Music;
            Moral = pkt.Moral;
            GreetingSpeaker = pkt.GreetingSpeaker;
            JoinSay = pkt.JoinSay;
            LeaveSay = pkt.LeaveSay;
            Indoors = pkt.Indoors;
            AlwaysLit = pkt.AlwaysLit;
            AlwaysDark = pkt.AlwaysDark;
            BootMap = pkt.BootMap;
            BootX = pkt.BootX;
            BootY = pkt.BootY;
            Territory = pkt.Territory;
        }
        finally { _loading = false; }

        IsLoaded = true;
        _dirty = false;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public MapGroupRecord ToRecord() => new()
    {
        Index = Index,
        Name = Name,
        DisplayName = DisplayNameText,
        Music = Music,
        Moral = Moral,
        GreetingSpeaker = GreetingSpeaker,
        JoinSay = JoinSay,
        LeaveSay = LeaveSay,
        Indoors = Indoors,
        AlwaysLit = AlwaysLit,
        AlwaysDark = AlwaysDark,
        BootMap = BootMap,
        BootX = BootX,
        BootY = BootY,
        Territory = Territory,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveMapGroupPacket BuildSavePacket() => new()
    {
        GroupNum = Index,
        Name = Name,
        DisplayName = DisplayNameText,
        Music = Music,
        Moral = Moral,
        GreetingSpeaker = GreetingSpeaker,
        JoinSay = JoinSay,
        LeaveSay = LeaveSay,
        Indoors = Indoors,
        AlwaysLit = AlwaysLit,
        AlwaysDark = AlwaysDark,
        BootMap = BootMap,
        BootX = BootX,
        BootY = BootY,
        Territory = Territory,
    };
}

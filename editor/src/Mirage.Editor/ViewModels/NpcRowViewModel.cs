using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
namespace Mirage.Editor.ViewModels;

/// <summary>
/// One NPC slot in the NPC editor's list — the editable mirror of an <see cref="NpcRecord"/>.
/// <para>Beyond the usual row duties (dirty tracking, load, save) this drives the editor's live
/// stat readout: level, vitals, regen, mitigation, damage, EXP, and the combat chances are all
/// computed through the shared formula classes, so the preview and the running game can never
/// disagree. Editing one re-raises the whole derived set rather than the field that changed.</para>
/// </summary>
public sealed partial class NpcRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    /// <summary>1-based NPC template number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Line the NPC speaks when it engages a player; blank for silent.</summary>
    [ObservableProperty] private string _says = "";
    [ObservableProperty] private int _sprite;
    /// <summary>Which sprite sheet <see cref="Sprite"/> is a row of.</summary>
    [ObservableProperty] private int _spriteSheet;
    // Footprint size class 1..3 (1 = 32x32, 2 = 64x64, 3 = 96x96); the form spinner clamps to [1, MaxNpcSize].
    [ObservableProperty] private int _size = 1;
    /// <summary>Seconds between despawn and respawn for this template's spawn slots.</summary>
    [ObservableProperty] private int _spawnSecs;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeWarning))]
    [NotifyPropertyChangedFor(nameof(HasRangeWarning))]
    private NpcBehavior _behavior;
    /// <summary>Comrade group id — same-group NPCs come to each other's aid (0 = no group).</summary>
    [ObservableProperty] private int _group;
    /// <summary>Aggro / sight radius in tiles, capped at <see cref="Constants.NpcRangeSoftCap"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeWarning))]
    [NotifyPropertyChangedFor(nameof(HasRangeWarning))]
    private int _range;
    /// <summary>This NPC's drop table — zero or more lines, each rolled independently on a kill. An empty
    /// table means "drops nothing", which is an ordinary state for trash rather than a misconfiguration.</summary>
    public ObservableCollection<NpcDropRowViewModel> Drops { get; } = [];

    // Supplied by the editor VM so a drop row can render an item picker. Defaulted rather than required
    // so the many plain `new NpcRowViewModel(i, record)` sites (and the tests) keep working — a row
    // constructed without them still round-trips its drops, it just cannot offer a picker.
    private Func<NamedEntry[]> _itemEntriesProvider = static () => [];
    private Func<int, bool> _isCurrency = static _ => false;
    [ObservableProperty] private bool _emitsLight;
    // Light attributes (used only when EmitsLight is true), authored via the conditional block in the form.
    [ObservableProperty] private Color _lightColor = ColorHex.ToColor(LightSpec.Torch.Rgb);
    partial void OnLightColorChanged(Color value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LightColorHex));
    }
    [ObservableProperty] private double _lightRadius = LightSpec.Torch.Radius;   // tiles
    partial void OnLightRadiusChanged(double value) => MarkDirty();
    [ObservableProperty] private FlickerStyle _lightFlicker = LightSpec.Torch.Flicker;
    partial void OnLightFlickerChanged(FlickerStyle value) => MarkDirty();
    [ObservableProperty] private int _lightIntensity = 100;   // percent, 0..100
    partial void OnLightIntensityChanged(int value) => MarkDirty();

    // Hex form of LightColor, kept in sync with the color picker (edit either, both update).
    /// <summary>"RRGGBB" text form of <see cref="LightColor"/>; an unparseable value is ignored.</summary>
    public string LightColorHex
    {
        get => $"{LightColor.R:X2}{LightColor.G:X2}{LightColor.B:X2}";
        set { if (ColorHex.TryParse(value, out var c)) LightColor = c; }
    }

    // The light config block shows only when the NPC emits light.
    /// <summary>Whether the form's light-attribute block is shown.</summary>
    public bool LightBlockVisible => EmitsLight;

    // Composed light spec, reused by ToRecord() and the save packet.
    /// <summary>The four light fields composed into the shared spec, with intensity as a 0..1 fraction.</summary>
    public LightSpec Light => new(ColorHex.ToRgb(LightColor), (float)LightRadius, LightFlicker,
        Math.Clamp(LightIntensity, 0, 100) / 100f);

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    /// <summary>Expected drops per kill — the SUM of the live chances, because the lines roll
    /// independently rather than competing. Shown because that sum is the one number a table can get
    /// quietly wrong: four 50% lines read as "all uncommon" but average two drops a kill.</summary>
    public string DropYieldText
    {
        get
        {
            int live = Drops.Count(d => !d.IsEmpty);
            if (live == 0) return EditorStrings.Get(EditorStrings.NpcEditor_DropYieldNone);
            double expected = Drops.Where(d => !d.IsEmpty).Sum(d => Math.Min(100, d.Chance) / 100.0);
            return EditorStrings.Format(EditorStrings.NpcEditor_DropYield, ("Lines", live), ("Expected", $"{expected:n2}"));
        }
    }

    // Non-blocking authoring guard: a row naming an item that can never land is almost always a slip.
    /// <summary>Warning text for half-configured drop rows; empty when every row is consistent.</summary>
    public string DropConfigWarning =>
        Drops.Any(d => d.ItemNum > 0 && d.Chance <= 0) ? EditorStrings.Get(EditorStrings.NpcEditor_DropWarnItemNoChance)
      : Drops.Any(d => d.ItemNum <= 0 && d.Chance > 0) ? EditorStrings.Get(EditorStrings.NpcEditor_DropWarnChanceNoItem)
      : string.Empty;
    /// <summary>Whether to show the drop-configuration warning.</summary>
    public bool HasDropConfigWarning => DropConfigWarning.Length > 0;

    // The same non-blocking guard, for a reach that reads as a slip at either end. Both are advisory:
    // the record is legal and occasionally deliberate, so this says so and writes it anyway.
    //
    // Too far is judged for every behavior; too near only for the two that read Range at all, since a
    // reach under MinAggressiveNpcRange leaves an NPC unable to notice anyone standing beside it.
    /// <summary>Warning text for a reach that will read as a surprise in play; empty otherwise.</summary>
    public string RangeWarning =>
        Range > Constants.NpcRangeSoftCap
            ? EditorStrings.Format(EditorStrings.NpcEditor_RangeWarnTooFar,
                ("Range", Range), ("Cap", Constants.NpcRangeSoftCap))
      : Behavior is (NpcBehavior.Pursue or NpcBehavior.Flee) && Range < Constants.MinAggressiveNpcRange
            ? EditorStrings.Format(EditorStrings.NpcEditor_RangeWarnTooShort,
                ("Range", Range), ("Min", Constants.MinAggressiveNpcRange))
      : string.Empty;
    /// <summary>Whether to show the reach warning.</summary>
    public bool HasRangeWarning => RangeWarning.Length > 0;

    /// <summary>Wire the item-picker providers after construction (the editor VM owns the live item
    /// list). Rebuilds the existing rows so any already loaded pick up the picker.</summary>
    public void AttachItemProviders(Func<NamedEntry[]> entries, Func<int, bool> isCurrency)
    {
        _itemEntriesProvider = entries;
        _isCurrency = isCurrency;
        foreach (var d in Drops) d.NotifyEntriesChanged();
    }

    /// <summary>Re-raise the picker-dependent bindings on every drop row (the item list changed).</summary>
    public void NotifyDropEntriesChanged()
    {
        foreach (var d in Drops) d.NotifyEntriesChanged();
    }

    private void LoadDrops(List<NpcDrop>? drops)
    {
        foreach (var d in Drops) d.PropertyChanged -= OnDropRowChanged;
        Drops.Clear();
        if (drops is null) { NotifyDropDerived(); return; }
        for (int i = 0; i < drops.Count; i++)
        {
            var row = new NpcDropRowViewModel(i + 1, drops[i], () => _itemEntriesProvider(), n => _isCurrency(n));
            row.PropertyChanged += OnDropRowChanged;
            Drops.Add(row);
        }
        NotifyDropDerived();
    }

    private void OnDropRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only a row reporting its OWN edit marks the NPC dirty — the same rule every row collection uses
        // for its loadout. NotifyDropDerived re-raises derived state on every row, and it runs on
        // selection, so treating any child raise as an edit made merely OPENING an NPC look modified.
        // The _loading guard does not help here: this fires long after construction.
        if (sender is NpcDropRowViewModel { IsDirty: false }) { NotifyDropDerived(); return; }
        if (!_loading) MarkDirty();
        NotifyDropDerived();
    }

    private void NotifyDropDerived()
    {
        OnPropertyChanged(nameof(DropYieldText));
        OnPropertyChanged(nameof(DropConfigWarning));
        OnPropertyChanged(nameof(HasDropConfigWarning));
    }

    /// <summary>Add an empty drop row. Unbounded — a hoard is authored as repeated lines, so a length cap
    /// would be a cap on payout; <see cref="DropYieldText"/> is what keeps the running total honest.</summary>
    [RelayCommand]
    private void AddDrop()
    {
        var row = new NpcDropRowViewModel(Drops.Count + 1, new NpcDrop(), () => _itemEntriesProvider(), n => _isCurrency(n));
        row.PropertyChanged += OnDropRowChanged;
        Drops.Add(row);
        MarkDirty();
        NotifyDropDerived();
    }

    /// <summary>Remove a drop row.</summary>
    [RelayCommand]
    private void RemoveDrop(NpcDropRowViewModel row)
    {
        row.PropertyChanged -= OnDropRowChanged;
        if (Drops.Remove(row)) MarkDirty();
        NotifyDropDerived();
    }

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public NpcRowViewModel(int index, NpcRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _says = r.Says;
        _sprite = r.Sprite;
        _spriteSheet = r.SpriteSheet;
        _size = r.EffectiveSize;
        _spawnSecs = r.SpawnSecs;
        _behavior = r.Behavior;
        _group = r.Group;
        _range = r.Range;
        // Guarded the way every row collection is: building drop rows subscribes change handlers that
        // land on MarkDirty, so an unguarded load flags every NPC with a drop table as edited on sight.
        _loading = true;
        try { LoadDrops(r.Drops); }
        finally { _loading = false; }
        _emitsLight = r.EmitsLight;
        _lightColor = ColorHex.ToColor(r.Light.Rgb);
        _lightRadius = r.Light.Radius;
        _lightFlicker = r.Light.Flicker;
        _lightIntensity = (int)Math.Round(r.Light.Intensity * 100);
        ClearDirty();   // a row built straight from disk has not been edited
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnEmitsLightChanged(bool value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LightBlockVisible));
    }
    partial void OnSaysChanged(string value) => MarkDirty();
    partial void OnSpriteChanged(int value) => MarkDirty();
    partial void OnSpriteSheetChanged(int value) => MarkDirty();
    partial void OnSizeChanged(int value) => MarkDirty();
    partial void OnSpawnSecsChanged(int value) => MarkDirty();
    partial void OnBehaviorChanged(NpcBehavior value) => MarkDirty();
    partial void OnGroupChanged(int value) => MarkDirty();
    partial void OnRangeChanged(int value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark the row clean after a successful save or discard.</summary>
    public void ClearDirty()
    {
        // Child rows too: one left dirty would re-mark the NPC on its next derived re-raise, and the
        // dot would come straight back with nobody having touched anything.
        foreach (var drop in Drops) drop.ClearDirty();
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Refill from an on-disk record (offline load or discard). Leaves the row clean.</summary>
    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(NpcRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(NpcRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Says = r.Says;
            Sprite = r.Sprite;
            SpriteSheet = r.SpriteSheet;
            Size = r.EffectiveSize;
            SpawnSecs = r.SpawnSecs;
            Behavior = r.Behavior;
            Group = r.Group;
            Range = r.Range;
            LoadDrops(r.Drops);
            EmitsLight = r.EmitsLight;
            LightColor = ColorHex.ToColor(r.Light.Rgb);
            LightRadius = r.Light.Radius;
            LightFlicker = r.Light.Flicker;
            LightIntensity = (int)Math.Round(r.Light.Intensity * 100);
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateNpcPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Says = pkt.Says;
            Sprite = pkt.Sprite;
            SpriteSheet = pkt.SpriteSheet;
            Size = pkt.Size;
            SpawnSecs = pkt.SpawnSecs;
            Behavior = pkt.Behavior;
            Group = pkt.Group;
            Range = pkt.Range;
            LoadDrops(pkt.Drops);
            EmitsLight = pkt.EmitsLight;
            LightColor = ColorHex.ToColor(pkt.Light.Rgb);
            LightRadius = pkt.Light.Radius;
            LightFlicker = pkt.Light.Flicker;
            LightIntensity = (int)Math.Round(pkt.Light.Intensity * 100);
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Project the row back into a record for saving.</summary>
    public NpcRecord ToRecord() => new()
    {
        Name = Name,
        Says = Says,
        Sprite = Sprite,
        SpriteSheet = SpriteSheet,
        Size = Size,
        SpawnSecs = SpawnSecs,
        Behavior = Behavior,
        Group = Group,
        Range = Range,
        // Empty rows are dropped here as well as server-side, so an offline save and an online save
        // produce the same file.
        Drops = Drops.Count == 0 ? null : [.. Drops.Where(d => !d.IsEmpty).Select(d => d.ToRecord())],
        EmitsLight = EmitsLight,
        Light = Light,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveNpcPacket BuildSavePacket() => new()
    {
        NpcNum = Index,
        Name = Name,
        Says = Says,
        Sprite = Sprite,
        SpriteSheet = SpriteSheet,
        Size = Size,
        SpawnSecs = SpawnSecs,
        Behavior = Behavior,
        Group = Group,
        Range = Range,
        Drops = Drops.Count == 0 ? null : [.. Drops.Where(d => !d.IsEmpty).Select(d => d.ToRecord())],
        EmitsLight = EmitsLight,
        Light = Light,
    };
}

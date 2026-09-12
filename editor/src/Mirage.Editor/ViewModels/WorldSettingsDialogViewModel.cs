using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One record family's ceiling. Clamped as it is typed, so the dialog cannot hold a value the
/// world would refuse.</summary>
public sealed partial class WorldLimitRowViewModel(string familyId, string labelKey, int value)
    : ObservableObject
{
    /// <summary>Which family this row sets the ceiling for. Carried so the dialog reads its rows back by
    /// name: added in one order and read by position, a reordered or removed row silently moves every
    /// ceiling after it onto the wrong family.</summary>
    public string FamilyId { get; } = familyId;

    public string Label => EditorStrings.Get(labelKey);

    [ObservableProperty] private int _value = value;

    partial void OnValueChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, RecordLimits.Ceiling);
        if (clamped != value) Value = clamped;
    }
}

/// <summary>
/// What a world says about itself: its name, the size new maps are created at, and its record ceilings.
///
/// <para>Read from and written to <c>world.json</c> at the world's root, so all three travel with the
/// folder and two worlds can be open in turn on their own terms. Lowering a ceiling hides the slots above
/// it from the pickers; it deletes nothing, and raising it again brings them back.</para>
///
/// <para>Offline only. Connected, the ceilings are the server's and are stated in the hello.</para>
/// </summary>
public sealed partial class WorldSettingsDialogViewModel : ObservableObject
{
    public ObservableCollection<WorldLimitRowViewModel> Rows { get; } = [];

    /// <summary>False while connected, which disables every field.</summary>
    public bool IsConfigurable { get; }

    /// <summary>What to call this world, for whoever is holding it. Never seen by a player — it names a
    /// set of records, not the game.</summary>
    [ObservableProperty] private string _worldName = string.Empty;

    /// <summary>The size a new map in this world is created at. A map may be resized afterwards; this is
    /// only where one starts.</summary>
    [ObservableProperty] private int _defaultMapWidth = MapSize.Default.Width;

    /// <inheritdoc cref="DefaultMapWidth"/>
    [ObservableProperty] private int _defaultMapHeight = MapSize.Default.Height;

    /// <summary>What the world will be called if the name is left empty, shown in the box rather than
    /// filled into it: a placeholder that becomes a value the moment somebody types beside it is a name
    /// nobody chose.</summary>
    public string UntitledPlaceholder => EditorStrings.Get(EditorStrings.World_Untitled);

    /// <summary>Said when either axis is past the soft cap — see <see cref="MapSize.SoftCap"/>.</summary>
    public string DefaultMapSizeWarning =>
        new MapSize(DefaultMapWidth, DefaultMapHeight).IsPastSoftCap
            ? EditorStrings.Format(EditorStrings.WorldSettings_MapSizeSoftCapWarning, ("Cap", MapSize.SoftCap))
            : string.Empty;

    partial void OnDefaultMapWidthChanged(int value) => ClampAndWarn(value, isWidth: true);
    partial void OnDefaultMapHeightChanged(int value) => ClampAndWarn(value, isWidth: false);

    private void ClampAndWarn(int value, bool isWidth)
    {
        if (value < 1)
        {
            if (isWidth) DefaultMapWidth = 1; else DefaultMapHeight = 1;
            return;
        }
        OnPropertyChanged(nameof(DefaultMapSizeWarning));
    }

    public string OfflineOnlyNotice => EditorStrings.Get(EditorStrings.WorldSettings_OfflineOnlyNotice);
    public string Intro => EditorStrings.Get(EditorStrings.WorldSettings_Intro);
    public string NameLabel => EditorStrings.Get(EditorStrings.WorldSettings_NameLabel);
    public string NameHint => EditorStrings.Get(EditorStrings.WorldSettings_NameHint);
    public string DefaultMapSizeLabel => EditorStrings.Get(EditorStrings.WorldSettings_DefaultMapSizeLabel);
    public string DefaultMapSizeHint => EditorStrings.Get(EditorStrings.WorldSettings_DefaultMapSizeHint);

    public event Action<WorldManifest>? Confirmed;
    public event Action? Canceled;

    public WorldSettingsDialogViewModel(WorldManifest manifest, bool isOnline)
    {
        IsConfigurable = !isOnline;
        WorldName = manifest.Name;
        DefaultMapWidth = manifest.DefaultMapSize.Width;
        DefaultMapHeight = manifest.DefaultMapSize.Height;
        var limits = manifest.Records;
        _limits = limits;
        _opened = manifest;
        // One row per family whose ceiling an operator may set. A family with a fixed ceiling is left
        // out: its count is baked into the save format, so offering it as a setting would offer a
        // change the world cannot take.
        foreach (var family in CoreRecordFamilies.World.Where(f => !f.LimitIsFixed))
        {
            Rows.Add(new(family.Id, MainWindowViewModel.SectionLabelKey(family.Id), limits.For(family.Id)));
        }
    }

    // What the dialog opened on. Read back for any family it does not offer a row for, and for
    // every part of the manifest this dialog does not edit.
    private readonly RecordLimits _limits = RecordLimits.Default;
    private readonly WorldManifest _opened = new();

    /// <summary>The ceiling typed for one family, or its current value when the dialog has no row for it
    /// — a family with a fixed ceiling is never offered, and must come back unchanged rather than
    /// zeroed.</summary>
    private int Of(string familyId, int current) =>
        Rows.FirstOrDefault(r => r.FamilyId == familyId)?.Value ?? current;

    /// <summary>What the world becomes. Built from <see cref="_opened"/> with only the fields this
    /// dialog edits replaced, because a manifest assembled from scratch drops whatever the dialog does
    /// not know about — and a world's authored appearance roster is not something a settings dialog
    /// should be able to delete by not mentioning it.</summary>
    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke(_opened with
    {
        Name = WorldName.Trim(),
        DefaultMapSize = new MapSize(DefaultMapWidth, DefaultMapHeight),
        Records = new RecordLimits
        {
            Items = Of(CoreRecordFamilies.Items, _limits.Items),
            Npcs = Of(CoreRecordFamilies.Npcs, _limits.Npcs),
            Shops = Of(CoreRecordFamilies.Shops, _limits.Shops),
            Spells = Of(CoreRecordFamilies.Spells, _limits.Spells),
            Quests = Of(CoreRecordFamilies.Quests, _limits.Quests),
            Conversations = Of(CoreRecordFamilies.Conversations, _limits.Conversations),
            Maps = Of(CoreRecordFamilies.Maps, _limits.Maps),
            MapGroups = Of(CoreRecordFamilies.MapGroups, _limits.MapGroups),
        }.Clamped(RecordLimits.Ceiling),
    });

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}

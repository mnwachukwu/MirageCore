using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One record family's ceiling. Clamped as it is typed, so the dialog cannot hold a value the
/// world would refuse.</summary>
public sealed partial class WorldLimitRowViewModel(string labelKey, int value) : ObservableObject
{
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
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Items"), limits.Items));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("NPCs"), limits.Npcs));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Shops"), limits.Shops));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Spells"), limits.Spells));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Quests"), limits.Quests));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Conversations"), limits.Conversations));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Maps"), limits.Maps));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("MapGroups"), limits.MapGroups));
    }

    private int At(int i) => Rows[i].Value;

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke(new WorldManifest
    {
        Name = WorldName.Trim(),
        DefaultMapSize = new MapSize(DefaultMapWidth, DefaultMapHeight),
        Records = new RecordLimits
        {
            Items = At(0),
            Npcs = At(1),
            Shops = At(2),
            Spells = At(3),
            Quests = At(4),
            Conversations = At(5),
            Maps = At(6),
            MapGroups = At(7),
        }.Clamped(RecordLimits.Ceiling),
    });

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}

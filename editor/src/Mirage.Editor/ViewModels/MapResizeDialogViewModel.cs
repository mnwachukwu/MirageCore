using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Changing one map's size.
///
/// <para><b>Nothing here can be undone.</b> The tiles a shrink discards are not written anywhere first, so
/// there is nothing to restore them from — not the editor, not the server, not a revision. The dialog's job
/// is to make sure the author knows exactly what goes before they agree to it, and to say plainly that a
/// backup of the world folder is the only way to get it back.</para>
///
/// <para>A map joined to a neighbor is refused outright: a neighborhood measures in one size, so resizing
/// one map alone would make every seam it shares lie about where a step lands.</para>
/// </summary>
public sealed partial class MapResizeDialogViewModel : ObservableObject
{
    private readonly MapRecord _map;
    private readonly IReadOnlyList<MapRecord?> _allMaps;
    private readonly int _mapNum;

    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;

    /// <summary>The map numbers this one is joined to. Non-empty means the resize is refused.</summary>
    public IReadOnlyList<int> LinkedMaps { get; }

    /// <summary>False when the map is linked — every field is disabled and the reason is shown.</summary>
    public bool CanResize => LinkedMaps.Count == 0;

    public string CurrentSizeText =>
        EditorStrings.Format(EditorStrings.MapResize_CurrentSize, ("Width", _map.Width), ("Height", _map.Height));

    public string LinkedRefusal =>
        CanResize ? string.Empty
                  : EditorStrings.Format(EditorStrings.MapResize_LinkedRefusal, ("Maps", string.Join(", ", LinkedMaps)));

    public string SoftCapWarning =>
        new MapSize(Width, Height).IsPastSoftCap
            ? EditorStrings.Format(EditorStrings.MapResize_SoftCapWarning, ("Cap", MapSize.SoftCap))
            : string.Empty;

    /// <summary>What this size would discard, itemized. Empty when it discards nothing.</summary>
    public string LossSummary
    {
        get
        {
            if (!CanResize) return string.Empty;
            var cost = MapResize.CostOf(_map, new MapSize(Width, Height), _allMaps, _mapNum);
            if (!cost.IsLossy) return string.Empty;

            var parts = new List<string>();
            if (cost.AuthoredTiles > 0)
                parts.Add(EditorStrings.Format(EditorStrings.MapResize_LossTiles, ("Count", cost.AuthoredTiles)));
            if (cost.Lights > 0)
                parts.Add(EditorStrings.Format(EditorStrings.MapResize_LossLights, ("Count", cost.Lights)));
            if (cost.NpcPins > 0)
                parts.Add(EditorStrings.Format(EditorStrings.MapResize_LossPins, ("Count", cost.NpcPins)));
            if (cost.InboundWarps > 0)
                parts.Add(EditorStrings.Format(EditorStrings.MapResize_LossWarps, ("Count", cost.InboundWarps)));
            return string.Join("\n", parts);
        }
    }

    public bool HasLoss => LossSummary.Length > 0;

    /// <summary>Shown whenever something would be discarded: it cannot be undone or restored by any means,
    /// and a copy of the world folder is the only way back.</summary>
    public string Irreversible => EditorStrings.Get(EditorStrings.MapResize_Irreversible);

    public string Intro => EditorStrings.Get(EditorStrings.MapResize_Intro);
    public string WidthLabel => EditorStrings.Get(EditorStrings.MapResize_WidthLabel);
    public string HeightLabel => EditorStrings.Get(EditorStrings.MapResize_HeightLabel);

    public event Action<MapSize>? Confirmed;
    public event Action? Canceled;

    public MapResizeDialogViewModel(MapRecord map, IReadOnlyList<MapRecord?> allMaps, int mapNum)
    {
        _map = map;
        _allMaps = allMaps;
        _mapNum = mapNum;
        _width = map.Width;
        _height = map.Height;
        LinkedMaps = MapResize.LinkedMaps(allMaps, mapNum);
    }

    partial void OnWidthChanged(int value) => Restate(value, isWidth: true);
    partial void OnHeightChanged(int value) => Restate(value, isWidth: false);

    private void Restate(int value, bool isWidth)
    {
        if (value < 1)
        {
            if (isWidth) Width = 1; else Height = 1;
            return;
        }
        OnPropertyChanged(nameof(LossSummary));
        OnPropertyChanged(nameof(HasLoss));
        OnPropertyChanged(nameof(SoftCapWarning));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!CanResize) return;
        Confirmed?.Invoke(new MapSize(Width, Height));
    }

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
